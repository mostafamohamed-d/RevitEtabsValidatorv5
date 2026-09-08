using System;
using System.Runtime.InteropServices;
using ETABSv1;

namespace ColumnGrouping.Exporter.Etabs
{
    public sealed class EtabsConnection
    {
        private const string ProgId = "CSI.ETABS.API.ETABSObject";

        [DllImport("oleaut32.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int GetActiveObject(
            ref Guid rclsid,
            IntPtr reserved,
            [MarshalAs(UnmanagedType.Interface)] out object ppunk);

        public cOAPI? EtabsObject { get; private set; }
        public cSapModel? SapModel { get { return EtabsObject?.SapModel; } }
        public string Message { get; private set; } = "";

        public bool ConnectRunning()
        {
            EtabsObject = null;
            try
            {
                var comType = Type.GetTypeFromProgID(ProgId, false);
                if (comType == null)
                {
                    Message = $"ETABS COM ProgID '{ProgId}' is not registered. Install/repair ETABS and its API registration.";
                    return false;
                }

                Guid clsid = comType.GUID;
                int hr = GetActiveObject(ref clsid, IntPtr.Zero, out object comObject);
                if (hr != 0 || comObject == null)
                {
                    Message = $"Could not attach to running ETABS. HRESULT=0x{hr:X8}. Check that ETABS is running and a model is open.";
                    return false;
                }

                if (comObject is not cOAPI api)
                {
                    Message = $"Running ETABS COM object could not be cast to ETABSv1.cOAPI. Actual type: {comObject.GetType().FullName}.";
                    return false;
                }

                EtabsObject = api;
                if (EtabsObject.SapModel == null)
                {
                    Message = "ETABS attached but SapModel is unavailable.";
                    return false;
                }

                Message = "Connected to the running ETABS instance.";
                return true;
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x800706BA)
            {
                Message = "ETABS RPC server unavailable (0x800706BA). Checklist: ETABS is running, the correct instance is open, the model is open, COM registration matches the ETABS installation, and x64/x64 process compatibility is correct.";
                return false;
            }
            catch (Exception ex)
            {
                Message = $"ETABS connection failed: {ex.GetType().Name}: {ex.Message}";
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
                message = rc == 0 ? "Units set to kN-mm-C." : $"SetPresentUnits failed. Return code={rc}.";
                return rc == 0;
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x800706BA)
            {
                message = "ETABS RPC server unavailable (0x800706BA) while setting units.";
                return false;
            }
            catch (Exception ex)
            {
                message = $"Could not set ETABS units: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }
    }
}
