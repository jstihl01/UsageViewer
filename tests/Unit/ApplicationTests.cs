using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

public class ApplicationTests
{
    private sealed class Log:IEventLog
    {public List<string> Events=new();public void Write(string name,string? method=null,Exception? error=null,int? code=null)=>Events.Add(name);}
    [Fact] public void LocalActionFailuresAreLoggedAndFatalErrorsCloseCleanly()
    {
        var log=new Log();int closed=0,notified=0;var safety=new ApplicationSafety(log,()=>closed++,()=>notified++);
        safety.Run(()=>throw new Win32Exception(5));Assert.Equal(0,closed);Assert.Equal(1,notified);
        safety.Unhandled(new IOException());Assert.Equal(0,closed);
        safety.Unhandled(new InvalidOperationException());Assert.Equal(1,closed);
        safety.Run(()=>throw new OutOfMemoryException());Assert.Equal(2,closed);Assert.Equal(4,log.Events.Count);
    }
    [Theory] [InlineData("")] [InlineData("{")] [InlineData("null")] [InlineData("huge")]
    public void CorruptAndOversizedSettingsUseSafeDefaults(string value)
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {File.WriteAllText(Path.Combine(root,"settings.json"),value=="huge"?new string('x',65537):value);Assert.Equal(new(),new SettingsStore(root,NullLog.Instance).Load());}
        finally {Directory.Delete(root,true);}
    }
    [Fact] public void SettingsRoundtripAndWriteFailureKeepTheLastValidFile()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var log=new Log();var store=new SettingsStore(root,log);var expected=new AppSettings(true,true,CheckForUpdates:false);
            store.Save(expected);Assert.Equal(expected,store.Load());
            using(var locked=new FileStream(Path.Combine(root,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.None))store.Save(new());
            Assert.Contains("settings_write_failed",log.Events);Assert.Equal(expected,store.Load());
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact] public void RepeatedEventsAreSummarizedAndNativeErrorCodesAreRetained()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));var time=new FakeTimeProvider();
        try
        {
            var log=new EventLog(root,"test",time:time);for(int i=0;i<100;i++)log.Write("native_failure",error:new Win32Exception(5));
            Assert.Single(File.ReadAllLines(Path.Combine(root,"usage-viewer.log")));
            time.Advance(TimeSpan.FromMinutes(1));log.Write("native_failure",error:new Win32Exception(5));
            var lines=File.ReadAllLines(Path.Combine(root,"usage-viewer.log"));Assert.Equal(3,lines.Length);
            using var first=JsonDocument.Parse(lines[0]);Assert.Equal(5,first.RootElement.GetProperty("code").GetInt32());
            using var summary=JsonDocument.Parse(lines[1]);Assert.Equal(99,summary.RootElement.GetProperty("count").GetInt32());
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
