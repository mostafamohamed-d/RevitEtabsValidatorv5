using System.Reflection;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
namespace RevitEtabsValidator.ETABS;

public sealed class EtabsModelReader
{
    // NOTE: this class intentionally keeps the original design goal of not
    // referencing a compiled ETABSv1 interop assembly, so it can attach to
    // any installed ETABS version by ProgID alone. C#'s `dynamic` keyword
    // CANNOT be used for this, though: COM methods here take `ref`/`out`
    // parameters (GetNameList, GetPoints, GetRectangle, ...), and C# refuses
    // to compile a `ref`/`out` argument on a dynamic call site (CS1975).
    // The previous version of this file did exactly that and could not build.
    //
    // The fix is to go one level lower than `dynamic`: call through
    // Type.InvokeMember directly (the same mechanism `dynamic` uses
    // internally, and the same one VB.NET's late binding has always used for
    // COM). InvokeMember returns the method's return value AND writes any
    // ByRef parameter's new value back into the same `object[] args` array
    // element, so each call below is: build an args array with initial
    // values, invoke, then read the updated values back out of that array.

    private readonly object _sap;
    public EtabsModelReader(object sap) => _sap = sap;

    public List<ColumnElement> ReadColumns() => ReadFrames<ColumnElement>(true);
    public List<BeamElement> ReadBeams() => ReadFrames<BeamElement>(false);

    private List<T> ReadFrames<T>(bool columns) where T : ElementBase, new()
    {
        var list = new List<T>();

        object[] nameListArgs = { 0, Array.Empty<string>() };
        int rc = (int)Invoke(_sap, "FrameObj.GetNameList", nameListArgs);
        if (rc != 0) return list;
        var names = (string[])nameListArgs[1] ?? Array.Empty<string>();

        foreach (var name in names)
        {
            try
            {
                object[] pointArgs = { name, "", "" };
                Invoke(_sap, "FrameObj.GetPoints", pointArgs);
                string p1 = (string)pointArgs[1];
                string p2 = (string)pointArgs[2];
                if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2)) continue;

                var a = Point(p1);
                var b = Point(p2);

                string label = name, story = "";
                try
                {
                    object[] labelArgs = { name, "", "" };
                    Invoke(_sap, "FrameObj.GetLabelFromName", labelArgs);
                    label = (string)labelArgs[1];
                    story = (string)labelArgs[2];
                }
                catch { /* keep defaults - label falls back to the frame name */ }

                string prop = "";
                try
                {
                    // SAuto is documented by CSI as a string (the auto-select list
                    // name when AutoSelect=True), not a bool - matching the real
                    // type here matters for COM marshaling, unlike a plain dynamic
                    // call where a mismatch might silently coerce.
                    object[] secArgs = { name, "", "" };
                    Invoke(_sap, "FrameObj.GetSection", secArgs);
                    prop = (string)secArgs[1];
                }
                catch { /* section stays unresolved - width/depth will read as 0 */ }

                (double w, double d) = Section(prop);

                // ETABS story label is preferred; if unavailable infer by closest story elevation to midpoint.
                if (string.IsNullOrWhiteSpace(story)) story = ClosestStory((a.Z + b.Z) / 2);

                if (columns)
                {
                    double angle = 0;
                    try
                    {
                        object[] axesArgs = { name, 0.0, 0.0 };
                        Invoke(_sap, "FrameObj.GetLocalAxes", axesArgs);
                        angle = (double)axesArgs[2];
                    }
                    catch { /* rotation stays 0 if this frame's axes can't be read */ }

                    var c = new ColumnElement
                    {
                        Id = name, Name = label, SectionName = prop, LevelName = story,
                        Source = SourceApplication.Etabs, StartPoint = a, EndPoint = b,
                        BaseElevation = Math.Min(a.Z, b.Z), TopElevation = Math.Max(a.Z, b.Z),
                        Width = w, Depth = d, Rotation = angle
                    };
                    list.Add((T)(ElementBase)c);
                }
                else
                {
                    list.Add((T)(ElementBase)new BeamElement
                    {
                        Id = name, Name = label, SectionName = prop, LevelName = story,
                        Source = SourceApplication.Etabs, StartPoint = a, EndPoint = b,
                        Width = w, Depth = d
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ETABS frame {name}: {ex.Message}");
            }
        }

        return list;
    }

    private Point3D Point(string pointName)
    {
        object[] args = { pointName, 0.0, 0.0, 0.0, "Global" };
        Invoke(_sap, "PointObj.GetCoordCartesian", args);
        return new Point3D((double)args[1], (double)args[2], (double)args[3]);
    }

    private (double w, double d) Section(string prop)
    {
        if (string.IsNullOrWhiteSpace(prop)) return (0, 0);
        try
        {
            object[] args = { prop, "", 0.0, 0.0 };
            int rc = (int)Invoke(_sap, "PropFrame.GetRectangle", args);
            if (rc == 0) return ((double)args[3], (double)args[2]); // t2 = width, t3 = depth
        }
        catch { /* not a rectangular section, or the call failed - treat as unknown */ }
        return (0, 0);
    }

    private string ClosestStory(double z)
    {
        try
        {
            object[] listArgs = { 0, Array.Empty<string>() };
            Invoke(_sap, "Story.GetNameList", listArgs);
            var stories = (string[])listArgs[1] ?? Array.Empty<string>();

            string best = "";
            double bestDelta = double.MaxValue;
            foreach (var s in stories)
            {
                double elevation;
                try
                {
                    object[] elevArgs = { s, 0.0 };
                    Invoke(_sap, "Story.GetElevation", elevArgs);
                    elevation = (double)elevArgs[1];
                }
                catch { continue; }

                var delta = Math.Abs(elevation - z);
                if (delta < bestDelta) { bestDelta = delta; best = s; }
            }
            return best;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Navigates dotted COM property paths (e.g. "FrameObj.GetNameList" means
    /// "call GetNameList on the SapModel.FrameObj sub-object") and invokes the
    /// final method via reflection, which is the one late-binding mechanism
    /// that supports ByRef parameters from C#. Returns the method's return
    /// value; any ByRef argument's new value is written back into `args`
    /// in place, so read updated values out of `args` after calling this.
    /// </summary>
    private static object Invoke(object target, string path, object[] args)
    {
        var parts = path.Split('.');
        object current = target;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            current = current.GetType().InvokeMember(
                parts[i], BindingFlags.GetProperty, null, current, null)
                ?? throw new InvalidOperationException($"COM property '{parts[i]}' returned null while resolving '{path}'.");
        }

        return current.GetType().InvokeMember(
            parts[^1], BindingFlags.InvokeMethod, null, current, args)
            ?? throw new InvalidOperationException($"COM method '{path}' returned null.");
    }
}
