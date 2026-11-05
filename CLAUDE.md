# Radial — decisiones permanentes del proyecto

Launcher de menús radiales para Windows. Producto de portfolio: calidad, estabilidad y mantenibilidad por encima de efectos.
Documento de referencia: [docs/technical-design.md](docs/technical-design.md). Si algo aquí contradice a ese documento, manda el documento más reciente y hay que corregir el otro.

## Reglas de trabajo

- **No hacer commits automáticamente.** El usuario revisa antes.
- **No avanzar de fase sin aprobación expresa.** Cada fase termina con informe y espera.
- No instalar herramientas ni dependencias sin necesidad ni sin autorización. Antes de añadir un paquete: licencia, mantenimiento, utilidad real y si hay alternativa nativa.
- No cambiar decisiones aprobadas sin justificarlo. No eliminar funcionalidad para resolver un problema sin evaluar consecuencias.
- No escribir código ficticio ni de demostración para lo que aún no existe. La excepción es el spike de la fase 2 (`spikes/`, desechable, fuera de `Radial.sln`).
- Una fase no está terminada hasta que compila y se ha comprobado que funciona.
- No tocar nada fuera de `C:\Coding\Radial`.
- Sin coste: nada de APIs de pago, cloud, servidores, cuentas ni licencias comerciales. Todo local. No adquirir certificados de firma ni servicios de pago sin autorización expresa.
- **Smart App Control (en modo de aplicación en el equipo de desarrollo) bloqueó el spike** (`0x800711C7`). **No desactivarlo, no tocar políticas de seguridad, no crear excepciones ni buscar formas de eludirlo.** Si un binario se bloquea, parar e informar. No se afirma que todas las compilaciones sin firmar se bloqueen. Firma de código y confianza: evaluar en la fase 8 (sección 15 del documento técnico).
- **Segundo bloqueo (fase 4, paso 3, 2026-11-03):** Smart App Control bloqueó `Radial.Windows.Tests.dll` y, tras el ajuste de URL, `Radial.Core.Tests.dll` (compilaciones nuevas; el mismo proyecto de Core había cargado minutos antes). **No se vuelven a lanzar las pruebas de Windows en este equipo, no se recompila para conseguir que un binario cargue y no se afirma como superado lo que no se ha ejecutado.** Es una limitación del entorno de validación, no un fallo de las pruebas (documento técnico, 17.8).
- **Dónde se validan las pruebas automáticas: GitHub Actions** (`.github/workflows/radial-windows-validation.yml`, runner `windows-latest`; se lanza con push a cualquier rama, con `pull_request` y con `workflow_dispatch`, este último solo desde la interfaz de GitHub cuando el workflow exista en la rama predeterminada). Es la referencia vigente; la regla anterior sobre el equipo de desarrollo no cambia. **Última ejecución de CI confirmada: commit `d4b3f8a` (cierre técnico de la fase 4), ejecución nº 4, <https://github.com/xrbrr24/Radial/actions/runs/41900198820>: el estado verde está verificado en la API de GitHub; las cifras «694 Core y 992 Windows» las comunicó el usuario (no son comprobables sin autenticación).** Esa ejecución no cubre el código de la fase 5, que sí lo cubre la ejecución nº 7 sobre `1772b59` (<https://github.com/xrbrr24/Radial/actions/runs/42001660093>): estado verde verificado en la API (compilación, Core y Windows); las cifras por suite no se han podido leer sin autenticación. Las cifras de informe 660/574 (4.1) y 694/855 (4.2) son históricas y de procedencia no confirmada: **no presentarlas como resultados de CI**. Que `Radial.App` se haya ejecutado a mano en el equipo no demuestra que Smart App Control no vuelva a bloquear otra compilación (documento técnico, 15.5).
- **Qué se puede lanzar en el equipo de desarrollo y qué no:** sí `dotnet build src/Radial.App/Radial.App.csproj -c Release` (compila el producto: Core, Windows y App; si hay una instancia de Radial en ejecución, añadir `-o <carpeta temporal fuera del repositorio>`) y ejecutar `Radial.App`. **No** `dotnet test`, no compilar `tests/Radial.Core.Tests` ni `tests/Radial.Windows.Tests` (tampoco `dotnet build -c Release` de la solución, que los incluye) y no ejecutar ensamblados de pruebas: la compilación y la ejecución de las pruebas son de GitHub Actions. Un error de compilación en las pruebas solo aparecerá allí.
- **No volver a ejecutar `spikes/Radial.BlurSpike`.** Los datos válidos están en `spikes/Radial.BlurSpike/results/run1-*`; no borrarlos. El código añadido tras `run1` (B2, experimento de activación, `RegionLog`, `--only`, `run.log`) compila pero **no está validado** y está marcado `NO VALIDADO`.

## Tecnología y compatibilidad

- C# + WPF + .NET 10 LTS. SDK fijado en `global.json` (10.0.403, `rollForward: latestFeature`).
- Objetivo oficial v1: **Windows 11 x64, versiones estables 25H2 y posteriores**. Las pruebas de desarrollo incluyen 26H2 (build 26300, disponibilidad general desde el 29-sep-2026). **Sin soporte oficial para Windows 10.**
- No depender de comportamientos exclusivos de una build concreta. Validar en una versión estable antes de dar el producto por terminado.
- Windows APIs: P/Invoke a mano con `LibraryImport`. Sin CsWin32.
- Descartadas: FluentAssertions, Moq.

## Arquitectura

```
Radial.App ──▶ Radial.Windows ──▶ Radial.Core
Radial.Core.Tests ──▶ Core        Radial.Windows.Tests ──▶ Windows, Core
```

- `Radial.Core` (`net10.0`): lógica pura, sin Windows ni WPF. Casi todas las pruebas viven aquí.
- `Radial.Windows` (`net10.0-windows`, sin WPF): hooks, SendInput, DPI y monitores, ejecutores, almacenamiento en disco. **Nunca `Radial.Infrastructure`.**
- `Radial.App` (`net10.0-windows`, WPF): único proyecto con WPF. Composición, overlay, ajustes, bandeja. Usa `UseWindowsForms` **solo** para `NotifyIcon` (bandeja, `Tray/TrayIcon`): sin paquetes; los usings implícitos de Windows Forms y Drawing están quitados del proyecto y el analizador WFO0003 (pide quitar el DPI del manifiesto) está silenciado porque `PerMonitorV2` en `app.manifest` es decisión fijada. Ninguna otra ventana ni control usa Windows Forms.
- Sin lógica de negocio en código de interfaz. Evitar clases gigantes y sobreingeniería.

## Comportamiento fijado

- Gesto predeterminado: mantener **Ctrl**, pulsar el **botón central**, mover, soltar el botón central para confirmar; después se puede soltar Ctrl.
- **Ctrl debe estar pulsado antes** del botón central. Si el botón central va primero no se activa y **el clic no se intercepta**. Esta regla de orden no es configurable en la v1.
- Esc o soltar Ctrl antes de confirmar cancela.
- Submenús: se recentran en la posición actual del cursor. El retroceso por el centro solo se arma después de que el cursor salga de la zona muerta.
- Geometría: ángulos normalizados sin usar `%` con negativos (en C# el resto conserva el signo).
- Atajos de teclado: esperan a que se liberen los modificadores físicos. Timeout predeterminado 5 s, configurable. Si se supera: se cancela, se informa al usuario y no se deja ningún estado de teclado modificado. No manipular Ctrl artificialmente.
- Configuración en `%LOCALAPPDATA%\Radial\` (config, copias, iconos, logs). JSON versionado con migraciones. Lo importado se valida antes de aplicarse y nunca se ejecuta nada durante la validación.
- Primera escritura de `config.json`: creación (el destino no existe). Siguientes: reemplazo atómico (`File.Replace`).
- Tipos de acción desconocidos: no se asume que ignorar el discriminador conserve los datos. Estrategia explícita y con pruebas (sección 7 del documento técnico).

## Captura global

- Hooks `WH_KEYBOARD_LL` y `WH_MOUSE_LL` en un hilo dedicado con bucle de mensajes. Callbacks mínimos.
- Teclas y botones: eventos críticos, en orden y sin pérdidas. Movimientos: coalescidos.
- La máquina de estados es la fuente de verdad de las transiciones. **No usar `GetAsyncKeyState` dentro de los callbacks** para conocer la transición que acaba de ocurrir.
- Windows puede eliminar un hook en silencio. **No prometer recuperación automática.**
- Implementado en la fase 3 (`Radial.Windows/Input` y `/Hooks`): `GlobalInputService` (API pública), `LowLevelHookHost` (hilo dedicado), `ChordTracker` (lógica pura), `HookMessages` (mapeo puro). Eventos críticos por un `Channel` sin límite (lector único); movimientos en `LatestPointer` (coalescidos).
- **El watchdog NO comprueba el botón central con `GetAsyncKeyState`: no refleja un evento consumido por un hook (medido).** Solo vigila Ctrl y que el hook de ratón siga entregando movimiento. No hay detección del hook de teclado eliminado ni de la liberación perdida del botón central.
- Ctrl nunca se consume; el botón central solo si activa (y su liberación siempre); Escape solo si cancela un gesto activo; movimiento y rueda nunca. Todo evento inyectado se ignora; solo con `AcceptOwnSyntheticInput` (autoprueba) se aceptan los inyectados **con la marca de Radial** (`dwExtraInfo`, 32 bits: el hook de ratón trunca el valor, medido), nunca los de otras aplicaciones.
- **Autoprueba con entrada sintética** (`Radial.App.exe --selftest <archivo>`): valida los hooks reales con `SendInput`, **no sustituye** la prueba con hardware. Toda inyección pasa por `GuardedSyntheticInput` (`SyntheticInput` es interno): antes de **cada** envío comprueba cursor, `WindowFromPoint` y `GetForegroundWindow`, y recuerda qué pulsó para liberar solo eso. Reduce el riesgo, **no es atómico** (ver documento técnico 16.9). La limpieza es síncrona y la prueba es cancelable (`CancellationToken`); cerrar la ventana a mitad la cancela y aplaza el cierre. No restaurar el cursor del usuario antes de terminar de inyectar. **No inyectar entrada con programas auxiliares fuera de Radial** (ni siquiera para depurar): un fallo de activación del gesto puede dejar pasar Ctrl+Escape (menú Inicio) a otras aplicaciones.
- Un segundo `GlobalInputService` en el proceso no instala hooks: `Start` devuelve `Success = false` y el estado queda `Faulted` (nunca `Starting`).
- Reanudar o desbloquear solo deja un aviso: no se reinstalan los hooks.
- **Fallo seguro del lector (17.14):** si el lector del canal de eventos termina sin que se haya pedido, `GestureReaderSupervisor` detiene la captura como fallo (`GlobalInputService.Fail`: hooks fuera, gesto cancelado, estado `Faulted`), oculta el overlay (`Dismiss`) y lo comunica una sola vez; la aplicación sigue abierta sin captura y «Iniciar captura» queda deshabilitado (no se reinicia nada solo: no hay recuperación automática). El cierre normal no es un fallo. No quitar el supervisor ni reintroducir un lector sin supervisar.

## Atajos de teclado (fase 4, paso 2 y su corrección)

- **El vocabulario de teclas tiene UNA fuente de verdad: `Radial.Core.Model.KeyVocabulary`.** Es una regla del dominio. `SendKeysAction.GetProblems()` valida la tecla con ella; Windows no tiene lista propia de nombres. Lo que valida una acción es siempre ejecutable.
- **Vocabulario cerrado de 66 teclas, sin alias:** `A`–`Z`, `0`–`9` (fila superior), `F1`–`F24`, `Enter`, `Tab`, `ArrowLeft`/`ArrowUp`/`ArrowRight`/`ArrowDown`. Sin distinguir mayúsculas, solo ASCII, sin recortar espacios, sin depender de la cultura. Fuera: Ñ, símbolos, teclas OEM, teclado numérico, Escape, Space, Backspace, Delete. **No ampliarlo sin decisión expresa**: exige tocar la lista de Core y la tabla de códigos de Windows (si se olvida la segunda, `ShortcutParser` falla al inicializarse) y actualizar 17.5 y 17.6.
- `Radial.Windows/Shortcuts/ShortcutParser` solo asigna a cada nombre de `KeyVocabulary.Names` su código VK y su indicador `Extended` (solo las cuatro flechas lo son). Los códigos de Windows **no** viven en Core. Es solo traducción: no envía teclas ni usa P/Invoke.
- Los modificadores (`KeyModifiers`) pasan sin tocar; validarlos es de `SendKeysAction.GetProblems()`. Una configuración con una tecla fuera del vocabulario se rechaza al validar; el editor (fases 5 y 6) debe ofrecer solo `KeyVocabulary.Names`.

## Ejecutores de acciones (fase 4, paso 3 — sin verificar en el equipo de desarrollo; ver 15.5 para lo confirmado por CI)

- `Radial.Windows/Actions`: `IProcessLauncher` (destino, argumentos y directorio de trabajo **separados**), `ShellProcessLauncher` (`Process.Start` con `UseShellExecute`; **nunca** `cmd.exe`, PowerShell ni scripts) y los ejecutores `LaunchApplicationActionExecutor`, `OpenFileActionExecutor`, `OpenFolderActionExecutor`, `OpenUrlActionExecutor` sobre la base `ProcessActionExecutor<T>`. Orden fijo: tipo no admitido → cancelado → `GetProblems()` de Core → comprobaciones propias (existencia, esquema) → lanzamiento. No hay expansión de variables de entorno ni resolución de rutas relativas.
- Códigos de error: `UnsupportedAction`, `InvalidAction`, `TargetNotFound`, `LaunchFailed`, `UrlNotAllowed`, `TargetNotAllowed` (más los de atajos y `UnexpectedError`, abajo). Los mensajes no incluyen rutas ni direcciones.
- **Política de destinos (`Radial.Windows/Actions/TargetPolicy`, cierre de la fase 4):** aplicación = ruta absoluta o nombre de programa sin ruta ni «:» (`notepad.exe`); se rechazan relativas, de unidad relativa, de dispositivo (`\\?\`, `\\.\`) y protocolos (`ms-settings:`, `shell:`…) con `TargetNotAllowed`; directorio de trabajo absoluto. Archivo y carpeta: la ruta relativa se trata como inexistente (nunca se resuelve contra el directorio del proceso) y la de dispositivo da `TargetNotAllowed`. Es el mínimo en el último punto; **no sustituye** la validación de la configuración. **Bloqueado para la fase 5 antes de importar/cargar configuración:** política de tipos de archivo ejecutables por asociación, rutas de red (UNC se aceptan hoy), la misma validación en `GetProblems()` de Core y nombres sin ruta. Detalle en 17.12.
- **`OpenUrlAction` solo admite `http` y `https`**, con una única definición en Core (`OpenUrlAction.AllowedSchemes`); `mailto:` y el resto se rechazan al validar. El ejecutor de Windows reutiliza esa lista en su comprobación defensiva independiente justo antes de lanzar. No ampliarla sin decisión expresa.
- Las pruebas de estos ejecutores usan un lanzador falso y no inician ningún proceso; `ShellProcessLauncher` se prueba con un arranque inyectado. El lanzador real solo está sin probar de extremo a extremo.

## DPI y overlay

- `PerMonitorV2` (ver `app.manifest`). No usar `GetDpiForMonitor`; usar `GetDpiForWindow`, `VisualTreeHelper.GetDpi` y gestionar `WM_DPICHANGED`.
- **Overlay (ADR-001, aceptado): ventana layered de WPF** — `AllowsTransparency=true`, fondo oscuro translúcido, forma circular, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `WS_EX_TRANSPARENT` para el click-through, `Topmost`, mostrar sin activar. Se crea una vez al arrancar. Hay que comprobar que transparencia, click-through y no activación funcionan **a la vez**.
- **Sin blur.** No usar Acrylic, Mica ni técnicas DWM (`DWMWA_SYSTEMBACKDROP_TYPE`, `SetWindowRgn` sobre material) en producción. **`SetWindowCompositionAttribute` no es una API pública documentada: no se usa.** La captura de pantalla + `BlurEffect` (variante E) está archivada como posible experimento futuro: no implementarla ni depender de ella. El desenfoque no es requisito del MVP.
- `WM_NCHITTEST → HTTRANSPARENT` no deja pasar el clic a otro proceso: no usarlo.
- La investigación de la fase 2 fue **parcial**: un monitor al 125 %, una ejecución. DPI 100/150/200 %, varios monitores, cambio de monitor, `WM_DPICHANGED` real y Windows 11 estable 25H2 siguen **pendientes de validar**; no afirmar que están probados.
- Las causas de los resultados de las variantes B, C y D **se desconocen**. No presentarlas como explicadas.

## Radial visible (fase 4.1 — validación manual básica hecha; pendientes abajo)

- `Radial.Windows/Overlay`: `RadialOverlayCoordinator` (gesto → motor → ventana → **acción**: con un `IActionExecutor` ejecuta el elemento confirmado sin bloquear y lo comunica con `ActionFinished`; informa también con `Completed`/`Aborted`), `IRadialPresenter`, `OverlayPlacement` (cálculo puro) y `OverlayWindowInterop` (estilos, colocación en píxeles físicos, DPI). `Radial.Core/Geometry/RadialLayout`: disposición pura del dibujo, con los mismos ángulos que la selección. `Radial.App/Overlay`: `OverlayWindow` (se crea una vez, `Prepare()`, mostrar/ocultar), `RadialView` (geometría `StreamGeometry`, nunca botones), `RadialTheme`, `GlyphCatalog`.
- **El menú sale de la configuración (fase 5), no del código.** `ProvisionalMenu` desapareció: la configuración inicial la genera `Radial.Windows/Storage/DefaultConfiguration` a partir del repertorio de acciones (las ocho acciones de siempre: cinco atajos en el menú principal y Carpeta, Web y Navegador dentro del grupo «Abrir», al oeste) y se guarda en `config.json` en el primer inicio. Los destinos salen de carpetas conocidas del sistema; no se inventan rutas.
- **Submenús conectados al overlay (fase 5).** El motor decide (`RadialEngine`: permanencia, recentrado, retroceso armado, progreso) y `RadialOverlayCoordinator` lo refleja: `IRadialPresenter` tiene `Show` (menú raíz), `ShowMenu` (cambio de menú con la ventana ya visible: se redibuja y se recoloca en un solo paso, sin ocultar ni recrear), `SetSelection`, `SetProgress` (`RadialProgress`: entrada, retroceso, armado) y `Hide`. **Coordenadas:** el motor usa UN sistema de DIP durante toda la interacción; el coordinador convierte con un ANCLA (píxel físico y su coordenada del motor) y la escala del monitor actual: `motor = ancla.motor + (píxel − ancla.píxel) / escala`. Al cambiar de menú el motor pone su origen geométrico en el cursor; el centro FÍSICO de la ventana es ese punto convertido de vuelta y se re-ancla ahí con la escala del monitor donde quedó. **No restar el origen dos veces ni volver a un origen fijo.** Red de seguridad: tras cada actualización la pila de menús mostrada se compara con la del motor (igual, +1 o −1 nivel); cualquier otra cosa cancela el gesto, oculta el overlay y no ejecuta nada (`UnsupportedNavigation`, no debería ocurrir). No quitarla. Una acción desconocida se dibuja apagada y el motor no la devuelve como ejecutable (`CancelReason.UnsupportedAction`). **Limitación vigente:** bordes de pantalla y cambio de monitor a mitad del gesto sin algoritmo propio (E-1, opción A, sin probar).
- El click-through (`WS_EX_TRANSPARENT`) está siempre activo: la interacción se resuelve con los hooks, no con el ratón sobre la ventana. El centro del radial es el cursor sin ajuste a los bordes (E-1, opción A).
- Todo se mide en DIP relativos al centro (píxeles físicos ÷ escala del monitor donde se mostró); el radio de la zona central de la configuración es el del botón central dibujado.
- **El coordinador se aísla durante la autoprueba sintética** (`Isolate()`/`Resume()`; sin supervisión nace aislado) y descarta por marca de tiempo los eventos que la autoprueba dejó en el canal. No quitar ese aislamiento: la entrada sintética no debe mostrar el overlay. **Barrera central añadida en el cierre de la fase 4:** `GestureEvent.SyntheticWindow` (campo aditivo) lo fija `GlobalInputService` al publicar mientras `AcceptOwnSyntheticInput` está activo; viaja con el evento y `RadialOverlayCoordinator.Handle` lo ignora aunque se lea tarde. Son tres barreras independientes (marca, `Isolate`, marca de tiempo): **no quitar ninguna**. Una autoprueba nunca debe mostrar el overlay ni ejecutar una acción.
- Un fallo del overlay se registra y se contiene en la ventana de diagnóstico: es el único lector del canal de eventos (sin límite) y no debe morir.
- Herramientas de desarrollo (sin hooks, sin entrada, sin clics): `Radial.App.exe --render-preview <png>` (dibuja el radial) y `--overlay-check <txt>` (muestra el overlay real un instante y escribe estilos, centrado y foco). **No probar el click-through con clics sintéticos.**
- **Probado a mano por el usuario (según su informe):** el overlay aparece con el gesto real, se seleccionan sectores y desaparece al confirmar o cancelar (método de cancelación no detallado).
- **Sin validar:** el efecto real del click-through, el foco medido con aplicaciones concretas, pantalla completa o elevadas, DPI distintos del 125 %, varios monitores y cambio de monitor a mitad de gesto, `WM_DPICHANGED` real, bordes de pantalla, 25H2, rendimiento medido, autoprueba tras la 4.1/4.2. Lista completa en el documento técnico, 17.11. No afirmar como comprobado nada de esto.

## Motor de acciones y atajos funcionales (fase 4.2 — validación manual básica hecha; pendientes abajo)

- **Un único ejecutor para todos los atajos:** `Radial.Windows/Shortcuts/ShortcutActionExecutor` (`SendKeysAction`). Ctrl+C, Ctrl+Mayús+S, Alt+Tab o Win+E son datos de la acción, no clases. **No crear una clase por atajo.** Nuevo atajo = nueva `ActionDefinition` (Core) con tecla en `KeyVocabulary`.
- Orden fijo: tipo → cancelado → `GetProblems()` → espera a que se suelten **los cuatro modificadores físicos** (Ctrl, Mayús, Alt, Win; `IModifierStateReader`, sondeo con `GetAsyncKeyState` fuera de los hooks, 5 s por defecto y configurable) → misma ventana en primer plano → UNA llamada a `SendInput`. Códigos: `ModifiersNotReleased`, `ForegroundChanged` (también si no hay ventana en primer plano), `InputInjectionFailed`, `KeyReleaseFailed` (no se pudo confirmar la liberación de lo pulsado; se reintenta una vez) (más `UnexpectedError` del coordinador). **No se pulsa ni se suelta ningún modificador artificialmente para «ayudar»** (documento técnico 3.4).
- **La entrada de teclado de producción está separada de la autoprueba:** `SendInputKeyboardInjector` no usa `SyntheticInput`/`GuardedSyntheticInput` ni lleva `SyntheticMarker` (`dwExtraInfo = 0`), así que los hooks la descartan por inyectada incluso con `AcceptOwnSyntheticInput`. No cambiar eso. Si Windows acepta solo parte de la secuencia, suelta lo pulsado, comprueba la liberación y reintenta una vez. **Límite sin solución (I3, documentado en 17.12):** aplicación destino elevada (UIPI) → Windows descarta el teclado sin avisar y `SendInput` no lo indica; **no se implementó detección** (heurística no verificable, con falsos positivos). El éxito significa «Windows aceptó la secuencia», no «la aplicación la procesó»: la ventana de diagnóstico dice «aceptada por Windows», no «correcta». **No presentar un atajo como entregado.**
- `Radial.Core/Catalog`: `ActionDefinition` (id estable, nombre, categoría, descripción, icono, acción), `ActionCatalog` (validado, ids únicos), `BuiltInShortcuts` (Copiar, Pegar, Deshacer, Seleccionar todo, Guardar). `Radial.Core/Execution/CompositeActionExecutor` elige el ejecutor por `CanExecute`; `Radial.App/ActionExecutors` es la raíz de composición. El coordinador **cancela** la acción pendiente al empezar otro gesto, al aislarse y al cerrar; durante el aislamiento (autoprueba) **no ejecuta nada**.
- Los fallos se comunican en la ventana de diagnóstico (registro y fila «Última acción»). **No usar cuadros de mensaje**: robarían el foco a la aplicación que recibe el atajo. Notificaciones de usuario: fases posteriores.
- La prueba manual exige otra aplicación en primer plano: el teclado va a la ventana activa (la de diagnóstico de Radial también lo recibiría).
- **No inyectar entrada real para probarlo desde fuera de Radial** (CLAUDE, autoprueba): las pruebas automáticas usan un inyector falso; el `SendInput` real solo se ha validado a mano.
- **Probado a mano por el usuario (según su informe):** Copiar, Pegar, Deshacer, Seleccionar todo y Guardar; abrir Documentos; abrir Wikipedia en el navegador predeterminado; abrir Microsoft Edge. Las aplicaciones de destino concretas no se registraron.
- **Sin validar:** el vencimiento de 5 s con el gesto real, los caminos de fallo a mano (`TargetNotFound`, `UrlNotAllowed`, `ForegroundChanged`, `InputInjectionFailed`), aplicaciones de destino elevadas (UIPI), `Alt+Tab`/`Win+E`, juegos y pantalla completa, `OpenFileAction` y aplicaciones con argumentos (el menú provisional no los usa). Ver 17.11.

## Configuración y almacenamiento (fase 5 — CI en verde en `1772b59`; sin validar a mano; ver documento técnico, sección 18)

- **`config.json` en `%LOCALAPPDATA%\Radial\` es la fuente de verdad** (`backups\`, `icons\`, `logs\`). La misma configuración y el mismo almacén (`ISettingsStore`) los usará el editor de la fase 6.
- **Core (`Radial.Core/Storage`):** `ConfigurationFile` (`Serialize`/`Parse`, sin disco; nunca lanza por contenido), DTOs y contexto de `System.Text.Json` **source-generated** (`ConfigurationJsonContext`), `ActionJson` (acciones con discriminador `type`, resueltas a mano sobre `JsonObject`), `ConfigurationMigrator` (migraciones encadenadas; hoy `schemaVersion` = 1 y ninguna migración), `ConfigurationCodes`, `ISettingsStore` y sus resultados. Límites: 1 MiB y 32 niveles de profundidad. Versión futura = `FutureVersion`, sin interpretar. **Un tipo de acción nuevo en Core exige su serialización en `ActionJson` y añadirlo a `Sample()` de `ConfigurationFileTests` (una prueba lo comprueba por reflexión).**
- **Windows (`Radial.Windows/Storage`):** `FileSettingsStore` (disco) y `DefaultConfiguration`. Escritura atómica: temporal en la misma carpeta; primera escritura = `File.Move` sin sobrescribir; siguientes = `File.Replace` con la versión anterior en `backups\config-aaaaMMdd-HHmmss-fff.json` (10 copias). Archivo corrupto/inválido (JSON mal formado, demasiado grande o profundo, sin estructura, con errores de validación o de lectura de acciones) → **cuarentena** `config.corrupt-….json` (10 como máximo) → copia **válida** más reciente o, si no hay, la predeterminada; nunca una copia sin validar. **Versión futura: el archivo no se toca, no se pone en cuarentena, se usan los valores predeterminados en memoria y `Save` se rechaza** (no se destruyen datos que una versión más nueva entiende). Un fallo del disco (bloqueo, permisos) no es corrupción: no se toca el archivo. Ninguna operación lanza.
- **Acciones desconocidas (7.1):** `UnknownAction(Type, RawJson)` conserva el objeto JSON íntegro (propiedades anidadas incluidas) y se reescribe tal cual; aviso de validación (`action.unknown-type`, advertencia, no error); **nunca se ejecuta** (ningún ejecutor la admite, el motor no la devuelve, no puede ser una definición). Un objeto sin `type`, o con `type` que no es texto, o una propiedad de tipo equivocado en una acción conocida, **rechaza la configuración entera** (se pone en cuarentena, no se descarta el elemento a escondidas). Las propiedades desconocidas de la estructura conocida se ignoran al leer (no se conservan al reescribir).
- **Definiciones y colocaciones:** `ActionDefinition` (repertorio, id estable) ≠ `RadialItem` (colocación, id único en toda la configuración, `DefinitionId` opcional). `ToItem(ItemId, posición)` o `ToItem(MenuId, posición)` (id derivado `{menú}.{definición}`; si una definición se coloca dos veces en el mismo menú hay que dar el id). Los grupos son estructura del árbol, no definiciones. La biblioteca de acciones del usuario (persistencia de definiciones propias) es de la fase 6.
- **Arranque (`App.OnStartup`):** carga la configuración con `FileSettingsStore`; un fallo inesperado nunca tumba la aplicación (valores predeterminados en memoria + aviso). La autoprueba (`--selftest`) usa la predeterminada en memoria y **no toca el disco**. La captura empieza sola al cargar la ventana (salvo en la autoprueba).
- **Bandeja (`Radial.App/Tray/TrayIcon`):** «Mostrar ventana de diagnóstico» y «Salir de Radial»; la X de la ventana la oculta. Al cerrarse la sesión de Windows se permite cerrar. Sin autoarranque ni instancia única (fase 7).

## Estética

Oscura, translúcida, minimalista, grafito con acento frío. Sin exceso de gradientes ni brillos.

## Comandos

```
dotnet restore
dotnet build src/Radial.App/Radial.App.csproj -c Release
dotnet run --project src/Radial.App
```

Las pruebas (`dotnet test -c Release`, solución completa) **no se lanzan en el equipo de desarrollo** (Smart App Control, ver arriba): las ejecuta GitHub Actions. En un equipo sin esa restricción, el flujo completo es `dotnet restore`, `dotnet build -c Release` y `dotnet test -c Release`.

## Núcleo (`Radial.Core`) — implementado en la fase 1

- `Geometry`: `RadialGeometry` (funciones puras), `SectorSelector` (histéresis, margen en `[0, 22.5)`), `RadialDirection`, `PointD`.
- `Model`: `MenuId`/`ItemId` tipados, `RadialConfiguration`, `RadialMenu`, `RadialItem`, `RadialAction` y sus derivadas (solo datos).
- `Engine`: `RadialEngine` (`Start`, `UpdatePointer`, `Tick`, `Confirm`, `Cancel`), `RadialResult`. Nunca ejecuta acciones.
- `Validation`: `ConfigurationValidator`, códigos estables en `ValidationCodes`. Las pruebas comprueban códigos, no mensajes.
- `Timing`: `IClock` monotónico. Las pruebas usan `ManualClock`; nunca esperas reales. `MonotonicClock` es el reloj de producción (fase 4, paso 1).
- `Execution` (fase 4, paso 1): `IActionExecutor` (`CanExecute` + `ExecuteAsync` con `CancellationToken`) y `ActionExecutionResult` (`Succeeded`/`Failed` con `Error` código+mensaje/`Cancelled`). Solo contratos: los ejecutores concretos viven en `Radial.Windows`. Un ejecutor devuelve los fallos previsibles y la cancelación como resultado, no lanza.
- `Storage` (fase 5): `ConfigurationFile` (JSON de la configuración, límites, versiones), `ConfigurationMigrator`, `ISettingsStore` y resultados; ver la sección «Configuración y almacenamiento».
- `Model.UnknownAction` (acción de tipo desconocido: se conserva, nunca se ejecuta), `RadialItem.DefinitionId` (colocación frente a definición) y `CancelReason.UnsupportedAction`.

Convenciones del núcleo:
- Un tipo de acción nuevo = un `record` que hereda de `RadialAction` e implementa `GetProblems()`.
- `MaxDepth` cuenta los menús apilados, raíz incluida (3 = raíz + 2 niveles).
- `DeadZoneRadius` > 0 y `HysteresisDegrees` en `[0, 22.5)` en la configuración.
- `Start` con el motor activo reinicia la interacción; `Confirm`/`UpdatePointer`/`Tick` inactivo se ignoran.
- Los mensajes de validación están en español; los identificadores y los códigos, en inglés.
- Recorridos de grafos iterativos (la configuración importada no es de confianza). Búsquedas por conjunto, no por lista.

## Estado

Fases 0 y 1 completadas. Fase 2 cerrada con cobertura parcial y decisión aceptada (ADR-001). **Fase 3 aprobada** (validación manual con hardware por el usuario; sin validar al cierre: bloqueo/suspensión reales, otros DPI, varios monitores, Windows 11 25H2; ver 16.12).

**Fase 4 (overlay real y acciones): implementada, con validación automática confirmada y validación manual básica parcial. NO está aprobada manualmente.** *Automáticas:* ejecución de CI sobre `d4b3f8a` (cierre técnico: 4.1, 4.2, I1–I6, protección de navegación y fallo seguro del lector) en verde, verificada en la API de GitHub, <https://github.com/xrbrr24/Radial/actions/runs/41900198820>; cifras comunicadas por el usuario: 694 Core y 992 Windows (no comprobables sin autenticación). *Manuales (por el usuario, informe):* overlay con el gesto real, selección, confirmar y cancelar; Copiar, Pegar, Deshacer, Seleccionar todo, Guardar; abrir Documentos, Wikipedia y Edge. *Pendientes (aplazados):* P1 efecto real del click-through, P3 vencimiento de 5 s con el gesto real, P4 caminos de fallo a mano, P5 Esc y Ctrl por separado, P6 autoprueba tras la 4.1/4.2, P9 aplicaciones elevadas (UIPI), cierre sin procesos colgados, DPI distintos del 125 %, varios monitores, bordes de pantalla, 25H2 y rendimiento medido (17.11 y 17.12). La limitación de ejecución local no impide continuar el desarrollo, pero no convierte nada de lo anterior en «probado».

**Fase 5 (submenús, configuración persistente, bandeja): IMPLEMENTADA; pruebas automáticas EN VERDE en CI (commit `1772b59`, ejecución nº 7, <https://github.com/xrbrr24/Radial/actions/runs/42001660093>; cifras por suite sin comprobar) y SIN validar a mano.** El producto compila en Release sin avisos; las pruebas no se compilan ni se ejecutan en el equipo de desarrollo (SAC): las dos primeras ejecuciones de CI de la rama (`6eb9977`, `0932f25`) fallaron al compilar las pruebas (un `using` ausente y un aviso del analizador de xUnit) y se corrigió en `0932f25` y `1772b59`. Comprobado en el equipo sin entrada ni hooks: `--render-preview` (dibujo de grupos, progreso, retroceso y acción desconocida) y `--overlay-check` (cambio de menú con la ventana visible: recentrado a ±1 px, foco y estilos intactos, un monitor al 125 %). Pendiente a mano: gesto real con grupos y tres niveles, retroceso, configuración en el primer inicio, reinicio, archivo corrupto, bandeja y «Salir» sin procesos colgados, fluidez/parpadeo, y todo lo que ya figura sin validar de DPI, monitores y bordes (sección 18 del documento técnico).

Roadmap: fase 3 captura global · fase 4 overlay real (variante A) y acciones · fase 5 submenús y configuración persistente, bandeja (MVP) · fase 6 ajustes y editor visual con biblioteca de acciones · fase 7 apariencia y temas, autoarranque, instancia única · fase 8 importar/exportar, multimonitor y DPI mixto, empaquetado y firma. **No se avanza a la fase 6 sin aprobación expresa.**
