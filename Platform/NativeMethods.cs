using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UsageViewer.Windows;

public static class NativeMethods
{
    public const uint CallbackMessage = 0x8001;
    [StructLayout(LayoutKind.Sequential)] public struct ScreenRect
    {
        public int Left, Top, Right, Bottom;
        public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo
    { public int Size; public ScreenRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] public struct IconIdentifier
    { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput
    { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    public delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct IconInfo
    { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint X, Y; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] public struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important;
    }
    [StructLayout(LayoutKind.Sequential)] public struct BasicLimit
    {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] public struct IoCounters
    { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] public struct ExtendedLimit
    {
        public BasicLimit Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] public struct TrustFile
    { public uint Size; public IntPtr Path, File, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)] public struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice;
        public IntPtr File; public uint StateAction; public IntPtr State, Url;
        public uint ProviderFlags, UiContext;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Credential
    {
        public uint Flags, Type; public IntPtr TargetName, Comment; public long LastWritten;
        public uint BlobSize; public IntPtr Blob; public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }

    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref ScreenRect rect, uint flags);
    [DllImport("user32.dll", EntryPoint="GetMonitorInfoW")] [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hwnd, out ScreenRect rect);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int metric,uint dpi);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id,MouseCallback callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool Shell_NotifyIcon(uint message,ref IconData data);
    [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier,out ScreenRect rect);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int width,int height,uint planes,uint bitCount,IntPtr bits);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetInformationJobObject(SafeFileHandle job,int type,ref ExtendedLimit info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern uint GetFinalPathNameByHandle(SafeFileHandle file,System.Text.StringBuilder path,uint length,uint flags);
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    public static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("wintrust.dll",ExactSpelling=true)] public static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
    [DllImport("advapi32.dll",EntryPoint="CredEnumerateW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] public static extern bool CredEnumerate(string? filter,uint flags,out uint count,out IntPtr credentials);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] public static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32.dll")] public static extern void CredFree(IntPtr buffer);
    [DllImport("advapi32.dll",EntryPoint="ConvertStringSecurityDescriptorToSecurityDescriptorW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] public static extern bool ConvertSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("advapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetKernelObjectSecurity(IntPtr handle,uint information,IntPtr descriptor);
    [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr memory);
}
