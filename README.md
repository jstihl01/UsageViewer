# Usage Viewer

[![Descargar el instalador para Windows](.github/assets/download-windows.svg)](https://github.com/jstihl01/UsageViewer/releases/latest/download/UsageViewer-Setup-x64.exe)

Windows 11 · **x64 (Intel/AMD; no ARM64)** · [Versiones, SHA-256 y componentes](https://github.com/jstihl01/UsageViewer/releases/latest)

![Icono de Usage Viewer](src/Assets/UsageViewer.png)

Consulta de un vistazo si tu consumo de Codex avanza más rápido o más despacio que el tiempo de sus ciclos.

El icono junto al reloj muestra la sesión de **5 horas**. Al pulsarlo se abre el detalle de **5 horas y 7 días**, con porcentajes restantes y fechas locales de reinicio. Otro clic en el icono, un clic fuera o Esc pliega el detalle sin retirar el icono.

El blanco representa consumo, el gris el tramo restante, el verde el margen y el rojo el solapamiento. Con ritmo equivalente no aparece texto «por delante» ni «por detrás». Los porcentajes del servidor pueden estar redondeados: el indicador no puede recuperar precisión que el servicio no facilite.

## Instalar y verificar

1. Descarga **UsageViewer-Setup-x64.exe** desde el botón superior.
2. Puedes comprobarlo con el archivo SHA-256 de esa misma versión:

       Get-FileHash .\UsageViewer-Setup-x64.exe -Algorithm SHA256

3. Ejecuta el instalador. Instala para tu usuario, incluye .NET y Codex y permite elegir el inicio automático con Windows. No necesitas instalar .NET por separado.
4. En la primera apertura manual aparece el panel para iniciar sesión con ChatGPT en tu navegador. Los siguientes arranques quedan como icono.

**Firma:** los artefactos propios todavía no están firmados con Authenticode. Windows puede mostrar advertencias de editor desconocido. El SHA-256 detecta corrupción de la descarga, pero no acredita por sí solo la identidad del editor. La procedencia de compilación publicada en GitHub es un control separado. No es necesario desactivar el antivirus ni añadir exclusiones generales para usar la aplicación.

Windows puede colocar un icono nuevo bajo la flecha de iconos ocultos. Puedes hacerlo visible desde las opciones de la bandeja de la barra de tareas. Ctrl+Alt+U abre el panel; si otra aplicación ocupa el atajo, Usage Viewer lo indica.

## Uso y recuperación

La consulta normal se realiza cada 60 segundos. Abrir el panel, reanudar Windows, recibir cambios o llegar un reinicio puede adelantarla. **Actualizar ahora**, en el menú del icono, permite solicitar una lectura.

Ante un fallo se conserva la última lectura con un aviso. Los fallos iniciales se reintentan con esperas de 5, 15, 45, 135 y 300 segundos. Los fallos repetidos de consulta usan esperas de 1, 2, 5 y 15 minutos. Tras un reinicio pendiente se consulta cada 5 segundos durante un minuto y después cada 60 segundos.

Una cuota con porcentaje pero sin fecha de reinicio conserva su cifra y muestra «Reinicio no disponible», sin inventar un ritmo. Las ventanas no compatibles y las respuestas irreconocibles tienen mensajes distintos.

El menú contextual permite actualizar, activar una marca de tiempo accesible, abrir los registros y salir. El tooltip añade el ritmo en texto; la marca accesible está desactivada por defecto.

## Actualizar y desinstalar

La aplicación avisa si hay una versión nueva mediante una consulta pública a GitHub, como máximo cada 24 horas. El aviso abre la página oficial; no descarga ni ejecuta instaladores automáticamente.

Ejecuta el nuevo instalador sobre la instalación existente. Conserva la ubicación y la elección de inicio con Windows. Si la app estaba funcionando, la actualización la cierra y puede reabrirla como icono. Una actualización silenciosa la reabre solo si estaba en marcha.

**Al pasar de 1.0.15 a 1.1.0 debes iniciar sesión una vez más.** Las credenciales pasan del archivo antiguo al almacén de Windows mediante un nuevo inicio de sesión; no se copian tokens en claro. El archivo antiguo solo se retira tras confirmar la sesión protegida.

Para desinstalar usa Configuración → Aplicaciones instaladas. El desinstalador cierra la app y su servidor y limpia sus datos y credenciales. Puedes cerrar sesión antes desde el panel. La limpieza local no revoca todas tus sesiones de ChatGPT: gestiona esa revocación desde la configuración de tu cuenta.

## Datos, privacidad y diagnóstico

Los datos de Usage Viewer están en **%LocalAppData%\UsageViewer**. El perfil de Codex está aislado de otras instalaciones y utiliza el almacén seguro de Windows sin recurrir silenciosamente al archivo en claro.

Los registros locales rotan en tres archivos de hasta 1 MB. No guardan tokens, correo, cuerpos JSON ni URLs de autenticación. Usa **Abrir carpeta de registros** en el menú del icono para localizarlos; no adjuntes auth.json ni otras credenciales. [Detalles de privacidad](PRIVACIDAD.md).

Si falta el servidor, comprueba si Windows lo ha puesto en cuarentena y reinstala desde la publicación oficial. Si falla la conexión, usa **Reintentar conexión**. Si la cuenta ha caducado, vuelve a iniciar sesión. Un fallo del comprobador de versiones no interrumpe la consulta de cuotas.

## Compilar y comprobar

Requiere Windows 11 x64 y PowerShell. Ejecuta:

    ./build.ps1

El script prepara en la caché local el SDK .NET **10.0.404**, los runtimes **10.0.15**, Inno Setup **6.7.4** y Codex **0.167.0**. Las URL, hashes y proveedores se fijan en el manifiesto de dependencias. La restauración NuGet usa únicamente nuget.org y archivos de bloqueo.

La compilación ejecuta xUnit (núcleo, Windows e integración con el binario oficial), audita NuGet, publica en una carpeta nueva, verifica el inventario y genera en **dist** el instalador, SHA-256, inventario y SBOM CycloneDX. Las pruebas de credenciales utilizan perfiles temporales y valores ficticios, nunca tu cuenta.

La publicación oficial se realiza desde **Windows release** en GitHub Actions: crea las atestaciones de procedencia y componentes, sube un borrador, comprueba todos sus archivos por tamaño y SHA-256 y solo entonces lo publica como versión más reciente. Los binarios propios permanecen sin firma Authenticode mientras no haya una identidad de firma.

Para comprobar las tres regresiones señaladas por el informe:

    ./tools/Test-Mutations.ps1

El generador del logo es una herramienta de autoría independiente; el build no reescribe los recursos versionados. La firma opcional se activa con el parámetro **-Sign** y una identidad externa ya configurada. No genera ni exporta certificados privados.

[Comprobaciones manuales](COMPROBACIONES.md) · [Matriz del informe](AUDITORIA.md) · [Mantenimiento y publicación](MANTENIMIENTO.md)

## Licencia

El código y los recursos propios se distribuyen bajo [MIT](LICENSE), © 2026 jstihl01. Codex, .NET y sus componentes conservan sus licencias y avisos, incluidos en el instalador. El inventario describe los componentes distribuidos; no constituye una auditoría exhaustiva del código de todos los componentes nativos de terceros.
