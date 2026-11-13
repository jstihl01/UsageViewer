using System.Globalization;
using UsageViewer.Core;

namespace UsageViewer;

internal static class UsageText
{
    private static readonly CultureInfo Spanish=CultureInfo.GetCultureInfo("es-ES");
    internal static string Remaining(Cycle? cycle,DateTimeOffset now) =>
        cycle is null || cycle.Expired(now) ? "—" : (1-cycle.Used).ToString("P0",Spanish);
    internal static string Reset(Cycle? cycle,DateTimeOffset now) => cycle is null ? "Sin datos" :
        cycle.Expired(now) ? "Esperando actualización" : cycle.Reset is not { } reset ?
        "Reinicio no disponible" : "Reinicio: "+reset.ToLocalTime().ToString("dd MMM yyyy · HH:mm",Spanish);
    internal static string PaceLabel(Pace? pace) => pace switch
    { Pace.Ahead=>"Por delante del tiempo",Pace.Behind=>"Por detrás del tiempo",_=>"" };
    internal static string Notice(ViewState view) => view.Notice switch
    {
        SessionNotice.Connecting=>"Conectando con Codex…",
        SessionNotice.Disconnected=>"No se pudo conectar con Codex. Se reintentará automáticamente.",
        SessionNotice.ServerMissing=>"Falta el servidor de Codex. Revisa la cuarentena del antivirus o reinstala Usage Viewer.",
        SessionNotice.ServerIntegrity=>"El servidor de Codex no supera la comprobación de integridad. Reinstala Usage Viewer.",
        SessionNotice.CredentialStoreUnavailable=>"No se pudo guardar o leer tu sesión de forma segura en Windows. Consulta los registros.",
        SessionNotice.SignIn=>"Inicia sesión con ChatGPT para ver tu uso de Codex.",
        SessionNotice.Migration=>"Para proteger tu sesión, inicia sesión una vez más. Se guardará en el almacén seguro de Windows.",
        SessionNotice.LoginPending=>"Completa el inicio de sesión en tu navegador.",
        SessionNotice.LoginFailed=>"No se pudo completar la operación. Vuelve a intentarlo.",
        SessionNotice.BrowserFailed=>"No se pudo abrir el inicio de sesión en el navegador. Vuelve a intentarlo.",
        SessionNotice.AuthenticationRequired=>"Tu sesión ya no es válida. Inicia sesión de nuevo.",
        SessionNotice.InvalidQuotas=>"Codex no devolvió cuotas reconocibles. Se conserva la última lectura.",
        SessionNotice.NoMatchingWindows=>"Codex no ofrece límites de 5 horas o 7 días para esta cuenta.",
        SessionNotice.CleanupFailed=>"La sesión está protegida, pero no se pudo retirar el archivo antiguo. Consulta los registros.",
        SessionNotice.Stale=>view.LastSuccess is {} last?
            "Datos desactualizados. Última lectura: "+last.ToLocalTime().ToString("HH:mm",Spanish)+". Reintentando…":
            "No se pudieron leer las cuotas. Reintentando…",
        SessionNotice.UnexpectedError=>"Se produjo un error inesperado. Consulta los registros.",
        _=>""
    };
}
