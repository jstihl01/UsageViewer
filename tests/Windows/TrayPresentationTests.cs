using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UsageViewer.Core;
using UsageViewer.Windows;
using Xunit;

namespace UsageViewer.Windows.Tests;

public class TrayPresentationTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeMenuTextHasContrastWithTheActualApplicationResources(bool dark)=>Sta(()=>
    {
        var menu=TrayMenu.Create();
        var background=new SolidColorBrush(dark?Color.FromRgb(32,32,32):Color.FromRgb(248,248,248));
        var foreground=new SolidColorBrush(dark?Color.FromRgb(245,245,245):Color.FromRgb(25,25,25));
        menu.Resources[SystemColors.MenuBrushKey]=background;
        menu.Resources[SystemColors.MenuTextBrushKey]=foreground;
        var item=new MenuItem {Header="Abrir Usage Viewer"};
        menu.Items.Add(item);
        item.Measure(new Size(280,50));item.Arrange(new Rect(0,0,280,50));item.UpdateLayout();
        var labels=Descendants(item).OfType<TextBlock>().Where(x=>x.Text=="Abrir Usage Viewer").ToArray();
        Assert.NotEmpty(labels);
        foreach(var label in labels)
        {
            var brush=Assert.IsType<SolidColorBrush>(label.Foreground);
            var actualBackground=Assert.IsType<SolidColorBrush>(menu.Background);
            double text=Luminance(brush.Color),surface=Luminance(actualBackground.Color);
            Assert.True((Math.Max(text,surface)+.05)/(Math.Min(text,surface)+.05)>=4.5,
                "The generated menu text must remain legible in the selected menu palette.");
        }
    });
    [Fact] public void TooltipContainsOnlyTheRequestedNameForEveryQuotaState()=>Sta(()=>
    {
        var shell=new CapturingShell();
        using var tray=new TrayIndicator(new(1),()=>{},()=>{},()=>{},()=>{},()=>{},_=>{},()=>{},false,NullLog.Instance,shell);
        var now=DateTimeOffset.Parse("2026-11-17T12:00:00Z");
        foreach(var limits in new[]{Limits.Empty,
            new Limits(new(.4,300,now.AddHours(2)),new(.7,10080,now.AddDays(2))),
            new Limits(new(.4,300,null),null,true),
            new Limits(new(.9,300,now.AddSeconds(-1)),null)})
        {
            tray.Update(limits,now);
            Assert.Equal("UsageViewer",shell.Tip);
        }
    });
    private sealed class CapturingShell:ITrayShell
    {
        private bool exists;
        public string Tip="";
        public bool Notify(uint operation,ref NativeMethods.IconData data)
        {
            if(operation==2){exists=false;return true;}
            if(operation==0){exists=true;Tip=data.Tip;return true;}
            if(operation==1){Tip=data.Tip;return exists;}
            return true;
        }
        public bool Bounds(ref NativeMethods.IconIdentifier id,out NativeMethods.ScreenRect bounds)
        {bounds=default;return exists;}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)
        {
            var child=VisualTreeHelper.GetChild(element,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte b){double c=b/255.0;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
        return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
    }
    private static readonly Lazy<Dispatcher> Ui=new(()=>
    {
        var ready=new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try
            {
                // Application-level implicit styles also reach text generated inside control templates.
                // A local resource dictionary alone would miss the white-on-white regression.
                var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var resources=(ResourceDictionary)Application.LoadComponent(
                    new Uri("/UsageViewer;component/Theme.xaml",UriKind.Relative));
                foreach(var value in resources.Values)
                {
                    if(value is Freezable freezable && freezable.CanFreeze)freezable.Freeze();
                    if(value is Style style)style.Seal();
                }
                application.Resources.MergedDictionaries.Add(resources);
                ready.TrySetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch(Exception error){ready.TrySetException(error);}
        }){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });
    private static void Sta(Action action)=>Ui.Value.Invoke(action);
}
