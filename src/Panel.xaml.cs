using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using UsageViewer.Core;

namespace UsageViewer;

public partial class Panel : Window
{
    private Action? update;
    public Panel()
    {
        InitializeComponent();
        PreviewKeyDown+=(_,args)=>
        { if(args.Key==Key.Escape) { HideRequested?.Invoke(); args.Handled=true; } };
    }
    internal event Action? SessionAction;
    internal event Action? HideRequested;
    internal void Update(ViewState view,DateTimeOffset now,string shellNotice)
    {
        Rings.Update(view.Limits,now);
        SetCycle(ShortCard,ShortTitle,ShortDetail,ShortPace,"5 horas",view.Limits.Short,now);
        SetCycle(WeeklyCard,WeeklyTitle,WeeklyDetail,WeeklyPace,"7 días",view.Limits.Weekly,now);
        string message=UsageText.Notice(view);
        if(shellNotice.Length>0) message+=(message.Length>0?"\n":"")+shellNotice;
        Status.Text=message;
        Status.Visibility=message.Length>0?Visibility.Visible:Visibility.Collapsed;
        Identity.Text=view.Email??"Cuenta de ChatGPT";
        Identity.Visibility=view.Session is SessionState.Connected or SessionState.SigningOut?Visibility.Visible:Visibility.Collapsed;
        SessionButton.Content=view.Session switch
        {
            SessionState.Connected=>"Cerrar sesión",
            SessionState.SigningIn=>"Cancelar inicio de sesión",
            SessionState.Disconnected=>"Reintentar conexión",
            SessionState.Connecting=>"Conectando…",
            SessionState.SigningOut=>"Cerrando sesión…",
            _=>"Iniciar sesión"
        };
        SessionButton.IsEnabled=view.Session is not (SessionState.Connecting or SessionState.SigningOut or SessionState.Stopping);
    }
    internal void SetUpdate(string? version,Action open)
    {
        update=open;
        UpdateButton.Content="Nueva versión "+version+" · Descargar";
        UpdateButton.Visibility=version is null?Visibility.Collapsed:Visibility.Visible;
    }
    private static void SetCycle(StackPanel card,TextBlock title,TextBlock detail,TextBlock paceText,
        string label,Cycle? cycle,DateTimeOffset now)
    {
        title.Text=cycle is null||cycle.Expired(now)?label:$"{label}  ·  {UsageText.Remaining(cycle,now)} restante";
        detail.Text=UsageText.Reset(cycle,now);
        var pace=Ring.GetPace(cycle,now);
        paceText.Text=UsageText.PaceLabel(pace);
        paceText.Visibility=paceText.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
        paceText.Foreground=pace switch
        {
            Pace.Ahead=>RingsView.PenFor(SegmentKind.Overlap).Brush,
            Pace.Behind=>RingsView.PenFor(SegmentKind.Margin).Brush,
            _=>detail.Foreground
        };
        AutomationProperties.SetName(card,$"{title.Text}. {detail.Text}. {paceText.Text}".TrimEnd());
    }
    private void ChangeSession(object sender,RoutedEventArgs args)=>SessionAction?.Invoke();
    private void OpenUpdate(object sender,RoutedEventArgs args)=>update?.Invoke();
}
