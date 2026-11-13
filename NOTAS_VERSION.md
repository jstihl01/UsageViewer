# Usage Viewer 1.1.0

Actualización de estabilidad, seguridad y distribución para Windows 11 x64 (Intel/AMD; no ARM64).

- Recuperación automática de conexión, sondeo monotónico, control del servidor hijo y manejo de mensajes corruptos.
- Credenciales en el almacén seguro de Windows, con perfil aislado. Es necesario iniciar sesión de nuevo una vez.
- Recuperación del icono ante fallos de Explorer; primer inicio manual con panel visible.
- Registros locales acotados sin tokens, correos ni URLs de acceso; menú de diagnóstico y marca temporal opcional.
- .NET 10.0.15 incluido y servidor oficial de Codex 0.167.0 fijado y verificado.
- Se conserva el diseño aprobado: icono de 5 horas con borde gris, detalle de ambos ciclos y logo sin borde exterior.
- Aviso de nuevas versiones, sin instalación automática.

Se adjuntan instalador, SHA-256, inventario completo, SBOM, procedencia y atestaciones de GitHub, migración, matriz de las 53 observaciones y comprobaciones manuales.

**Los binarios propios siguen sin firma Authenticode**: la firma está preparada y aplazada hasta disponer de una identidad válida. El SHA-256 y la atestación no sustituyen esa firma. Los avisos oficiales de terceros están verificados; la auditoría exhaustiva de bibliotecas nativas internas de Codex queda como reserva documentada.

Las pruebas automáticas no sustituyen las comprobaciones manuales. Instalación, actualización y desinstalación reales, OAuth con una cuenta de ChatGPT, suspensión, Explorer, DPI y accesibilidad quedan pendientes de validación por el usuario. Consulta los documentos adjuntos.
