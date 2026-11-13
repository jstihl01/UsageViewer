# Usage Viewer 1.1.1

Corrige **persist_failed** al iniciar sesión con ChatGPT en Windows.

En 1.1.0, algunos tokens OAuth superaban el límite de 2.560 caracteres admitido por una entrada del Administrador de credenciales de Windows. El acceso en el navegador terminaba, pero no podía guardarse la sesión.

Esta versión usa el backend oficial cifrado del mismo servidor Codex 0.167.0: guarda los tokens cifrados con age en el perfil privado y conserva solo una clave aleatoria pequeña en Windows. No vuelve silenciosamente a un archivo con tokens en claro.

- Prueba automática con credenciales sintéticas de 16.000 caracteres, persistencia tras reiniciar y aislamiento entre perfiles.
- Comprobación del cierre de sesión y de que un fallo de guardado no crea auth.json.
- Limpieza al desinstalar de la clave de cifrado y de las credenciales antiguas, exclusivamente para Usage Viewer.
- Explicación del fallo de almacenamiento en la interfaz y diagnóstico seguro.
- Se conserva el diseño aprobado y las demás correcciones de 1.1.0.

**Instala 1.1.1 y vuelve a iniciar sesión.** Puede ser necesario repetir el acceso incluso si 1.1.0 sí había guardado una sesión corta.

Se adjuntan instalador, SHA-256, inventario, SBOM y atestaciones de procedencia. Windows 11 x64 (Intel/AMD; no ARM64). Los binarios propios siguen sin firma Authenticode, como se documentó en 1.1.0.

La prueba con una cuenta real de ChatGPT y las demás comprobaciones manuales permanecen pendientes del usuario. No se ha controlado su escritorio.
