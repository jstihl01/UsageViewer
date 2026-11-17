# Matriz de revisión · Usage Viewer 1.1.3

Esta es la matriz de las **53 observaciones del primer informe**. El segundo informe del 17/11/2026 y sus **51 observaciones nuevas** se resumen, con identidad del documento y límites, en [REVISION_2026-11-17.md](REVISION_2026-11-17.md). Ambas revisiones se contrastan con el código actual, sin dar por superadas comprobaciones que requieren una cuenta real.

**C** = corregida y probada automáticamente. **M** = mitigada, con la reserva indicada. **P** = pendiente de validación manual del escenario completo. **D** = documentada, sin equivaler a prueba automática. **A** = aplazada por decisión expresa.

Las pruebas emplean referencias entre proyectos, xUnit, datos simulados y TimeProvider; las de Windows renderizan fuera de pantalla. La integración utiliza perfiles temporales con credenciales sintéticas, nunca la cuenta del usuario. Los TRX de los artefactos temporales de Actions caducan a los 14/30 días; desde 1.1.3 se adjuntan comprimidos y atestados a la publicación. Los casos manuales continúan pendientes.

| ID | Solución contrastada | Prueba o evidencia | Estado / reserva |
| --- | --- | --- | --- |
| DEP-02 | SDK 10.0.404, runtimes 10.0.15; aviso público de versión y mantenimiento | global.json, versiones de runtime comprobadas al empaquetar, UpdateTests, MANTENIMIENTO.md | C; no actualiza runtimes por su cuenta |
| FIA-01 | Acciones y render protegidos; política global explícita y cierre de errores desconocidos/fatales | LocalActionFailuresAreLoggedAndFatalErrorsCloseCleanly; ApplicationSafety usada en App y menú | P; política probada, fallos reales de escritorio pendientes |
| OPS-01 | Registros 1 MiB × 3, métodos y códigos, stderr categorizado y acotado | LogsRotateAndNeverPersistUntrustedText, UnwritableLogsNeverThrow, OversizedDiagnosticsAreDiscardedWithoutStoppingTheDrain | C; no se conserva stderr literal |
| COR-02 | Recuperación inicial 5/15/45/135/300 segundos, diferenciada de inicio de sesión | StartupFailuresRecoverWithoutAskingUserToLogIn, TimeProvider simulado | C |
| COR-05 | Parser explícito; una respuesta inválida mantiene la última cuota desactualizada | MalformedPayloadIsNotSuccessfulEmptyData, InvalidQuotaPreservesLastGoodReadingAndExplainsFailure | C |
| FIA-02 | Plazo RPC y descarte del proceso antes de reconectar | DeadlineClosesTransportAndFailsPendingRequests, ConcurrentReconnectAndLogoutNeverStartTwoChildren; RequestAsync descarta el hijo en TimeoutException | C |
| FIA-06 | Recuperación de NIM_MODIFY conservando la identidad elegida; comprobación explícita tras reanudar y limpieza del GUID residual de versiones anteriores | TransientModifyFailureRecoversWithoutRemovingIcon y TrayRecoveryTests, con un Shell que distingue GUID y HWND y cuenta sus iconos | P; fallo real aportado en 1.1.1, 1.1.2 pendiente de repetición manual |
| DEP-01 | Firma oficial no implementada hasta disponer de identidad válida | tools/Sign-Release.ps1 es solo auxiliar local; no hay certificado integrado en Actions | A; artefactos propios sin firma Authenticode |
| SEG-07 | Fuentes y hashes fijados, firmas de proveedores, inventario completo, SBOM y procedencia | Restore-Dependencies.ps1, build.ps1, Windows release con atestación, comprobación de activos del borrador | C para la cadena construida; ver DEP-08 |
| ARQ-01 | SessionController independiente de WPF, estados y lecturas inmutables | tests/Unit/SessionTests.cs y proyecto Core | C |
| ARQ-04 | Transporte JSON-RPC separado del proceso y del handshake | RpcTests con pipes y errores; ProcessTests con servidor firmado | C |
| COR-03 | Relectura de cuenta ante avisos, autenticación y tres fallos; distingue ausencia de sesión | AuthenticationFailureRechecksAccountAndClearsOldQuota, RepeatedAccountNotificationsAreCoalesced | P; revocación real de ChatGPT |
| OPS-02 | Orden de cierre explícita, detección de instancia y reapertura en segundo plano de actualización silenciosa | App --shutdown, InitializeSetup, RestartSilentUpgrade; compilación Inno | P; actualización desde 1.0.15 y con app abierta |
| FIA-03 | Exclusión asíncrona en creación y retirada; arranque compartido | ConnectionGateSharesAnActuallyPendingServerStart, con inicio suspendido y mutación de exclusión | C |
| FIA-04 | Líneas mal formadas diagnosticadas; EOF resuelve pendientes y rechaza futuras | CorrelationMalformedLinesAndUnknownIds, EndOfStreamFailsPendingAndFutureRequests | C |
| FIA-08 | Job Object; cierre de stdin, espera acotada, terminación del hijo | ActualProcessClientAssignsTheServerAndItsDescendantsToItsJob, SilentRequestTimesOutAndDiscardsTheActualServer y mutación job.Assign | C; suspensión/cierre de Windows pendientes en checklist |
| FIA-09 | Hook temporal, se retira al ocultar; el clic propio se deja a la bandeja | ClickOnOwnIconClosesOnceAndNextClickReopens, PanelVisibility; callbacks siempre continúan la cadena | P; secuencias reales y otros programas |
| COR-06 | Valida respuesta y URL; cancela el login si falla el navegador | BrowserFailureCancelsPendingLogin, NullLoginUrlIsContainedAndCanceled | C |
| COR-08 | Sondeo monotónico; reinicios por reloj de pared | PollIsDueAtSixtySecondsNotBefore, WallClockChangesDoNotStopMonotonicPolling | C; reanudación real en checklist |
| FIA-11 | Límite 1 MiB en stdout; descarte acotado para stderr demasiado largo | OversizedUnterminatedMessageIsRejected, OversizedDiagnosticsAreDiscardedWithoutStoppingTheDrain | C |
| FIA-12 | Respuestas, notificaciones y solicitudes diferenciadas; -32601 al método no admitido | ServerRequestCannotCompleteClientRequestWithSameId | C |
| FIA-13 | Esperas de errores 1/2/5/15 minutos, actualización forzada inmediata | FailureBackoffAndExplicitForceAreBounded y ManualRefreshBypassesTheFailureBackoff, sobre RefreshAsync(true) real | C |
| FIA-17 | Comprueba RegisterHotKey y explica conflicto; falla de permisos no se oculta | App y aviso de shell; el flujo no depende del éxito del atajo | P; conflicto real Ctrl+Alt+U |
| SEG-01 | Keyring obligatorio para la clave pequeña; tokens cifrados con age mediante el backend oficial y CODEX_HOME privado | Integración con credenciales sintéticas de 16.000 caracteres, aislamiento entre perfiles y ausencia de fallback; CredentialMigrationWaitsForConfirmedProtectedAccount | P; 1.1.0 falló con OAuth real por tamaño; 1.1.1 pendiente de revalidación del usuario |
| SEG-03 | Hash y Authenticode antes de ejecutar, archivo bloqueado contra escritura durante arranque | ChangedBinaryIsRejectedBeforeItCanRun y CorrectHashStillRejectsAnInvalidAuthenticodeSignature; mutación de firma | C; no pretende resistir un administrador que sustituya toda la app |
| OPS-10 | Alternativa HWND limitada al registro inicial y después de retirar el GUID candidato; nunca cambia una identidad ya registrada por un fallo transitorio | GuidFailureFallsBackToWindowIdentityAndUsesSameIdentityForBounds, FailedVersionNegotiationRetriesAndExplorerRestartReregisters, RepeatedResumeAndTaskbarNotificationsKeepTheEstablishedIdentity | P; movimiento de ruta y Explorer real |
| DEP-06 | Publicación en carpeta nueva; compara todos los archivos con el inventario y entrada del compilador | build.ps1, package-manifest.json, log Inno | C |
| DEP-08 | Avisos oficiales del runtime real y Codex fijados, MIT citado incluido, inventario explícito | TERCEROS.md y huellas de avisos en dependencies.json | M; proveedor no aporta inventario completo de dependencias nativas enlazadas |
| DEP-09 | MIT y editor jstihl01 en programa, instalador y documentación | LICENSE, Directory.Build.props, metadatos del paquete e instalador | C |
| DEP-12 | Proyectos en carpetas distintas, bloqueos y NuGet.config cerrado; sin ignorar errores | restore --locked-mode de cinco proyectos, paquetes verificados y auditoría | C |
| OPS-03 | Desinstalador ordena cierre y limpia solo keyring/perfil propios; aborta si falla | integración de aislamiento/retirada del perfil A sin afectar B; InitializeUninstall | P; desinstalación interactiva completa |
| OPS-05 | Una versión de producto en Directory.Build.props para app, RPC e Inno | build.ps1 extrae versión; Publish-Release contrasta commit/versión | C |
| OPS-07 | Instalación, privacidad, SHA, atestaciones, desinstalación y diagnóstico documentados | README.md, PRIVACIDAD.md, MIGRACION.md | D |
| ARQ-06 | Menú WPF, conversión nativa a HICON y P/Invoke centralizado | sin referencia WinForms/System.Drawing en app; NativeNotificationStructuresMatchWindowsLayout, render real a HICON | C |
| ARQ-08 | Core y Windows separados; referencias de proyecto y acceso interno declarado | csproj e InternalsVisibleTo; sin fuentes enlazadas ni reflexión de pruebas | C |
| TST-03 | Aserciones de 59/60 segundos y cruce semanal independiente | PollIsDueAtSixtySecondsNotBefore, WeeklyResetTriggersIndependently; ambas mutaciones detectadas | C |
| TST-04 | Duraciones, buckets y rechazo de tipos según esquema oficial de cuotas | DomainTests y DurationlessWindowDoesNotDiscardTheOtherCycle; tipos contrastados con account.rs 0.167.0 | M; sin payload de cuenta real capturado ni comparación manual |
| TST-05 | Texto y cultura centralizados, comparación de ritmo compartida con arcos | UserTextIsConsistentlySpanish, EqualAndNearlyEqualPaceHaveNoColoredSliver | C |
| TST-06 | Windows CI, restauración bloqueada, auditoría NuGet, Gitleaks y release atestada | .github/workflows/ci.yml y release.yml; evidencia de ejecución conservada | C tras ejecución satisfactoria del flujo |
| TST-08 | Checklist con precondiciones, pasos, resultado esperado y registro por versión | COMPROBACIONES.md, todos los resultados manuales pendientes | D; no son pruebas manuales superadas |
| SUP-01 | Primer inicio manual abre el panel; siguientes y --background permanecen como icono | OnlyFirstManualLaunchShowsPanel | P; primer inicio y bandeja de Windows real |
| SUP-02 | Por decisión expresa del usuario del 17/11/2026 el tooltip se reduce a UsageViewer. El detalle conserva cuota y ritmo en sus etiquetas accesibles y la marca sigue siendo opcional | TooltipContainsOnlyTheRequestedNameForEveryQuotaState, UserTextIsConsistentlySpanish y MarkerIsOptionalAndMissingOrExpiredQuotaStaysNeutral | P; lector de pantalla y legibilidad |
| COR-07 | Generación de sesión solo cambia al confirmar logout; ante fallo reconsulta cuenta conservando cuotas | LogoutCancelsInFlightRefreshWithoutLeakingOldQuota, FailedLogoutRetainsConfirmedAccountAndItsLastValidQuota | C |
| COR-09 | Arcos y etiquetas usan misma tolerancia; igualdad sin texto; se documenta redondeo del servidor | EqualAndNearlyEqualPaceHaveNoColoredSliver, README.md | M; no se inventa precisión ni se oculta una diferencia real de 0,5 puntos |
| COR-11 | Reinicio no confirmado: 5 segundos durante un minuto por ciclo, luego 60 | ExpiredResetGetsShortRetriesThenReturnsToNormal, NewWeeklyResetGetsItsOwnRetryMinuteWhileShortResetIsStillPending | C |
| SEG-05 | Solo HTTPS, host exacto ChatGPT/Auth OpenAI, puerto estándar y sin credenciales en URL | UntrustedLoginUrlsAreRejected, OfficialLoginHostsAreAccepted | C |
| SEG-09 | Objetos con SID y DACL restringida al propietario/SYSTEM; errores explícitos | NamedHandlesAreRestrictedAndRemainUsableByTheOwner | M; un proceso malicioso bajo la misma identidad sigue fuera de esta frontera |
| ARQ-09 | Dominio sin WPF; formato español y paleta en presentación | Core independiente; UsageText y pruebas culturales | M; siguen existiendo textos de avisos y acciones en varios archivos de presentación |
| TST-07 | Integración separada; en release el binario es obligatorio y no se omite | ProcessTests y variables requeridas de build.ps1; TRX sin omitidas en build de entrega | C |
| TST-09 | Contraste sobre píxeles renderizados y HICON real a tamaños 16–48 | BorderContrastUsesRenderedPixelsInsteadOfCopiedConstants, ActualIconPixelsRemainVisibleAtAllDpiSizes | C; legibilidad humana pendiente |
| TST-10 | xUnit con casos independientes, tolerancias explícitas y TRX | suites Unit/Windows/Integration; no abortan al primer fallo | C |
| OPS-14 | LZMA2 máxima para entrega; rápida solo con Development | ramas de UsageViewer.iss; log de empaquetado | C |
| SUP-03 | Descarga e instalación identifican Windows 11 x64 Intel/AMD, no ARM64 | README y MIGRACION; ArchitecturesAllowed=x64os | C |

## Segunda revisión y límites de cierre

Se revisan de nuevo las 53 filas antes de publicar. Se comprueba que cada estado se corresponde con código, prueba o reserva explícita. Los tres cambios deliberados del informe deben hacer fallar sus pruebas; no se aceptan errores de compilación como prueba de detección.

La revisión incluye separación de credenciales con el servidor oficial firmado, cierre de hijos, líneas corruptas y sobredimensionadas, estados de sesión, último dato válido, calendario, negociación de bandeja simulada, píxeles reales, registro sin secretos, inventario y borrador de publicación. Se preservan los hashes de los dos recursos gráficos durante la compilación.

No se afirma haber observado animaciones, instalado/desinstalado en el escritorio del usuario, probado suspensión con su sesión, autorizado OAuth con su cuenta ni validado accesibilidad humana. Tampoco se afirma una auditoría de todas las dependencias nativas de OpenAI. Estas reservas permanecen visibles tras la publicación.

DEP-01 se mantiene aplazada hasta disponer de una identidad de firma válida. No se ha contratado ningún servicio.

## Corrección del inicio de sesión en 1.1.1

El usuario aportó una captura de **persist_failed** en 1.1.0: la autenticación había finalizado, pero el backend directo no podía guardar más de 2.560 caracteres UTF-16 en Windows. La prueba original con una API key corta no representaba el tamaño de los tokens OAuth reales.

Se activa el backend opcional oficial **secret_auth_storage** del mismo servidor firmado 0.167.0. Cifra el registro con age y conserva solo una clave aleatoria de 32 bytes en Windows. La selección **keyring**, sin fallback a archivo en claro, permanece obligatoria. Este backend queda fijado a esa versión y sujeto a la revisión de mantenimiento.

La integración usa ahora una credencial sintética de 16.000 caracteres, comprueba su formato cifrado, la persistencia tras reiniciar el servidor, la presencia y retirada de su clave y la conservación del segundo perfil. Se añaden cierre de sesión con reinicio y fallo real de escritura sin fallback. La interfaz distingue el fallo del almacenamiento de otros errores de acceso y registra solo su categoría segura.

No se ha usado la cuenta del usuario ni se ha observado o controlado su escritorio. La autenticación OAuth real de 1.1.1 permanece pendiente de su validación.

## Correcciones del icono y del menú en 1.1.2

Las capturas aportadas por el usuario y el evento tray_identity_fallback de su instalación confirman el problema de recuperación en 1.1.1. Ante tres fallos transitorios, el código podía conservar un icono GUID y crear otro por HWND. Las pruebas originales solo modelaban un booleano de existencia y no podían detectar ambas identidades.

El nuevo modelo de prueba mantiene un registro por identidad y reproduce dos iconos con la implementación anterior. En la corrección, la identidad queda fijada tras el primer registro correcto y la reanudación provoca una comprobación inmediata sin crear una identidad alternativa. Al iniciar se retira el GUID residual de versiones anteriores. Se prueban veinte ciclos simulados de recuperación y los casos de Explorer con y sin el icono todavía presente.

El texto blanco del menú provenía del estilo TextBlock implícito de aplicación. El texto claro se limita al panel y el menú usa la paleta del sistema. La prueba carga los recursos reales a nivel de Application, inspecciona el texto generado dentro de la plantilla WPF y verifica su contraste en paletas clara y oscura, sin abrir ventanas. Al restaurar el estilo antiguo, la prueba vuelve a fallar sobre el menú claro.

El tooltip queda reducido a **UsageViewer** por petición expresa del usuario. La prueba comprueba datos presentes, ausentes, caducados y desactualizados.

Estas pruebas y los registros no sustituyen una nueva suspensión física ni la revisión visual por parte del usuario. No se ha tomado el control de su escritorio; la reinstalación se realiza mediante el instalador autorizado.
