using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using UsageViewer.Windows;
using Xunit;

namespace UsageViewer.Windows.Tests;

public class TrayRecoveryTests
{
    [Fact] public void TransientGuidFailuresNeverCreateASecondWindowIdentity()
    {
        var time=new FakeTimeProvider();var shell=new IdentityShell();
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance,time);
        Assert.True(registration.Update(new(1),"UsageViewer",true));
        shell.GuidFailures=3; // MODIFY, ADD and recovery MODIFY fail while Explorer retains the icon.
        registration.Update(new(2),"UsageViewer",true);
        Assert.Single(shell.Icons);
        Assert.True(registration.UsesGuid);
        Assert.Equal(1,shell.MaximumIcons);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(registration.Update(new(2),"UsageViewer",true));
        Assert.Single(shell.Icons);
    }
    [Fact] public void RepeatedResumeAndTaskbarNotificationsKeepTheEstablishedIdentity()
    {
        var time=new FakeTimeProvider();var shell=new IdentityShell();
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance,time);
        registration.Update(new(1),"UsageViewer",true);
        for(int i=0;i<20;i++)
        {
            registration.RecheckAfterResume();shell.GuidFailures=3;
            registration.Update(new(1),"UsageViewer",false);
            registration.ExplorerRestarted(); // It can arrive while Explorer is still recovering.
            time.Advance(TimeSpan.FromSeconds(5));
            registration.Update(new(1),"UsageViewer",false);
            Assert.True(registration.UsesGuid);Assert.Single(shell.Icons);
        }
        Assert.Equal(1,shell.MaximumIcons);
    }
    [Fact] public void ResumeProbesAnUnchangedIconAndRestoresItIfExplorerLostIt()
    {
        var shell=new IdentityShell();
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance);
        registration.Update(new(1),"UsageViewer",true);
        shell.Icons.Clear();
        registration.RecheckAfterResume();
        Assert.True(registration.Update(new(1),"UsageViewer",false));
        Assert.Single(shell.Icons);Assert.True(registration.Version4);
    }
    [Fact] public void StartupRetiresAGuidGhostLeftByThePreviousVersion()
    {
        var shell=new IdentityShell();var guid=Guid.NewGuid();
        shell.Icons.Add(guid.ToString());
        using var registration=new TrayRegistration(shell,new(9),guid,NullLog.Instance);
        Assert.True(registration.Update(new(1),"UsageViewer",true));
        Assert.Single(shell.Icons);Assert.Equal(1,shell.MaximumIcons);
    }
    [Fact] public void InitialFallbackCannotAddUntilTheGuidCandidateHasBeenRetired()
    {
        var shell=new IdentityShell {GuidFailures=3};
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance);
        Assert.True(registration.Update(new(1),"UsageViewer",true));
        Assert.False(registration.UsesGuid);
        Assert.Single(shell.Icons);
        Assert.Equal(1,shell.MaximumIcons);
    }
    private sealed class IdentityShell:ITrayShell
    {
        public readonly HashSet<string> Icons=new();
        public int GuidFailures,MaximumIcons;
        private static string Key(IntPtr window,uint id,Guid guid)=>guid==Guid.Empty?$"{window}:{id}":guid.ToString();
        public bool Notify(uint operation,ref NativeMethods.IconData data)
        {
            if(data.Guid!=Guid.Empty&&GuidFailures>0){GuidFailures--;return false;}
            string key=Key(data.Window,data.Id,data.Guid);
            bool result=operation switch
            {
                0=>Icons.Add(key),
                2=>Icons.Remove(key),
                _=>Icons.Contains(key)
            };
            MaximumIcons=Math.Max(MaximumIcons,Icons.Count);
            return result;
        }
        public bool Bounds(ref NativeMethods.IconIdentifier id,out NativeMethods.ScreenRect bounds)
        {bounds=default;return Icons.Contains(Key(id.Window,id.Id,id.Guid));}
    }
}
