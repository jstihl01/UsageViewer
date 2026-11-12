using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace UsageViewer;

public partial class Panel : Window
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public Panel()
    {
        InitializeComponent();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Hide(); args.Handled = true; }
        };
    }

    internal event Action? SessionAction;

    internal void Update(Limits limits, DateTimeOffset now, bool signedIn, bool signingIn, bool busy,
        string? email, string message)
    {
        Rings.Update(limits, now);
        SetCycle(WeeklyCard, WeeklyTitle, WeeklyDetail, WeeklyPace, "7 días", limits.Weekly, now);
        SetCycle(ShortCard, ShortTitle, ShortDetail, ShortPace, "5 horas", limits.Short, now);
        Status.Text = message.Length == 0 && limits.Stale ? "Datos desactualizados. Reintentando…" : message;
        Status.Visibility = Status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Identity.Text = email ?? "Cuenta de ChatGPT";
        Identity.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SessionButton.Content = signedIn ? "Cerrar sesión" : signingIn ? "Cancelar inicio de sesión" : "Iniciar sesión";
        SessionButton.IsEnabled = !busy;
    }

    private static void SetCycle(StackPanel card, TextBlock title, TextBlock detail, TextBlock paceText, string label,
        Cycle? cycle, DateTimeOffset now)
    {
        bool expired = cycle is not null && cycle.Reset <= now;
        title.Text = cycle is null || expired ? label :
            $"{label}  ·  {(1 - cycle.Used).ToString("P0", Spanish)} restante";
        detail.Text = cycle is null ? "Sin datos" : expired ? "Esperando actualización" :
            $"Reinicio: {cycle.Reset.ToLocalTime().ToString("dd MMM yyyy · HH:mm", Spanish)}";
        var pace = Ring.GetPace(cycle, now);
        paceText.Text = Ring.PaceLabel(pace) ?? "";
        paceText.Visibility = paceText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        paceText.Foreground = Ring.PaceInk(pace) is { } ink ? RingsView.PenFor(ink).Brush : detail.Foreground;
        AutomationProperties.SetName(card, $"{title.Text}. {detail.Text}. {paceText.Text}".TrimEnd());
    }

    private void ChangeSession(object sender, RoutedEventArgs args) => SessionAction?.Invoke();
}
