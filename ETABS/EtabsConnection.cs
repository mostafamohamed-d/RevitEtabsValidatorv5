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

        // Preferred CSI mechanism: ETABSv1.Helper.GetObject(...).
        // CSI documents Helper.GetObject as the supported way to attach to a
        // running ETABS OAPI object. This avoids depending on the exact COM
        // interface returned by a raw ROT lookup.
        try
        {
            var helperType = Type.GetTypeFromProgID(HelperProgId, throwOnError: false);
            if (helperType != null)
            {
                var helper = Activator.CreateInstance(helperType);
                if (helper != null)
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
                        Message = "Connected to the running ETABS instance through ETABSv1.Helper.";
                        return IsConnected;
                    }
                }
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            Message = "ETABS Helper could not attach to the running instance: " + ex.InnerException.Message;
        }
        catch (Exception ex)
        {
            Message = "ETABS Helper connection failed: " + ex.Message;
        }

        // Fallback for installations where the Helper ProgID is not exposed.
        try
        {
            var objectType = Type.GetTypeFromProgID(EtabsObjectProgId, throwOnError: false);
            if (objectType == null)
            {
                Message = "ETABS COM ProgID was not found: " + EtabsObjectProgId;
                return false;
            }

            var clsid = objectType.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            EtabsObject = obj;

            if (IsConnected)
            {
                Message = "Connected to the running ETABS instance through the COM running-object table.";
                return true;
            }

            Message = "ETABS object was found, but SapModel could not be obtained.";
            return false;
        }
        catch (Exception ex)
        {
            Message = "No running ETABS instance could be attached. " + ex.Message;
            return false;
        }
    }

    public bool StartAndConnect()
    {
        EtabsObject = null;

        // Preferred CSI mechanism for creating/starting ETABS.
        try
        {
            var helperType = Type.GetTypeFromProgID(HelperProgId, throwOnError: false);
            if (helperType != null)
            {
                var helper = Activator.CreateInstance(helperType);
                if (helper != null)
                {
                    var created = TryInvoke(helperType, helper, "CreateObjectProgID", EtabsObjectProgId);
                    if (created != null)
                    {
                        EtabsObject = created;
                        var rc = InvokeInt(EtabsObject, "ApplicationStart");
                        if (rc == 0)
                        {
                            Message = "ETABS started and connected through ETABSv1.Helper.";
                            return IsConnected;
                        }

                        Message = $"ETABS object was created, but ApplicationStart returned {rc}.";
                        return IsConnected;
                    }
                }
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            Message = "ETABS Helper could not start ETABS: " + ex.InnerException.Message;
        }
        catch (Exception ex)
        {
            Message = "ETABS Helper start failed: " + ex.Message;
        }

        // Final fallback: activate the COM class directly.
        try
        {
            var t = Type.GetTypeFromProgID(EtabsObjectProgId, throwOnError: true)!;
            EtabsObject = Activator.CreateInstance(t);
            if (EtabsObject == null)
            {
                Message = "ETABS COM object could not be created.";
                return false;
            }

            var rc = InvokeInt(EtabsObject, "ApplicationStart");
            Message = rc == 0
                ? "ETABS started and connected through COM."
                : $"ETABS object was created; ApplicationStart returned {rc}.";
            return IsConnected;
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

    private static object? TryInvoke(Type targetType, object target, string method, params object[] args)
    {
        return targetType.InvokeMember(
            method,
            BindingFlags.InvokeMethod,
            null,
            target,
            args);
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
