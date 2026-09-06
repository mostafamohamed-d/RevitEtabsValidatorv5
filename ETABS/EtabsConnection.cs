using ETABSv1;
using System.Runtime.InteropServices;

namespace RevitEtabsValidator.ETABS;

public sealed class EtabsConnection
{
    private const string EtabsObjectProgId = "CSI.ETABS.API.ETABSObject";

    [DllImport("oleaut32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.Interface)] out object ppunk);

    public cOAPI? EtabsObject { get; private set; }
    public cSapModel? SapModel => EtabsObject?.SapModel;
    public bool IsConnected => EtabsObject != null && SapModel != null;
    public string Message { get; private set; } = "";

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
