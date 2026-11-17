using UsageViewer.Core;

namespace UsageViewer.Windows;

public static class InstallerCommands
{
    public const int CloseFailed = 2, CleanupFailed = 3;
    public static bool IsCommand(IEnumerable<string> args) => args.Contains("--shutdown") || args.Contains("--uninstall-cleanup");
    public static int Execute(IEnumerable<string> args,string prefix,Action cleanup,IEventLog log,TimeSpan? timeout=null)
    {
        try
        {
            try { using var signal = EventWaitHandle.OpenExisting(prefix+"-shutdown"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            try
            {
                using var running = Mutex.OpenExisting(prefix);
                try { if (!running.WaitOne(timeout ?? TimeSpan.FromSeconds(12))) return CloseFailed; }
                catch (AbandonedMutexException) { }
                running.ReleaseMutex();
            }
            catch (WaitHandleCannotBeOpenedException) { }
        }
        catch (Exception error) { log.Write("installer_shutdown_failed", error: error); return CloseFailed; }
        if (!args.Contains("--uninstall-cleanup")) return 0;
        try { cleanup(); log.Write("uninstall_credentials_removed"); return 0; }
        catch (Exception error) { log.Write("uninstall_cleanup_failed", error: error); return CleanupFailed; }
    }
}
