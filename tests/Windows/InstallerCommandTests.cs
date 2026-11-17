using UsageViewer.Core;
using System.IO;
using UsageViewer.Windows;
using Xunit;

namespace UsageViewer.Windows.Tests;

public class InstallerCommandTests
{
    [Fact] public void MissingInstanceCanBeShutDownAndCleanupFailureHasADistinctCode()
    {
        string prefix=@"Local\UsageViewer-Test-"+Guid.NewGuid().ToString("N");
        Assert.Equal(0,InstallerCommands.Execute(new[]{"--shutdown"},prefix,()=>throw new Exception(),NullLog.Instance));
        Assert.Equal(3,InstallerCommands.Execute(new[]{"--uninstall-cleanup"},prefix,()=>throw new IOException(),NullLog.Instance));
        int cleanups=0;Assert.Equal(0,InstallerCommands.Execute(new[]{"--uninstall-cleanup"},prefix,()=>cleanups++,NullLog.Instance));Assert.Equal(1,cleanups);
    }
    [Fact] public async Task InstallerSignalsARealIsolatedInstanceAndWaitsForItsMutex()
    {
        string prefix=@"Local\UsageViewer-Test-"+Guid.NewGuid().ToString("N");
        using var ready=new ManualResetEventSlim();
        var owner=Task.Run(()=>
        {using var mutex=new Mutex(true,prefix);using var signal=new EventWaitHandle(false,EventResetMode.AutoReset,prefix+"-shutdown");ready.Set();Assert.True(signal.WaitOne(3000));mutex.ReleaseMutex();});
        ready.Wait();int code=InstallerCommands.Execute(new[]{"--shutdown"},prefix,()=>{},NullLog.Instance);
        await owner.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(0,code);
    }
    [Fact] public async Task InstanceThatDoesNotCloseReturnsCodeTwoWithoutCleaningCredentials()
    {
        string prefix=@"Local\UsageViewer-Test-"+Guid.NewGuid().ToString("N");using var ready=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var owner=Task.Run(()=>{using var mutex=new Mutex(true,prefix);ready.Set();release.Wait();mutex.ReleaseMutex();});ready.Wait();
        try {Assert.Equal(2,InstallerCommands.Execute(new[]{"--uninstall-cleanup"},prefix,()=>throw new Exception("must not clean"),NullLog.Instance,TimeSpan.FromMilliseconds(10)));}
        finally {release.Set();await owner;}
    }
}
