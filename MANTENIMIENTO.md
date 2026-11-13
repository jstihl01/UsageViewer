# Mantenimiento y publicación

Responsable: **jstihl01**. Versión de esta revisión: **1.1.0**.

## Dependencias

- Cada semana: revisar las publicaciones y avisos de seguridad de Codex. Mantener inicialmente 0.167.0; cualquier cambio de binario exige nuevo digest oficial, firma del proveedor, revisión del protocolo y pruebas de integración.
- Cada mes, tras los parches de Microsoft: revisar .NET 10 LTS y actualizar juntos SDK, runtime y bloqueos. No publicar una versión nueva con un parche anterior al vigente de la rama elegida.
- Ante un aviso de seguridad aplicable: priorizar actualización, compilación y nueva publicación.
- Inno Setup y las herramientas de CI se fijan por versión y hash. Las acciones se fijan por SHA de commit.

El digest de Codex procede de los metadatos de la publicación **rust-v0.167.0** de OpenAI; el binario validado está firmado por **OpenAI OpCo, LLC**. El compilador fijado está firmado por **Pyrsys B.V.**. Su campo FileVersion no es una fuente fiable de versión; la comprobación usa su huella y procedencia.

Los avisos de .NET se extraen de los paquetes runtime exactos. WindowsDesktop no aporta un archivo separado de avisos en este paquete: se añaden los oficiales de las revisiones WPF y WinForms correspondientes a 10.0.15, con URL y huella fijadas. Se conservan los avisos realmente suministrados y su inventario. Codex incluye LICENSE y NOTICE de la etiqueta fijada; la comprobación no acredita una auditoría exhaustiva de todas las dependencias Rust enlazadas.

## Publicar

1. Actualizar la única versión central y los pines necesarios.
2. Ejecutar compilación, xUnit, integración con perfiles ficticios, auditoría NuGet, análisis de secretos y las tres mutaciones de regresión.
3. Revisar la matriz de observaciones y los resultados manuales disponibles.
4. Crear un borrador con el instalador, SHA-256, inventario, SBOM, procedencia y notas de migración.
5. Confirmar tamaños y hashes de los archivos recibidos por GitHub. Solo entonces publicar como última versión estable.
6. Comprobar la descarga del botón del README. Su nombre de archivo se mantiene estable entre versiones.

El build usa una carpeta nueva. El instalador solo retira archivos sobrantes de 1.0.15 si coinciden con las huellas del inventario de esa versión, sin borrar archivos ajenos de la carpeta de instalación.

## Firma digital

DEP-01 permanece **aplazada por decisión expresa**. No se contrata ni registra ningún servicio.

La preparación de firma acepta una identidad existente mediante la huella pública de su certificado y SignTool. Firma los binarios propios y permite que Inno Setup firme instalador y desinstalador. Las claves privadas no se crean, exportan ni guardan en el repositorio. Un build sin identidad permanece explícitamente sin firma.

El SHA-256 comprueba integridad. La atestación de GitHub relaciona el artefacto con su compilación y código. Ninguno sustituye por sí solo Authenticode ni garantiza la reputación de SmartScreen.

El mantenedor debe proteger su cuenta con doble factor y restringir las etiquetas/publicaciones desde GitHub. Son operaciones del propietario de la cuenta y no se dan por realizadas en esta revisión.

## Segunda revisión y reservas

Revisar cada fila de AUDITORIA.md contra su prueba o evidencia. No cerrar por inspección estática los escenarios de escritorio, la autenticación con una cuenta real ni la completitud de los avisos de dependencias nativas de terceros.

Las comprobaciones manuales se delegan íntegramente al usuario; no se instala ni desinstala en una VM o escritorio real para simular que ya han sido validadas.
