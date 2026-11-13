# Componentes y avisos redistribuidos

El inventario de cada entrega enumera todos los archivos empaquetados y sus SHA-256. El SBOM identifica aplicación, runtimes y paquetes administrados. El registro de procedencia fija herramientas, URLs y huellas. Ninguno pretende enumerar por inspección binaria todas las bibliotecas Rust o nativas enlazadas dentro del ejecutable de un tercero.

| Componente | Versión fijada | Avisos incluidos |
| --- | --- | --- |
| Usage Viewer | Directory.Build.props | LICENSE.txt, MIT, jstihl01 |
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.15 | LICENSE y THIRD-PARTY-NOTICES del paquete NuGet efectivamente publicado |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 10.0.15 | LICENSE del paquete NuGet y THIRD-PARTY-NOTICES de WPF y WinForms de las revisiones oficiales de 10.0.15, con huellas fijadas |
| OpenAI Codex app-server x64 | 0.167.0 | LICENSE Apache-2.0 y NOTICE de su etiqueta oficial, descargados con huella fijada |
| Código derivado de Ratatui mencionado por Codex | Referencia del NOTICE de Codex | NOTICE de Codex y texto completo MIT del proyecto Ratatui v0.29.0 |

Los avisos de Microsoft describen las licencias específicas de sus componentes; no se presupone que todos sean MIT. El copyright de Ratatui en el NOTICE de Codex se conserva, aunque la copia de referencia de la licencia MIT tenga años anteriores.

Inno Setup y el SDK son herramientas de construcción verificadas, no runtimes instalados como carpetas independientes. El motor del instalador incluye sus propios avisos del proveedor. Las dependencias exclusivas de pruebas y Gitleaks se usan en construcción y no se distribuyen con la aplicación.

**Reserva DEP-08:** el fabricante de Codex no aporta en este artefacto un inventario completo de las dependencias nativas enlazadas con sus correspondientes licencias. Se incluyen sus avisos oficiales y el MIT citado, con procedencia comprobada; una auditoría exhaustiva de ese binario sigue siendo una limitación conocida. No se declara resuelta por completo.
