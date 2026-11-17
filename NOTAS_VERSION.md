# Usage Viewer 1.1.2

- Corrige una causa de iconos duplicados tras suspender y reanudar Windows: mantiene una sola identificación durante la recuperación y retira el GUID residual de las versiones anteriores al iniciar.
- Corrige el texto blanco sobre blanco del menú contextual. El menú utiliza los colores del sistema y el panel conserva su texto claro.
- Al pasar el cursor sobre el icono aparece únicamente **UsageViewer**.

Se añaden pruebas que distinguen ambos registros del icono, simulan ciclos repetidos de recuperación y comprueban el texto generado por WPF con los recursos reales de la aplicación. Las pruebas reproducen ambos defectos con la implementación anterior.

Se conserva la sesión cifrada de 1.1.1 y el diseño aprobado. No cambia el almacenamiento de credenciales ni la consulta de cuotas.

Windows 11 x64 (Intel/AMD; no ARM64). Se adjuntan instalador, SHA-256, inventario, SBOM y atestaciones de procedencia. Los binarios propios siguen sin firma Authenticode.

La suspensión física y la revisión visual del menú quedan pendientes de validación por el usuario. No se ha controlado su escritorio para hacer pruebas manuales.
