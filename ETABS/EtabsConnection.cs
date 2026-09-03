using System.Runtime.InteropServices;
namespace RevitEtabsValidator.ETABS;
public sealed class EtabsConnection
{
    // `dynamic` can't itself carry a `?` nullable annotation in C#, but this
    // genuinely starts out null (no COM object yet) until Connect/StartAndConnect
    // succeeds - null! and the null-forgiving return below document that honestly
    // instead of leaving the compiler's CS8618/CS8603 warnings unaddressed.
    // IsConnected below is what callers should actually check before use.
    public dynamic EtabsObject { get; private set; } = null!;
    public dynamic SapModel => EtabsObject?.SapModel!;
    public bool IsConnected => EtabsObject != null && SapModel != null;
    public string Message { get; private set; } = "";

    [DllImport("oleaut32.dll", PreserveSig=false)] private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object ppunk);

    public bool ConnectRunning()
    {
        try
        {
            var t=Type.GetTypeFromProgID("CSI.ETABS.API.ETABSObject",throwOnError:false);
            if(t==null){Message="ETABS COM ProgID was not found. Install ETABS and register its API.";return false;}
            var clsid=t.GUID; object obj; GetActiveObject(ref clsid,IntPtr.Zero,out obj); EtabsObject=obj; Message="Connected to running ETABS instance."; return true;
        }
        catch(Exception ex){Message="No running ETABS instance could be attached: "+ex.Message; return false;}
    }
    public bool StartAndConnect()
    {
        try
        {
            var t=Type.GetTypeFromProgID("CSI.ETABS.API.ETABSObject",true)!;
            EtabsObject=Activator.CreateInstance(t)!;
            int rc=0; try{rc=EtabsObject.ApplicationStart();}catch{}
            Message=rc==0?"ETABS started and connected.":$"ETABS object created; ApplicationStart returned {rc}."; return SapModel!=null;
        }
        catch(Exception ex){Message="Unable to start ETABS: "+ex.Message; return false;}
    }
    public bool SetUnitsKnMmC(){try{int rc=SapModel.SetPresentUnits(5); return rc==0;}catch{return false;}}
}
