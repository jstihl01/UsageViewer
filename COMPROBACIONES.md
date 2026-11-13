# Comprobaciones manuales · Usage Viewer 1.1.0

Todas las comprobaciones de esta tabla quedan **pendientes de validación por el usuario**. Las pruebas automatizadas, sus resultados y sus límites se describen en AUDITORIA.md. No se ha controlado ni observado el escritorio del usuario para dar estas comprobaciones por superadas.

Al ejecutar un caso, anota fecha, versión exacta, configuración de Windows y resultado. Usa una VM o cuenta de prueba para revocaciones, cambios de reloj y fallos provocados.

| ID | Precondición | Pasos | Resultado esperado | Resultado / fecha |
| --- | --- | --- | --- | --- |
| MAN-01 | Windows 11 x64, instalación nueva | Instalar; revisar opción de inicio con Windows | Instala por usuario; la opción está desmarcada de origen y se respeta la elección | Pendiente |
| MAN-02 | Primer inicio manual | Abrir desde Inicio o marcar Abrir tras instalar | Aparece el panel de inicio de sesión; el icono permanece | Pendiente |
| MAN-03 | Primer uso completado | Salir y abrir otra vez; iniciar sesión en Windows con autoarranque | Inicia como icono; el arranque automático no despliega el panel | Pendiente |
| MAN-04 | Cuenta de prueba | Iniciar sesión en el navegador | Muestra la cuenta y las cuotas; no altera otras instalaciones de Codex | Pendiente |
| MAN-05 | Actualización desde 1.0.15 | Instalar y volver a iniciar sesión una vez | Usa el almacén seguro; el archivo antiguo solo desaparece tras confirmar la nueva sesión | Pendiente |
| MAN-06 | Sesión activa, Codex sin consumo durante la comparación | Actualizar ahora y comparar con el visor oficial | Porcentajes compatibles con su redondeo y momento de lectura; reinicios coinciden al minuto mostrado | Pendiente |
| MAN-07 | Sesión activa | Abrir desde el icono, volver a pulsarlo, repetir rápidamente | Abre y pliega una vez por clic; nunca desaparece el icono ni se reabre por el mismo gesto | Pendiente |
| MAN-08 | Panel abierto | Pulsar dentro, fuera, otra app, icono y Esc | Dentro permanece; fuera e icono pliegan; el clic llega a su destino; Esc pliega | Pendiente |
| MAN-09 | Tema claro y oscuro | Revisar tamaños y colores; activar y desactivar marca accesible | Un anillo de 5 horas con borde gris; detalle con dos; logo sin borde; textos legibles | Pendiente |
| MAN-10 | Panel e icono disponibles | Cambiar escala y resolución; probar otro monitor | Icono nítido; panel dentro del área útil y cercano al icono | Pendiente |
| MAN-11 | Cuenta activa, VM | Suspender, reanudar; bloquear y desbloquear; probar RDP | Conserva la instancia y vuelve a consultar; el panel no sale de pantalla | Pendiente |
| MAN-12 | Cuenta activa | Desconectar y volver a conectar la red | Conserva última lectura con aviso; se recupera o permite Actualizar ahora | Pendiente |
| MAN-13 | VM, servidor de prueba congelado o terminado | Simular silencio o fin del servidor | Detecta plazo/EOF, termina el hijo y reconecta sin acumular procesos | Pendiente |
| MAN-14 | Cuenta de prueba | Invalidar su sesión y volver a abrir el detalle | Diferencia autenticación de red y permite iniciar sesión de nuevo | Pendiente |
| MAN-15 | Cuenta activa | Cerrar sesión mientras actualiza | No reaparecen porcentajes ni identidad de la cuenta anterior | Pendiente |
| MAN-16 | VM | Adelantar y atrasar el reloj | El sondeo normal no queda detenido por el salto; no inventa un reinicio confirmado | Pendiente |
| MAN-17 | Ciclo cercano a su reinicio | Esperar y observar | Muestra Esperando actualización hasta confirmar el nuevo ciclo; reintenta pronto | Pendiente |
| MAN-18 | Atajo ocupado por una app de prueba | Abrir Usage Viewer | Indica el conflicto de Ctrl+Alt+U; el icono sigue funcionando | Pendiente |
| MAN-19 | VM con Explorer reiniciable | Reiniciar Explorer | El icono vuelve y acepta ratón y teclado; no queda congelado | Pendiente |
| MAN-20 | App abierta | Actualizar de forma interactiva y silenciosa | Cierre ordenado, sin hijos huérfanos; conserva ubicación y preferencias; vuelve como icono si correspondía | Pendiente |
| MAN-21 | App cerrada | Actualización silenciosa | No abre una instancia que no estaba funcionando | Pendiente |
| MAN-22 | Copia instalada en otra carpeta | Ejecutar o reinstalar en esa ubicación | Solo una instancia; el icono aparece o usa su identificación alternativa | Pendiente |
| MAN-23 | VM, app y servidor activos | Desinstalar | Cierra procesos, borra datos y solo sus credenciales; conserva otros perfiles de Codex | Pendiente |
| MAN-24 | VM, servidor ausente o puesto en cuarentena | Abrir y después desinstalar | Explica el componente ausente; la limpieza no declara éxito si falla | Pendiente |
| MAN-25 | Registros existentes | Abrir registros desde el menú | Hay diagnóstico acotado; no hay tokens, correo, URL de login ni respuestas completas | Pendiente |
| MAN-26 | Versión posterior publicada o entorno de prueba | Abrir el panel | Aviso discreto; abre la página oficial y nunca ejecuta una actualización automáticamente | Pendiente |

La comparación con el visor oficial debe hacerse sin consumo simultáneo. Si hay diferencias, anota las horas de ambas lecturas; no envíes credenciales ni archivos de autenticación.
