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
- **Dónde se validan las pruebas automáticas: GitHub Actions** (`.github/workflows/radial-windows-validation.yml`, runner `windows-latest`; se lanza con push a cualquier rama, con `pull_request` y con `workflow_dispatch`, este último solo desde la interfaz de GitHub cuando el workflow exista en la rama predeterminada). Es la referencia vigente; la regla anterior sobre el equipo de desarrollo no cambia. **Única ejecución de CI confirmada: 622 Core + 522 Windows en verde** (cifras comunicadas por el usuario, no comprobables sin autenticación; el estado verde de la ejecución nº 2 sobre `349fbde`, <https://github.com/xrbrr24/Radial/actions/runs/41624193526>, sí está verificado en la API de GitHub y esa ejecución es anterior a la 4.1, la 4.2 y el cierre técnico: no cubre su código). 660/574 (informe de la 4.1) y 694/855 (informe de la 4.2) son cifras de informe de procedencia no confirmada: conservarlas como histórico, **no presentarlas como resultados de CI**. Que `Radial.App` se haya ejecutado a mano en el equipo no demuestra que Smart App Control no vuelva a bloquear otra compilación (documento técnico, 15.5).
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
- `Radial.App` (`net10.0-windows`, WPF): único proyecto con WPF. Composición, overlay, ajustes, bandeja.
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

- `Radial.Windows/Overlay`: `RadialOverlayCoordinator` (gesto → motor → ventana → **acción**: con un `IActionExecutor` ejecuta el elemento confirmado sin bloquear y lo comunica con `ActionFinished`; informa también con `Completed`/`Aborted`), `IRadialPresenter`, `OverlayPlacement` (cálculo puro) y `OverlayWindowInterop` (estilos, colocación en píxeles físicos, DPI). `Radial.Core/Geometry/RadialLayout`: disposición pura del dibujo, con los mismos ángulos que la selección. `Radial.App/Overlay`: `OverlayWindow` (se crea una vez, `Prepare()`, mostrar/ocultar), `RadialView` (geometría `StreamGeometry`, nunca botones), `RadialTheme`, `GlyphCatalog`, `ProvisionalMenu`.
- **El menú es PROVISIONAL (ocho elementos fijos, hasta la fase 5).** Los destinos de apertura (navegador, carpeta, URL) están en `ProvisionalTargets`, en un solo sitio y marcados como provisionales; no se inventan rutas: salen de carpetas conocidas del sistema y, si no existen, la acción falla con `TargetNotFound` y se comunica. Desde la 4.2 las acciones SE EJECUTAN (ver la sección siguiente).
- **Submenús: implementados en el núcleo (`RadialEngine`: permanencia, recentrado, retroceso armado, progreso), NO integrados en el overlay (fase 5).** `IRadialPresenter`, `OverlayWindow` y `RadialView` solo representan el menú raíz. **Protección temporal:** si el motor pasa a un menú distinto del mostrado (en el sondeo o con el propio evento de soltar), `RadialOverlayCoordinator` cancela el gesto, oculta el overlay, no ejecuta nada y avisa con `UnsupportedNavigation`; la selección del otro menú nunca llega al presentador. No quitarla hasta que la fase 5 represente el cambio de menú (y entonces sustituirla por pruebas de la navegación real). El menú provisional no tiene grupos, así que hoy no se alcanza en la aplicación.
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

Convenciones del núcleo:
- Un tipo de acción nuevo = un `record` que hereda de `RadialAction` e implementa `GetProblems()`.
- `MaxDepth` cuenta los menús apilados, raíz incluida (3 = raíz + 2 niveles).
- `DeadZoneRadius` > 0 y `HysteresisDegrees` en `[0, 22.5)` en la configuración.
- `Start` con el motor activo reinicia la interacción; `Confirm`/`UpdatePointer`/`Tick` inactivo se ignoran.
- Los mensajes de validación están en español; los identificadores y los códigos, en inglés.
- Recorridos de grafos iterativos (la configuración importada no es de confianza). Búsquedas por conjunto, no por lista.

## Estado

Fases 0 y 1 completadas. Fase 2 cerrada con cobertura parcial y decisión aceptada (ADR-001). **Fase 3 aprobada** (401 pruebas automáticas en verde: 252 Core + 149 Windows; autoprueba sintética; validación manual con hardware y cierre durante la autoprueba realizados por el usuario; auditoría D1–D3 corregida). Sin validar al cierre: bloqueo/suspensión reales, otros DPI, varios monitores, Windows 11 25H2, Ctrl atascado (hipótesis); ver 16.12 del documento técnico. **Fase 4 (overlay real y acciones: pasos 1 a 3, 4.1 y 4.2) implementada y validada parcialmente; PENDIENTE DE APROBACIÓN EXPRESA, no aprobada.** *Automáticas:* **confirmado con GitHub Actions: 622 pruebas de Core y 522 de Windows en verde** (cifras comunicadas por el usuario; la ejecución verde sobre `349fbde` está verificada en la API de GitHub, es anterior a la 4.1 y no cubre el código posterior). **Comunicado por los informes de implementación, NO confirmado por CI:** 660 Core / 574 Windows (4.1) y 694 Core / 855 Windows (4.2). Las pruebas automáticas de la 4.1 y la 4.2 están pendientes de confirmación por CI (documento técnico, 15.5 y P21 de 17.11). Pasos 1 y 2 verificados al cerrarlos. El paso 3 quedó sin verificar en el equipo de desarrollo por el bloqueo de Smart App Control (17.8); el equipo de desarrollo sigue sin lanzar las pruebas de Windows. *Manuales (por el usuario):* overlay con el gesto real, selección de sectores, confirmar y cancelar; Copiar, Pegar, Deshacer, Seleccionar todo, Guardar; abrir Documentos, Wikipedia y Edge. *Cierre técnico posterior a la auditoría (documento técnico, 17.12, v0.16):* I1, I2, I4, I5 e I6 (parte inmediata) implementados; el producto compila en Release sin avisos (`Radial.App` y sus dependencias); sus pruebas nuevas y las de la protección contra la navegación no soportada (siguiente frase) están **escritas, sin compilar ni ejecutar en el equipo de desarrollo (SAC) y sin confirmar por CI** hasta que se anote una ejecución del workflow sobre el commit que las contiene; I3 es una limitación documentada; nada de esto se ha probado a mano todavía. *Protección contra navegación no soportada* (17.13): el coordinador cancela el gesto, sin ejecutar nada, si el motor cambia de menú; los submenús siguen pendientes de integrar en el overlay (fase 5). *Pendientes:* efecto real del click-through, vencimiento de 5 s con el gesto real, caminos de fallo a mano, DPI distintos del 125 %, varios monitores, aplicaciones elevadas, bordes de pantalla, 25H2, rendimiento medido, autoprueba tras la 4.1/4.2 y tras I5 (documento técnico, 17.11 y 17.12), CI sobre el commit que contenga todo (P20/P21; la ejecución sobre el commit de cierre se anota cuando se haya consultado, sin inventar cifras). Las cifras de 17.9 (660/574) y 17.10 (694/855) son históricas, de informe y sin procedencia confirmada. Ver las secciones 10, 12, 14, 15, 16 y 17.

Roadmap: fase 3 captura global · fase 4 overlay real (variante A) y acciones · fase 5 submenús y configuración (MVP) · fase 6 ajustes y editor visual · fase 7 apariencia y sistema · fase 8 importar/exportar y distribución.
