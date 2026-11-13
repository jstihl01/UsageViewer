using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using UsageViewer.Windows;
using Xunit;

namespace UsageViewer.Windows.Tests;

public class ShellTests
{
    [Fact] public void NamedHandlesAreRestrictedAndRemainUsableByTheOwner()
    {
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();
        string name=@"Local\UsageViewer-Test-"+Guid.NewGuid().ToString("N");
        using var mutex=new Mutex(false,name);
        using var signal=new EventWaitHandle(false,EventResetMode.AutoReset,name+"-event");
        WaitHandleSecurity.Restrict(mutex,identity.User!.Value);
        WaitHandleSecurity.Restrict(signal,identity.User.Value);
        using var reopened=EventWaitHandle.OpenExisting(name+"-event");
        reopened.Set();Assert.True(signal.WaitOne(1000));
        Assert.True(mutex.WaitOne(1000));mutex.ReleaseMutex();
    }
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-11-13T12:00:00Z");
    [Theory] [InlineData(16)] [InlineData(20)] [InlineData(24)] [InlineData(32)] [InlineData(48)]
    public void ActualIconPixelsRemainVisibleAtAllDpiSizes(int size)=>Sta(()=>
    {
        foreach(var cycle in new[]{new Cycle(.65,300,Now.AddHours(3)),new Cycle(.25,300,Now.AddHours(2))})
        {
            var bitmap=TrayRenderer.Draw(new(cycle,new(.9,10080,Now.AddDays(5))),Now,size);
            var bytes=Pixels(bitmap);
            Assert.Equal(0,bytes[3]);
            int center=((size/2)*size+size/2)*4;
            Assert.Equal(new byte[]{37,37,37,255},bytes[center..(center+4)]);
            bool white=false,gray=false,accent=false;
            for(int i=0;i<bytes.Length;i+=4)
            {
                white|=bytes[i]>230&&bytes[i+1]>230&&bytes[i+2]>230;
                gray|=bytes[i]==133&&bytes[i+1]==133&&bytes[i+2]==133;
                accent|=cycle.Used>.4?bytes[i+2]>180&&bytes[i+2]>bytes[i+1]*1.5:
                    bytes[i+1]>130&&bytes[i+1]>bytes[i+2]*1.5;
            }
            Assert.True(white&&gray&&accent);
            using var native=TrayRenderer.ToIcon(bitmap);
            Assert.NotEqual(IntPtr.Zero,native.Handle);
        }
    });
    [Fact] public void WeeklyQuotaCannotChangeTheSingleRing()=>Sta(()=>
    {
        var shortCycle=new Cycle(.4,300,Now.AddHours(3));
        Assert.Equal(Pixels(TrayRenderer.Draw(new(shortCycle,null),Now,32)),
            Pixels(TrayRenderer.Draw(new(shortCycle,new(.95,10080,Now.AddDays(1))),Now,32)));
    });
    [Fact] public void MarkerIsOptionalAndMissingOrExpiredQuotaStaysNeutral()=>Sta(()=>
    {
        var limits=new Limits(new(.3,300,Now.AddHours(3)),null);
        Assert.NotEqual(Pixels(TrayRenderer.Draw(limits,Now,32,false)),Pixels(TrayRenderer.Draw(limits,Now,32,true)));
        Assert.Equal(Pixels(TrayRenderer.Draw(Limits.Empty,Now,32)),
            Pixels(TrayRenderer.Draw(new(new(.5,300,Now),null),Now,32)));
    });
    [Fact] public void BorderContrastUsesRenderedPixelsInsteadOfCopiedConstants()=>Sta(()=>
    {
        var image=TrayRenderer.Draw(Limits.Empty,Now,128);
        var bytes=Pixels(image);
        int border=(4*128+64)*4;
        double luminance=Linear(bytes[border]);
        Assert.True((luminance+.05)/(Linear(32)+.05)>=3);
        int center=(64*128+64)*4;
        Assert.True((Linear(243)+.05)/(Linear(bytes[center])+.05)>=3);
    });
    [Fact] public void NativeNotificationStructuresMatchWindowsLayout()
    {
        Assert.Equal(IntPtr.Size==8?976:956,Marshal.SizeOf<NativeMethods.IconData>());
        Assert.Equal(IntPtr.Size==8?40:28,Marshal.SizeOf<NativeMethods.IconIdentifier>());
    }
    [Fact] public void TransientModifyFailureRecoversWithoutRemovingIcon()
    {
        var shell=new FakeShell();using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance);
        Assert.True(registration.Update(new(1),"first",true));
        shell.FailModifyOnce=true;
        Assert.True(registration.Update(new(2),"second",true));
        Assert.True(registration.Registered);Assert.True(registration.UsesGuid);
        Assert.DoesNotContain(2u,shell.Operations);
    }
    [Fact] public void GuidFailureFallsBackToWindowIdentityAndUsesSameIdentityForBounds()
    {
        var shell=new FakeShell { RejectGuid=true };
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance);
        Assert.True(registration.Update(new(1),"first",true));
        Assert.False(registration.UsesGuid);
        registration.TryGetBounds(out _);
        Assert.Equal(Guid.Empty,shell.LastBounds.Guid);
    }
    [Fact] public void FailedVersionNegotiationRetriesAndExplorerRestartReregisters()
    {
        var time=new FakeTimeProvider();var shell=new FakeShell { RejectVersion=true };
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance,time);
        registration.Update(new(1),"first",true);Assert.False(registration.Version4);
        shell.RejectVersion=false;time.Advance(TimeSpan.FromSeconds(5));
        registration.Update(new(1),"first",false);Assert.True(registration.Version4);
        shell.Exists=false;registration.ExplorerRestarted();
        Assert.True(registration.Update(new(1),"first",false));
    }
    [Fact] public void RepeatedRegistrationFailureHasBackoff()
    {
        var shell=new FakeShell { FailAll=true };var time=new FakeTimeProvider();
        using var registration=new TrayRegistration(shell,new(1),Guid.NewGuid(),NullLog.Instance,time);
        registration.Update(new(1),"",true);int before=shell.Operations.Count;
        registration.Update(new(1),"",true);Assert.Equal(before,shell.Operations.Count);
        time.Advance(TimeSpan.FromSeconds(5));registration.Update(new(1),"",true);
        Assert.True(shell.Operations.Count>before);
    }
    [Theory] [InlineData("en-US")] [InlineData("es-ES")] [InlineData("tr-TR")]
    public void UserTextIsConsistentlySpanish(string culture)
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=new(culture);
            Assert.Equal("35 %",UsageText.Remaining(new(.65,300,Now.AddHours(1)),Now));
            Assert.Equal("Reinicio no disponible",UsageText.Reset(new(.65,300,null),Now));
            Assert.Equal("Esperando actualización",UsageText.Reset(new(.65,300,Now),Now));
            Assert.Equal("",UsageText.PaceLabel(Pace.Even));
        }
        finally { CultureInfo.CurrentCulture=previous; }
    }
    [Fact] public void ClickOnOwnIconClosesOnceAndNextClickReopens()
    {
        int hooks=0,unhooks=0;
        var visibility=new PanelVisibility(()=>{},()=>{},()=> {hooks++;return new Cleanup(()=>unhooks++);});
        visibility.Open();Assert.Equal(1,hooks);
        visibility.Press(false,true,true);Assert.True(visibility.Visible);
        visibility.Toggle();Assert.False(visibility.Visible);Assert.Equal(1,unhooks);
        visibility.Toggle();Assert.True(visibility.Visible);Assert.Equal(2,hooks);
        visibility.Press(true,false,true);Assert.True(visibility.Visible);
        visibility.Press(false,false,true);Assert.False(visibility.Visible);Assert.Equal(2,unhooks);
    }
    private sealed class Cleanup(Action action):IDisposable {public void Dispose()=>action();}
    [Theory]
    [InlineData(false,false,true)] [InlineData(false,true,false)]
    [InlineData(true,false,false)] [InlineData(true,true,false)]
    public void OnlyFirstManualLaunchShowsPanel(bool completed,bool background,bool expected)=>
        Assert.Equal(expected,StartupPolicy.ShowPanel(completed,background?new[]{"--background"}:Array.Empty<string>()));
    private sealed class FakeShell:ITrayShell
    {
        public bool Exists,FailModifyOnce,RejectGuid,RejectVersion,FailAll;
        public List<uint> Operations=new();public NativeMethods.IconIdentifier LastBounds;
        public bool Notify(uint op,ref NativeMethods.IconData data)
        {
            Operations.Add(op);
            if(FailAll||RejectGuid&&data.Guid!=Guid.Empty)return false;
            if(op==0) {if(Exists)return false;Exists=true;return true;}
            if(op==1) {if(FailModifyOnce){FailModifyOnce=false;return false;}return Exists;}
            if(op==2){Exists=false;return true;}
            return !RejectVersion;
        }
        public bool Bounds(ref NativeMethods.IconIdentifier id,out NativeMethods.ScreenRect bounds)
        { LastBounds=id;bounds=default;return true; }
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);return bytes;}
    private static double Linear(byte value)
    {double v=value/255.0;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
    private static void Sta(Action action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{action();}catch(Exception error){failure=error;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
