# Migración a Usage Viewer 1.1.3

- Windows 11 x64 (Intel/AMD); ARM64 no está soportado.
- Si ya usabas 1.1.1 o 1.1.2, esta actualización conserva su sesión cifrada. Mejora conexión, cuotas, diagnóstico y desinstalación. Mantiene el icono, el menú corregido y el tooltip UsageViewer.
- Se conserva el diseño del icono de 5 horas, su borde gris, el detalle con ambos ciclos y el logo aprobado.
- Si apareció **persist_failed** al conectar en 1.1.0, instala 1.1.1 y vuelve a iniciar sesión. También puede ser necesario repetir el acceso si la sesión anterior sí se guardó en 1.1.0.
- El backend oficial cifra los tokens en **codex-profile\secrets\codex_auth.age** y guarda solo su clave pequeña en Windows. Así admite credenciales largas sin recurrir a un archivo en claro. No se copia el token antiguo al nuevo formato.
- El archivo antiguo de credenciales solo se elimina después de confirmar una cuenta de ChatGPT en el almacén protegido. Si la eliminación falla se muestra un aviso.
- El primer inicio manual muestra el panel para facilitar el acceso. Los siguientes inicios y el arranque automático de Windows permanecen como icono.
- La aplicación abierta se cierra antes de actualizar. Una actualización silenciosa la vuelve a abrir en segundo plano si estaba funcionando; en una instalación interactiva se ofrece abrirla al finalizar.
- El menú incorpora actualización de datos, marca temporal opcional, acceso a registros y la opción de desactivar la consulta de nuevas versiones. Un fallo de esta consulta reintenta en una hora; tras éxito se esperan 24 horas. Nunca instala por su cuenta.
- Esta entrega está **sin firma Authenticode propia**. No se ha contratado un certificado. Verifica el SHA-256 y la procedencia publicados junto al instalador.

La reinstalación, la autenticación con una cuenta real y el resto de comprobaciones de escritorio están pendientes de validación por el usuario en COMPROBACIONES.md. El cambio de almacenamiento no modifica las credenciales de otras instalaciones de Codex.

Esta versión se distribuye inicialmente como **preliminar**, sin sustituir el botón de descarga estable, mientras sigan pendientes el acceso y la renovación reales. La desinstalación interactiva permite continuar si no puede completar la limpieza; el aviso y el registro explican cómo retirar los restos. La desinstalación silenciosa sigue fallando con un error visible en el log.
