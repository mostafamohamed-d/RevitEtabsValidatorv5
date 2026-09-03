using ETABSv1;

namespace RevitEtabsValidator.ETABS;

public sealed class EtabsConnection
{
    private const string EtabsObjectProgId = "CSI.ETABS.API.ETABSObject";

    public cOAPI? EtabsObject { get; private set; }
    public cSapModel? SapModel => EtabsObject?.SapModel;
    public bool IsConnected => EtabsObject != null && SapModel != null;
    public string Message { get; private set; } = "";

    public bool ConnectRunning()
    {
        EtabsObject = null;

        try
        {
            // CSI's documented ETABS v1 connection pattern:
            // create the typed Helper, then get the active running ETABS OAPI object.
            cHelper helper = new Helper();
            EtabsObject = helper.GetObject(EtabsObjectProgId);

            if (EtabsObject != null && EtabsObject.SapModel != null)
            {
                Message = "Connected to the running ETABS instance through ETABSv1.Helper.GetObject().";
                return true;
            }

            Message = "ETABSv1.Helper.GetObject() returned no running ETABS OAPI object.";
            return false;
        }
        catch (Exception ex)
        {
            Message = "Could not attach to the running ETABS instance: " + ex.Message;
            return false;
        }
    }

    public bool StartAndConnect()
    {
        EtabsObject = null;

        try
        {
            // CSI's documented ETABS v1 pattern for starting the installed ETABS version.
            cHelper helper = new Helper();
            EtabsObject = helper.CreateObjectProgID(EtabsObjectProgId);

            if (EtabsObject == null)
            {
                Message = "ETABS Helper could not create the ETABS OAPI object.";
                return false;
            }

            int rc = EtabsObject.ApplicationStart();

            if (rc == 0 && EtabsObject.SapModel != null)
            {
                Message = "ETABS started and connected through ETABSv1.Helper.CreateObjectProgID().";
                return true;
            }

            Message = $"ETABS object was created, but ApplicationStart returned {rc}.";
            return EtabsObject.SapModel != null;
        }
        catch (Exception ex)
        {
            Message = "Unable to start ETABS: " + ex.Message;
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
            message = "Could not set ETABS units: " + ex.Message;
            return false;
        }
    }

    public bool SetUnitsKnMmC() => SetUnitsKnMmC(out _);
}
