using ETABSv1;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RevitEtabsValidator.ETABS;

// One running ETABS process found in the Windows COM Running Object Table.
// DisplayName is best-effort (the raw ROT moniker display name); it exists so a
// user picking between several open instances has *something* to distinguish them
// by, not a promise of a human-friendly model name - the ETABS OAPI has no
// documented, verified-in-this-codebase call for "which model is open" that this
// project can safely call without a real ETABS install to check the signature against.
public sealed record EtabsRunningInstance(string DisplayName, cOAPI Api);

public sealed class EtabsConnection
{
    private const string EtabsObjectProgId = "CSI.ETABS.API.ETABSObject";

    [DllImport("oleaut32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.Interface)] out object ppunk);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    public cOAPI? EtabsObject { get; private set; }
    public cSapModel? SapModel => EtabsObject?.SapModel;
    public bool IsConnected => EtabsObject != null && SapModel != null;
    public string Message { get; private set; } = "";

    // Enumerates every ETABS process currently registered in the Running Object
    // Table, so the caller can offer a picker when more than one is open instead of
    // blindly attaching to whichever GetActiveObject happens to return (which is
    // normally just the most-recently-activated instance). Robust to any moniker
    // naming convention CSI uses internally: rather than matching display-name text
    // against the ProgID, every running COM object is fetched and kept only if it
    // actually implements cOAPI.
    public IReadOnlyList<EtabsRunningInstance> ListRunningInstances()
    {
        var results = new List<EtabsRunningInstance>();

        try
        {
            if (CreateBindCtx(0, out var bindCtx) != 0 || bindCtx == null)
                return results;

            if (GetRunningObjectTable(0, out var rot) != 0 || rot == null)
                return results;

            rot.EnumRunning(out var enumMoniker);
            if (enumMoniker == null)
                return results;

            enumMoniker.Reset();
            var monikers = new IMoniker[1];
            var index = 0;

            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];

                object? comObject = null;
                try
                {
                    rot.GetObject(moniker, out comObject);
                }
                catch
                {
                    continue;
                }

                if (comObject is not cOAPI api)
                    continue;

                index++;
                var displayName = $"ETABS instance {index}";
                try
                {
                    moniker.GetDisplayName(bindCtx, null, out var name);
                    if (!string.IsNullOrWhiteSpace(name))
                        displayName = $"ETABS instance {index} ({name})";
                }
                catch
                {
                    // Fall back to the plain "ETABS instance N" label above.
                }

                results.Add(new EtabsRunningInstance(displayName, api));
            }
        }
        catch
        {
            // Best-effort discovery only; the caller falls back to ConnectRunning()
            // (single-instance GetActiveObject) when this finds nothing.
        }

        return results;
    }

    // Attaches to a specific instance already chosen by the caller (for example, from
    // ListRunningInstances() when more than one ETABS process is open).
    public bool ConnectTo(EtabsRunningInstance instance)
    {
        EtabsObject = null;

        try
        {
            EtabsObject = instance.Api;

            if (EtabsObject.SapModel == null)
            {
                Message = $"{instance.DisplayName} was found, but its SapModel is not available.";
                return false;
            }

            Message = $"Connected to {instance.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            Message = $"Could not connect to {instance.DisplayName}: " + ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    public bool ConnectRunning()
    {
        EtabsObject = null;

        try
        {
            var comType = Type.GetTypeFromProgID(EtabsObjectProgId, throwOnError: false);

            if (comType == null)
            {
                Message = $"ETABS COM ProgID '{EtabsObjectProgId}' is not registered on this computer.";
                return false;
            }

            var clsid = comType.GUID;
            var hr = GetActiveObject(ref clsid, IntPtr.Zero, out var comObject);

            if (hr != 0 || comObject == null)
            {
                Message = $"Could not attach to the running ETABS instance. HRESULT=0x{hr:X8}.";
                return false;
            }

            if (comObject is not cOAPI api)
            {
                Message = $"The running ETABS COM object was found, but it could not be cast to ETABSv1.cOAPI. Actual type: {comObject.GetType().FullName}.";
                return false;
            }

            EtabsObject = api;

            if (EtabsObject.SapModel == null)
            {
                Message = "ETABS COM object was attached, but SapModel is not available.";
                return false;
            }

            Message = "Connected to the running ETABS instance through the Windows COM Running Object Table.";
            return true;
        }
        catch (Exception ex)
        {
            Message = "Could not attach to the running ETABS instance: " + ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    public bool StartAndConnect()
    {
        EtabsObject = null;

        try
        {
            var comType = Type.GetTypeFromProgID(EtabsObjectProgId, throwOnError: false);

            if (comType == null)
            {
                Message = $"ETABS COM ProgID '{EtabsObjectProgId}' is not registered on this computer.";
                return false;
            }

            var instance = Activator.CreateInstance(comType);

            if (instance is not cOAPI api)
            {
                Message = instance == null
                    ? "Could not create the ETABS COM object."
                    : $"ETABS COM object was created, but it could not be cast to ETABSv1.cOAPI. Actual type: {instance.GetType().FullName}.";
                return false;
            }

            EtabsObject = api;
            int rc = EtabsObject.ApplicationStart();

            if (rc != 0)
            {
                Message = $"ETABS ApplicationStart returned {rc}.";
                return false;
            }

            if (EtabsObject.SapModel == null)
            {
                Message = "ETABS started, but SapModel is not available.";
                return false;
            }

            Message = "ETABS started and connected through direct COM activation.";
            return true;
        }
        catch (Exception ex)
        {
            Message = "Unable to start ETABS: " + ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    public bool SetUnitsKnMmC(out string message)
    {
        try
        {
            if (SapModel == null)
            {
                message = "ETABS is not connected.";
                return false;
            }

            int rc = SapModel.SetPresentUnits(eUnits.kN_mm_C);
            message = rc == 0
                ? "ETABS units set to kN-mm-C."
                : $"SetPresentUnits returned {rc}.";
            return rc == 0;
        }
        catch (Exception ex)
        {
            message = "Could not set ETABS units: " + ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    public bool SetUnitsKnMmC() => SetUnitsKnMmC(out _);
}
