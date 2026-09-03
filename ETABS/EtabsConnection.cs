using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RevitEtabsValidator.ETABS;

public sealed class EtabsConnection
{
    private const string EtabsObjectProgId = "CSI.ETABS.API.ETABSObject";
    private const string HelperProgId = "ETABSv1.Helper";

    public object? EtabsObject { get; private set; }
    public object? SapModel => GetProperty(EtabsObject, "SapModel");
    public bool IsConnected => EtabsObject != null && SapModel != null;
    public string Message { get; private set; } = "";

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.Interface)] out object ppunk);

    public bool ConnectRunning()
    {
        EtabsObject = null;
        var diagnostics = new List<string>();

        try
        {
            var helperType = Type.GetTypeFromProgID(HelperProgId, throwOnError: false);
            if (helperType == null)
            {
                diagnostics.Add("ETABSv1.Helper ProgID was not found.");
            }
            else
            {
                var helper = Activator.CreateInstance(helperType);
                if (helper == null)
                {
                    diagnostics.Add("ETABSv1.Helper could not be created.");
                }
                else
                {
                    // Invoke directly instead of GetMethod(): COM automation
                    // methods can be absent from managed reflection metadata
                    // even though IDispatch exposes them.
                    var processes = Process.GetProcessesByName("ETABS")
                        .OrderBy(p => p.Id)
                        .ToList();

                    foreach (var process in processes)
                    {
                        try
                        {
                            var result = helperType.InvokeMember(
                                "GetObjectProcess",
                                BindingFlags.InvokeMethod,
                                null,
                                helper,
                                new object[] { process.Id });

                            if (result != null)
                            {
                                EtabsObject = result;
                                if (IsConnected)
                                {
                                    Message = $"Connected to ETABS process {process.Id} through ETABSv1.Helper.GetObjectProcess().";
                                    return true;
                                }
                            }
                        }
                        catch (TargetInvocationException ex) when (ex.InnerException != null)
                        {
                            diagnostics.Add($"GetObjectProcess PID {process.Id}: {ex.InnerException.Message}");
                        }
                        catch (Exception ex)
                        {
                            diagnostics.Add($"GetObjectProcess PID {process.Id}: {ex.Message}");
                        }
                    }

                    // If ETABS 22 exposes an active API instance, use it.
                    try
                    {
                        var result = helperType.InvokeMember(
                            "GetObject",
                            BindingFlags.InvokeMethod,
                            null,
                            helper,
                            new object[] { EtabsObjectProgId });

                        if (result != null)
                        {
                            EtabsObject = result;
                            if (IsConnected)
                            {
                                Message = "Connected to the active ETABS instance through ETABSv1.Helper.GetObject().";
                                return true;
                            }
                        }

                        diagnostics.Add("ETABSv1.Helper.GetObject returned no usable SapModel.");
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException != null)
                    {
                        diagnostics.Add("GetObject: " + ex.InnerException.Message);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add("GetObject: " + ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add("Helper setup: " + Unwrap(ex).Message);
        }

        // Final fallback: Windows COM running-object table.
        try
        {
            var objectType = Type.GetTypeFromProgID(EtabsObjectProgId, throwOnError: false);
            if (objectType != null)
            {
                var clsid = objectType.GUID;
                GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
                EtabsObject = obj;
                if (IsConnected)
                {
                    Message = "Connected to the running ETABS instance through the COM running-object table.";
                    return true;
                }

                diagnostics.Add("COM ROT returned an object, but SapModel was unavailable.");
            }
            else
            {
                diagnostics.Add("ETABS COM ProgID was not found: " + EtabsObjectProgId);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add("COM ROT: " + Unwrap(ex).Message);
        }

        Message = diagnostics.Count == 0
            ? "No running ETABS instance could be attached."
            : "No running ETABS instance could be attached. " + string.Join(" | ", diagnostics);
        return false;
    }

    public bool StartAndConnect()
    {
        EtabsObject = null;
        var diagnostics = new List<string>();

        try
        {
            var helperType = Type.GetTypeFromProgID(HelperProgId, throwOnError: false);
            if (helperType != null)
            {
                var helper = Activator.CreateInstance(helperType);
                if (helper != null)
                {
                    var created = helperType.InvokeMember(
                        "CreateObjectProgID",
                        BindingFlags.InvokeMethod,
                        null,
                        helper,
                        new object[] { EtabsObjectProgId });

                    if (created != null)
                    {
                        EtabsObject = created;
                        var rc = InvokeInt(EtabsObject, "ApplicationStart");
                        if (rc == 0 && IsConnected)
                        {
                            Message = "ETABS started and connected through ETABSv1.Helper.";
                            return true;
                        }

                        diagnostics.Add($"Helper CreateObjectProgID/ApplicationStart returned {rc}.");
                        if (IsConnected) return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add("Helper start: " + Unwrap(ex).Message);
        }

        try
        {
            var t = Type.GetTypeFromProgID(EtabsObjectProgId, throwOnError: true)!;
            EtabsObject = Activator.CreateInstance(t);
            if (EtabsObject != null)
            {
                var rc = InvokeInt(EtabsObject, "ApplicationStart");
                Message = rc == 0
                    ? "ETABS started and connected through COM."
                    : $"ETABS object was created; ApplicationStart returned {rc}.";
                return IsConnected;
            }

            diagnostics.Add("ETABS COM object could not be created.");
        }
        catch (Exception ex)
        {
            diagnostics.Add("COM start: " + Unwrap(ex).Message);
        }

        Message = "Unable to start ETABS. " + string.Join(" | ", diagnostics);
        return false;
    }

    public bool SetUnitsKnMmC(out string message)
    {
        try
        {
            if (!IsConnected)
            {
                message = "ETABS is not connected.";
                return false;
            }

            var rc = InvokeInt(SapModel!, "SetPresentUnits", 5);
            message = rc == 0
                ? "ETABS units set to kN-mm-C."
                : $"SetPresentUnits returned {rc}.";
            return rc == 0;
        }
        catch (Exception ex)
        {
            message = "Could not set ETABS units: " + Unwrap(ex).Message;
            return false;
        }
    }

    public bool SetUnitsKnMmC() => SetUnitsKnMmC(out _);

    private static object? GetProperty(object? target, string name)
    {
        if (target == null) return null;
        return target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            null,
            target,
            null);
    }

    private static int InvokeInt(object target, string method, params object[] args)
    {
        var result = target.GetType().InvokeMember(
            method,
            BindingFlags.InvokeMethod,
            null,
            target,
            args);

        return result == null ? 0 : Convert.ToInt32(result);
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }
}
