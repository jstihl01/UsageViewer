# Usage Viewer

[![Descargar el instalador para Windows](.github/assets/download-windows.svg)](https://github.com/jstihl01/UsageViewer/releases/latest/download/UsageViewer-Setup-x64.exe)

Windows 11 · 64 bits · [Versiones y archivo SHA-256](https://github.com/jstihl01/UsageViewer/releases/latest)

![Icono de Usage Viewer](src/Assets/UsageViewer.png)

Aplicación WPF para Windows 11 x64 que compara el consumo de Codex con el tiempo transcurrido en sus ventanas de 5 horas y 7 días.

Se inicia como un icono dinámico junto al reloj que muestra el ciclo de 5 horas. Un clic abre el detalle con ambos ciclos, sus porcentajes restantes y sus fechas de reinicio. Un clic fuera de la ventana o `Esc` vuelve a plegarlo.

El blanco indica consumo; el gris, el tramo restante; el verde, el margen respecto al tiempo transcurrido; y el rojo, el solapamiento cuando el consumo va por delante del tiempo. La consulta se actualiza cada 60 segundos y al recibir avisos del servidor, abrir el panel, reanudar Windows o llegar una hora de reinicio.

El inicio de sesión usa ChatGPT en el navegador predeterminado y el servidor oficial de Codex, con un perfil aislado de otras instalaciones de Codex. El instalador incluye el entorno de ejecución y permite elegir el inicio automático con Windows.

## Compilar

Requiere el SDK de .NET 8 y el compilador de Inno Setup 6.7.4. Descarga en `.build` estos archivos de la versión `rust-v0.167.0` de Codex:

| Archivo oficial | Ruta local |
| --- | --- |
| [Servidor para Windows x64](https://github.com/openai/codex/releases/download/rust-v0.167.0/codex-app-server-x86_64-pc-windows-msvc.exe) | `.build/codex-app-server.exe` |
| [Licencia](https://raw.githubusercontent.com/openai/codex/rust-v0.167.0/LICENSE) | `.build/Codex-LICENSE.txt` |
| [Avisos](https://raw.githubusercontent.com/openai/codex/rust-v0.167.0/NOTICE) | `.build/Codex-NOTICE.download` |

Ejecuta `./build.ps1 -Compiler 'C:/ruta/a/ISCC.exe'`. El script valida la huella del servidor, publica la aplicación con su entorno de ejecución, ejecuta las pruebas y genera el instalador y su huella en `dist`.

El logo vectorial está en `src/Assets/UsageViewer.svg`. `tools/Generate-AppLogo.ps1` genera los archivos PNG e ICO en ocho tamaños, con las uniones de los tercios dirigidas al centro y fondo transparente. La compilación ejecuta este generador automáticamente.

Las comprobaciones manuales están descritas en [COMPROBACIONES.md](COMPROBACIONES.md). Los archivos temporales de compilación, los instaladores y los perfiles de prueba no se incluyen en Git.
