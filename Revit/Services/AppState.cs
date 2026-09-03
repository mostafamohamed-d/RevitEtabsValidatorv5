using Autodesk.Revit.UI;
namespace RevitEtabsValidator.Revit.UI;
public static class AppState
{
    public static MainWindow? Window {get;private set;}
    public static void Open(UIApplication uiapp)
    {
        if(Window==null){Window=new MainWindow(uiapp); Window.Closed+=(s,e)=>Window=null; Window.Show(); Window.Activate();}
        else {Window.Activate();}
    }
}
