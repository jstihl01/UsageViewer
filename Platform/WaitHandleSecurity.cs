using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UsageViewer.Windows;

public static class WaitHandleSecurity
{
    public static void Restrict(WaitHandle handle,string sid)
    {
        if(!NativeMethods.ConvertSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;"+sid+")",1,out var descriptor,out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if(!NativeMethods.SetKernelObjectSecurity(handle.SafeWaitHandle.DangerousGetHandle(),4,descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally{NativeMethods.LocalFree(descriptor);}
    }
}
