using System.ComponentModel;

namespace UsageViewer.Core;

public sealed class ApplicationSafety(IEventLog log, Action close, Action notify)
{
    public void Run(Action action, string name = "ui_action_failed")
    {
        try { action(); }
        catch (Exception error) { Handle(error, name, knownBoundary: true); }
    }
    public void Unhandled(Exception error) => Handle(error, "dispatcher_unhandled", knownBoundary: false);
    private void Handle(Exception error, string name, bool knownBoundary)
    {
        log.Write(name, error: error);
        bool fatal = error is OutOfMemoryException or AccessViolationException;
        if (fatal || !knownBoundary && error is not (IOException or Win32Exception or UnauthorizedAccessException)) close();
        else notify();
    }
}
