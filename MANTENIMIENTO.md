# Mantenimiento y publicación

Responsable: **jstihl01**. Versión de esta revisión: **1.1.3**.

## Componentes

- Cada semana: revisar las publicaciones y avisos de seguridad de Codex. Cualquier cambio exige digest oficial, firma del proveedor, revisión de `CodexContract`, esquema de cuenta/configuración y pruebas de integración.
- Cada mes, tras los parches de Microsoft: actualizar juntos SDK, runtime, `global.json`, `dependencies.json`, `RuntimeFrameworkVersion`, avisos y bloqueos. No publicar con un parche anterior al vigente de la rama elegida.
- Ante un aviso de seguridad aplicable: priorizar el parche. Producción siempre bloquea vulnerabilidades. Una excepción solo de pruebas debe incluir `package`, `project`, `advisory`, `reason` y `expires` UTC en `dependency-audit-exceptions.json`, con revisión explícita y caducidad; ninguna está activa. No se ocultan avisos ni fallos de restauración.
- Dependabot revisa SDK, proyectos NuGet y acciones. Weekly dependency review compara cada lunes SDK/runtime/Codex/Inno y audita NuGet; mantiene una incidencia si requiere revisión, sin actualizar pines automáticamente.
- Planificar xUnit v3 como trabajo separado en la próxima revisión de pruebas: actualizar runner/atributos y conservar el inventario y cero omitidas. xUnit v2 solo se usa en pruebas y no se distribuye.

Las herramientas se descargan a `.partial`, verifican y mueven. Codex 0.167.0 está firmado por **OpenAI OpCo, LLC**; Inno 6.7.4 por **Pyrsys B.V.**. Se comprueban también los stubs de Inno. La caché del SDK conserva el hash del ZIP autenticado y se contrastan el host y componentes ejecutados contra ese ZIP. Los runtimes llevan SHA-512 y firma NuGet obligatoria; el contenido publicado se coteja contra sus paquetes. Al rotar certificados de nuget.org hay que actualizar las huellas desde su endpoint oficial de RepositorySignatures, sin desactivar la verificación.

El backend de sesión exige **keyring** y **secret_auth_storage**. Revisar `login/src/auth/storage.rs` y `secrets/src/{lib,local}.rs` al cambiar Codex. La prueba sintética de 16.000 caracteres usa **apiKey**: no acredita OAuth de ChatGPT, callback ni renovación. El fallo provocado bloquea el archivo cifrado, no el Administrador de credenciales. Deben completarse MAN-04, MAN-05 y MAN-29 con una cuenta real antes de promover a estable.

Las pruebas de bandeja deben distinguir GUID y HWND. No restablecer el GUID tras establecer una identidad: puede reintroducir duplicados. Para el menú, cargar recursos al nivel real de Application; un diccionario local no reproduce los estilos implícitos de las plantillas WPF.

Los avisos .NET proceden de los paquetes exactos y de los commits WPF/WinForms fijados. Codex incluye LICENSE/NOTICE de su etiqueta. La completitud de avisos de todas las dependencias Rust sigue sin constituir una auditoría exhaustiva.

## Preparar y verificar una entrega

1. Trabajar en un clon Git limpio. Actualizar la versión central y las cabeceras de notas, migración, comprobaciones y matrices.
2. Ejecutar `tools/Test-Lockfiles.ps1 -Update` si cambian versiones/proyectos; revisar el diff. CI regenera, compara y restaura con `--locked-mode`.
3. Ejecutar `build.ps1`, las once mutaciones y el escáner de secretos. TRX: mínimo por suite, cero omitidas/fallos y límite de cuelgue de dos minutos. El humo del instalador solo puede correr con `GITHUB_ACTIONS=true` en un runner efímero, nunca sobre la instalación del usuario.
4. Revisar ambas matrices y las comprobaciones manuales. Los resultados pendientes no se convierten en superados por tener CI verde.
5. Lanzar **Windows release**. El trabajo de compilación solo tiene lectura; el trabajo de publicación descarga su artefacto, verifica origen/huella, atesta y prepara el borrador. `environment: release` permite configurar revisores obligatorios desde GitHub; no se afirma que esa protección externa esté configurada.
6. Por defecto se publica como **preliminar** y no cambia `latest`. El ZIP de evidencia, matrices, instalador, SHA, inventario y SBOM se adjuntan y verifican. El ZIP de pruebas queda también bajo atestación; los artefactos temporales de Actions caducan.
7. Para promover a estable, completar la validación manual y ejecutar el flujo con `stable=true`. El publicador bloquea pendientes; una excepción consciente exigiría `-AcceptPendingManualChecks` explícito, que el workflow ordinario no pasa. No usar esta excepción para presentar OAuth como probado.

`Publish-Release.ps1` tiene tres modos:

- `-Mode Prepare`: sube o reanuda el borrador y verifica todos los activos.
- `-Mode Publish`: verifica el borrador y lo publica como preliminar. `-Stable` solicita la promoción con la guarda manual anterior.
- `-Mode Verify`: comprueba tamaños, SHA-256, etiqueta y commit de una entrega descargada en `dist`; no exige HEAD igual al del artefacto histórico, pero sí versión y procedencia coherentes.

Prepare/Publish solo aceptan un artefacto limpio del mismo flujo oficial de Actions. Verify usa `GITHUB_TOKEN` o la sesión de Git guardada para GitHub; no imprime el token. Para verificación pública criptográfica:

    gh attestation verify .\UsageViewer-Setup-x64.exe --repo jstihl01/UsageViewer --signer-workflow jstihl01/UsageViewer/.github/workflows/release.yml
    gh attestation verify .\UsageViewer-pruebas.zip --repo jstihl01/UsageViewer --signer-workflow jstihl01/UsageViewer/.github/workflows/release.yml

Comparar commit y etiqueta con la publicación. `Get-FileHash` verifica integridad, no acredita por sí solo la identidad del editor.

## Limpieza de versiones anteriores

Los inventarios 1.1.0, 1.1.1 y 1.1.2 están en `installer/legacy`; 1.0.15 conserva el inventario histórico separado. Para cada nueva versión estable, ejecutar `tools/Import-LegacyManifest.ps1 -Version X.Y.Z`: descarga el inventario público y verifica su digest antes de añadirlo. `New-LegacyCleanup.ps1` genera el borrado de sobrantes de todos los inventarios. Solo retira archivos cuyo hash coincida; nunca borra por nombre o directorio completo ni toca archivos ajenos. El inventario antiguo no demuestra por sí solo ejecución de una actualización desde esa versión.

## Retirar una versión

Ante una regresión, añadir a sus notas una advertencia y marcarla preliminar, sin reemplazar sus archivos inmutables:

    gh release edit vX.Y.Z --repo jstihl01/UsageViewer --prerelease
    gh release edit vPREVIA --repo jstihl01/UsageViewer --latest

Verificar el endpoint `/releases/latest`, la descarga del botón y la atestación de la estable elegida. Si hay que retirar un archivo dañino, guardar primero su hash/evidencia y documentar la retirada; no sustituirlo silenciosamente. Publicar la corrección con otra versión.

El aviso de actualización ignora preliminares y versiones anteriores. Para volver atrás el usuario descarga la versión elegida e instala después de cerrar la app. Volver desde 1.1.3 a 1.1.1/1.1.2 conserva el formato cifrado; **no recomendar 1.1.0**, cuyo backend no admite algunos tokens largos. Bajar a 1.0.15 pierde compatibilidad con el formato protegido y exige un nuevo acceso; no exportar tokens a claro para conservar sesión.

## Firma y límites

DEP-01 permanece **aplazada por decisión expresa**. La firma de la ruta oficial de Actions **no está implementada** hasta elegir una identidad válida y un runner/servicio apropiado. `Sign-Release.ps1` es un auxiliar local que requiere un certificado y SignTool ya instalados; no genera ni exporta claves privadas. No se han contratado servicios ni creado certificados.

SHA-256 y atestaciones no sustituyen Authenticode ni garantizan reputación de SmartScreen. Proteger cuenta, ramas, etiquetas y el entorno release desde GitHub es una tarea del propietario; no se da por realizada.

Las comprobaciones manuales se delegan al usuario. El humo de CI no acredita el inicio/renovación real de ChatGPT, suspensión, DPI, contraste humano ni el comportamiento completo en Windows 11.
