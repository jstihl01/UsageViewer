# Datos y privacidad

Usage Viewer consulta la cuenta y las cuotas mediante el servidor oficial de Codex. No inicia conversaciones ni realiza tareas de Codex.

## Sesión

La versión 1.1.1 usa el backend oficial de autenticación cifrada de Codex 0.167.0, con **keyring obligatorio** y **secret_auth_storage activado**. No vuelve silenciosamente al archivo en claro. El perfil está aislado en **%LocalAppData%\UsageViewer\codex-profile**.

Los tokens se cifran con age en **secrets\codex_auth.age**. Una clave aleatoria de 32 bytes, codificada en 44 caracteres, se guarda en el Administrador de credenciales de Windows; su identificador se deriva de la ruta canónica del perfil. Así se evita el límite de 2.560 caracteres que impedía guardar algunos tokens de ChatGPT en 1.1.0. La clave no se guarda junto al archivo cifrado.

Al actualizar desde 1.0.15 o 1.1.0 puede ser necesario iniciar sesión una vez más. El archivo antiguo **auth.json** no se importa ni se lee para extraer tokens: solo se retira después de confirmar el guardado protegido. No compartas ni adjuntes ese archivo.

Cerrar sesión elimina el registro de autenticación del archivo cifrado. Puede conservar una clave local reutilizable y un archivo cifrado vacío, sin credenciales de la cuenta. Esto no equivale a revocar todas las sesiones de ChatGPT: puedes gestionarlas desde la configuración de tu cuenta.

## Otros datos locales

- settings.json: primer inicio, preferencia de marca de tiempo y caché de comprobación de actualizaciones. No contiene credenciales ni correo.
- logs: hasta tres archivos de 1 MB. Contienen versión, fecha, eventos, métodos conocidos, tipos de error y códigos. No contienen cuerpos RPC, tokens, correo, URLs de autenticación ni trazas con rutas personales.
- Los diagnósticos del servidor se clasifican y descartan: no se guardan sus líneas originales. Las últimas 50 clasificaciones se mantienen en memoria.
- Los porcentajes y el correo mostrado permanecen en memoria durante la ejecución.

Los registros se abren desde el menú contextual del icono. No se envían automáticamente a nadie.

## Red

Codex gestiona la autenticación y la consulta de cuotas con OpenAI. Usage Viewer también consulta la última versión pública de **jstihl01/UsageViewer** en GitHub, como máximo una vez cada 24 horas. No envía cuenta, correo, tokens ni porcentajes a GitHub. No instala actualizaciones automáticamente.

## Desinstalación

El desinstalador cierra la aplicación y el servidor, retira exclusivamente la clave de cifrado y las credenciales antiguas de su perfil y elimina **%LocalAppData%\UsageViewer**. No modifica las sesiones ni los perfiles de otras instalaciones de Codex. Si no se puede completar la limpieza, comunica el error en lugar de darla por realizada.

El almacén de Windows reduce el riesgo de copiar un archivo con tokens; no protege frente a un atacante que ya controla por completo tu sesión de Windows.
