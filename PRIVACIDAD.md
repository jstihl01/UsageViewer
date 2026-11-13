# Datos y privacidad

Usage Viewer consulta la cuenta y las cuotas mediante el servidor oficial de Codex. No inicia conversaciones ni realiza tareas de Codex.

## Sesión

La versión 1.1.0 usa el almacén de credenciales de Windows mediante el modo obligatorio **keyring** de Codex. No vuelve silenciosamente al archivo en claro. El perfil está aislado en **%LocalAppData%\UsageViewer\codex-profile**. La clave del almacén se deriva de la ruta canónica de ese perfil, como establece el código de Codex 0.167.0.

Al actualizar desde 1.0.15 será necesario iniciar sesión una vez más. El archivo antiguo **auth.json** no se importa ni se lee para extraer tokens: solo se retira después de confirmar la sesión protegida. No compartas ni adjuntes ese archivo.

Cerrar sesión elimina las credenciales locales de este perfil. Esto no equivale a revocar todas las sesiones de ChatGPT: puedes gestionarlas desde la configuración de tu cuenta.

## Otros datos locales

- settings.json: primer inicio, preferencia de marca de tiempo y caché de comprobación de actualizaciones. No contiene credenciales ni correo.
- logs: hasta tres archivos de 1 MB. Contienen versión, fecha, eventos, métodos conocidos, tipos de error y códigos. No contienen cuerpos RPC, tokens, correo, URLs de autenticación ni trazas con rutas personales.
- Los diagnósticos del servidor se clasifican y descartan: no se guardan sus líneas originales. Las últimas 50 clasificaciones se mantienen en memoria.
- Los porcentajes y el correo mostrado permanecen en memoria durante la ejecución.

Los registros se abren desde el menú contextual del icono. No se envían automáticamente a nadie.

## Red

Codex gestiona la autenticación y la consulta de cuotas con OpenAI. Usage Viewer también consulta la última versión pública de **jstihl01/UsageViewer** en GitHub, como máximo una vez cada 24 horas. No envía cuenta, correo, tokens ni porcentajes a GitHub. No instala actualizaciones automáticamente.

## Desinstalación

El desinstalador cierra la aplicación y el servidor, retira exclusivamente las credenciales de su perfil y elimina **%LocalAppData%\UsageViewer**. No modifica las sesiones ni los perfiles de otras instalaciones de Codex. Si no se puede completar la limpieza, comunica el error en lugar de darla por realizada.

El almacén de Windows reduce el riesgo de copiar un archivo con tokens; no protege frente a un atacante que ya controla por completo tu sesión de Windows.
