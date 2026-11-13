# Migración a Usage Viewer 1.1.1

- Windows 11 x64 (Intel/AMD); ARM64 no está soportado.
- Se conserva el diseño del icono de 5 horas, su borde gris, el detalle con ambos ciclos y el logo aprobado.
- Si apareció **persist_failed** al conectar en 1.1.0, instala 1.1.1 y vuelve a iniciar sesión. También puede ser necesario repetir el acceso si la sesión anterior sí se guardó en 1.1.0.
- El backend oficial cifra los tokens en **codex-profile\secrets\codex_auth.age** y guarda solo su clave pequeña en Windows. Así admite credenciales largas sin recurrir a un archivo en claro. No se copia el token antiguo al nuevo formato.
- El archivo antiguo de credenciales solo se elimina después de confirmar una cuenta de ChatGPT en el almacén protegido. Si la eliminación falla se muestra un aviso.
- El primer inicio manual muestra el panel para facilitar el acceso. Los siguientes inicios y el arranque automático de Windows permanecen como icono.
- La aplicación abierta se cierra antes de actualizar. Una actualización silenciosa la vuelve a abrir en segundo plano si estaba funcionando; en una instalación interactiva se ofrece abrirla al finalizar.
- El menú incorpora actualización de datos, marca temporal opcional y acceso a registros. El aviso de nueva versión abre la página oficial; nunca instala por su cuenta.
- Esta entrega está **sin firma Authenticode propia**. No se ha contratado un certificado. Verifica el SHA-256 y la procedencia publicados junto al instalador.

La reinstalación, la autenticación con una cuenta real y el resto de comprobaciones de escritorio están pendientes de validación por el usuario en COMPROBACIONES.md. El cambio de almacenamiento no modifica las credenciales de otras instalaciones de Codex.
