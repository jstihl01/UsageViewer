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
- Los diagnósticos del servidor se clasifican y descartan: no se guardan sus líneas originales. Las repeticiones de un evento o categoría se agrupan durante un minuto y se registra su contador.
- Los porcentajes y el correo mostrado permanecen en memoria durante la ejecución.

Los registros se abren desde el menú contextual del icono. No se envían automáticamente a nadie.

El servidor de Codex puede crear configuración, cachés, registros, bases de datos o repositorios auxiliares de skills/plugins dentro de su perfil aislado. No se promete que solo contenga el archivo de autenticación. Usage Viewer no inicia tareas, conversaciones ni herramientas de Codex; desde 1.1.3 desactiva analítica, exportadores OTEL, búsqueda de actualizaciones propia de Codex, plugins, prelectura de plugins, recomendaciones, apps y descubrimiento de modelos remotos mediante opciones del esquema fijado. La ausencia exhaustiva de descargas auxiliares y los destinos de red del binario externo requieren la observación MAN-32 en una VM.

## Red

Codex gestiona la autenticación y la consulta de cuotas con OpenAI. Usage Viewer también consulta la última versión pública de **jstihl01/UsageViewer** en GitHub, como máximo una vez cada 24 horas. No envía cuenta, correo, tokens ni porcentajes a GitHub. No instala actualizaciones automáticamente.

GitHub recibe tu dirección IP y el `User-Agent` con la versión exacta de Usage Viewer. Puedes desactivar esta consulta en **Buscar nuevas versiones**, en el menú del icono. Si una consulta falla, se reintenta tras una hora; tras éxito se esperan 24 horas. Desactivarla no interrumpe las cuotas.

## Desinstalación

El desinstalador cierra la aplicación y el servidor, retira exclusivamente la clave de cifrado y las credenciales antiguas de su perfil y elimina **%LocalAppData%\UsageViewer**. No modifica las sesiones ni los perfiles de otras instalaciones de Codex. Si no se puede completar la limpieza, comunica el error en lugar de darla por realizada.

Si falta el ejecutable o falla la limpieza, en modo interactivo puedes desinstalar de todos modos tras la advertencia. Pueden quedar claves en Windows. En modo silencioso se detiene, registra la causa y recomienda reinstalar antes de volver a desinstalar. No se muestra un cuadro de diálogo bloqueante en la vía silenciosa.

## Limpieza manual

Reinstala en la misma ubicación y vuelve a desinstalar para que la herramienta determine el identificador exacto de su perfil. Si necesitas hacerlo a mano, cierra Usage Viewer y su servidor. Revisa **Administrador de credenciales → Credenciales de Windows → Credenciales genéricas** o `cmdkey /list`, sin compartir su salida públicamente. Busca exclusivamente las entradas del perfil de Usage Viewer: servicio `codex`, cuenta `secrets|<hash>`; en la versión antigua, servicio `Codex Auth`, cuenta `cli|<hash>`.

El hash es los primeros 16 caracteres hexadecimales en minúsculas del SHA-256 de la ruta **canónica de Windows** del directorio `%LocalAppData%\UsageViewer\codex-profile`, codificada en UTF-8. La forma canónica puede incluir `\\?\`; no calcules el hash sobre una ruta abreviada o adivinada. No borres entradas de otros perfiles. Si no puedes identificarla con certeza, usa la reinstalación y limpieza automática. Tras retirar su clave puedes eliminar el directorio de datos de Usage Viewer. Borrar archivos no revoca tokens en OpenAI.

Un `auth.json` heredado de 1.0.15 permanece hasta confirmar el nuevo acceso protegido, según la decisión de migración acordada. El aviso lo indica; tras confirmarlo también se retira la entrada antigua `Codex Auth`. Si aparece un nuevo archivo en claro en un perfil que no lo tenía al arrancar, se registra y se muestra un error de almacenamiento seguro; no se borra silenciosamente para ocultar el fallo.

El almacén de Windows reduce el riesgo de copiar un archivo con tokens; no protege frente a un atacante que ya controla por completo tu sesión de Windows.
