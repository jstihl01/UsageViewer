# Usage Viewer 1.1.3

Versión preliminar con las correcciones del informe del 17/11/2026. El diseño aprobado y la sesión cifrada de 1.1.1/1.1.2 se conservan.

- Un error de una petición ya no cierra toda la conexión. Se recuperan los intentos de login cancelados y se conserva el aviso específico de cuotas inválidas.
- Una ventana sin duración no oculta la otra cuota válida. Los refrescos solicitados durante otra lectura no se pierden.
- El icono conserva su identidad al recuperar Explorer; un fallo antes de estar lista la barra no activa inmediatamente el respaldo.
- Menú con opción para desactivar la consulta de versiones. Fallos de consulta reintentan en una hora.
- Registros con códigos nativos y repeticiones agrupadas. Las acciones recuperables de la interfaz no cierran la app.
- Desinstalación con salida interactiva si falta el ejecutable o falla la limpieza, e instrucciones en el log para la vía silenciosa.
- Runtimes autenticados por firma y SHA-512, inventarios de versiones anteriores y SBOM con identificadores de componentes.
- Instalador probado automáticamente en runner efímero; publicación separada de la compilación, con entrega preliminar por defecto y evidencia duradera.

**Límites de las pruebas:** la integración de almacenamiento usa una **apiKey sintética de 16.000 caracteres**, no un flujo OAuth real de ChatGPT. El inicio de sesión con una cuenta real, la renovación de sus tokens y las comprobaciones de Windows 11 siguen pendientes del usuario en COMPROBACIONES.md. Los resultados de prueba no se presentan como sustituto de esos casos.

La matriz nueva está en UsageViewer-REVISION.md; la anterior, en UsageViewer-AUDITORIA.md. El ZIP de pruebas incluye TRX y diagnóstico de CI. Los binarios propios permanecen **sin Authenticode**; la firma oficial sigue aplazada hasta disponer de identidad válida. Verifica el SHA-256 y las atestaciones de GitHub antes de instalar.
