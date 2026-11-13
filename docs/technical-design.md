# Radial — Documento técnico v0.28

Estado: **Fase 6 (biblioteca de acciones y editor radial): implementada, con CI en verde (`abca8b2`, ejecución nº 14) y sin validar a mano (19.1); las fases 4 y 5 siguen sin estar aprobadas manualmente.** **Fases 0, 1, 2 y 3 cerradas** (la fase 3 aprobada tras la validación manual con hardware y la auditoría final; limitaciones sin validar en 16.12). La fase 2 se cerró con cobertura parcial (sección 10, [ADR-001](adr/ADR-001-overlay-rendering.md)). **Fase 4 (overlay real y acciones): implementada, con validación automática confirmada y con validación manual básica parcial; NO está aprobada manualmente.** El commit `d4b3f8a` (que incluye la 4.1, la 4.2, el cierre técnico I1–I6, la protección de navegación y el fallo seguro del lector) pasó el workflow de GitHub Actions: ejecución nº 4, <https://github.com/xrbrr24/Radial/actions/runs/41900198820>, estado verde verificado en la API de GitHub; las cifras 694 Core / 992 Windows las comunicó el usuario y no son comprobables sin autenticación (15.5). Siguen **aplazadas** las pruebas manuales P1, P3–P6 y P9, el cierre sin procesos colgados y todo lo de DPI, varios monitores, bordes y 25H2 (17.11, 17.12). **Fase 5 (submenús en el overlay, configuración persistente en JSON, bandeja): implementada; compilación y pruebas automáticas en verde en CI (commit `1772b59`, ejecución nº 7, <https://github.com/xrbrr24/Radial/actions/runs/42001660093>, estado verde verificado en la API; cifras por suite sin comprobar), y no se ha probado a mano** (sección 18). Todo lo demás (fases 6, 7 y 8) sigue siendo diseño.

Convención: **[Comprobado]** = verificado con documentación oficial o en este equipo. **[Por validar]** = propuesta que debe confirmarse con un prototipo o prueba.

## 0. Historial de cambios

| Versión | Cambios |
|---|---|
| **v0.28** | **Acciones útiles además de atajos y versión portátil** (sección 21). Cuatro capacidades de punta a punta (modelo, `config.json`, validación, editor, dibujo en el radial, ejecución sin bloquear la interfaz): **insertar texto** (`TypeTextAction`, entrada Unicode `KEYEVENTF_UNICODE`, sin portapapeles), **volumen** (`VolumeAction`, Windows Core Audio: subir, bajar, fijar nivel, alternar silencio), **controlar la ventana activa** (`WindowControlAction`: minimizar, maximizar, restaurar, cerrar; nunca Radial, el escritorio ni la barra de tareas) y **argumentos y directorio de trabajo de «iniciar aplicación»** (`LaunchApplicationAction.Arguments` pasa de texto a LISTA, `ProcessStartInfo.ArgumentList`; el formato antiguo de texto se sigue leyendo). Ejemplo en `docs/examples/config-acciones-nuevas.json`. Publicación autocontenida de archivo único (`tools/publish-portable.ps1`) y trabajo `portable` de CI que solo corre tras `validate`. Pruebas escritas y sin ejecutar en el equipo de desarrollo (15.5); sin validar a mano (21.9). |
| **v0.27** | **Corrección de alcance antes de fusionar la fase 6.** Se retiran de la entrega funciones de la fase 7 que no estaban aprobadas: instancia única (`SingleInstance`: mutex, tubería y activación de otra instancia), autoarranque (`StartupRegistration`, opción «Iniciar con Windows» en Ajustes), arranque oculto (`--background`) y `WindowPlacementStore` (código sin uso). **Siguen pendientes de la fase 7.** Los fallos de **acciones** dejan de avisarse por la bandeja: se registran en disco (`ErrorReporter.RecordProblem`) y se dicen dentro de la ventana, como antes de la entrega (20.2, 20.7 y 20.8). Se conserva el sistema de avisos (`NotificationRouter`, canales, limitador) para los errores de arranque, configuración, captura y de la propia aplicación, y `BuildIdentity`. |
| **v0.26** | **El seguimiento entre monitores no se activaba en la aplicación real.** Causa: `CaptureHost` construía `RadialOverlayCoordinator` sin el `IMonitorLocator` (parámetro opcional, nulo), así que `FollowMonitor` retornaba siempre; las pruebas y `--edge-check` lo construían con él. Corrección: `RadialOverlayCoordinatorFactory` (única composición de producción, usada también por `--edge-check`), diagnóstico `FollowTrace` («[monitores]» en Diagnóstico) y `PointerPacking` (posición del hook con signo, probada con coordenadas negativas). Pruebas escritas y sin ejecutar (15.5); sin validar a mano. |
| **v0.25** | **El radial sigue al cursor entre monitores** (6.4). `IMonitorLocator`, `MonitorFollow` (regla pura con histéresis de 12 DIP), `IRadialPresenter.FollowCursor` y `OverlayWindowInterop.FollowCursor` (monitor del cursor, DPI recalculado, desfase en DIP). Con desplazamiento forzado por el borde, la selección se suspende hasta que el cursor se mueve (nunca un salto de sector). `--edge-check` recorre ahora los cruces reales en ambos sentidos. Pruebas escritas y sin ejecutar (15.5); DPI distintos solo probados con monitores falsos; sin validar a mano. |
| **v0.24** | **Bordes: la prueba manual vio el menú cortado.** Causa comprobada: se ejecutaba una compilación del 5-nov (`src/Radial.App/bin/Release`) sin ningún código de ajuste a bordes (verificado por la ausencia de `MonitorFromPoint`/`ArmDistanceDip`/`PlacementChanged` en su `Radial.Windows.dll`), mientras las compilaciones de la sesión iban a una carpeta temporal por la regla de no pisar una instancia en marcha; con instancia única, abrir la nueva solo trae al frente la vieja. Medidas: `BuildIdentity` (fecha de compilación en el título, la bandeja y el registro), `--edge-check` (ruta real coordinador + ventana en cada borde/esquina de cada monitor; 192 comprobaciones correctas en este equipo, dos monitores a 125 %), y el ajuste pasa al área de trabajo (una barra anclada ya no puede tapar el radial). Sin resolver: barra de tareas oculta automáticamente. Pruebas escritas y sin ejecutar (15.5). |
| **v0.23** | **Acciones inertes y DPI.** Una acción inválida al cargar conserva identidad, vínculo con la biblioteca y colocaciones al guardar y reiniciar (`ConfigurationFile.IsLinked`; antes se guardaba en línea y se desvinculaba); el aviso es uno por acción; sus mensajes solo ofrecen eliminarla o corregir `config.json` con Radial cerrado. Copias de seguridad con acciones inválidas: recuperables con el mismo criterio tolerante, restauradas byte a byte y con sus avisos. `IRadialPresenter.PlacementChanged`: el coordinador se entera de que la ventana se recolocó sola por un cambio de DPI. Corregido un olvido de la v0.22: dos dobles de `IRadialPresenter` de las pruebas seguían devolviendo `double` y no habrían compilado. Pruebas escritas y sin ejecutar (15.5); sin validar a mano. |
| **v0.22** | **Bordes de pantalla (E-1) resuelto con la opción C** (6.4): `OverlayPlacement.Fit` desplaza el radial hacia dentro lo justo para caber en el monitor del cursor, el motor mide desde el centro dibujado (`PresentedPlacement`) y no hay selección hasta que el cursor se mueve 10 DIP. **Política de destinos única en Core** (`ActionTargets`, antes `TargetPolicy` de Windows): ejecutores, formulario del editor y validador (aviso `action.target-rejected`). **Carga tolerante de `config.json`**: una acción de tipo conocido con errores propios se neutraliza sola (`UnknownAction` con su JSON íntegro, aviso `config.action-neutralized`) en lugar de poner en cuarentena toda la configuración; el editor y `Parse` por defecto siguen estrictos. `ProcessActionExecutor` hace sus comprobaciones de disco fuera del hilo de interfaz. Pruebas escritas y sin ejecutar (15.5); sin validar a mano. |
| **v0.21** | **S2: fiabilidad y gestión de errores** (nueva sección 20). Registro persistente en `%LOCALAPPDATA%\Radial\logs\` con rotación y retención, captura global de excepciones (interfaz, otros hilos, tareas sin observar, arranque y cierre) con clasificación recuperable/degradado/fatal y presupuesto de errores, avisos al usuario que funcionan con Radial oculto en la bandeja (globo y banner, con limitación de repeticiones), marcador de cierre grave y fallos de acciones y de captura comunicados por este sistema. Pruebas escritas y sin ejecutar (15.5); sin validar a mano. |
| **v0.20** | **Fase 6** (nueva sección 19). Biblioteca de acciones persistente independiente de las colocaciones (`RadialConfiguration.Actions`, categorías abiertas, etiquetas, reutilización), esquema versión 2 con migración real desde la 1 (`SchemaV1ToV2Migration`), vocabulario de teclas ampliado a 118 nombres estables por tecla virtual con captura de atajos y nombres según el teclado activo (sustituye al vocabulario cerrado de 66 de 17.5 y 17.6), `ConfigurationEditor`/`ActionSearch`/`ConfigurationSession` en Core, centro de control (editor radial con la misma vista previa del overlay, biblioteca, ajustes y diagnóstico) con tema centralizado, modelos de vista sin WPF en `Radial.Windows/Presentation` y `CaptureHost`. CI en verde en `abca8b2` (ejecución nº 14); sin validar a mano; las fases 4 y 5 siguen sin aprobarse manualmente. |
| **v0.19** | **Fase 5** (nueva sección 18). Submenús conectados al overlay (`ShowMenu`, `SetProgress`, ancla de coordenadas y red de seguridad que sustituye a la protección temporal de 17.13), dibujo de grupos y progreso; configuración persistente en `%LOCALAPPDATA%\Radial\config.json` (JSON con `schemaVersion`, contexto `System.Text.Json` generado, migraciones encadenadas, versión futura intacta, cuarentena y recuperación, copias de seguridad, escritura atómica); acciones desconocidas conservadas y nunca ejecutadas (7.1); separación entre definición de acción y colocación (`DefinitionId`); configuración predeterminada generada (las ocho acciones, con un grupo «Abrir»); bandeja del sistema; la captura empieza sola. Documentación al día: CI confirmada en `d4b3f8a` (694 Core / 992 Windows según el usuario), fase 4 **no** aprobada manualmente. **Pruebas de la fase 5 en verde en CI en `1772b59` (las ejecuciones previas fallaron solo al compilar las pruebas, ver 18.1) y sin validar a mano.** |
| **v0.18** | **Fallo seguro del ciclo de vida de la captura** (nueva 17.14). Si el lector de eventos termina sin que se haya pedido, `GestureReaderSupervisor` detiene la captura como fallo (`GlobalInputService.Fail`: hooks fuera, gesto cancelado, estado `Faulted`), oculta el overlay y lo comunica una sola vez; el cierre normal no es un fallo. Pruebas escritas, sin ejecutar localmente y pendientes de CI. La fase 4 sigue pendiente de aprobación expresa. |
| **v0.17** | **Cierre de la auditoría de la fase 4** (nueva 17.13). Código: protección temporal contra la navegación entre menús no soportada por el overlay (`RadialOverlayCoordinator.UnsupportedNavigation`) y pruebas específicas. Workflow de CI: se lanza con push a cualquier rama, con `pull_request` y con `workflow_dispatch`. Documentación sincronizada: README, menú provisional de 17.9, orden del historial, cifras de pruebas (qué está confirmado por CI y qué es solo cifra de informe) y regla de qué se puede lanzar en el equipo de desarrollo. **Los submenús siguen sin estar integrados en el overlay (fase 5).** La fase 4 sigue pendiente de aprobación expresa. |
| **v0.16** | **Cierre técnico de la fase 4 tras la auditoría I1–I6** (nueva 17.12). Código: lector resiliente del canal (I1) y aislamiento de suscriptores del coordinador (I2); liberación comprobada y con un reintento en `SendInputKeyboardInjector` y código `KeyReleaseFailed` (I4); marca `GestureEvent.SyntheticWindow` publicada por el servicio como barrera central contra la autoprueba (I5, campo aditivo en la fase 3); `TargetPolicy` en los ejecutores de aplicación, archivo y carpeta y código `TargetNotAllowed` (I6, parte inmediata); primer plano nulo ⇒ `ForegroundChanged`; cierre robusto de la ventana. I3 queda como **limitación documentada** (sin detección de destino elevado). Según el informe de cierre, compilado en Release sin avisos; **pruebas nuevas no ejecutadas localmente (Smart App Control) ni confirmadas por CI** (su compilación y ejecución las valida GitHub Actions). La fase 4 sigue pendiente de aprobación expresa. |
| v0.15 | **Cierre documental de la fase 4** (nueva sección 17.11; solo documentación, sin cambios de código). Se registra la validación manual del usuario de las fases 4.1 y 4.2, se separan pruebas automáticas, manuales y pendientes, y se resuelven las contradicciones sobre Smart App Control y dónde se ejecutan las pruebas: la validación automática de referencia es **GitHub Actions** (nueva 15.5); el equipo de desarrollo sigue sin lanzar las pruebas de Windows. **Corrección posterior (misma versión):** la última ejecución de CI confirmada es de **622 Core / 522 Windows**; 660/574 (4.1) y 694/855 (4.2) son cifras de los informes de implementación, sin procedencia confirmada, y no se presentan como resultado de CI. Estado de la fase 4 y de su fila en la sección 12 actualizados: implementada y validada parcialmente, **pendiente de aprobación expresa**. |
| v0.14 | Fase 4.2: **motor de acciones y primeros atajos funcionales** (17.10). `ShortcutActionExecutor` (un único ejecutor para todos los atajos, con la espera de 3.4), `SendInputKeyboardInjector` y `Win32ModifierStateReader` en `Radial.Windows`; `ActionDefinition`/`ActionCatalog`/`BuiltInShortcuts` y `CompositeActionExecutor` en Core; el coordinador ejecuta la acción confirmada y el menú provisional tiene ocho acciones reales. Códigos de error nuevos: `ModifiersNotReleased`, `ForegroundChanged`, `InputInjectionFailed`, `UnexpectedError`. Precisión sobre 3.4 (la espera se sondea, no usa `ChordTracker`). Sin validación manual todavía. La fase 4 no está aprobada. |
| v0.13 | Fase 4.1: **primera interfaz radial visible** (17.9). `RadialLayout` en Core; colocación y estilos del overlay (`OverlayPlacement`, `OverlayWindowInterop`) y `RadialOverlayCoordinator` en `Radial.Windows`; `OverlayWindow`, `RadialView` y un menú provisional de ocho elementos en `Radial.App`. El overlay muestra el radial con el gesto real y resalta el sector bajo el cursor; **no ejecuta acciones**. Sin cambios en la captura global ni en sus protecciones. Sin validación manual todavía. La fase 4 no está aprobada. |
| **v0.12** | Fase 4, **paso 3** (de varios): `IProcessLauncher`, `ShellProcessLauncher` y los ejecutores de aplicación, archivo, carpeta y URL en `Radial.Windows` (17.7). **`OpenUrlAction` solo admite `http` y `https`** (se retira `mailto:`), con una única definición en Core (`OpenUrlAction.AllowedSchemes`). Códigos de error nuevos: `TargetNotFound`, `LaunchFailed`, `UrlNotAllowed`. **Verificación incompleta:** Smart App Control bloqueó la carga de `Radial.Windows.Tests.dll` y, tras el ajuste de URL, de `Radial.Core.Tests.dll` (17.8). El paso 3 no está aprobado ni la fase 4. |
| v0.11 | Corrección del paso 2: el vocabulario de teclas de un atajo (66 nombres) tiene **una única fuente de verdad en `Radial.Core`** (`KeyVocabulary`). `SendKeysAction` lo usa para validar y `ShortcutParser` (Windows) solo asigna códigos VK y `Extended`; se acabó la incoherencia entre validar y poder ejecutar (17.6). Se eliminó `SendKeysAction.MaxKeyLength`. La fase 4 no está aprobada. |
| v0.10 | Fase 4, **paso 2** (de varios): parser de atajos (`ShortcutParser`) en `Radial.Windows`, con vocabulario cerrado de 66 teclas (17.5). Solo traducción a códigos de tecla virtual: sin envío de teclas, sin P/Invoke, sin cambios en `Radial.Core` ni en la captura global. La fase 4 no está aprobada. |
| v0.9 | Fase 4, **paso 1** (de varios): contratos de ejecución de acciones (`IActionExecutor`, `ActionExecutionResult`, `ActionErrorCodes`) y reloj de producción (`MonotonicClock`) en `Radial.Core`. Sin ejecutores concretos ni cambios en `Radial.Windows` ni `Radial.App`. La fase 4 no está aprobada. Nueva sección 17. |
| v0.8 | Fase 3 **aprobada**. Se registra el resultado de la validación manual con hardware y del paso 14 (cierre durante la autoprueba), realizados por el usuario (16.9 y 16.10), y lo que sigue sin validar (16.12). Solo documentación: sin cambios de código respecto a la v0.7. |
| v0.7 | Correcciones de la auditoría final de la fase 3 (D1, D2, D3): la autoprueba comprueba cursor, ventana bajo el cursor y ventana en primer plano antes de **cada** inyección, marca sus eventos (`dwExtraInfo`, 32 bits) y solo acepta los propios; un segundo servicio ya no deja el estado en `Starting`; cerrar la ventana durante la autoprueba la cancela y libera de forma síncrona lo que pulsó. Secciones 16.1, 16.2, 16.4, 16.6, 16.7, 16.9 y 16.11. |
| v0.6 | Fase 3 implementada: captura global (`GlobalInputService`, hooks de bajo nivel en un hilo dedicado, `ChordTracker`, watchdog, sonda de salud, diagnóstico) en `Radial.Windows`, y ventana de diagnóstico en `Radial.App`. **Cambio de diseño respecto a v0.5/3.3: el watchdog ya no comprueba el botón central con `GetAsyncKeyState`**, porque la autoprueba demostró que Windows no refleja un evento consumido por un hook (sección 16.6). Nueva sección 16. |
| v0.5 | Fase 2 cerrada con cobertura parcial (un monitor al 125 %, una ejecución válida). Overlay de producción: **ventana layered de WPF (variante A)**, sin Acrylic, Mica ni técnicas DWM; el blur deja de ser requisito del MVP ([ADR-001](adr/ADR-001-overlay-rendering.md)). Nueva sección 10 con resultados y anomalías sin explicar, y sección 15 sobre Smart App Control. Fases 4 y 7 y el criterio 9 del MVP actualizados. |
| v0.4 | Fase 1 implementada: modelo de dominio, geometría, histéresis, motor, submenús y validador en `Radial.Core`. La fórmula de normalización de la sección 5 se corrige para reflejar el código (`%` con corrección explícita). Nueva sección 14. Cambios de diseño respecto a v0.3 listados en 14.6. |
| v0.3 | Compatibilidad oficial: **Windows 11 x64 estable 25H2+**, sin Windows 10. Corrección: la build 26300 es Windows 11 **26H2**, disponible en el canal de disponibilidad general desde el 29-sep-2026, no una build Insider. E-1 (bordes) vuelve a **abierto** con alternativas documentadas. Timeout de atajos 5 s configurable. Primera escritura por creación y siguientes por reemplazo atómico. Estrategia explícita para acciones de tipo desconocido. Eliminada la resincronización con `GetAsyncKeyState` en callbacks. Resultados de la fase 0. |
| v0.2 | Ctrl primero obligatorio, geometría normalizada, DPI con `GetDpiForWindow`, hooks con eventos críticos en orden, almacenamiento en `%LOCALAPPDATA%`, spike de blur, fases 0 a 8. |

## 1. Compatibilidad y entorno

- **Objetivo oficial v1:** Windows 11 x64, versiones estables 25H2 y posteriores. Sin soporte oficial para Windows 10.
- **Desarrollo:** Windows 11 Pro 26H2 (build 26300). No se depende de comportamientos exclusivos de esta build. **Antes de dar el producto por terminado hay que validar en una versión estable** (fase 8).
- **Herramientas [Comprobado]:** SDK .NET 10.0.403, runtimes 10.0.14 (incluye WindowsDesktop.App), MSBuild 18.9.15, Git 2.56.0. Sin Visual Studio ni workloads. Soporte de .NET 10 LTS hasta el 14-nov-2028 ([política de soporte](https://dotnet.microsoft.com/en-us/platform/support/policy)).

## 2. Arquitectura

```
                 ┌──────────────┐
                 │  Radial.App  │  WPF: overlay, ajustes, bandeja, composición
                 └──┬────────┬──┘
          ┌─────────▼──┐   ┌─▼───────────┐
          │Radial.Windows│─▶│ Radial.Core │  net10.0 puro, sin dependencias
          └────────────┘   └─────────────┘
 Radial.Core.Tests ──▶ Core        Radial.Windows.Tests ──▶ Windows, Core
```

| Proyecto | Responsabilidad |
|---|---|
| **Radial.Core** (`net10.0`) | Geometría, motor de selección, máquina de estados, modelo de configuración, validación, migraciones y abstracciones (`IClock`, `IActionExecutor`, `IProcessLauncher`, `ISettingsStore`). Sin APIs de Windows ni WPF. |
| **Radial.Windows** (`net10.0-windows`, sin WPF) | Hooks, `ChordTracker`, `SendInput`, DWM, DPI y monitores, ejecutores de acciones, almacenamiento en disco (autoarranque: fase 7, pendiente). |
| **Radial.App** (`net10.0-windows`, WPF) | Composición manual de dependencias, overlay, `RadialView`, ViewModels, ajustes, bandeja (instancia única: fase 7, pendiente). |
| **Radial.Core.Tests / Radial.Windows.Tests** | xUnit con aserciones nativas y dobles a mano. |

Fuera de la solución: `spikes/Radial.BlurSpike` (fase 2, cerrada). Es código desechable y no ejecutable hoy en el equipo de desarrollo (sección 15); parte de él nunca se ejecutó (sección 10.5).

## 3. Activación y captura global

### 3.1 Gesto

1. Se mantiene Ctrl.
2. Se pulsa el botón central **con Ctrl ya pulsado**. Se abre el radial y ese clic se traga.
3. Se mueve el ratón.
4. Al soltar el botón central se confirma.
5. Después se puede soltar Ctrl.

Esc o soltar Ctrl antes de confirmar cancela. Si el botón central se pulsa **sin Ctrl previo**, no se activa nada y el clic **llega íntegro a la aplicación**; ese pulso queda marcado como *foráneo* hasta que el botón se suelte, aunque se pulse Ctrl después. El gesto (tecla y botón) será configurable; la regla de orden no lo es en la v1.

### 3.2 Hilo de hooks

```
 hilo de hooks (bucle de mensajes propio)                hilo de aplicación
 ┌───────────────────────────────────────────┐
 │ WH_KEYBOARD_LL / WH_MOUSE_LL callback     │
 │  1. ChordTracker.Process(evento) → ¿tragar?│
 │  2. teclas/botones → Channel (en orden)    │──────▶  RadialEngine / Dispatcher
 │  3. movimiento → ranura "última posición"  │──────▶  se lee en cada Tick
 │  4. return (tragar o CallNextHookEx)       │
 └───────────────────────────────────────────┘
```

- **Eventos críticos** (pulsación y liberación de teclas y botones): struct pequeño con la posición del cursor en ese instante. Van por un `Channel` con un único lector, sin descartes y en orden. Así la selección al soltar es exacta aunque se hayan coalescido movimientos.
- **Movimientos:** solo se conserva la última posición. Se pueden coalescer; los cambios de estado de teclas y botones no.
- **Fuente de verdad:** el `ChordTracker` es una máquina de estados alimentada únicamente por los eventos de los hooks. **No se usa `GetAsyncKeyState` dentro de los callbacks** para conocer la transición que acaba de producirse.
- **Qué se traga:** el botón central (pulsación y liberación) solo si se activó el chord; Esc mientras está activo. Ctrl nunca se traga.
- **Eventos inyectados** (`LLKHF_INJECTED`, `LLMHF_INJECTED`): se ignoran.
- **Instrumentación:** tiempo máximo del callback, visible en el diagnóstico.
- `Channel<T>` asigna memoria de forma ocasional. **[Por validar]** con medición; si fuera problemático, anillo preasignado.

### 3.3 Limitaciones

- **Eliminación silenciosa [Comprobado]:** si el callback supera `LowLevelHooksTimeout` (tope de 1000 ms), Windows elimina el hook sin avisar ([documentación](https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc)). **No se garantiza ninguna recuperación automática.** Se reducen las probabilidades con callbacks mínimos y medición. El estado conocido del hook se muestra en el diagnóstico y en la bandeja, y el usuario puede reiniciar la captura manualmente. Reinstalar al reanudar o desbloquear la sesión es una acción de mejor esfuerzo, sin garantías.
- **Estado de Ctrl obsoleto:** si se pierde un evento de liberación de Ctrl, el tracker puede creer que sigue pulsado. Como no se consulta `GetAsyncKeyState` dentro del callback, la defensa es el **watchdog**: un temporizador fuera de los callbacks (~50 ms) que, mientras el chord está activo, contrasta Ctrl con `GetAsyncKeyState` y comprueba que el hook de ratón sigue entregando movimiento. **No comprueba el botón central:** un evento consumido por un hook no actualiza el estado asíncrono de Windows (medido, sección 16.6). Esto protege contra estados atascados de Ctrl y contra un hook de ratón que deja de funcionar durante un gesto; no protege contra la pérdida silenciosa del hook de teclado ni contra la liberación perdida del botón central.
- **UIPI:** con una ventana elevada en primer plano no recibimos sus eventos ni podemos enviarle teclas.
- **Pantallas seguras:** UAC y Ctrl+Alt+Supr no son interceptables.
- **Juegos:** el overlay no se verá en pantalla completa exclusiva. Los antitrampas pueden reaccionar a hooks globales. **[Por validar]**.

### 3.4 Atajos de teclado

- Las acciones de teclado **esperan a que se liberen los modificadores físicos** (el Ctrl de activación y cualquier otro que afecte). Las demás acciones se ejecutan al instante.
- **Timeout predeterminado: 5 s, configurable.** Si se supera: se cancela la acción, se informa al usuario y no se emite ninguna tecla.
- No se manipula el estado de Ctrl artificialmente. Hacerlo exigiría una justificación técnica y pruebas específicas que hoy no existen.
- La espera se detecta con los eventos del propio `ChordTracker`, sin sondeo en callbacks. Si hay que sondear (ej. cancelación por timeout), se hace fuera del callback.
- **Cómo se implementó (fase 4.2, 17.10):** `ChordTracker` solo conoce Ctrl y el botón central, y no se modifica; la espera cubre los cuatro modificadores («el Ctrl de activación y cualquier otro»), así que se **sondea** con `GetAsyncKeyState` fuera de los hooks (`IModifierStateReader`), cada 10 ms, y se compara también la ventana en primer plano antes de enviar. No es una contradicción de la regla (el sondeo está previsto fuera del callback), pero sí una precisión de «con los eventos de `ChordTracker`».

## 4. Máquina de estados

```
Idle ──[button↓ con Ctrl ya pulsado]──▶ Active(stack=[main])
Idle ──[button↓ sin Ctrl]──▶ Idle   (evento NO tragado; pulso foráneo)

Active:
   mover        → Hover(sector) | Centro
   Hover(grupo) ──dwell ≥ T_sub──▶ PushMenu → recentrar en cursor, backArmed=false
   Centro       ──salir de la zona muerta──▶ backArmed=true
   Centro [backArmed] ──dwell ≥ T_back──▶ PopMenu → recentrar en cursor, backArmed=false
   button↑      → Execute(sector) | Cancel si no hay selección o el sector está vacío
   Esc          → Cancel
   Ctrl↑        → Cancel   (si aún no se ha confirmado)
   watchdog     → Cancel

Execute → Idle     Cancel → Idle
```

- Motor determinista: `Update(posición, instante)` y `Tick(instante)` con `IClock` inyectado. Expone `SubmenuProgress` y `BackProgress` (0 a 1).
- **Recentrado:** al abrir un submenú su origen pasa a ser la posición actual del cursor. **Armado:** el retroceso por permanencia solo se arma tras salir una vez de la zona muerta. El menú raíz no tiene retroceso.
- Profundidad máxima configurable (3 por defecto). Los ciclos entre menús se rechazan al validar.

## 5. Geometría

Con `dx`, `dy` relativos al origen (Y crece hacia abajo):

```
θ = atan2(dx, -dy) en grados        → (-180, 180]
r = θ % 360; if (r < 0) r += 360    → [0, 360]
if (r >= 360) r = 0                 → caso -1e-20 + 360 == 360.0
sector = floor((r + 22.5) / 45) mod 8     (0 = Norte, 1 = NE … 7 = NO)
```

En C# `%` conserva el signo del dividendo (`-90 % 360 == -90`), por eso se corrige a mano. Se usa `%` y no `θ - 360·floor(θ/360)` porque el resto de coma flotante es exacto, mientras que la resta puede salirse del rango con ángulos enormes. Límites en intervalos `[inicio, fin)` en sentido horario. Zona muerta: `distancia < deadZone`, y el vector nulo también lo es. Se rechazan NaN e infinitos. La histéresis angular solo se aplica cuando ya hay un sector seleccionado.

**Pruebas obligatorias:** las 8 direcciones exactas; para cada uno de los 8 límites (22.5° + 45°·k) un punto a ±0.01°, que debe caer en sectores contiguos distintos (con píxeles enteros un límite exacto es inalcanzable, así que no se afirma qué sector gana en él); normalización de `-0.0`, `-1e-20`, `±180`, `360`, `720` y ángulos grandes; barrido 0°–359.9° cada 0.1° contra tabla de referencia; zona muerta (dentro, fuera, nulo); histéresis sin parpadeo.

## 6. Overlay, DPI y monitores

### 6.1 Ventana

Estrategia aceptada: **ventana layered de WPF** (ADR-001, variante A del spike).

- Se crea **una vez al arrancar la aplicación** y se reutiliza (mostrar y ocultar). Una misma ventana sirve a todos los niveles de submenú. Motivo observado en el spike: la primera aparición tardó 163.92 ms frente a una mediana de 27.55 ms en las siguientes.
- `AllowsTransparency=true`, `WindowStyle=None`, fondo transparente, `ShowInTaskbar=false`, `ShowActivated=false`, `Topmost=true`, sin barra de título ni borde.
- Estilos extendidos **[Comprobado en el spike, a 125 % y en una build 26H2]**: `WS_EX_LAYERED` (lo aporta WPF) `| WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, más `WS_EX_TRANSPARENT` para el click-through, más `WS_EX_TOPMOST`. Valor observado: `0x080800A8`.
- `WM_MOUSEACTIVATE → MA_NOACTIVATE`: el spike lo usó y el clic sobre el overlay no lo activó; no se midió su aportación por separado.
- El click-through se aplica añadiendo `WS_EX_TRANSPARENT` sobre la ventana layered. `WM_NCHITTEST → HTTRANSPARENT` **no** dejó pasar el clic al receptor (otro proceso) en las variantes en que se probó (A, C, D y E; en B se omitió) y no debe usarse. Si el radial necesita el click-through siempre (la interacción se resuelve con los hooks y no con el ratón sobre la ventana) o solo en ciertos estados, se decide en la fase 4.
- Foco: se comprueba con `GetForegroundWindow` antes y después de mostrar; en el spike no cambió en ninguna variante.
- **[Parcialmente validado en la fase 4, 17.11]** que transparencia, click-through y no activación siguen funcionando juntas en la implementación definitiva: los estilos y el foco se comprobaron con `--overlay-check` (un monitor al 125 %) y el usuario comprobó a mano que el overlay se ve y que los atajos llegan a la aplicación de destino; **el efecto real del click-through (que un clic atraviese el overlay) no se ha comprobado**.
- **No se usa** Acrylic, Mica ni ninguna técnica DWM (`DWMWA_SYSTEMBACKDROP_TYPE`, `SetWindowRgn` sobre material, `SetWindowCompositionAttribute`). **No hay blur.**

### 6.2 DPI (PerMonitorV2)

El manifiesto de `Radial.App` ya declara `PerMonitorV2` **[Comprobado]**: la ventana de prueba reporta 120 DPI al 125 %. No se usa `GetDpiForMonitor`.

Secuencia al mostrar: (1) `SetWindowPos` en píxeles físicos con tamaño mínimo y centrada en el cursor, para que quede en el monitor correcto; (2) `GetDpiForWindow`; (3) tamaño físico = lógico × dpi / 96 con `SetWindowPos`; (4) la ventana permanece invisible hasta tener posición y tamaño definitivos. Se atiende `WM_DPICHANGED` ignorando el rectángulo sugerido y recalculando con la regla anterior. Escala interna con `VisualTreeHelper.GetDpi`. Los límites de monitor se obtienen con `MonitorFromPoint` + `GetMonitorInfo`.

**[Por validar]:** que las coordenadas del hook lleguen en píxeles físicos con PerMonitorV2, y el cambio de monitor a mitad de interacción con DPI mixto.

### 6.3 Rendimiento

Redibujado solo desde `CompositionTarget.Rendering`. Referencia del spike (variante A, 125 %, 60 Hz, GPU integrada): aparición visible con mediana de 27.55 ms y P95 de 34.79 ms tras la primera, midiendo con un sondeo de píxel a 60 Hz (resolución ~16.7 ms). Objetivo propuesto, **sin validar en producción**: del chord al primer fotograma ≤ 100 ms. El consumo de CPU y los fotogramas por segundo del spike **no son representativos** (el overlay de prueba incluía un contador que forzaba el render continuo); el consumo real del overlay definitivo hay que medirlo en la fase 4.

### 6.4 Bordes de pantalla (E-1) — RESUELTO con la opción C (v0.22); sin validar a mano

**Problema:** si el cursor está en el borde, un sector que apunta hacia fuera es inalcanzable: el cursor no puede alejarse del origen en esa dirección.

**Alternativas** (ninguna está decidida):

| Opción | Idea | Inconvenientes |
|---|---|---|
| **A. Sin ajuste** | Origen = cursor siempre; el radial puede quedar parcialmente fuera de pantalla. | Sectores exteriores inalcanzables en bordes y esquinas. |
| **B. Desplazar el origen hacia dentro** (hipótesis) | El radial se dibuja desplazado lo mínimo necesario. | El cursor empieza fuera del centro: puede quedar un sector seleccionado sin moverse. Desconexión entre posición visual y origen geométrico. |
| **C. B + distancia mínima de activación** (hipótesis) | Como B, pero no hay selección hasta que el cursor se mueva una distancia mínima desde el punto de activación. | Soltar de inmediato en el borde no ejecuta nada. Añade un umbral que explicar. |
| **D. Cursor virtual** | Acumular movimiento relativo (Raw Input) y mostrar un indicador propio, con el cursor real bloqueado. | Alta complejidad, dependencia de Raw Input, comportamiento menos natural. |
| **E. Selección por dirección de movimiento** | Usar el desplazamiento desde el punto de activación en lugar de la posición absoluta. | A pesar de ello, el movimiento hacia fuera sigue siendo imposible en el borde. |

**MVP: criterios para decidir.** Habrá que comprobar que (1) no aparece un sector seleccionado accidentalmente; (2) el usuario entiende la correspondencia entre movimiento y sector; (3) la posición visual y el origen geométrico no causan confusión; (4) la solución funciona también con submenús recentrados. Hasta entonces la implementación base del MVP será la opción **A**, aislada tras una abstracción sustituible, y B/C se prueban como experimento.

**Decisión (v0.22): opción C, implementada y sin validar a mano.** `OverlayPlacement.Fit` calcula, eje por eje, el centro más cercano al cursor con el que la ventana (tamaño físico = 420 DIP × escala del monitor) cabe entera en el área completa del monitor que contiene el cursor (`MonitorFromPoint`, `GetMonitorInfo`; sin `GetDpiForMonitor`). Lejos de los bordes devuelve el cursor sin tocarlo. Casos límite sin lanzar: monitor más pequeño que la ventana (ese eje se centra en el monitor), rectángulo no utilizable (no se ajusta), centro fuera del monitor (se lleva dentro), coordenadas extremas (aritmética de 64 bits). `IRadialPresenter.Show/ShowMenu` devuelven el centro dibujado; el coordinador ancla el motor en él, así que **dibujo y selección usan el mismo origen** (el cursor de activación solo sirve para el umbral). Hasta que el cursor se mueva 10 DIP (`ArmDistanceDip`, × escala del monitor) el motor ve el cursor en el centro: soltar sin mover no ejecuta nada ni entra en un grupo. Mismo tratamiento al cambiar de submenú. Criterios de arriba: (1) cubierto por pruebas del coordinador; (2)-(3) **por validar a mano** (¿se entiende que el radial se «pegue» al borde?); (4) cubierto por pruebas con presentador falso. **Límites:** desde la v0.25 el radial sigue al cursor a otro monitor (regla y límites en CLAUDE.md, «Seguimiento entre monitores»).

## 7. Configuración y almacenamiento

*Implementado en la fase 5: ver la sección 18 (JSON, migraciones, almacén en disco, recuperación, acciones desconocidas). Las diferencias respecto al diseño inicial están anotadas allí.*

```
%LOCALAPPDATA%\Radial\
├─ config.json
├─ backupsnfig-aaaaMMdd-HHmmss-fff.json   (rotación, últimas 10; implementado en 18.4)
├─ icons\                                   (caché de iconos extraídos)
└─ logs\
```

- `System.Text.Json` con contexto *source-generated*. `schemaVersion` y migraciones encadenadas. Una versión **futura** o desconocida se rechaza.
- **Escritura:** se escribe siempre a un archivo temporal en el mismo directorio. **Primera escritura** (el destino no existe): `File.Move` del temporal al destino sin sobreescritura. **Escrituras posteriores:** `File.Replace(temporal, destino, copia)`. `File.Replace` exige que el destino exista, por eso hay dos caminos.
- Un `config.json` corrupto no tumba la app: se pone en cuarentena (renombrado) y se arranca con valores predeterminados.

### 7.1 Acciones de tipo desconocido

`[JsonPolymorphic]` por sí solo **no conserva los datos** de un discriminador desconocido (`FallBackToBaseType` los pierde y `Fail` lanza una excepción). Estrategia propuesta:

- Un `JsonConverter` propio para la acción: lee el objeto completo en un `JsonObject`, resuelve `type` y despacha a los tipos conocidos.
- Si el tipo es desconocido, devuelve `UnknownAction { Type, Raw }` con el JSON original íntegro, y lo **reescribe tal cual** al serializar.
- Una `UnknownAction` no se ejecuta, se muestra en el editor como "no soportada" y genera un aviso de validación.
- Sin discriminador, o con un `type` que no es una cadena: rechazo controlado del elemento con un error de validación, sin lanzar.
- **Pruebas:** ida y vuelta con propiedades anidadas y arrays, equivalencia semántica tras reescribir; no se ejecuta; discriminador ausente o mal formado; versión futura rechazada.

### 7.2 Importación

1. Límite de tamaño y de profundidad del JSON.
2. Deserialización a DTO, comprobación de `schemaVersion` y migración.
3. Validación estructural: posiciones 0–7 sin duplicados, ids únicos, referencias de menú existentes, sin ciclos, profundidad máxima, longitudes, esquemas de URL permitidos (`http` y `https`; `mailto` dejó de admitirse en la fase 4, paso 3, ver 17.7), rutas bien formadas.
4. `ValidationReport` y resumen de acciones al usuario, con atención a ejecutables y atajos. Las `UnknownAction` se listan como no ejecutables.
5. Solo si no hay errores: copia de la configuración activa y aplicación. Nunca se ejecuta nada durante la validación.

## 8. Ejecución de acciones

`IActionExecutor` por tipo, resueltos por discriminador en un registro. Añadir un tipo es una clase más y un registro.

- **App, archivo, carpeta, URL:** `Process.Start` con `UseShellExecute=true`; nunca por `cmd`. Argumentos (lista, `ArgumentList`) y directorio de trabajo.
- **Teclado:** `SendInput` con la política de la sección 3.4.
- **Texto, volumen y ventana activa:** ver la sección 21.
- Los errores no tumban la app y se notifican. Iconos: glifo de Segoe Fluent Icons, o extraído de un ejecutable con la API del shell.

## 9. Pruebas

- **Core (la mayoría):** geometría, motor, máquina de estados con reloj falso, validación, serialización, migraciones, acciones desconocidas.
- **Windows:** `ChordTracker` con secuencias simuladas (Ctrl primero, botón primero, pulso foráneo, cancelaciones), parser de atajos, almacenamiento (primera escritura, reemplazo, archivo corrupto), ejecutores con `IProcessLauncher` falso, espera y timeout de atajos.
- **Manual, con checklist por fase:** hooks reales, foco, click-through, DPI y monitores.
- **Guardas de arquitectura:** ya existen en la fase 0 (sección 12). Con ensamblados todavía vacíos son una red de seguridad para el futuro y solo se vuelven realmente discriminantes cuando haya código.

## 10. Spike de ventana y desenfoque (fase 2): resultado

**Estado: cerrado con cobertura parcial. Decisión: [ADR-001](adr/ADR-001-overlay-rendering.md), aceptada.** Informe completo y datos en `spikes/Radial.BlurSpike/README.md` y `results/run1-results.{md,json}`. Todas las cifras de esta sección salen de **una única ejecución válida**; no se repitió ningún experimento.

Prototipo desechable en `spikes/Radial.BlurSpike`, fuera de `Radial.sln`, sin referencias a los proyectos de Radial ni paquetes NuGet. Las variantes probadas:

| Variante | Técnica |
|---|---|
| A | `AllowsTransparency` + pincel translúcido (base y alternativa garantizada). |
| B | Sin `AllowsTransparency`, `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`. Documentada, Windows 11 22H2+. |
| C | B + `SetWindowRgn` circular, para comprobar el borde. |
| D | `SetWindowCompositionAttribute` (no documentada), solo para conocer sus limitaciones. No se adoptaría. |
| E | Captura de la pantalla bajo el cursor + `BlurEffect` (falso blur). |

### 10.1 Resultados

Entorno: Windows 11 26H2 (build 26300), .NET 10.0.14, GPU Intel integrada, **un monitor de 1920×1200 al 125 % (120 DPI) y 60 Hz**. Overlay de 300 DIP (375 px).

| Variante | Blur | Circular | Foco intacto | Click-through (clic real, receptor en otro proceso) | Aparición visible: mediana / P95 |
|---|---|---|---|---|---|
| A | No (translúcido) | Sí | Sí | **Sí** con `WS_EX_TRANSPARENT` | 27.55 / 34.79 ms |
| B | No (gris uniforme) | No | Sí | **No demostrado** | 29.92 / 795.52 ms (12 muestras perdidas; no representativo) |
| C | No (gris uniforme) | No | Sí | No | 43.83 / 69.91 ms |
| D | No (rectángulo oscuro) | No | Sí | No | 39.70 / 56.69 ms |
| E | Estático (foto de la pantalla) | Sí | Sí | **Sí** con `WS_EX_TRANSPARENT` | 68.90 / 87.20 ms |

Otros hechos observados:
- El clic sobre el overlay (sin click-through) no lo activó en ninguna variante: el foreground siguió siendo el receptor.
- A: primera aparición en frío 163.92 ms; ocultar, mediana 45.47 ms.
- E: la captura de la región tarda 16.35 ms de mediana (P95 19.64 ms) y el bitmap ocupa 2.41 MB en memoria.
- `WM_NCHITTEST → HTTRANSPARENT` no entregó el clic al receptor en ninguna de las variantes en que se probó (A, C, D y E; en B se omitió). No se observó adónde fue el clic.
- En B, C y D, `WS_EX_LAYERED` no quedó aplicado con el modo "layered + transparente" (estilo observado `0x080000A8`). No se investigó por qué; esas variantes no llegaron a probarse con esa combinación.
- Alt+Tab se evaluó con una heurística sobre estilos de ventana (no elegible en las cinco), no observando la lista real.
- CPU, memoria y fotogramas del spike **no son representativos** del overlay final: el overlay de prueba tenía un contador de fotogramas que forzaba el render continuo y la memoria es la del proceso completo del harness.

### 10.2 Anomalías (hechos observados, causas desconocidas)

| Variante | Observado | Causa |
|---|---|---|
| B | Fondo gris uniforme y sin blur apreciable. Click-through no demostrado: dos modos sin paso de clic y dos omitidos porque `WindowFromPoint` devolvió una ventana ajena (pid 33208, sin identificar). 12 muestras de latencia perdidas. | **Desconocida** |
| C | La ventana siguió viéndose rectangular. No se registró el resultado de `SetWindowRgn`. | **Desconocida** |
| D | Rectángulo oscuro, sin blur apreciable ni forma circular. Ningún modo entregó el clic. | **Desconocida** |

No se afirma que Acrylic use un color de reserva por estar la ventana inactiva, ni que `SetWindowRgn` fallara o que DWM ignore la región: son hipótesis sin demostrar. La referencia a `SetWindowCompositionAttribute` (variante D) se mantiene como **API no documentada como interfaz pública estable, que no se usará en producción**.

### 10.3 Decisión

- **Overlay de producción: ventana layered de WPF (variante A)** con las propiedades de la sección 6.1.
- **Blur: no es requisito del MVP.** No se añaden Acrylic, Mica ni técnicas DWM a la arquitectura.
- **E (captura + `BlurEffect`)** queda archivada como posible experimento futuro. No se implementa ni se convierte en dependencia.
- **B, C y D no se adoptan.** No se debe a que se haya demostrado que sean imposibles: sus causas no se determinaron.
- Si más adelante se quiere blur, hay que abrir un estudio nuevo; no se parte de una explicación de B, C y D.

### 10.4 Cobertura y validación pendiente

| Aspecto | Estado |
|---|---|
| Un monitor al 125 % | Probado |
| DPI al 100, 150 y 200 % | **Pendiente de validación** |
| Varios monitores, escalados distintos, cambio de monitor | **Pendiente de validación** |
| Forma circular y tamaño físico tras un cambio de DPI; `WM_DPICHANGED` real | **Pendiente de validación** (el tamaño se calculó con `GetDpiForWindow`, pero solo se ejercitó a 120 DPI) |
| Windows 11 estable 25H2 | **Pendiente** (solo 26H2, build 26300) |
| Aplicaciones reales, pantalla completa exclusiva, ventanas elevadas | **Pendiente** |
| Combinación transparencia + click-through + no activación en el overlay definitivo | **Pendiente de validar en la fase 4** |

### 10.5 Código del spike no validado

El spike compiló y se ejecutó una vez. Después se añadieron, sin poder ejecutarlas, la variante B2 (B con `WindowChrome`), un experimento de activación del overlay, el registro del resultado de `SetWindowRgn`, el desglose de muestras perdidas, `--only` y un log a archivo. Compilan en Release, **no están validados** y no hay resultados de ellos. Están marcados `NO VALIDADO` en el código y listados en el README del spike.

## 11. Dependencias

| Dependencia | Uso | Licencia | Estado |
|---|---|---|---|
| .NET 10 / WPF / `System.Text.Json` | Base | MIT | ✅ |
| Microsoft.NET.Test.Sdk 17.14.1 | Pruebas | MIT **[Comprobado en el `.nuspec`]** | ✅ instalada |
| xunit 2.9.3 | Pruebas | Apache-2.0 **[Comprobado]** | ✅ instalada |
| xunit.runner.visualstudio 3.1.4 | Pruebas | Apache-2.0 **[Comprobado]** | ✅ instalada |
| `CommunityToolkit.Mvvm` | MVVM | MIT (a verificar) | Opcional, se decide en la fase 4 |
| WPF-UI | Estilos | A verificar | Se decide en la fase 6 |
| P/Invoke | Win32 | — | A mano con `LibraryImport` |
| FluentAssertions, Moq | — | — | ❌ Descartadas |

La plantilla de xUnit incluía `coverlet.collector`; se **retiró** por no ser necesario.

## 12. Fases y criterios de aceptación

**MVP = fases 0 a 5.**

| Fase | Contenido | Criterio de aceptación | Estado |
|---|---|---|---|
| **0** Esqueleto | Repositorio, solución, 5 proyectos, configuración, documentación. | `restore`, `build -c Release` y `test -c Release` en verde; la app WPF arranca. | ✅ **Completada** |
| **1** Núcleo | Geometría, motor, máquina de estados con submenús y retroceso, modelo, validación. La serialización y las acciones desconocidas (7.1) pasan a la fase 5, donde existe el almacenamiento. | Pruebas de las secciones 4 y 5 en verde. | ✅ **Completada** (sección 14) |
| **2** Spike de ventana | Variantes A–E, informe y ADR. | Tabla de resultados y decisión registrada. | ✅ **Cerrada con cobertura parcial** (sección 10, ADR-001). Smart App Control interrumpió los experimentos pendientes (sección 15). |
| **3** Captura global | Hilo de hooks, `ChordTracker`, watchdog, diagnóstico. | Ctrl→botón activa; botón→Ctrl no activa y el clic llega a la aplicación; la rueda no activa; Esc y soltar Ctrl cancelan; sin retraso perceptible del ratón. | ✅ **Aprobada** (sección 16). Pruebas automáticas, autoprueba con entrada sintética sobre hooks reales y prueba manual con hardware: correctas. Sin validar: bloqueo/suspensión reales, otros DPI, varios monitores, 25H2 (16.12). |
| **4** Overlay real + acciones | Overlay con la **variante A** (ventana layered de WPF, sección 6.1), `RadialView`, ejecutores (app, archivo, carpeta, URL, atajo diferido). | El foco no cambia; transparencia, click-through y no activación funcionan **a la vez**; se ejecuta una acción real; el atajo espera a soltar Ctrl y vence a los 5 s; DPI y monitores comprobados o documentados como pendientes. | ✅ **Implementada; validación automática confirmada; validación manual parcial; NO aprobada manualmente** (secciones 17 y 17.11). CI: commit `d4b3f8a`, ejecución nº 4, <https://github.com/xrbrr24/Radial/actions/runs/41900198820>, verde verificado en la API de GitHub; 694 Core / 992 Windows comunicadas por el usuario. Manual (informe del usuario): overlay con el gesto real y ocho acciones. **Aplazado:** efecto real del click-through, vencimiento de 5 s con el gesto real, caminos de fallo a mano, Esc y Ctrl por separado, autoprueba tras la 4.1/4.2, aplicaciones elevadas, cierre sin procesos colgados, DPI distintos del 125 %, varios monitores, bordes (E-1), 25H2 y rendimiento medido |
| **5** Submenús + configuración | Interfaz del dwell con progreso, serialización JSON con acciones desconocidas (7.1), `config.json` en `%LOCALAPPDATA%`, bandeja con "Salir". **→ MVP.** | Dos niveles estables; archivo corrupto no rompe la app; E-1 evaluado con los criterios de 6.4. | 🟡 **Implementada** (sección 18): submenús anidables en el overlay con progreso y retroceso, configuración JSON persistente con migraciones, versión futura intacta, cuarentena y recuperación, acciones desconocidas conservadas y no ejecutables, definiciones frente a colocaciones y bandeja con «Salir». **Pruebas automáticas en verde en CI (`1772b59`, ejecución nº 7); sin validación manual; E-1 sigue abierto y sin evaluar** (la limitación de bordes se conserva). El MVP no se da por cumplido hasta la validación manual |
| **6** Ajustes y editor | Ventana de ajustes, editor del radial con vista previa. | El ejemplo completo del enunciado se configura solo desde la interfaz. | Pendiente |
| **7** Apariencia y sistema | Temas, colores, autoarranque, instancia única. Sin blur (ADR-001). | Apariencia configurable con el panel translúcido oscuro. | Pendiente |
| **8** Importar/exportar y entrega | Importar, exportar, copias, robustez multimonitor y DPI, README, empaquetado, evaluación de firma de código y de Smart App Control (sección 15). | Checklist manual completa, con DPI mixto y **en Windows 11 estable**. | Pendiente |

### Criterios de aceptación del MVP

1. Ctrl + clic central abre el radial de 8 sectores.
2. Botón central sin Ctrl previo **no** abre nada y el clic llega a la aplicación de debajo.
3. Soltar el botón ejecuta; Esc cancela; soltar Ctrl antes de confirmar cancela.
4. El foco de la aplicación activa no cambia y el radial no interfiere con el movimiento del ratón.
5. Funcionan `.exe`, carpeta, URL y atajo diferido (con timeout de 5 s configurable).
6. Un submenú abre por permanencia con indicador de progreso, se recentra y se puede volver por el centro armado.
7. La configuración se carga desde `%LOCALAPPDATA%\Radial\config.json` con validación.
8. El estado del hook es visible en el diagnóstico y no se promete recuperación automática.
9. Panel translúcido oscuro y circular, **sin blur** (ADR-001). El desenfoque no es requisito del MVP.
10. Comportamiento en bordes (E-1) evaluado y las conclusiones registradas.

## 13. Resultado de la fase 0

### Estructura

```
C:\Coding\Radial
├─ .editorconfig  .gitattributes  .gitignore  global.json  Directory.Build.props
├─ CLAUDE.md  README.md  Radial.sln
├─ docs\technical-design.md
├─ src\
│  ├─ Radial.Core\      Radial.Core.csproj
│  ├─ Radial.Windows\   Radial.Windows.csproj
│  └─ Radial.App\       Radial.App.csproj, app.manifest, App.xaml(.cs), MainWindow.xaml(.cs), AssemblyInfo.cs
└─ tests\
   ├─ Radial.Core.Tests\     ArchitectureTests.cs
   └─ Radial.Windows.Tests\  ArchitectureTests.cs
```

### Referencias

`Radial.Windows → Core` · `Radial.App → Core, Windows` · `Core.Tests → Core` · `Windows.Tests → Core, Windows`. `Radial.Core` no referencia a ningún otro proyecto.

### Resultados

| Comprobación | Resultado |
|---|---|
| `dotnet restore` | Correcto (5 proyectos) |
| `dotnet build -c Release` | **0 advertencias, 0 errores** (warnings como errores activo) |
| `dotnet test -c Release` | Core.Tests **2/2**, Windows.Tests **1/1** |
| Arranque de `Radial.App` | Ventana "Radial" visible, proceso responde, DPI 120 (PerMonitorV2 activo) |

## 14. Fase 1: núcleo implementado

Todo el código vive en `Radial.Core` (22 archivos, ~1200 líneas). Sin dependencias NuGet ni referencias a otros proyectos.

### 14.1 Estado: implementado y pendiente

| Implementado (fase 1) | Pendiente |
|---|---|
| Modelo de dominio y acciones como datos | Serialización JSON y acciones de tipo desconocido (fase 5) |
| Geometría, zona muerta e histéresis | Ejecutores de acciones (fase 4) |
| Motor: inicio, movimiento, tiempo, confirmación, cancelación | Reloj de producción para `IClock` (fase 3/4) |
| Submenús por permanencia, recentrado y retroceso armado | Entrada real de teclado y ratón (fase 3) |
| Validador de configuración con códigos estables | Overlay, interfaz, DPI (fases 4, 6, 7) |
| Pruebas con reloj simulado | Comportamiento en bordes de pantalla, E-1 (fase 5) |

### 14.2 Modelo (`Radial.Core.Model`)

| Tipo | Función |
|---|---|
| `MenuId`, `ItemId` | Identificadores tipados (`record struct` sobre `string`). Independientes del nombre visible. Los de elementos son únicos en **toda** la configuración. |
| `RadialConfiguration` | `RootMenuId`, `Menus`, `Geometry`, `Navigation`. `CreateEmpty()` da la configuración mínima válida. |
| `RadialMenu` | `Id`, `Name`, `Items`. `GetItem(posición)` devuelve el elemento o `null` si la posición está vacía. Un menú puede tener entre 0 y 8 elementos. |
| `RadialItem` | `Id`, `Name`, `Position`, `Action`, `Icon?`. `IsGroup` es verdadero si abre otro menú. |
| `RadialAction` y derivadas | `LaunchApplicationAction`, `OpenFileAction`, `OpenFolderAction`, `OpenUrlAction`, `SendKeysAction`, `OpenMenuAction`. Solo datos; cada una describe sus problemas con `GetProblems()`. |
| `KeyChord`, `KeyModifiers`, `IconReference` | Representaciones propias, sin tipos de WPF. La tecla es un nombre ("T", "F5"); su traducción real queda para `Radial.Windows`. |
| `GeometrySettings` | `DeadZoneRadius` (30) y `HysteresisDegrees` (5). |
| `NavigationSettings` | `SubmenuDwell` (600 ms), `BackDwell` (800 ms), `MaxDepth` (3). |

**Extensibilidad:** una acción nueva es un `record` que hereda de `RadialAction` e implementa `GetProblems()`. Ni el validador ni los modelos existentes cambian. La serialización polimórfica sí necesitará registrarla (fase 5).

**Profundidad:** `MaxDepth` cuenta los menús apilados, raíz incluida. Con 3: raíz → submenú → sub-submenú.

### 14.3 Geometría (`Radial.Core.Geometry`)

- `RadialGeometry`: funciones puras. `NormalizeDegrees`, `DirectionFromAngle`, `CenterAngle`, `AngleDifference`, `TryGetAngle` y `Select(origen, cursor, zonaMuerta)`.
- Fórmula en la sección 5. Convenciones fijadas y probadas:
  - Los límites son `[inicio, fin)`: 22.5° pertenece al noreste, 337.5° al norte.
  - Zona muerta: `distancia < radio` no selecciona. Una distancia **igual** al radio ya selecciona.
  - El vector nulo no selecciona nunca, ni siquiera con radio 0.
  - NaN, infinitos y radios negativos lanzan `ArgumentOutOfRangeException`. El cálculo usa `double.Hypot`, así que distancias enormes no desbordan.
- **Límite exacto y coma flotante:** con coordenadas de píxel un límite exacto es inalcanzable. Un punto a un ulp del límite puede caer en cualquiera de los dos lados. Las pruebas usan ±0.01° y no afirman nada más fino.
- `SectorSelector`: histéresis, separada de la geometría básica. Conserva el sector actual mientras el ángulo del cursor esté a menos de `22.5° + margen` de su centro. Al entrar desde la zona muerta no hay histéresis. **El margen debe estar en `[0, 22.5)`**: con 22.5° o más, el sector vecino no se podría alcanzar sin pasar de su propio centro. El constructor y el validador lo rechazan.

### 14.4 Motor (`Radial.Core.Engine`)

`RadialEngine(configuración, reloj)`. Valida la configuración al construirse y lanza `InvalidConfigurationException` si hay errores. No es seguro entre hilos: quien lo use serializa las llamadas.

| Operación | Efecto |
|---|---|
| `Start(origen)` | Activa el menú raíz con el centro en `origen`, sin selección. Si ya estaba activo, **reinicia** la interacción. |
| `UpdatePointer(punto)` | Recalcula la selección y evalúa los tiempos. Inactivo: se ignora. |
| `Tick()` | Evalúa los tiempos sin mover el cursor. Inactivo: se ignora. |
| `Confirm()` | Devuelve `RadialResult` y vuelve a inactivo. Inactivo: `NotActive`, sin efectos. |
| `Cancel()` | Limpia todo y vuelve a inactivo. |

Estado observable: `IsActive`, `CurrentMenu`, `NavigationStack`, `Origin`, `Pointer`, `SelectedDirection`, `SelectedItem`, `SubmenuProgress`, `BackProgress`, `IsBackArmed`.

`Confirm()` devuelve `Execute(item)` solo si hay un elemento seleccionado que **no** sea un grupo. En otro caso devuelve `Cancelled` con motivo `NoSelection`, `EmptySlot` o `GroupNotEntered`. El motor nunca ejecuta nada.

**Tiempo:** `IClock.Now` es un `TimeSpan` monotónico. Los tiempos se evalúan en cada `UpdatePointer` y `Tick`, así que el submenú se abre al llegar a `SubmenuDwell` aunque nadie llame a `Tick`. `Confirm()` no evalúa tiempos: si se suelta sobre un grupo antes de la última lectura que completa el dwell, se cancela. No hay `Thread.Sleep` ni `DateTime`.

### 14.5 Navegación entre menús

```
Idle ──Start──▶ Active[main]
   Hover(grupo) ──permanencia ≥ SubmenuDwell──▶ Push(submenú):
        origen := cursor · selección := ninguna · IsBackArmed := false · progresos := 0
   Fuera del centro ──▶ IsBackArmed := true
   Centro + IsBackArmed + profundidad ≥ 2 ──permanencia ≥ BackDwell──▶ Pop:
        origen := cursor · selección := ninguna · IsBackArmed := false · progresos := 0
```

- Cambiar de sector, pasar al centro o salir del grupo reinicia `SubmenuProgress`. Moverse dentro del mismo sector no.
- Salir del centro cancela la espera de retroceso y la reinicia al volver.
- En la raíz no hay retroceso.
- No se entra en un submenú si la pila ya tiene `MaxDepth` menús o si el destino ya está en la pila. Una configuración validada nunca lo provoca; es una defensa adicional.

### 14.6 Validador (`Radial.Core.Validation`)

`ConfigurationValidator.Validate(configuración)` no lanza por contenido inválido y devuelve **todos** los problemas con `Severity`, `Code` estable, `Message` y `Path` (por ejemplo `menus[work].items[notes].action`). Los mensajes están en español y no se prueban; las pruebas usan los códigos.

Comprueba: raíz vacía o inexistente; identificadores vacíos, demasiado largos o duplicados (menús, y elementos en toda la configuración); nombres vacíos o de más de 64 caracteres; posiciones inválidas o duplicadas; iconos; acciones ausentes o con datos obligatorios vacíos; URLs que no sean absolutas `http` o `https` (en la fase 1 también se admitía `mailto`; se retiró en la fase 4, paso 3, ver 17.7); referencias a submenús inexistentes; **ciclos** (incluso entre menús inalcanzables); profundidad por encima de `MaxDepth`; zona muerta, histéresis, tiempos (50 ms a 10 s) y profundidad (1 a 10) fuera de rango. Un menú inalcanzable desde la raíz es una **advertencia**, no un error.

El análisis del grafo es iterativo: una cadena de 50 000 menús no desborda la pila (hay una prueba). La búsqueda de referencias usa conjuntos, no listas: una primera versión era cuadrática y tardaba 33 s con esa prueba, ahora tarda milisegundos.

### 14.7 Cambios de diseño respecto a la v0.3

| Cambio | Motivo |
|---|---|
| `DeadZoneRadius` debe ser **mayor que 0** en la configuración. | Con 0, el retroceso solo se activaría con el cursor exactamente en el píxel del origen. La función geométrica sí admite 0. |
| La normalización usa `%` con corrección. | El resto de coma flotante es exacto; la fórmula con `floor` no lo es con ángulos enormes. |
| Serialización y acciones desconocidas pasan a la fase 5. | Aún no existe almacenamiento; implementarlas ahora sería código sin usar. |
| `Start` en estado activo reinicia en vez de lanzar excepción. | Más tolerante con la capa de entrada, que podría duplicar una activación. |
| No hay reloj de producción en Core todavía. | Nadie lo usa hasta la fase 3 o 4. |

### 14.8 Pruebas

**253 pruebas en verde** (252 en Core, 1 en Windows), ~0.5 s en total, todas con reloj simulado.

| Archivo | Cubre |
|---|---|
| `RadialGeometryTests` | 8 direcciones (ejes y diagonales exactos), 8 límites a ±0.01°, límites exactos, ángulos negativos y múltiplos de 360, `-0.0`, `-1e-20`, ángulos enormes, vector nulo, zona muerta justo dentro, en el radio y fuera, barrido de 3600 ángulos contra una tabla independiente, NaN e infinitos. |
| `SectorSelectorTests` | Cambio normal, margen, oscilación y *jitter* en la frontera, cruce de 0°/360°, entrada desde la zona muerta, margen 0 equivalente a la geometría sin estado, márgenes extremos, todos los pares de sectores alcanzables, márgenes inválidos. |
| `RadialEngineLifecycleTests` | Inicio, selección en las 8 direcciones, posición vacía, confirmación (ejecutable, sin selección, vacía), cancelación, doble inicio, operaciones inactivas, limpieza, valores no finitos, configuración inválida. |
| `RadialEngineNavigationTests` | Progreso gradual, apertura exacta a los 500 ms, apertura sin `Tick`, recentrado, reinicios, soltar antes de completar, armado del retroceso, retroceso exacto a los 400 ms, tres niveles, raíz sin retroceso. |
| `ConfigurationValidatorTests` | Todos los códigos de error y casos límite aceptados. |
| `RadialModelTests`, `ArchitectureTests` | Modelo; Core sin referencias a Windows o WPF y sin P/Invoke. |

**Mutaciones comprobadas a mano** (se introdujo cada fallo y se confirmó que las pruebas lo detectan; después se restauró el código): `>=` por `>` en la permanencia (18 pruebas fallan), `<` por `<=` en la zona muerta (1), histéresis sin margen (7), retroceso siempre armado (11), sin recentrado (3).

### 14.9 Limitaciones conocidas

- Bordes de pantalla (E-1): resuelto en la v0.22 (6.4). El motor sigue trabajando con el origen que le dan; el coordinador le da el centro dibujado.
- La zona muerta está en las unidades del cursor. Quien lo integre debe escalarla con el DPI.
- `NavigationStack` es una vista en vivo, no una copia; la interfaz debe leerla en el mismo hilo.
- No hay límite al número de menús o elementos. Es aceptable para la validación (lineal), pero la importación (fase 8) deberá limitar el tamaño del archivo.

## 15. Smart App Control y distribución

### 15.1 Qué ocurrió

- Smart App Control está en **modo de aplicación** en el equipo de desarrollo (`VerifiedAndReputablePolicyState = 1`).
- Tras la primera ejecución válida del spike, Windows bloqueó la carga de `Radial.BlurSpike.dll` con el error **`0x800711C7`** ("An Application Control policy has blocked this file"). Code Integrity registró que la DLL no cumplía el nivel de firma exigido.
- El primer intento de ejecución sí funcionó. Las ejecuciones posteriores fueron bloqueadas, incluido un reintento sin cambios.
- **No se modificó ninguna configuración de seguridad.** Las fases 0 y 1 compilaron, ejecutaron pruebas y arrancaron `Radial.App` sin bloqueos; esos binarios **no se han vuelto a ejecutar tras el bloqueo**, así que no se sabe si les afecta también.

### 15.2 Decisiones

1. **No se desactiva Smart App Control.** Desactivarlo es una decisión del titular del equipo y no puede revertirse sin reinstalar Windows.
2. **No se modifican las políticas de seguridad.**
3. **No se intenta crear excepciones individuales** para el prototipo ni para Radial.
4. **No se buscan mecanismos para eludir la protección** (ni cambios deliberados de binario, ni ejecución por vías alternativas).
5. La fase 2 se cerró con los datos de la primera ejecución. No se repiten los experimentos.
6. **Firma de código y opciones de confianza de aplicaciones se evaluarán en la fase de distribución** (fase 8).
7. **No se adquieren certificados ni se contratan servicios de pago sin autorización expresa.** El coste es una restricción del proyecto.

### 15.3 Riesgo técnico pendiente

- **No se afirma que todas las compilaciones sin firmar vayan a bloquearse.** El comportamiento depende también de los mecanismos de inteligencia y reputación de Microsoft; aquí un binario compilado localmente se ejecutó una vez y después se bloqueó, sin que se haya determinado la causa de esa diferencia.
- Es un **riesgo de compatibilidad y de distribución**: una compilación sin firmar de Radial podría no ejecutarse en equipos con Smart App Control en modo de aplicación. Esto afecta también al desarrollo en equipos así.
- Pendiente de evaluar en la fase 8, con las restricciones de coste de la sección 15.2: opciones de firma, confianza y reputación, y cómo documentar la instalación para el usuario final.
- Hasta entonces, el spike no puede ejecutarse en este equipo y los experimentos de su sección 10.5 permanecen sin validar.

### 15.4 Estado durante la fase 3

El preflight de la fase 3 comprobó antes de tocar nada: SDK, compilación en Release, `dotnet test` y arranque de `Radial.App`. **No hubo ningún bloqueo.** Los binarios nuevos de las fases 3 (`Radial.Windows.dll`, `Radial.App.dll`, las pruebas) también se compilaron, se ejecutaron y no aparecieron eventos de Code Integrity. No se modificó ninguna configuración de seguridad. Esto no cambia el riesgo de 15.3: que no se haya bloqueado aquí no demuestra que no ocurra en otro equipo ni en otra compilación.

### 15.5 Estado durante la fase 4: dónde se validan las pruebas

Esta sección unifica lo que 17.8, 17.9 y 17.10 decían por separado.

- **Paso 3 (17.8):** Smart App Control bloqueó `Radial.Windows.Tests.dll` y `Radial.Core.Tests.dll` (compilaciones nuevas, `0x800711C7`). Sigue siendo cierto, y la regla se mantiene: **en el equipo de desarrollo no se vuelven a lanzar las pruebas de Windows, no se recompila para conseguir que un binario cargue y no se afirma como superado lo que no se ha ejecutado.** No se desactivó Smart App Control ni se tocó ninguna política.
- **Validación automática de referencia: GitHub Actions** (`.github/workflows/radial-windows-validation.yml`, runner `windows-latest`, independiente del equipo de desarrollo). Compila la solución completa en Release y ejecuta `Radial.Core.Tests` y `Radial.Windows.Tests`. Se lanza con cada `push` a cualquier rama, con `pull_request` y con `workflow_dispatch` (este último solo se puede lanzar desde la interfaz de GitHub cuando el archivo existe en la rama predeterminada; hasta entonces el disparador efectivo es el push). Hasta la v0.16 solo se lanzaba con push a `validation/fase-4-paso-3`. No firma nada, no cambia opciones de compilación y no ejecuta aplicaciones (las pruebas usan lanzadores e inyectores falsos).
- **Ejecuciones de CI verificadas en la API pública de GitHub (v0.17), solo estado, sin cifras:** (1) ejecución nº 1, commit `9ce8129`, 2026-11-03: **fallo** en el paso de `Radial.Windows.Tests` (compilación y `Radial.Core.Tests` correctos); el commit siguiente, `349fbde` («Fix URL error message assertion»), corrige la aserción; (2) **ejecución nº 2, commit `349fbde` (el último commit antes del cierre de la fase 4), 2026-11-03: conclusión `success`**, con compilación, `Radial.Core.Tests` y `Radial.Windows.Tests` en verde — <https://github.com/xrbrr24/Radial/actions/runs/41624193526>. Esa ejecución es anterior a la 4.1, la 4.2 y el cierre técnico: **no cubre nada de ese código**. Los números de pruebas de cada suite solo constan en el resumen de la ejecución y en los artefactos TRX (`resultados-core`, `resultados-windows`, retención de 14 días), que GitHub no entrega sin autenticación: las cifras **622 Core / 522 Windows** son las que comunicó el usuario y **no se han podido comprobar desde aquí**. Observación (deducción aritmética, no confirmada): 622 = 660 − 38 y 522 = 574 − 52, es decir, las cifras que 17.9 da para la 4.1 menos las pruebas que la 4.1 añadió; es coherente con que esa ejecución cubriera el código anterior a la 4.1. No se reconcilia con las **571** de Core que 17.8 anotó para el paso 3 (ejecución local anterior al bloqueo): la diferencia no está explicada en el repositorio, así que ninguna de esas cifras se usa para deducir cobertura.
- **Cifras comunicadas por los informes de implementación, NO confirmadas por CI:** **660 Core / 574 Windows** (informe de la 4.1, 17.9) y **694 Core / 855 Windows** (informe de la 4.2, 17.10). Los informes las anotaron como «última ejecución local»; no consta si salieron del equipo de desarrollo, de otro entorno o de CI, ni existe commit o enlace asociado. Se conservan como histórico, pero **no deben citarse como resultados de GitHub Actions**. Mientras no se confirmen, las pruebas automáticas de la 4.1 y la 4.2 (en especial las 281 de Windows de la 4.2) figuran como **no verificadas por CI**.
- **Aplicación en el equipo de desarrollo:** el usuario ha ejecutado `Radial.App` y realizado las pruebas manuales de 17.11 sin que se registrara un bloqueo. Eso no cambia el riesgo de 15.3: que un binario cargue una vez no demuestra que otra compilación, ni los ensamblados de pruebas, no se bloqueen, y no se ha determinado por qué el comportamiento varía.
- **Referencia vigente (v0.19).** Lo último confirmado por CI es la ejecución nº 4 sobre `d4b3f8a` (cierre técnico de la fase 4), <https://github.com/xrbrr24/Radial/actions/runs/41900198820>: conclusión `success` con compilación, `Radial.Core.Tests` y `Radial.Windows.Tests` en verde, verificado en la API de GitHub; las cifras **694 Core / 992 Windows** las comunicó el usuario y no son comprobables sin autenticación (los números por suite solo constan en el resumen de la ejecución y en los artefactos TRX). La ejecución nº 3 (`6759b04`) también fue verde. **Esa ejecución no cubre el código de la fase 5.** La fase 5 la cubre la ejecución nº 7 sobre `1772b59` (<https://github.com/xrbrr24/Radial/actions/runs/42001660093>): `success` con compilación, `Radial.Core.Tests` y `Radial.Windows.Tests` en verde (verificado en la API; sin leer las cifras por suite). Las ejecuciones nº 5 (`6eb9977`) y nº 6 (`0932f25`) de la rama fallaron en la compilación de las pruebas (falta de un `using`; aviso `xUnit2029` del analizador, tratado como error): errores de las pruebas, corregidos sin cambiar el producto. 660/574 y 694/855 son cifras de informe, anteriores y de procedencia no confirmada.

## 16. Fase 3: captura global de teclado y ratón

Código en `Radial.Windows` (sin WPF) con la ventana de diagnóstico en `Radial.App`. `Radial.Core` no cambió.

### 16.1 Estado: implementado, probado y pendiente

| Aspecto | Estado |
|---|---|
| Hooks `WH_KEYBOARD_LL` y `WH_MOUSE_LL` en un hilo dedicado con bucle de mensajes | Implementado; instalación y liberación comprobadas con los hooks reales |
| `ChordTracker`, servicio, watchdog, sonda de salud, eventos del sistema | Implementados; **401 pruebas automáticas** en verde (252 Core + 149 Windows) |
| Gesto completo sobre hooks reales | Comprobado con **entrada sintética** (`SendInput`), no con hardware |
| Prueba manual con teclado y ratón físicos | **Realizada por el usuario, satisfactoria** (resultados y alcance en 16.9) |
| Bloqueo de sesión y suspensión | Cancelan el gesto activo (probado con eventos falsos). **No se reinstalan los hooks al reanudar**; sin probar con un bloqueo o suspensión reales |
| Detección de que Windows eliminó el hook | Solo el síntoma "el cursor se mueve y el hook de ratón no recibe nada"; para el hook de teclado **no hay detección** |

### 16.2 Arquitectura

```
 hilo de hooks (dedicado, bucle GetMessage)                 hilo de aplicación (UI)
 ┌─────────────────────────────────────────────┐
 │ LowLevelKeyboardHook / LowLevelMouseHook    │
 │   callback: leer struct → HookMessages.Map  │
 │            → GlobalInputService.OnInput      │
 │   (mide la duración; captura excepciones)   │
 └───────────────┬─────────────────────────────┘
                 │ lock(_gate): ChordTracker.Process → Publish
                 ▼
   ┌──────────────────────────┐   eventos críticos (Activated/Confirmed/Cancelled)
   │    GlobalInputService     │ ───────────────▶ Channel SIN límite ──▶ lector único (UI / fase 4)
   │ tracker + contadores      │   posición         LatestPointer + PointerSequence (coalescida)
   └──────┬─────────┬─────────┘
          │         └── Timer de salud (2 s, solo con la captura en marcha)
          └── Timer del watchdog (50 ms, SOLO con un gesto activo)
```

| Pieza | Proyecto | Responsabilidad |
|---|---|---|
| `GlobalInputService` | Windows | Iniciar/detener, publicar eventos, estado y diagnóstico, watchdog, sonda de salud, sesión/suspensión. Un solo gesto activo. |
| `LowLevelHookHost` | Windows | Hilo dedicado, bucle de mensajes, instalación y liberación de ambos hooks en ese hilo. Una sola instancia activa por proceso. |
| `LowLevelHookBase` / `LowLevelKeyboardHook` / `LowLevelMouseHook` | Windows | Delegado como campo (el GC no lo recoge), lectura sin asignaciones, medición, errores fuera del evento. |
| `ChordTracker` | Windows | Máquina de estados pura: decide consumir o no y emite señales. Sin P/Invoke ni hilos. |
| `HookMessages` | Windows | Traducción pura de mensajes y flags a `RawInputEvent`. |
| `GuardedSyntheticInput` / `SyntheticInput` (interno) / `SyntheticMarker` (interno) | Windows | **Solo diagnóstico:** `SendInput` para la autoprueba. `GuardedSyntheticInput` es la única vía de uso: comprueba el destino antes de cada envío, recuerda qué pulsó y lo libera de forma síncrona. Cada evento lleva la marca de Radial en `dwExtraInfo`. No es el servicio de envío de teclas de la fase 4. |
| `MainWindow`, `InputSelfTest`, `SystemEventsAdapter` | App | Ventana de diagnóstico, autoprueba y eventos de sesión/energía (`SystemEvents`, que no está en el framework base de `Radial.Windows`; así se evita un paquete NuGet). |

Puntos de diseño: `Radial.Core` no sabe nada de esto; `IHookHost`, `IPhysicalInputReader`, `ITimerFactory` e `ISystemStateEvents` permiten probar el servicio con hooks, relojes y eventos del sistema falsos. P/Invoke con `LibraryImport`, salvo `SetWindowsHookEx`, que usa `DllImport` porque el generador no marshala delegados (se necesita un delegado vivo).

### 16.3 Máquina de estados de `ChordTracker`

```
Idle ──[Ctrl pulsado y llega botón central]──▶ Active           (consume el botón; señal Activated)
Idle ──[botón central sin Ctrl]──▶ Idle        (pulso foráneo: pasa; Ctrl después NO activa)

Active ──[mover]──▶ Active                      (señal Moved; no se consume)
Active ──[botón central ↑]──▶ Idle              (consume; señal Confirmed con la posición de la liberación)
Active ──[Escape ↓]──▶ AwaitingMiddleRelease    (consume Escape; señal Cancelled(Escape))
Active ──[último Ctrl ↑]──▶ AwaitingMiddleRelease  (Ctrl pasa; señal Cancelled(CtrlReleased))
Active ──[watchdog / manual / sesión / parada]──▶ AwaitingMiddleRelease  (señal Cancelled(causa))

AwaitingMiddleRelease ──[botón central ↑]──▶ Idle   (consume; SIN señal: no se confirma)
```

Ctrl izquierdo y derecho se siguen por separado; el modificador está "pulsado" si lo está alguno. Soltar uno mientras el otro sigue pulsado no cancela. El orden Ctrl→botón es obligatorio.

### 16.4 Qué se consume y qué pasa a la aplicación

| Evento | ¿Se consume? |
|---|---|
| Ctrl izquierdo/derecho, pulsar o soltar | **Nunca** |
| Botón central ↓ con Ctrl ya pulsado (activa) | **Sí** |
| Botón central ↑ del gesto (confirmar), y también tras cancelar | **Sí** (siempre se consume la liberación del clic consumido) |
| Pulsación repetida del botón central consumido | Sí |
| Botón central sin Ctrl previo (↓ y ↑) | **No** (pasa íntegro) |
| Escape ↓ que cancela un gesto activo, sus repeticiones y su ↑ | **Sí** |
| Escape en cualquier otro momento | No |
| Movimiento del ratón | **Nunca** |
| Rueda (`WM_MOUSEWHEEL`, `WM_MOUSEHWHEEL`) | **Nunca**; no activa |
| Otros botones y otras teclas | No (ni siquiera entran en la lógica) |
| Eventos inyectados (`SendInput`), de cualquier origen | Se ignoran (pasan). Durante la autoprueba solo se aceptan los que llevan la marca de Radial; los de otras aplicaciones se siguen ignorando (ver 16.9) |

Detalle: la bandera de "Escape consumido" se borra cuando termina el gesto; un Escape que se siga manteniendo después de soltar el botón central pasa a la aplicación.

### 16.5 Hilos, comunicación y rendimiento

- **Hilo de hooks:** crea su cola con `PeekMessage`, instala ambos hooks, y ejecuta `GetMessage` hasta `WM_QUIT`. Los quita en el mismo hilo. `Stop` publica `WM_QUIT` y espera 3 s (si no termina, lo notifica como error). Nunca se instalan desde el hilo de interfaz.
- **Callbacks:** leen los campos con `Marshal.ReadInt32` (sin asignaciones), traducen, entregan al servicio y miden. Sin disco, sin interfaz, sin esperas. Sin gesto activo, ni el movimiento ni la rueda toman el cerrojo. Si un callback lanza, se anota el error (fuera del evento, en la cola de errores) y el evento pasa a la aplicación.
- **Un solo cerrojo (`_gate`)** protege el `ChordTracker` y la publicación de eventos: el orden de los eventos del canal es el de las transiciones. El watchdog toma el mismo cerrojo solo unos microsegundos y lee el estado físico fuera de él.
- **Eventos críticos:** `Channel` sin límite, lector único: no descarta nada. Cada evento lleva `Sequence` y `Timestamp` (monotónico) y, para activar y confirmar, la posición del cursor **en ese evento** (la de la liberación al confirmar).
- **Movimientos:** solo se conserva la última posición (`LatestPointer`, `PointerSequence`). No pasan por el canal.
- **Medido (autoprueba, equipo de desarrollo):** duración máxima de un callback 1.78 ms (teclado) y 2.82 ms (ratón) **incluyendo el arranque en frío**; 0 callbacks ≥ 50 ms. No se midió bajo carga ni durante uso prolongado.
- **Sin sondeo en reposo:** el watchdog solo corre con un gesto activo; la sonda de salud corre cada 2 s mientras la captura está en marcha.

### 16.6 Watchdog y sonda de salud

**Hallazgo medido (autoprueba con los hooks reales):** con un gesto activo, `GetAsyncKeyState(VK_MBUTTON)` devuelve **suelto** aunque el botón esté pulsado, porque el hook consumió su evento y Windows no lo registra en el estado asíncrono. Ctrl, que nunca se consume, sí se ve correctamente. La primera versión del watchdog comprobaba el botón central y cancelaba todos los gestos sanos; la ejecución real lo mostró y se rediseñó.

El watchdog (50 ms, solo con el gesto activo, dos muestras malas seguidas, solo cancela, nunca confirma) comprueba:
1. **Ctrl sigue pulsado** (`GetAsyncKeyState`, fuera de los callbacks). Si no → `Cancelled(WatchdogCtrlLost)`.
2. **El hook de ratón entrega movimiento:** si el cursor real (`GetCursorPos`) se aleja más de 40 px de la última posición del hook → `Cancelled(WatchdogMouseHookSilent)`.

Descarta la muestra si el gesto cambió entre la lectura y la decisión (identificador de activación).

**No detecta:** que se pierda la liberación del botón central (no hay forma fiable de verlo sin entrada en bruto), ni que Windows quite el hook de teclado. Si la liberación del botón se perdiera con el gesto en `Active`, el estado seguiría activo hasta que se suelte Ctrl o se pulse Escape. Con el gesto ya cancelado y esperando la liberación, no hay supervisión.

**Sonda de salud (2 s):** si el cursor cambió desde la última sonda y el hook de ratón no recibió ningún evento, el diagnóstico pasa a `Faulted` con el mensaje "puede haber sido eliminado por Windows". Es un síntoma, no una prueba. No hay equivalente para el teclado.

**Sin recuperación automática.** El servicio informa; no reinstala los hooks. El usuario puede detener y volver a iniciar la captura. Tras reanudar el equipo o desbloquear la sesión solo se deja un aviso.

**Resumen de lo que el watchdog y la sonda pueden y no pueden detectar**

| Fallo | ¿Se detecta? | Cómo / por qué no |
|---|---|---|
| Ctrl deja de estar pulsado con el gesto activo y el hook no lo notificó | Sí, con el gesto activo | Watchdog: `GetAsyncKeyState` de Ctrl, 2 muestras (~100 ms) → `Cancelled(WatchdogCtrlLost)`. Ctrl nunca se consume, por eso su estado asíncrono es fiable. |
| El hook de ratón deja de entregar movimiento con el gesto activo | Solo si el cursor se mueve | Watchdog: el cursor real se aleja más de 40 px de la última posición del hook (acumulado, no por muestra) → `Cancelled(WatchdogMouseHookSilent)`. Con el cursor quieto no hay síntoma. |
| El hook de ratón deja de recibir eventos, sin gesto | Solo si el cursor se mueve | Sonda de 2 s → estado `Faulted`. Es un síntoma, no una prueba. |
| Se pierde la liberación del botón central | **No** | `GetAsyncKeyState` no ve el botón consumido (medido); no hay entrada en bruto. |
| Windows elimina el hook de teclado | **No** | No hay sonda equivalente. Solo se nota si, además, Ctrl se suelta con el gesto activo. |
| El gesto ya está cancelado y espera la liberación | **No** | Sin supervisión en `AwaitingMiddleRelease`. |
| Ctrl "atascado" en el tracker (ver abajo) | Solo al activarse un gesto | El watchdog solo corre con el gesto activo. |

**Ctrl atascado en el tracker** (sin reproducir). El tracker conoce el estado de Ctrl solo por los eventos de los hooks y lo inicializa una vez, en `Start`. Si un `KeyUp` de Ctrl no llegara al hook, el tracker seguiría creyendo que Ctrl está pulsado.
- *Comprobado (código y pruebas automáticas con eventos falsos):* con Ctrl "atascado" un clic central activa el gesto y lo consume; el watchdog, con Ctrl físicamente suelto, lo cancela con `WatchdogCtrlLost` tras dos muestras (~100 ms), olvida el Ctrl atascado y sigue consumiendo la liberación del botón. No hay detección fuera de un gesto activo.
- *Hipótesis, no reproducida ni con hardware ni con hooks reales:* que ocurra, por ejemplo, si Ctrl se suelta en el escritorio seguro (Ctrl+Alt+Supr) o durante un bloqueo de sesión. De ocurrir, el efecto sería un clic central que no llega a la aplicación y un gesto que dura ~100 ms antes de cancelarse. No se ha comprobado que Windows deje de entregar ese `KeyUp`.
- *Mitigación disponible, no implementada:* volver a inicializar el estado de Ctrl con el estado físico al desbloquear la sesión o reanudar el equipo. Hoy solo se deja un aviso.

**Otras hipótesis sin comprobar** (de la auditoría): (a) AltGr en un teclado español genera un Ctrl izquierdo; si ese evento no llega marcado como inyectado, AltGr + clic central activaría el gesto. (b) Otra aplicación que mueva el cursor más de 40 px con `SetCursorPos` durante un gesto podría provocar una cancelación `WatchdogMouseHookSilent` falsa.

### 16.7 Ciclo de vida

- `Start` lanza `InvalidOperationException` si **ese** servicio ya está en marcha. Cualquier otro fallo, incluido que **otro** servicio del mismo proceso tenga ya los hooks, se devuelve en el resultado (`Success = false`) y deja la captura en `Faulted` sin ningún hook instalado (nunca solo uno). Se puede reintentar. *(Corrección D2: antes la excepción del host por "ya hay otra captura activa" escapaba con el estado en `Starting`; reproducido con hooks reales y cubierto ahora por pruebas.)*
- `Stop` quita los hooks, cancela un gesto activo (`Cancelled(ServiceStopped)`) y se puede volver a llamar a `Start` (reinicio probado). `Dispose` hace `Stop` y completa el canal.
- Al iniciar, el tracker se inicializa con el estado físico (Ctrl ya pulsado cuenta; un botón central ya pulsado es foráneo).
- Bloqueo de sesión o suspensión: cancelan el gesto activo. Desbloqueo o reanudación: solo un aviso.
- La ventana de diagnóstico llama a `Dispose` al cerrarse. Si hay una autoprueba en curso, el cierre se **aplaza**: se cancela la prueba, su bloque final libera de forma síncrona lo que pulsó, y entonces se completa el cierre (ver 16.9).
- Un `static` impide dos hosts de hooks activos en el mismo proceso.

### 16.8 Limitaciones de Windows

- **Eliminación silenciosa del hook** por superar `LowLevelHooksTimeout` (tope de 1000 ms): no hay aviso ni recuperación garantizada. Mitigación parcial: callbacks mínimos y la sonda de 16.6.
- **UIPI:** con una ventana elevada en primer plano no se reciben sus eventos. Sin soporte; ejecutar como administrador o `uiAccess` quedan fuera del alcance.
- **Pantallas seguras** (UAC, Ctrl+Alt+Supr): no interceptables.
- **Juegos** en pantalla completa exclusiva y antitrampas: sin probar.
- **Botón central pulsado antes de Ctrl:** la aplicación ya recibió el clic; no se puede deshacer (por eso el orden es obligatorio).
- **Teclas genéricas:** `VK_CONTROL` (0x11) sin lado se ignora; solo cuentan `VK_LCONTROL` y `VK_RCONTROL`.
- **Coordenadas:** las del hook coinciden con `GetCursorPos` en el equipo de prueba (125 %, un monitor). Que lleguen en píxeles físicos con otros DPI o varios monitores **sigue pendiente de validar**.
- **Windows 11 estable 25H2:** sin probar (solo 26H2).

### 16.9 Verificación

**Automática (401 pruebas en verde: 252 Core + 149 Windows; build sin advertencias):**
- `ChordTrackerTests`: los 15 casos exigidos y más (Ctrl izquierdo y derecho, botón primero, soltar Ctrl, confirmar, Escape y sus repeticiones, rueda, clic sin Ctrl, Ctrl tras clic foráneo, doble activación, consumo tras cancelar, eventos inyectados, cancelación del watchdog, secuencias duplicadas o incorrectas, reinicio, inicialización desde el estado físico).
- `HookMessagesTests`: mapeo de mensajes y flags de inyección; marca propia (solo inyectado **y** con la marca; un evento físico nunca es propio; solo se comparan los 32 bits bajos).
- `ChordTrackerTests` (marca): inyectados ignorados por defecto; los propios, ignorados con el modo apagado y aceptados con él encendido; los inyectados por otras aplicaciones, ignorados incluso con el modo encendido y sin alterar un gesto sintético activo; los físicos, siempre aceptados.
- `GlobalInputServiceTests`: ciclo de vida, fallo de instalación, orden de eventos, movimientos coalescidos, posición de la confirmación, watchdog (incluido el caso "el botón consumido parece suelto"), sonda de salud, sesión y suspensión, reinicio, `Dispose`. **Con hooks, temporizadores y eventos del sistema falsos: no prueban los hooks reales.**
- `SecondServiceTests` (D2): un segundo servicio se rechaza (`Success = false`, estado `Faulted`, nunca `Starting`), no instala hooks (la plaza se ocupó una sola vez), el original conserva `Running` y sigue procesando entrada, detener al rechazado no libera los hooks del original, detener al original libera la plaza y entonces el segundo puede iniciar. Se comprobó que **fallan con el código anterior** (las 3 pruebas) y pasan con la corrección.
- `RealHookHostGuardTests`: con los hooks **reales** de Windows (sin inyectar nada, el sink no consume): un segundo host real lanza `InvalidOperationException`, parar libera la plaza. Instala hooks unos milisegundos.
- `GuardedSyntheticInputTests` (D1/D3, con un destino falso, sin tocar Windows): no se envía nada si el cursor se movió, si la ventana bajo el cursor es de otro proceso o no hay ventana, o si el primer plano es de otro proceso; la comprobación se repite en **cada** inyección; `ReleaseAll` libera solo lo que se pulsó, en orden (central, Escape, Ctrl der., Ctrl izq.), es idempotente y no hace la comprobación de seguridad; un fallo de `SendInput` al liberar deja la marca y se reintenta; las liberaciones de lo que no se pulsó no se envían; el cursor solo se restaura si nadie lo movió.

**Integración con hooks reales y entrada SINTÉTICA** (`Radial.App.exe --selftest <archivo>`, o el botón de la ventana): instala los hooks reales, inyecta con `SendInput` y comprueba. **Resultado tras las correcciones de la v0.7 (marca propia y comprobaciones de seguridad): 10/10 pasos correctos** (callbacks máximos 1.14 ms teclado y 2.56 ms ratón, incluido el arranque en frío, 0 lentos), una ejecución limpia tras varias abortadas por las propias protecciones (ver incidentes). Pasos:
- Ctrl izquierdo y derecho → botón central → mover → soltar: activa, el clic **no** llega a la aplicación, el watchdog real no cancela un gesto sano durante 500 ms, el movimiento se publica y el cursor se mueve, confirma en la posición de la liberación, la liberación **no** llega a la aplicación.
- Botón central antes de Ctrl: no activa y el clic llega íntegro. Clic central normal: llega.
- Rueda con Ctrl: no activa. Escape: cancela y **no** llega a la aplicación. Soltar Ctrl: cancela. Cancelación manual: cancela. En todas, la liberación del botón se consume y no confirma.
- Callbacks: máximos del párrafo anterior (antes de la v0.7: 1.78 ms teclado y 2.82 ms ratón).
- Detener la captura libera los hooks (un clic central posterior llega a la aplicación) y se puede reiniciar.

**Protecciones de la autoprueba (v0.7, correcciones D1 y D3)**
- *Comprobación previa a cada inyección* (`GuardedSyntheticInput.EnsureSafe`, antes de **cada** tecla, botón o rueda, no solo al empezar): (1) el cursor sigue donde la autoprueba lo dejó (±3 px); (2) `WindowFromPoint` bajo el cursor es una ventana de este proceso; (3) `GetForegroundWindow`, la ventana que recibe el teclado, también. Si algo falla, se lanza `SyntheticInputUnsafeException`, no se envía nada, la prueba se aborta y se ejecuta la limpieza. Al empezar se reintenta durante ~1.5 s para dar tiempo a que la ventana pase a primer plano.
- *Qué pulsó la propia prueba:* `GuardedSyntheticInput` recuerda las teclas y el botón que ha pulsado. Una liberación de algo que no pulsó no se envía; `ReleaseAll` solo libera lo recordado.
- *Limpieza en toda salida* (fin normal, comprobación fallida, cancelación o error): bloque `finally` **síncrono**, sin `await`: libera lo pulsado (botón central primero, para que el servicio consuma su liberación), reinicia la captura si la prueba la había parado y no la reinició, espera de forma acotada (500 ms) a que el servicio vea las liberaciones, desactiva la aceptación de eventos propios y devuelve el cursor solo si nadie lo movió. El resultado informa de qué estaba pulsado al abortar.
- *Cancelación (D3):* `RunAsync` recibe un `CancellationToken` que usan todas las esperas. Al cerrar la ventana durante la autoprueba, `OnWindowClosing` cancela el cierre (`e.Cancel`), cancela el token y, cuando la prueba ha terminado su limpieza, `ExecuteSelfTestAsync` vuelve a llamar a `Close()`. La limpieza no depende de que el dispatcher siga vivo.
- *Marca propia (`dwExtraInfo`):* cada evento inyectado por la autoprueba lleva una marca aleatoria por proceso (`SyntheticMarker`, 32 bits, distinta de cero). Los hooks distinguen tres casos: **físico** (sin flag de inyección), **inyectado propio** (flag + marca) e **inyectado ajeno** (flag sin marca). En uso normal se ignora todo lo inyectado; durante la autoprueba solo se aceptan los propios, y los de otras aplicaciones se siguen ignorando. *Medido con los hooks reales:* el hook de teclado entrega `dwExtraInfo` completo (64 bits) pero el de **ratón lo entrega truncado a 32 bits** (se envió `0x8D9904E27A4D5E79`, llegó `0x7A4D5E79`). Una primera versión con marca de 64 bits no coincidía nunca en el ratón y ningún gesto sintético se activaba; por eso la marca es de 32 bits y se comparan solo los 32 bits bajos. La marca evita confusiones accidentales; no es protección frente a alguien que lea la memoria del proceso.

**Límites de estas protecciones (importante):** `EnsureSafe` **reduce** el riesgo de inyectar entradas en otras aplicaciones, pero **no ofrece una garantía atómica**: entre la comprobación y el `SendInput` pasan microsegundos en los que otra ventana puede aparecer, tomar el primer plano o recibir el cursor, y Windows no permite hacer ambas cosas como una operación indivisible. Lo que sí garantiza es que un cambio sostenido (otra ventana encima, el usuario moviendo el ratón) se detecta en la inyección siguiente, como mínimo 150 ms después. Además, `ReleaseAll` no comprueba el destino a propósito: dejar una tecla o el botón pulsados es peor que enviar su liberación a otra ventana, de modo que una liberación sí puede llegar a una ventana ajena si el cambio ocurre justo entonces. Un hook de otro proceso que reaccione a la entrada tampoco está bajo control. Por todo ello, durante la autoprueba no se debe tocar el ratón ni el teclado.

**Qué NO demuestra la autoprueba:** que funcione con el hardware y los controladores reales, ni con aplicaciones reales, ni el comportamiento percibido del ratón, ni que los eventos inyectados y los físicos sean idénticos (aquí se aceptan los propios a propósito; en uso normal se ignoran todos). (El cierre de la ventana con la captura activa lo comprobó el usuario a mano; ver abajo.)

**Cierre durante la autoprueba (D3): lo que está y lo que no está verificado.** La limpieza (`ReleaseAll`: solo lo pulsado, orden, idempotencia, reintento tras fallo) y el rechazo de inyectar en destinos inseguros **están cubiertos por pruebas automáticas** con un destino falso. **No hay prueba automatizada (xUnit) del flujo completo "cerrar la ventana a mitad de la autoprueba"** (`MainWindow.OnWindowClosing` + `ExecuteSelfTestAsync` + `Close()`): exigiría una ventana WPF real inyectando entrada en el sistema. Sí se comprobó **a mano con un guion externo, con la aplicación real**: se lanzó `Radial.App.exe --selftest`, se envió `CloseMainWindow()` a los 2.3, 3.3 y 4.7 s (con Ctrl y el botón central pulsados por la autoprueba en los tres casos) y en las tres ocasiones: la prueba se canceló y listó lo que tenía pulsado (p. ej. "Ctrl izquierdo, botón central"), la limpieza lo liberó ("todo liberado; cursor restaurado"), el proceso terminó en ~110 ms con código 0 y, tras salir, `GetAsyncKeyState` de Ctrl izquierdo, Ctrl derecho, Escape y el botón central daba "suelto". Cubre el modo desatendido. **El modo interactivo (botón de la ventana) lo probó el usuario a mano (paso 14), con resultado satisfactorio** (ver "Validación manual"). No se probó en una máquina cargada ni con el cierre justo durante `Stop`/`Start` del último paso.

**Incidentes durante las pruebas** (de mi autoprueba, no del producto):
- *Fase 3, primeras ejecuciones:* la autoprueba no comprobaba qué ventana había bajo el cursor antes de inyectar. En la primera, las pulsaciones sintéticas (Ctrl, clics centrales, Escape) pudieron ir a otra ventana si la de Radial no estaba encima. En la segunda, el clic central del último paso se envió en la posición original del cursor del usuario, fuera de la ventana de Radial. No se sabe qué ventana recibió esas pulsaciones.
- *Corrección v0.7, ejecuciones de verificación:* con la marca de 64 bits que no coincidía en el ratón, el gesto no se activaba y el paso "Escape" pulsó Ctrl+Escape, que Windows interpreta como la tecla de Windows y **abrió el menú Inicio**. Las comprobaciones de `EnsureSafe` lo detectaron (la ventana bajo el cursor era `StartMenuExperienceHost`) y abortaron sin inyectar nada más; varias ejecuciones seguidas encontraron el menú todavía abierto y abortaron al empezar. Los eventos que sí se enviaron (Ctrl y Escape) llegaron a la ventana de Radial o al menú Inicio, pero **no se ha comprobado qué recibió cada aplicación**. En esas ejecuciones también se detectaron desviaciones del cursor que atribuyo a movimientos reales del ratón (cientos de callbacks de ratón en la ejecución); es una inferencia.

**Validación manual con hardware (realizada por el usuario; resultado satisfactorio).** Según el informe del usuario, comprobó físicamente: activación con Ctrl izquierdo y derecho; rechazo de la secuencia botón central → Ctrl; clic central normal y rueda; cancelación con Escape y al soltar Ctrl; consumo correcto del clic central con Radial activo; clics normales tras detener la captura; reinicio del servicio; foco correcto con Zen en primer plano. El diagnóstico mostró **45 activaciones, 41 confirmaciones, 4 cancelaciones y ningún callback lento (≥ 50 ms)**. Además, **el paso 14** (cerrar la ventana durante la autoprueba, modo interactivo) se probó a mano: Radial se cierra correctamente, libera las entradas y los hooks, y Windows y Zen siguen funcionando con normalidad. Son datos aportados por el usuario; no los reprodujo el asistente. **No se informó** de los pasos 10 y 11 (bloqueo de sesión y suspensión reales), del cierre simple de la ventana con la captura activa (paso 12, fuera del paso 14) ni de otro DPI o varios monitores (paso 13): siguen sin validar (16.12).

**Procedimiento manual con hardware (referencia; el usuario ejecutó lo que resume el párrafo anterior, sin detallar la correspondencia paso a paso):**
1. Abrir `Radial.App.exe` y pulsar **Iniciar captura**. Comprobar: servicio "En marcha", ambos hooks "instalado".
2. Mantener Ctrl izquierdo, pulsar el botón central, mover el ratón, soltar el botón: el registro debe mostrar `Activated`, luego `Confirmed` con la posición de la liberación. El campo "Cursor" sigue al ratón.
3. Repetir con Ctrl derecho.
4. Pulsar el botón central **antes** de Ctrl: no debe aparecer ningún evento y, sobre un navegador, el clic central debe comportarse con normalidad (abrir un enlace en pestaña nueva, autoscroll).
5. Clic central normal sin Ctrl sobre un enlace: se abre como siempre.
6. Ctrl + rueda: no debe activarse.
7. Con el gesto activo, pulsar Escape: `Cancelled` causa `Escape`, y Escape no debe llegar a la aplicación de debajo. Soltar luego el botón central no debe ejecutar nada.
8. Con el gesto activo, soltar Ctrl: `Cancelled` causa `CtrlReleased`; soltar el botón no confirma.
9. Mover el ratón durante unos minutos con la captura activa: sin saltos, sin retraso perceptible, "Callbacks lentos" en 0.
10. Bloquear la sesión (Win+L) con un gesto activo y desbloquear: debe aparecer un aviso y el gesto se cancela. Anotar si, tras desbloquear, el gesto vuelve a funcionar sin reiniciar la captura.
11. Suspender y reanudar el equipo: igual que el punto anterior.
12. Cerrar la ventana con la captura activa: el ratón y el teclado deben seguir funcionando con normalidad y el clic central debe volver a llegar a todas las aplicaciones.
13. Repetir 2 y 4 con una aplicación real en primer plano (navegador) y, si es posible, con otro DPI o con varios monitores.
14. *(v0.7, cierre durante la autoprueba)* Con el menú Inicio cerrado y sin tocar nada más, pulsar **Autoprueba** y, a los 2–3 s, cerrar la ventana de Radial (Alt+F4 o la X). Esperado: la ventana se cierra poco después (cuando la limpieza termina, menos de ~1 s), el registro no llega a mostrarse, y **Ctrl, Escape y el botón central quedan liberados**: el clic central y Ctrl+clic funcionan con normalidad en otra aplicación. Repetirlo 3–4 veces a distintos tiempos. Si algo queda pulsado, anotar en qué momento.

### 16.10 Criterios de aceptación de la fase 3

| # | Criterio | Estado |
|---|---|---|
| 1 | Los hooks se instalan | ✅ Comprobado con los hooks reales |
| 2 | Ctrl primero activa | ✅ Autoprueba sintética · ✅ hardware (usuario) |
| 3 | Botón central primero no activa | ✅ Autoprueba sintética · ✅ hardware (usuario) |
| 4 | El clic central normal llega | ✅ Autoprueba sintética · ✅ hardware (usuario) |
| 5 | La rueda no activa | ✅ Autoprueba sintética y pruebas automáticas · ✅ hardware (usuario) |
| 6 | Escape cancela | ✅ Autoprueba sintética · ✅ hardware (usuario) |
| 7 | Soltar Ctrl cancela | ✅ Autoprueba sintética · ✅ hardware (usuario) |
| 8 | Soltar el botón central confirma | ✅ Autoprueba sintética · ✅ hardware (usuario: 41 confirmaciones) |
| 9 | Los movimientos se comunican | ✅ Autoprueba sintética · ✅ hardware (usuario) |
| 10 | Sin bloqueos perceptibles del ratón | ✅ Callbacks de 1.1–1.8 / 2.5–2.8 ms máximo medidos; el usuario no observó problemas y el diagnóstico mostró 0 callbacks ≥ 50 ms. No se midió bajo carga prolongada |
| 11 | Los hooks se liberan al cerrar | ✅ Parar libera los hooks (comprobado); cerrar la ventana durante la autoprueba, con la captura activa, libera entradas y hooks (usuario, paso 14, y guion del asistente en modo desatendido) |
| 12 | Pruebas de `ChordTracker` | ✅ Pasan |
| 13 | La ventana de diagnóstico refleja el estado real | ✅ El servicio informa de forma veraz (pruebas) y el usuario la usó en la validación manual; el asistente no la inspeccionó visualmente |
| 14 | Compila sin advertencias ni errores | ✅ |
| 15 | Pruebas reales ejecutadas y documentadas | ✅ Sintéticas, y con hardware por el usuario (resultados aportados por él; ver 16.9) |

**La fase 3 está aprobada** (validación manual y auditoría final completadas). Lo que no se validó está en 16.12.

### 16.11 Decisiones y cambios respecto a lo previsto

| Cambio | Motivo |
|---|---|
| El watchdog no comprueba el botón central y añade la comprobación de silencio del hook de ratón | `GetAsyncKeyState` no ve el botón consumido (medido). |
| Movimientos fuera del canal: última posición coalescida | Un canal sin límite con cientos de movimientos por segundo no aporta nada; los críticos conservan su orden. |
| `ISystemStateEvents` público; la implementación sobre `SystemEvents` vive en `Radial.App` | `SystemEvents` no está en el framework base de `Radial.Windows`; evita añadir un paquete. |
| `SetWindowsHookEx` con `DllImport` | `LibraryImport` no marshala delegados. |
| La autoprueba y `SyntheticInput` existen | Sin ellas no había forma de comprobar los hooks reales sin pulsar físicamente; son diagnóstico y se documenta lo que no demuestran. |
| Sin reinstalación automática de hooks | No se puede verificar que ayudaría; se informa y se deja al usuario reiniciar la captura. |
| `SyntheticInput` pasa a interno; la autoprueba usa `GuardedSyntheticInput` | Que no se pueda inyectar sin la comprobación previa ni sin recordar qué se pulsó (D1/D3). |
| `AcceptInjectedInput` → `AcceptOwnSyntheticInput`; `RawInputEvent.OwnSynthetic`; marca `dwExtraInfo` de 32 bits | Aceptar "cualquier inyectado" no distingue a Radial de otras aplicaciones. 32 bits porque el hook de ratón trunca `dwExtraInfo` (medido). |
| `InputDiagnostics.LastEventOwnSynthetic` | Diagnóstico: permitió ver con datos por qué la marca de 64 bits no coincidía en el ratón. |
| `Start` ya no deja `Starting` si el host lanza `InvalidOperationException` | D2; ver 16.7. |
| El cierre de la ventana durante la autoprueba se aplaza hasta que la limpieza síncrona termina | D3; ver 16.9. |

### 16.12 Sin validar al cierre de la fase 3

Lo siguiente **no se ha comprobado** y no debe darse por probado:
- Bloqueo de sesión y suspensión reales (solo con eventos falsos); que el gesto funcione tras desbloquear o reanudar sin reiniciar la captura.
- El Ctrl atascado en el tracker (16.6): hipótesis sin reproducir. AltGr y `SetCursorPos` (16.6): hipótesis.
- DPI distinto de 125 %, varios monitores y que las coordenadas del hook sean píxeles físicos en esos casos.
- Windows 11 estable 25H2 (solo 26H2, build 26300).
- Aplicaciones elevadas (UIPI), pantallas seguras, juegos en pantalla completa exclusiva.
- Comportamiento bajo carga prolongada; eliminación del hook de teclado por Windows (sin detección).
- Smart App Control: no hubo bloqueos en la fase 3, pero no demuestra que no ocurran en otro equipo o compilación (15.3).

## 17. Fase 4: overlay real y acciones (pasos 1 a 3, 4.1 y 4.2)

**Estado: implementada y validada parcialmente; pendiente de aprobación expresa del usuario.** Los pasos 1 y 2 se verificaron al cerrarlos; el paso 3 quedó sin verificar en el equipo de desarrollo por el bloqueo de Smart App Control (17.8); su verificación en GitHub Actions depende de la ejecución confirmada de 622 / 522, que no tiene commit asociado en el repositorio (15.5). Las pruebas automáticas de la 4.1 y la 4.2 no están confirmadas por CI. Los criterios de aceptación de la sección 12 no han cambiado; su evaluación está en 17.11. Hasta la 4.1 (17.9) ningún paso era visible para el usuario; con ella aparece el overlay y con la 4.2 (17.10) el radial ejecuta las acciones elegidas (ocho acciones reales provisionales). Los apartados 17.1 a 17.4 son el paso 1, el 17.5 y el 17.6 el paso 2 y su corrección, el 17.7 y el 17.8 el paso 3, el 17.9 la 4.1, el 17.10 la 4.2 y el **17.11 el resumen de cierre** (automático / manual / pendiente).

### Paso 1: contratos de ejecución de acciones y reloj de producción

### 17.1 Qué contiene

Todo en `Radial.Core` (sin dependencias nuevas, sin APIs de Windows):

| Tipo | Espacio de nombres | Función |
|---|---|---|
| `IActionExecutor` | `Execution` | Contrato: `CanExecute(RadialAction)` y `ExecuteAsync(RadialAction, CancellationToken)`. |
| `ActionExecutionResult`, `ActionExecutionStatus`, `ActionExecutionError` | `Execution` | Resultado: `Succeeded`, `Failed` (con `Error`: código y mensaje) o `Cancelled`. |
| `ActionErrorCodes` | `Execution` | Códigos estables del propio contrato: `UnsupportedAction`, `InvalidAction`. |
| `MonotonicClock` | `Timing` | `IClock` de producción sobre `Stopwatch`. |

**Contrato de `ExecuteAsync`** (documentado en el código): un fallo previsible no lanza, devuelve `Failure(código, mensaje)`; una cancelación devuelve `Cancelled` en lugar de lanzar `OperationCanceledException`; una acción que `CanExecute` rechaza da `UnsupportedAction`; una acción con problemas según `GetProblems()` da `InvalidAction` y no se ejecuta nada; no requiere el hilo de interfaz.

### 17.2 Decisiones de diseño de este paso

| Decisión | Motivo |
|---|---|
| Una sola interfaz no genérica con `CanExecute` | Coincide con la sección 8 (un ejecutor por tipo, resueltos en un registro) sin reflexión ni genéricos: el consumidor inyecta una única `IActionExecutor` y cada ejecutor decide qué tipos acepta. Añadir un ejecutor no toca el modelo, el motor ni el validador. |
| Tres estados, no dos: `Cancelled` separado de `Failed` | Cancelar (otro gesto, cierre de la aplicación) no es un error y no debe notificarse como tal. |
| Error = `Code` (inglés, estable) + `Message` (español) | Misma convención que `ValidationIssue`: las pruebas comprueban códigos, no mensajes, y no depende de ninguna interfaz. |
| El resultado solo se crea con `Success`, `Failure` o `Cancelled` | Garantiza que `Error` existe si y solo si el estado es `Failed`. |
| `ActionErrorCodes` solo contiene los códigos del propio contrato | Cada ejecutor añadirá los suyos (p. ej. el vencimiento de un atajo) cuando exista, en lugar de definirlos por adelantado. |
| `MonotonicClock` en `Radial.Core`, no en `Radial.Windows` | Solo usa la biblioteca base (`Stopwatch`), no depende de ninguna plataforma, y así lo cubren las pruebas de Core. Reemplaza lo que decía 14.7 ("no hay reloj de producción en Core todavía"). |
| `Now` es el tiempo desde que se crea el reloj | `IClock` solo garantiza diferencias fiables. Medir contra un origen evita convertir el contador absoluto a ticks, que desbordaría con tiempos de actividad largos. |

### 17.3 Qué NO hay todavía

Ejecutores concretos (aplicación, archivo, carpeta, URL, atajo), `IProcessLauncher`, envío de teclas, espera de modificadores, coordinador, canal de eventos para varios lectores, overlay, `RadialView`, DPI y monitores, configuración provisional, notificaciones y cambios de ciclo de vida. Son pasos posteriores de la fase 4. **Nada de la fase 3 se ha modificado** (hooks, `ChordTracker`, servicio de entrada, autoprueba).

### 17.4 Verificación de este paso

**Automática (421 pruebas en verde: 272 Core + 149 Windows; 20 nuevas, las 401 anteriores sin modificar; build sin advertencias):**
- `ActionExecutionResultTests`: éxito, fallo con código y mensaje, cancelación sin error, rechazo de código o mensaje vacíos, los tres resultados distinguibles, igualdad por valor, códigos estables.
- `ExecutionContractIndependenceTests`: Core no referencia ensamblados de procesos, red, Win32 ni WPF; la API pública de `Execution` solo expone tipos de Core o de la biblioteca base; la firma se integra con `RadialAction` y `CancellationToken`. Se comprobó que fallan si se introduce un `System.Diagnostics.Process` en la API (mutación hecha a mano y revertida).
- `MonotonicClockTests`: es un `IClock`, no retrocede (100 000 lecturas), avanza sin que nadie lo avance, mide el tiempo real en las unidades correctas (cota inferior estricta de 20 ms, superior muy holgada) y el motor funciona con él. Son las únicas pruebas de Core con tiempo real, y solo ~20 ms.
- **No hay prueba de `IActionExecutor` en sí**: es una interfaz sin implementación. Se probará con cada ejecutor concreto.

**Manual:** ninguna; este paso no cambia el comportamiento de la aplicación.

### 17.5 Paso 2: parser de atajos

> **Sustituido en la fase 6 (v0.20, 19.4):** el vocabulario ya no es de 66 teclas sino de 118 nombres estables por tecla virtual, con captura y nombres según el teclado activo. La regla de la fuente única (`KeyVocabulary` en Core, tabla de códigos en Windows) se mantiene. Lo que sigue describe el estado de la fase 4.

**Estado: implementado y probado automáticamente. Es solo traducción: no envía ninguna tecla, no usa P/Invoke ni `SendInput`, no lee el teclado.** El envío de teclas, la espera de modificadores y el ejecutor del atajo son pasos posteriores. *(Tras la revisión del paso 2, la lista de nombres de tecla pasó a `Radial.Core` como única fuente de verdad y el parser solo asigna códigos; ver 17.6. Lo que sigue describe el parser y la tabla de códigos, que no cambiaron.)*

Código en `Radial.Windows/Shortcuts` (tipos internos; las pruebas acceden por `InternalsVisibleTo`):

| Tipo | Función |
|---|---|
| `ShortcutParser` | `TryParse(KeyChord?, out ParsedShortcut)` y `TryParseKey(string?, out VirtualKey)`. Devuelven falso, sin lanzar, ante una entrada nula, vacía o fuera del vocabulario. `SupportedKeys` lista el vocabulario. |
| `VirtualKey(Name, Code, Extended)` | Nombre canónico, código `VK_*` e indicador de tecla extendida. |
| `ParsedShortcut(Modifiers, Key)` | El `KeyModifiers` **del modelo, sin tocar**, y la tecla traducida. |

**Vocabulario cerrado (66 teclas), definido en `Radial.Core` (17.6).** Sin alias, traducciones ni nombres alternativos. No se distingue entre mayúsculas y minúsculas, pero el nombre canónico es estable (letras en mayúscula; el resto, tal como aparece abajo). No se recortan espacios. Solo se admite ASCII.

| Teclas | Nombre canónico | Código VK | `Extended` |
|---|---|---|---|
| Letras | `A` … `Z` | `0x41` … `0x5A` | no |
| Dígitos (fila superior) | `0` … `9` | `0x30` … `0x39` | no |
| Función | `F1` … `F24` | `0x70` … `0x87` | no |
| Enter | `Enter` | `0x0D` (`VK_RETURN`) | no |
| Tab | `Tab` | `0x09` | no |
| Flecha izquierda | `ArrowLeft` | `0x25` | **sí** |
| Flecha arriba | `ArrowUp` | `0x26` | **sí** |
| Flecha derecha | `ArrowRight` | `0x27` | **sí** |
| Flecha abajo | `ArrowDown` | `0x28` | **sí** |

**Fuera del vocabulario** (se rechazan): Ñ y letras no ASCII, símbolos y puntuación y cualquier tecla OEM que dependa de la distribución, el teclado numérico y su Enter, Escape, Space, Backspace, Delete y toda tecla no enumerada. Ampliar el vocabulario es una decisión de diseño que exige revisar esta tabla.

Decisiones y observaciones:
- **Independiente de la distribución.** La tabla es fija: no se usa `VkKeyScan` ni nada que dependa del teclado, del idioma o de la cultura (hay una prueba con la cultura turca). Por eso Ñ y los símbolos quedan fuera: su tecla depende de la distribución.
- **Los modificadores no se parsean ni se validan aquí.** `KeyChord.Modifiers` es un `KeyModifiers` ya estructurado y pasa tal cual (incluidos bits desconocidos). Comprobar que los bits son válidos es de `SendKeysAction.GetProblems()`, que el ejecutor debe consultar antes de ejecutar (contrato del paso 1).
- **`Extended` solo en las flechas.** Sin ese indicador, `SendInput` con `VK_LEFT`… puede interpretarse como las flechas del teclado numérico.
- **Incoherencia detectada en la revisión y corregida (17.6).** Tal como se entregó este paso, `SendKeysAction.GetProblems()` aceptaba cualquier nombre de hasta 32 caracteres sin espacios y el parser solo las 66 teclas, así que `Delete` o `Space` validaban pero no se podían ejecutar.
- **Filtro ASCII** (ahora en `KeyVocabulary`, 17.6). Medido en .NET 10: sin él, `OrdinalIgnoreCase` ya rechaza el signo Kelvin, la «s» larga, la «i» sin punto y los caracteres de ancho completo. Se mantiene para que «solo ASCII» sea una garantía por construcción y no depender de las tablas del runtime; las pruebas fijan esos casos.

**Verificación del parser (automática; 191 pruebas nuevas, 612 en total tras este paso: 272 Core + 340 Windows; build sin advertencias; sin cambios tras la corrección 17.6, que las mantiene y añade más):** las 26 letras y los 10 dígitos con su código, F1–F24 (cada una, más valores conocidos y el rechazo de F0, F25, F01, F100…), Enter y Tab sin `Extended`, las cuatro flechas con `Extended`, que solo las flechas son extendidas, mayúsculas y minúsculas mezcladas con nombre canónico, la cultura turca, que el vocabulario es exactamente el aprobado (66 nombres, sin dos nombres para la misma tecla), el rechazo de Ñ, acentos, letras de ancho completo, Kelvin, símbolos, nombres fuera del vocabulario, cadenas vacías o con espacios, nulos y atajos nulos, y que las 16 combinaciones de modificadores se conservan intactas. Los códigos esperados de las pruebas están escritos a mano, no calculados con la misma fórmula. Se comprobó que fallan si se desplaza F1, si `ArrowUp` pierde `Extended` o si se añade `Space` (mutaciones hechas a mano y revertidas).

**No hay verificación manual ni envío real:** este paso no puede fallar en la práctica hasta que algo envíe las teclas.

### 17.6 Corrección del paso 2: una única fuente de verdad del vocabulario

> **Ampliado en la fase 6 (19.4):** la lista cerrada de 66 teclas se amplió a 118 nombres; la regla de la única fuente de verdad sigue vigente.

**Problema (detectado al revisar el paso 2).** Había dos definiciones de qué teclas son válidas: `SendKeysAction.GetProblems()` en Core (cualquier nombre de ≤ 32 caracteres sin espacios) y `ShortcutParser` en Windows (las 66 aprobadas). Una acción con `Delete`, `Space` o `Ñ` pasaba la validación de la configuración y no se podía ejecutar.

**Decisión.** El vocabulario permitido es una regla del dominio y vive **solo en `Radial.Core`**; Windows únicamente convierte esos nombres a códigos de tecla virtual:

| Pieza | Proyecto | Responsabilidad |
|---|---|---|
| `KeyVocabulary` (`Radial.Core.Model`, junto a `KeyChord`) | Core | **Única lista** de los 66 nombres (`Names`) y la regla de comparación (`TryGetCanonicalName`: sin distinguir mayúsculas, solo ASCII, sin recortar espacios, sin depender de la cultura). `Description` es un resumen para mensajes, construido con la misma definición. Sin códigos de Windows. |
| `SendKeysAction.GetProblems()` | Core | Valida la tecla con `KeyVocabulary`: vacía o fuera del vocabulario es un problema (`action.invalid`). El mensaje lista las teclas admitidas y no repite el texto recibido, que puede ser enorme. Los modificadores se validan exactamente como antes. |
| `ShortcutParser` | Windows | Recorre `KeyVocabulary.Names` y asigna a cada nombre su código VK y su indicador `Extended`. **No tiene una segunda lista de nombres**: lo que Core no admite no es alcanzable, y un nombre que Core añadiera sin código VK haría fallar la inicialización en lugar de aceptarse a medias. |

**Consecuencias.**
- Lo que valida una acción es siempre ejecutable (y al revés), para cualquier entrada; una prueba lo comprueba sobre más de 500 candidatos.
- Ampliar el vocabulario exige tocar dos cosas, a propósito: la lista de Core y la tabla de códigos de Windows. Si se olvida la segunda, el fallo es inmediato y explícito.
- Se eliminó `SendKeysAction.MaxKeyLength` (32): el vocabulario cerrado hace redundante el chequeo de longitud y espacios (el nombre más largo mide 10). Nadie más la usaba.
- `KeyChord` y `KeyModifiers` no cambian. Una configuración con una tecla fuera del vocabulario ahora **se rechaza al validar**, no al ejecutar; el editor de las fases 5 y 6 deberá ofrecer solo `KeyVocabulary.Names`.

**Pruebas modificadas por ser incorrectas con el nuevo comportamiento (2, cambio mínimo):** `RadialModelTests.EveryActionKind_CanBeRepresented` exigía que `Delete` no tuviera problemas (ahora usa `ArrowLeft`), y `ConfigurationValidatorTests.InvalidActions` usaba la constante eliminada (ahora el valor literal anterior, 33 caracteres, que sigue siendo inválido). No se eliminó ninguna prueba.

**Verificación (automática; 304 pruebas nuevas, 916 en total: 568 Core + 348 Windows; build sin advertencias):**
- Core, `KeyVocabularyTests`: los nombres son exactamente los 66 aprobados (lista escrita a mano), solo lectura, aceptados en cualquier grafía con su nombre canónico, rechazo de Ñ, Space, Delete, Escape, símbolos, numpad, alias, nombres desconocidos, vacíos, con espacios y nulos, cultura turca.
- Core, `SendKeysActionValidationTests`: cada una de las 66 teclas supera `GetProblems()` y el validador de la configuración en cualquier grafía; cada nombre rechazado produce un único problema y `action.invalid`; las 16 combinaciones de modificadores válidas se aceptan, los bits desconocidos se siguen rechazando y una tecla inválida con modificadores inválidos da dos problemas; una tecla de 100 000 caracteres no se repite en el mensaje.
- Windows, `KeyVocabularyConsistencyTests`: el parser admite exactamente los nombres de Core; cada nombre de Core se traduce en cualquier grafía; el parser y Core coinciden en cada candidato (los nombres en tres grafías, con espacios, todos los caracteres sueltos hasta U+017F, Space, Delete, Ñ, nulo…); una acción valida si y solo si se traduce.
- Las 191 pruebas del parser (códigos VK y `Extended`) siguen pasando **sin modificarlas**.
- Mutaciones hechas a mano y revertidas: añadir `Space` solo en Core (fallan Core y Windows), volver a la validación antigua (fallan 68 en Core y 5 en Windows), quitar la tabla de `ArrowUp` en Windows (fallan 106 en Windows).

### 17.7 Paso 3: ejecutores de aplicación, archivo, carpeta y URL

**Estado al cerrar el paso 3: escrito y compila (0 advertencias, 0 errores); sin verificar en el equipo de desarrollo (17.8).** *(Actualizado en la v0.15: desde la 4.2 el coordinador los invoca; las pruebas se ejecutan en GitHub Actions (ejecución verde sobre `349fbde`, cifras 622 / 522 comunicadas por el usuario, 15.5); en la validación manual del usuario funcionaron abrir una carpeta, una URL y una aplicación.)*

**Piezas en `Radial.Windows/Actions`** (públicas, para que `Radial.App` las componga a mano):

| Pieza | Función |
|---|---|
| `IProcessLauncher`, `ProcessLaunchRequest`, `ProcessLaunchResult` | Lo único que los ejecutores necesitan del sistema, y lo que las pruebas sustituyen. La solicitud lleva **destino, argumentos y directorio de trabajo por separado**. El resultado es `Started`, `TargetNotFound` o `Failed` (con el código nativo). Un fallo previsible se devuelve, no se lanza. La cancelación solo se atiende **antes** de iniciar: una vez iniciado, no se informa de una cancelación. `Started` significa que el sistema aceptó el lanzamiento, no que el programa llegara a mostrarse. |
| `ShellProcessLauncher` | Implementación real: `Process.Start` con `UseShellExecute = true`, en un hilo del pool (`ShellExecute` puede bloquear). **Nunca** `cmd.exe`, PowerShell ni scripts. Traduce `Win32Exception` 2 y 3 (y `FileNotFoundException`) a `TargetNotFound`, el resto de errores del sistema y `InvalidOperationException` a `Failed`, y deja pasar cualquier otra excepción (error de programa). La función de arranque es inyectable (interno) para probarlo sin iniciar procesos. |
| `ProcessActionExecutor<TAction>` | Esqueleto común. Orden fijo: tipo no admitido (`UnsupportedAction`) → token cancelado (`Cancelled`) → `GetProblems()` de Core no vacío (`InvalidAction`, sin pedir lanzamiento; la validación **no se repite**, se reutiliza) → comprobaciones propias del tipo → última comprobación de cancelación → lanzamiento → traducción del resultado. Nunca se da por bueno un lanzamiento antes de que el lanzador lo confirme. Los mensajes no incluyen rutas ni direcciones. |
| `LaunchApplicationActionExecutor` | Pasa `Path`, `Arguments` y `WorkingDirectory` tal cual y separados. **No** comprueba que el archivo exista: un nombre sin ruta lo resuelve el sistema y, si no lo encuentra, lo informa el lanzador (`TargetNotFound`). |
| `OpenFileActionExecutor` | Comprueba que el archivo existe (`File.Exists`: una carpeta no es un archivo) y lo abre con la asociación del sistema. No interpreta la extensión. |
| `OpenFolderActionExecutor` | Comprueba que la ruta es una carpeta existente (`Directory.Exists`: un archivo no es una carpeta) y la abre con el sistema. |
| `OpenUrlActionExecutor` | Repite, de forma independiente y justo antes de lanzar, que la dirección es absoluta, tiene host y su esquema está permitido; lanza la forma analizada (`Uri.AbsoluteUri`). Falla con `UrlNotAllowed`. |

**Códigos de error nuevos** (`ActionErrorCodes`, solo situaciones reales que se distinguen): `TargetNotFound` (el destino no existe), `LaunchFailed` (el sistema rechazó abrirlo: sin asociación, acceso denegado, elevación cancelada...) y `UrlNotAllowed` (la comprobación defensiva de la URL). Se reutilizan `UnsupportedAction` e `InvalidAction`.

**Decisión: `OpenUrlAction` solo admite `http` y `https`.** El modelo admitía también `mailto:` (secciones 7.2 y 14.6 de versiones anteriores), pero la ejecución de la fase 4 es solo http y https; con los dos criterios distintos, una acción `mailto:` validaba en Core y no se podía ejecutar (la misma incoherencia que se corrigió con las teclas en 17.6). Ahora hay **una única definición**, `OpenUrlAction.AllowedSchemes` en Core: la usa `GetProblems()` (todo lo demás, incluidos `mailto:`, `javascript:`, `file:`, `data:`, esquemas desconocidos, direcciones relativas y mal formadas, se rechaza al validar) y la reutiliza el ejecutor de Windows en su comprobación defensiva. Ampliarla es una decisión de diseño. Pruebas modificadas por este cambio: `ConfigurationValidatorTests` (`mailto:` pasa de las acciones válidas a las inválidas) y la prueba de Windows que fijaba la discrepancia.

**Revisión estática** (hecha porque las pruebas de Windows no se pueden ejecutar; resultado detallado en 17.8):
- Sin `cmd`, PowerShell ni scripts: aparecen solo en comentarios que dicen que no se usan. El único `Process.Start` de Radial es el valor por defecto del constructor público de `ShellProcessLauncher`; ninguna prueba usa ese constructor.
- Sin concatenación: destino, argumentos y directorio van en campos distintos de `ProcessStartInfo`; los argumentos se pasan como los escribió el usuario, sin interpretarlos.
- Ningún camino devuelve éxito sin que el lanzador confirme `Started`.
- No se corrigió ningún defecto de código por esta revisión.
- **Riesgos residuales (no son defectos respecto a los requisitos de este paso, pero hay que decidirlos antes de importar configuraciones, fase 8):**
  1. `LaunchApplicationAction` acepta como destino cualquier texto no vacío, incluido uno que parezca un identificador de protocolo (`ms-settings:`, etc.): `ShellExecute` lo despacharía a su controlador. No se restringió porque no era un requisito; una configuración importada no es de confianza.
  2. `OpenFileAction` con una ruta a un ejecutable o un script lo **ejecuta Windows** a través de su asociación. Radial no interpreta la extensión, pero tampoco la bloquea.
  3. Las rutas relativas y las variables de entorno no se resuelven (fuera del alcance de la fase): `File.Exists` y `Directory.Exists` resolverían una ruta relativa contra el directorio actual del proceso, que en una aplicación de bandeja es impredecible.

### 17.8 Paso 3: verificación y limitación del entorno de validación

**No se dispone de evidencia suficiente para aprobar el paso 3.** Las pruebas de Windows nunca se han ejecutado y las de Core no se han ejecutado sobre el código actual.

| Qué | Resultado |
|---|---|
| Compilación (`dotnet build -c Release`) | **Correcta**: 0 advertencias, 0 errores, incluidos los proyectos de pruebas |
| Pruebas de Core, **antes** del ajuste de URL | 571 pruebas superadas (las 568 anteriores y 3 nuevas de códigos de error). Ejecución previa al bloqueo |
| Pruebas de Core, **con el código actual** (ajuste de URL) | **No ejecutadas**: Smart App Control bloqueó `Radial.Core.Tests.dll` recién compilado |
| Pruebas de Windows (las 348 anteriores y todas las del paso 3) | **No ejecutadas**: Smart App Control bloqueó `Radial.Windows.Tests.dll` |
| Revisión estática de los ejecutores | Hecha (17.7); no sustituye a las pruebas |

**El bloqueo es una limitación del entorno de validación, no un fallo de las pruebas.** Detalle: `dotnet test` termina con `FileLoadException 0x800711C7` («An Application Control policy has blocked this file»). El registro de Code Integrity (eventos 3077 y 3033) dice que `testhost.exe` intentó cargar `Radial.Windows.Tests.dll` (08:50) y después `Radial.Core.Tests.dll` (09:11) y que «no cumplían los requisitos de nivel de firma Enterprise»; identificador de política `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`. Los dos binarios eran compilaciones nuevas sin firmar. Es el riesgo descrito en 15.3: el mismo proyecto de pruebas de Core cargó sin problema minutos antes con otra compilación. No se conoce la causa de la diferencia.

**Qué no se hizo, a propósito (15.2):** no se desactivó Smart App Control, no se tocaron políticas, no se crearon excepciones, no se modificaron firmas ni nombres ni opciones de compilación, no se repitieron compilaciones para conseguir que se cargara un binario y no se volvieron a lanzar las pruebas de Windows. Tras el bloqueo del binario de Core solo se repitió `dotnet test` sobre el mismo binario, sin recompilar.

**Qué falta para verificar el paso 3 (sin validar):**
- Ejecutar las pruebas de Windows (348 anteriores y todas las nuevas) y las de Core con el código actual, en un entorno donde se puedan cargar: otro equipo o una compilación firmada. La firma de código se evalúa en la fase 8 (15.2), con la restricción de coste.
- Algunas pruebas nuevas dependen de cómo analiza .NET ciertas direcciones (p. ej. `http://exa mple.com` o `https://[no-es-ipv6` como mal formadas); si alguna falla al ejecutarse, es una expectativa de la prueba que hay que revisar, no necesariamente un defecto del ejecutor. Se retiraron dos entradas cuya expectativa no se podía respaldar (`http:///solo-ruta`, `http:/example`).
- Validación manual del lanzador real (`ShellProcessLauncher` solo se ha probado con el arranque inyectado): abrir una aplicación, un archivo, una carpeta y una dirección web en un equipo donde se pueda ejecutar, y comprobar los fallos (destino inexistente, archivo sin aplicación asociada).

*(Nota de la v0.13, corregida en la v0.15: este apartado describe el estado al cerrar el paso 3 y se conserva como histórico. Las pruebas de Core y de Windows se ejecutaron después; la referencia de validación es GitHub Actions (cifras confirmadas y cifras solo de informe, distinguidas en 15.5). La limitación del equipo de desarrollo —no lanzar allí las pruebas de Windows— se mantiene.)*

### 17.9 Fase 4.1: primera interfaz radial visible

**Estado: implementada y compilada; comprobada con pruebas automáticas, con una comprobación del overlay real sin entrada y, desde la v0.15, con la validación manual básica del usuario (17.11). Lo que sigue sin validar está en la lista de abajo y en 17.11.** No cambia el comportamiento de la captura global ni sus protecciones. En la 4.1 el radial no ejecutaba acciones (solo informaba de la elegida); la ejecución se añadió en la 4.2 (17.10). Quedan fuera submenús, persistencia y editor (fases posteriores).

**Qué se añadió**

| Dónde | Tipo | Función |
|---|---|---|
| `Radial.Core/Geometry` | `RadialLayout`, `SectorShape` | Disposición pura del dibujo: los cuatro vértices de cada sector anular y el punto de su etiqueta, con los mismos ángulos que `RadialGeometry` (norte = 0°, horario). Lo dibujado en una dirección es lo que el motor selecciona para un cursor sobre ese dibujo (prueba). |
| `Radial.Windows/Overlay` | `OverlayPlacement` | Cálculo puro: tamaño físico (par, para que el cursor caiga en el píxel central) y rectángulo centrado en el cursor. No ajustaba a los bordes hasta la v0.22 (ahora `Fit`, 6.4) (E-1, opción A: sin ajuste). |
| `Radial.Windows/Overlay` | `OverlayWindowInterop` | Parte Win32 sin WPF: añade `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_TRANSPARENT`, lee los estilos, coloca según la secuencia de 6.2 (`GetDpiForWindow` del monitor de destino), contesta `WM_MOUSEACTIVATE` con `MA_NOACTIVATE`. P/Invoke aparte, en `Native/WindowNativeMethods.cs`; `NativeMethods` no se tocó. |
| `Radial.Windows/Overlay` | `RadialOverlayCoordinator`, `IRadialPresenter` | Une gesto, motor y ventana: `Activated` → muestra el menú raíz sobre el cursor; el cursor se lee periódicamente y resalta el sector; `Confirmed`/`Cancelled` → oculta e informa (`Completed`/`Aborted`). No sabe de WPF. Mide en DIP relativos al centro (píxeles físicos ÷ escala del monitor), de modo que el radio de la zona central de la configuración coincide con el del botón central dibujado a cualquier DPI. |
| `Radial.App/Overlay` | `OverlayWindow` | La ventana (ADR-001, variante A): `AllowsTransparency`, sin borde, `Topmost`, `ShowActivated=false`, `Focusable=false`. Se crea y prepara una vez al arrancar y se reutiliza. Implementa `IRadialPresenter`. |
| `Radial.App/Overlay` | `RadialView`, `RadialTheme`, `GlyphCatalog` | El dibujo: disco oscuro translúcido, ocho sectores con `StreamGeometry` (no botones), botón central, icono (Segoe Fluent Icons con Segoe MDL2 Assets de respaldo) y etiqueta por sector. El sector resaltado cambia de relleno, borde, color de icono y etiqueta, crece 8 DIP hacia fuera y una marca del botón central apunta a él. Sin degradados, sombras ni efectos: grafito con un único acento frío. |
| `Radial.App/Overlay` | `ProvisionalMenu` | Ocho elementos **fijos y provisionales**, sin grupos ni submenús. En la 4.1 eran solo marcadores sin acción real (Navegador, Carpeta, Notas, Calculadora, Ajustes, Copiar, Pegar, Buscar); **desde la 4.2 son las ocho acciones reales de 17.10** (Copiar, Pegar, Deshacer, Seleccionar todo, Guardar, Carpeta, Web, Navegador). Se sustituye por la configuración cargada en la fase 5. |
| `Radial.App` | `MainWindow` | Reenvía los eventos del gesto al coordinador y lo sondea cada 15 ms mientras haya un radial. La captura se sigue iniciando con el botón «Iniciar captura»: no arranca sola. |

**Decisiones**

| Decisión | Motivo |
|---|---|
| El coordinador está en `Radial.Windows`, no en la interfaz | «Sin lógica de negocio en código de interfaz»; se prueba con un presentador falso, sin WPF y sin Windows real. |
| Click-through (`WS_EX_TRANSPARENT`) **siempre** activo | La interacción se resuelve con los hooks, nunca con el ratón sobre la ventana (pendiente de 6.1). Todavía no hay nada en el overlay que necesite recibir clics. |
| Sin `WM_NCHITTEST → HTTRANSPARENT` | No deja pasar el clic a otro proceso (6.1). |
| La ventana se coloca con `Opacity = 0`, se muestra, se vuelve a colocar y entonces `Opacity = 1` | WPF puede situar la ventana por su cuenta en la primera aparición; no debe verse ese fotograma. |
| El cursor se lee con un `DispatcherTimer` (15 ms) y la confirmación usa la posición del evento de soltar | `CompositionTarget.Rendering` no se dispara de forma continua si la escena no cambia (6.3); el evento de soltar lleva la posición exacta. |
| Aislamiento durante la autoprueba: `Isolate()` / `Resume()` | La entrada sintética no debe mostrar el overlay (taparía la zona de prueba y falsearía sus comprobaciones de ventana). Los eventos que la autoprueba deja sin leer se descartan al reanudar por su marca de tiempo (`Stopwatch`). Sin supervisión (`--selftest`) el coordinador nace aislado. |
| Un fallo del overlay se registra y se contiene | La ventana de diagnóstico es el único lector del canal de eventos (sin límite): si el bucle muriera, el canal dejaría de vaciarse. |

**Verificación**

| Qué | Resultado |
|---|---|
| `dotnet build -c Release` (solución completa) | Correcta: 0 advertencias, 0 errores |
| Pruebas de Core *(cifra del informe de la 4.1; procedencia no registrada, **no confirmada por CI**, ver 15.5)* | **660 superadas**, 0 fallos (38 nuevas: `RadialLayout`) |
| Pruebas de Windows *(ídem)* | **574 superadas**, 0 fallos (52 nuevas: colocación del overlay y coordinador, incluidos el aislamiento y el descarte por marca de tiempo) |
| Comprobación del overlay real (`Radial.App.exe --overlay-check <archivo>`) | Superada en un monitor al 125 % (120 DPI): estilos `0x080800A8` (layered, transparente al ratón, sin activación, ventana de herramienta, topmost) sin cambios al mostrar; ventana de 526 px (420 DIP × 1,25) con el centro **exactamente** en el cursor; `GetForegroundWindow` igual antes y después de mostrar; `IsActive = false` |
| Aspecto (`Radial.App.exe --render-preview <archivo.png>`) | Dibujo en reposo y con dos sectores resaltados revisado a partir del PNG |

`--overlay-check` y `--render-preview` son herramientas de desarrollo: no instalan hooks, no inyectan entrada ni hacen clics (el click-through no se prueba con clics sintéticos). Comprueban el estado de la ventana, no sustituyen la prueba con ratón.

**Sin validar (no afirmar que funciona)** *(lista escrita al cerrar la 4.1; el estado actual de cada punto está en 17.11 y los puntos que el usuario ya probó llevan la nota correspondiente)*
- ~~El gesto real (Ctrl + botón central) mostrando el overlay con hardware~~ → **probado a mano por el usuario** (17.11, M1–M3): el overlay aparece, se seleccionan sectores y desaparece al confirmar o cancelar. Siguen sin detallarse el método de cancelación ni las aplicaciones usadas.
- Que el clic **atraviesa** realmente el overlay hacia la ventana de debajo (solo se comprobó el estilo `WS_EX_TRANSPARENT`, no el efecto).
- Foco con aplicaciones reales (el overlay no se activa según el estilo y según `GetForegroundWindow` justo después de mostrar; no se ha medido con aplicaciones concretas, pantalla completa exclusiva ni ventanas elevadas).
- DPI de 100, 150 y 200 %, varios monitores con escalados distintos, cambio de monitor a mitad del gesto (el coordinador usa la escala del monitor donde se mostró; si el cursor cruza a otro monitor con distinto DPI durante el gesto, la medida queda desfasada) y `WM_DPICHANGED` real.
- Comportamiento en los bordes de la pantalla (E-1, opción C desde la v0.22, 6.4): implementado y sin validar a mano.
- Windows 11 estable 25H2 (solo 26H2).
- Rendimiento medido: el objetivo de 6.3 (del gesto al primer fotograma ≤ 100 ms) no se ha medido con este overlay.
- Los iconos de archivo (`IconSource.File`) no se dibujan: se muestra un glifo neutro.

**Cómo probarlo a mano**
1. `dotnet build -c Release` y `dotnet run -c Release --project src/Radial.App` (o ejecutar `Radial.App.exe`).
2. Pulsar **Iniciar captura** y comprobar que «Hook de teclado» y «Hook de ratón» están instalados. La fila «Overlay (estilos)» muestra los estilos reales de la ventana.
3. Abrir otra aplicación (p. ej. el Bloc de notas), dejar el cursor sobre ella y mantener **Ctrl**, pulsar el **botón central** y mantenerlo: el radial aparece centrado en el cursor.
4. Mover el ratón: el sector bajo el cursor se resalta (borde azul, relleno más claro, icono azul) y la marca del botón central apunta a él. En el centro no hay ninguno resaltado.
5. Foco: el título de la ventana de la otra aplicación sigue activo y se puede seguir escribiendo en ella.
6. Soltar el botón central oculta el radial y la ventana de diagnóstico registra `[radial] confirmado: «…»` (en la 4.1 no se ejecutaba nada; desde la 4.2 se ejecuta la acción, ver 17.10). **Esc** o soltar **Ctrl** antes de confirmar registra `[radial] cancelado (...)`.
7. Click-through: con el radial visible, ver que el cursor y la aplicación de debajo siguen respondiendo (p. ej. el cursor de texto del Bloc de notas); no usar entrada sintética.
8. **Autoprueba**: lanzar «Autoprueba (entrada sintética)» y comprobar que no aparece el radial (el coordinador permanece aislado) y que la autoprueba da los mismos resultados que antes.

### 17.10 Fase 4.2: motor de acciones y primeros atajos funcionales

**Estado: implementada y compilada; pruebas automáticas: 694 Core / 855 Windows según el informe de esta subfase, **sin confirmar por GitHub Actions** (15.5); validación manual básica del usuario realizada (17.11): las ocho acciones funcionan con el gesto real. Siguen sin validar los puntos de la lista de abajo y de 17.11.** El `SendInput` real no se ha ejecutado desde ninguna prueba automática (las pruebas usan un inyector falso, a propósito: no se inyecta entrada real sobre el escritorio); su única validación es la manual del usuario. No cambia `GlobalInputService`, los hooks, `ChordTracker`, `GuardedSyntheticInput` ni la autoprueba.

**Auditoría previa (resumen).** `SendKeysAction` ya validaba con `KeyVocabulary` (66 teclas) y representaba los modificadores con `KeyModifiers` (flags `Control`, `Shift`, `Alt`, `Windows`); `ShortcutParser` ya traducía a VK. **La espera de liberación de modificadores solo existía como diseño (3.4), no como código**; `ChordTracker` solo conoce Ctrl, así que la espera se sondea (precisión añadida a 3.4). `IActionExecutor` y los cuatro ejecutores de apertura se reutilizan tal cual; el coordinador no ejecutaba nada. `SyntheticInput` es de la autoprueba y no se reutiliza. No hay conflicto con la arquitectura: nada de lo pedido contradice el diseño.

**Qué se añadió**

| Dónde | Tipo | Función |
|---|---|---|
| `Radial.Core/Execution` | `CompositeActionExecutor` | Elige el ejecutor de cada acción por `CanExecute` (el «registro» de la sección 8). Sin comportamiento propio. |
| `Radial.Core/Execution` | 4 códigos en `ActionErrorCodes` | `ModifiersNotReleased`, `ForegroundChanged`, `InputInjectionFailed`, `UnexpectedError`. |
| `Radial.Core/Catalog` | `ActionDefinition`, `ActionCategory`, `ActionCatalog`, `BuiltInShortcuts` | Estructura del repertorio: id estable, nombre, categoría, descripción, icono y acción (tipo + parámetros). Distintas definiciones comparten ejecutor. `ActionCatalog` valida y rechaza ids repetidos. `BuiltInShortcuts`: Copiar, Pegar, Deshacer, Seleccionar todo y Guardar. |
| `Radial.Windows/Shortcuts` | `ShortcutActionExecutor` | **El** ejecutor de atajos (ver abajo). |
| `Radial.Windows/Shortcuts` | `ShortcutSequence` (interno) | Traducción pura atajo → secuencia de eventos: modificadores en orden fijo (Ctrl, Mayús, Alt, Win), tecla, y liberación en orden inverso. Ctrl+Mayús+S o Win+E son datos distintos de la misma función. |
| `Radial.Windows/Shortcuts` | `IKeyboardInjector`, `SendInputKeyboardInjector` | Envío con `SendInput` (API documentada) de la secuencia completa en **una** llamada. Sin `dwExtraInfo`: no lleva `SyntheticMarker`, así que los hooks la descartan por inyectada incluso con `AcceptOwnSyntheticInput`. Si Windows acepta solo parte, suelta lo que quedó pulsado y devuelve el fallo. |
| `Radial.Windows/Shortcuts` | `IModifierStateReader`, `Win32ModifierStateReader` | Estado físico de Ctrl, Mayús, Alt y Win (`GetAsyncKeyState`, solo lectura, fuera de los hooks). |
| `Radial.Windows/Overlay` | `RadialOverlayCoordinator` | Nuevo: ejecuta el elemento confirmado con un `IActionExecutor`, sin bloquear, y publica `ActionFinished`; mantiene una acción pendiente cancelable. |
| `Radial.App` | `ActionExecutors` | Raíz de composición: los cuatro ejecutores de apertura (un único `ShellProcessLauncher`) y el de atajos, detrás de un `CompositeActionExecutor`. |
| `Radial.App/Overlay` | `ProvisionalMenu`, `ProvisionalTargets`, `GlyphCatalog` *(en la fase 5 los dos primeros se sustituyeron por `DefaultConfiguration` y la configuración en disco, 18.6)* | Las ocho acciones reales y sus iconos (ver abajo); destinos provisionales aislados en `ProvisionalTargets`. |

**Contrato del ejecutor de atajos** (orden fijo; nada se envía antes del último paso)
1. Acción de otro tipo → `UnsupportedAction`. Token ya cancelado → `Cancelled`.
2. `GetProblems()` de Core no vacío, o tecla sin traducción → `InvalidAction`; no se lee ningún estado ni se envía nada, y el mensaje no repite el texto introducido.
3. Se espera a que se suelten los **cuatro** modificadores físicos (el Ctrl del gesto y cualquier otro), sondeando cada 10 ms. No se pulsa ni se suelta nada artificialmente. Vence a los 5 s (configurable, `ShortcutExecutorOptions`) → `ModifiersNotReleased` indicando cuáles siguen pulsados. Cancelable → `Cancelled`.
4. Si la ventana en primer plano cambió desde el principio de la ejecución → `ForegroundChanged`: el atajo iba para la aplicación que había al confirmar.
5. Una sola secuencia con `IKeyboardInjector`; si el sistema la rechaza → `InputInjectionFailed` (con el código de Windows).
Después de enviar ya no se puede cancelar. Un fallo previsible se devuelve, no se lanza.

**Las ocho acciones del menú** (provisional)

| Posición | Acción | Id | Tipo / destino |
|---|---|---|---|
| Norte | Copiar | `edit.copy` | `SendKeysAction` Ctrl+C |
| Noreste | Pegar | `edit.paste` | `SendKeysAction` Ctrl+V |
| Este | Deshacer | `edit.undo` | `SendKeysAction` Ctrl+Z |
| Sureste | Seleccionar todo | `edit.select-all` | `SendKeysAction` Ctrl+A |
| Sur | Guardar | `edit.save` | `SendKeysAction` Ctrl+S |
| Suroeste | Carpeta | `files.folder` | `OpenFolderAction`: carpeta Documentos del usuario (**provisional**) |
| Oeste | Web | `web.url` | `OpenUrlAction`: `https://www.wikipedia.org` (**provisional**) |
| Noroeste | Navegador | `web.browser` | `LaunchApplicationAction`: `msedge.exe` bajo `Archivos de programa (x86)` (**provisional**) |

Los destinos no se inventan: salen de carpetas conocidas del sistema. Una carpeta que no existe da `TargetNotFound` (la comprueba el ejecutor antes de lanzar), una aplicación que no se encuentra da `TargetNotFound` (la devuelve el lanzador) y una URL que no es `http`/`https` da `UrlNotAllowed` o `InvalidAction`. Todo fallo se comunica en el registro y en la fila «Última acción» de la ventana de diagnóstico; no hay cuadros de mensaje (robarían el foco al destino del atajo) ni notificaciones todavía.

**Decisiones**

| Decisión | Motivo |
|---|---|
| Una clase de ejecutor para todos los atajos | El atajo es dato (`KeyChord`), no comportamiento; un repertorio amplio no exige código. |
| La espera sondea `GetAsyncKeyState` en lugar de usar `ChordTracker` | Hay que cubrir Mayús, Alt y Win, que `ChordTracker` no conoce (y es de la captura protegida). El sondeo está fuera de los callbacks, como prevé 3.4. |
| Se compara la ventana en primer plano antes de enviar | Evita que un atajo pensado para una aplicación llegue a otra si el usuario cambió de ventana mientras esperaba. |
| `SendInputKeyboardInjector` sin la marca de la autoprueba | Separación clara con la captura: los hooks lo ignoran siempre. Reutilizar `SyntheticInput` habría mezclado ambos mundos. |
| Una acción pendiente se cancela al empezar otro gesto, al aislar y al cerrar | Un atajo que espera no debe dispararse más tarde, ya fuera de contexto. |
| El coordinador captura cualquier excepción del ejecutor como `UnexpectedError` | Una tarea que nadie espera perdería el fallo en silencio. |
| Durante la autoprueba no se ejecuta nada | El coordinador aislado ignora los eventos antes de tocar el motor o el ejecutor. |

**Verificación**

| Qué | Resultado |
|---|---|
| Pruebas de Core *(cifra del informe de la 4.2; procedencia no registrada, **no confirmada por CI**, ver 15.5)* | **694 superadas**, 0 fallos (34 nuevas: catálogo, ejecutor compuesto, códigos) |
| Pruebas de Windows *(ídem)* | **855 superadas**, 0 fallos (281 nuevas: secuencias de los 16 conjuntos de modificadores con 6 teclas, inyector con la llamada al sistema sustituida —éxito, parcial, nada aceptado, sin marca—, ejecutor —combinaciones, espera, vencimiento, cancelación, validación, foco, fallos— y coordinador con acciones —incluidas ausencia de ejecución aislado y descarte tras la autoprueba—) |
| Compilación | La aplicación y los dos proyectos de pruebas compilan en Release sin advertencias. **La compilación de la solución completa se hizo con `Radial.App` en un directorio de salida aparte** porque había una instancia de Radial en ejecución bloqueando sus DLL (no se cerró: era del usuario) |
| Overlay real (`--overlay-check`) y aspecto (`--render-preview`) | Igual que en 4.1: estilos `0x080800A8`, centrado exacto, foco sin cambios; el menú de ocho acciones se revisó en el PNG (la etiqueta «Seleccionar todo» ocupa dos líneas) |

**Sin validar (no afirmar que funciona)** *(lista escrita al cerrar la 4.2; estado actual en 17.11)*
- ~~El `SendInput` real y cada una de las ocho acciones con el gesto real y un destino real~~ → **probado a mano por el usuario** (17.11, M4–M6): las cinco de teclado, Documentos, Wikipedia y Microsoft Edge funcionan. No se detalló en qué aplicaciones de destino, y los caminos de fallo (`TargetNotFound`, `ModifiersNotReleased`...) no se probaron a mano.
- Aplicaciones destino elevadas (UIPI): Windows puede descartar el teclado sin que `SendInput` lo indique; el fallo no es detectable.
- Atajos que Windows trata especialmente: `Alt+Tab` y `Win+E` se envían, pero su efecto no se ha comprobado; `Ctrl+Alt+Supr` no se puede enviar. Ninguno forma parte del menú actual.
- Aplicaciones que leen el teclado por scancode o con entrada sin procesar (algunos juegos): el envío usa códigos de tecla virtual.
- Carrera residual: entre la última comprobación de modificadores y el envío (microsegundos) el usuario podría pulsar otra tecla; no es atómico, como la inyección de la autoprueba (16.9).
- Los avisos al usuario (bandeja, notificaciones) son de fases posteriores: hoy los fallos solo se ven en la ventana de diagnóstico.
- Todo lo ya listado como sin validar en 17.9 (DPI, varios monitores, bordes, 25H2, rendimiento).

**Cómo probarlo a mano** (ejecutar `Radial.App`, pulsar «Iniciar captura» y poner **otra aplicación en primer plano**; el teclado va a la ventana activa). Gesto: mantener Ctrl, pulsar el botón central, mover al sector, soltar el botón central y **después** soltar Ctrl.
- **Copiar / Pegar / Deshacer / Seleccionar todo / Guardar:** en el Bloc de notas con texto: Seleccionar todo (selecciona), Copiar, mover el cursor y Pegar (pega), Deshacer (revierte), y Guardar (abre «Guardar como» o guarda). Esperado en el registro: `[acción] «…»: correcta` justo al soltar Ctrl.
- **Espera de modificadores:** confirmar un atajo y mantener Ctrl más de 5 s → `FALLÓ (ModifiersNotReleased) … Siguen pulsadas estas teclas…: Ctrl` y no llega nada a la aplicación.
- **Navegador:** abre Edge. **Web:** abre la dirección en el navegador predeterminado. **Carpeta:** abre Documentos en el Explorador.
- **Fallo comunicado:** renombrar temporalmente la carpeta Documentos no es necesario; basta con cambiar `ProvisionalTargets` a una ruta inexistente y comprobar `FALLÓ (TargetNotFound)`.
- **Cancelación:** Esc o soltar Ctrl antes de confirmar → `[radial] cancelado` y no se ejecuta nada. Con un atajo esperando (Ctrl pulsado), empezar otro gesto cancela el primero (`[acción] … cancelada`).
- **Autoprueba:** no aparece el radial y no se ejecuta ninguna acción.

### 17.11 Cierre de la fase 4: qué está probado y qué no

**Resumen de la v0.15 (solo documentación; ni código ni pruebas cambiaron).** Separa tres cosas que antes estaban mezcladas: lo que comprueban las **pruebas automáticas**, lo que el usuario ha **probado a mano** y lo que **sigue pendiente**. Lo que no aparece en las dos primeras tablas no se considera comprobado.

**Procedencia.** Las pruebas manuales las hizo el usuario con el gesto real y hardware y las comunicó en su informe; no las observó el asistente que redacta esto. El informe no recoge la versión de Windows concreta, la configuración de pantallas ni las aplicaciones de destino usadas; esos datos **no constan** y no se suponen. Las cifras de CI confirmadas (622 / 522) también son del informe del usuario; las de 660/574 y 694/855 vienen de los informes de implementación y no están confirmadas (15.5).

#### A. Pruebas automáticas

| Qué | Resultado | Origen |
|---|---|---|
| `Radial.Core.Tests` | Suite en verde; **622** superadas según el usuario | **GitHub Actions**, ejecución nº 2 sobre `349fbde` ([enlace](https://github.com/xrbrr24/Radial/actions/runs/41624193526)): el estado verde está verificado en la API de GitHub; la cifra la comunicó el usuario y no es comprobable sin autenticación. **Código anterior a la 4.1** |
| `Radial.Windows.Tests` | Suite en verde; **522** superadas según el usuario | Ídem |
| `Radial.Core.Tests` (informe de la 4.1 / de la 4.2) | 660 / **694** superadas | **Comunicado por el informe de implementación; NO confirmado por CI**; procedencia no registrada |
| `Radial.Windows.Tests` (informe de la 4.1 / de la 4.2) | 574 / **855** superadas | **Comunicado por el informe de implementación; NO confirmado por CI**; procedencia no registrada |
| Compilación Release de la solución completa | Sin advertencias ni errores al cerrar la 4.2 | Equipo de desarrollo (anotado en 17.10); CI la repite en cada ejecución |
| `--overlay-check` (un monitor al 125 %, 120 DPI) | Estilos `0x080800A8`, centrado exacto en el cursor, `GetForegroundWindow` sin cambios, `IsActive = false` | Equipo de desarrollo; herramienta sin hooks ni entrada (17.9) |
| `--render-preview` | Aspecto del radial en reposo y con sectores resaltados, revisado a partir del PNG | Equipo de desarrollo (17.9, 17.10) |

Qué cubren las pruebas de la 4.1 y la 4.2 (según los informes; **sin confirmar por CI**): disposición del dibujo frente al motor (`RadialLayout`), colocación del overlay, coordinador con presentador falso (aislamiento, descarte por marca de tiempo, acción pendiente y su cancelación, excepciones del ejecutor), catálogo y ejecutor compuesto, secuencias de teclado de los 16 conjuntos de modificadores, `SendInputKeyboardInjector` con la llamada al sistema sustituida (éxito, parcial, rechazo, sin marca), `ShortcutActionExecutor` con inyector y lector de modificadores falsos (espera, vencimiento, cancelación, validación, cambio de foco, fallos) y los ejecutores de aplicación, archivo, carpeta y URL con un lanzador falso.

**Lo que las pruebas automáticas NO cubren, por diseño:** el `SendInput` real, `ShellProcessLauncher` real (solo con arranque inyectado), el renderizado de WPF, los hooks reales y el efecto del click-through. No se inyecta entrada real sobre el escritorio (CLAUDE.md).

#### B. Pruebas manuales realizadas (por el usuario)

| Id | Prueba | Resultado |
|---|---|---|
| M1 | Con el gesto real (Ctrl + botón central) aparece el overlay radial de ocho sectores | Correcto |
| M2 | Mover el cursor permite seleccionar sectores | Correcto |
| M3 | El overlay desaparece al confirmar y al cancelar | Correcto. No se detalló el método de cancelación (Esc o soltar Ctrl) |
| M4 | Acciones de teclado: Copiar, Pegar, Deshacer, Seleccionar todo, Guardar | Correctas (primera validación del `SendInput` real) |
| M5 | Carpeta: abre Documentos | Correcto |
| M6 | Web: abre Wikipedia en el navegador predeterminado | Correcto |
| M7 | Navegador: abre Microsoft Edge | Correcto |

*Inferencia, no prueba específica:* que los atajos del punto M4 llegaran a la aplicación de destino sugiere que el overlay no le quitó el foco y que la espera a soltar Ctrl (sin llegar al vencimiento) funciona en el uso normal. No se midió el foco ni se probó con aplicaciones concretas, y el vencimiento de 5 s no se probó a mano.

#### C. Pendiente o limitaciones conocidas

| Id | Qué falta | Nota |
|---|---|---|
| P1 | **Efecto real del click-through** (que un clic atraviese el overlay) | Solo se comprobó el estilo `WS_EX_TRANSPARENT`; no se prueba con clics sintéticos |
| P2 | Foco medido con aplicaciones concretas, pantalla completa o ventanas elevadas | Solo `--overlay-check` en el escritorio |
| P3 | Vencimiento de 5 s con el gesto real (`ModifiersNotReleased`) | Solo pruebas automáticas con relojes falsos |
| P4 | Caminos de fallo a mano: `TargetNotFound`, `UrlNotAllowed`, `ForegroundChanged`, `InputInjectionFailed` y su aviso en la ventana de diagnóstico | Solo automáticas |
| P5 | Cancelación con Esc y con soltar Ctrl por separado; cancelación de una acción pendiente al empezar otro gesto | Solo automáticas (M3 no distingue el método) |
| P6 | Autoprueba con entrada sintética tras la 4.1/4.2: que el radial no aparezca y no se ejecute nada (17.9 paso 8, 17.10) | No consta que se haya repetido a mano |
| P7 | DPI del 100 %, 150 % y 200 % | Solo el 125 % en comprobaciones sin entrada |
| P8 | Varios monitores, cambio de monitor a mitad del gesto y `WM_DPICHANGED` real | El coordinador usa la escala del monitor donde se mostró |
| P9 | **Aplicaciones de destino elevadas (UIPI)** | Windows puede descartar el teclado sin avisar; el fallo no es detectable |
| P10 | Bordes y esquinas de la pantalla (E-1, opción C, v0.22) | Implementado (ajuste + umbral de 10 DIP); pendiente de validar con ratón real, DPI y varios monitores |
| P11 | Juegos y pantalla completa exclusiva; aplicaciones que leen el teclado por scancode o entrada sin procesar | |
| P12 | `Alt+Tab` y `Win+E` | Se envían, efecto sin comprobar; no están en el menú |
| P13 | Windows 11 estable 25H2 | Solo 26H2 |
| P14 | Rendimiento medido (objetivo de 6.3: ≤ 100 ms del gesto al primer fotograma) | Sin medir con este overlay |
| P15 | Iconos de archivo (`IconSource.File`) | Se muestra un glifo neutro |
| P16 | Avisos al usuario (bandeja, notificaciones) | Fases posteriores; hoy los fallos solo se ven en la ventana de diagnóstico |
| P17 | Archivo abierto con `OpenFileAction` y aplicación con argumentos | El menú provisional no los usa; solo automáticas |
| P18 | Riesgos residuales de 17.7 (destinos tipo protocolo, ejecutables por asociación, rutas relativas) | Decidir antes de importar configuraciones (fase 8) |
| P19 | Smart App Control | Riesgo de distribución sin resolver (15.3); firma y confianza en la fase 8 |
| P20 | **Fijar la versión aprobada**: anotar el commit y el enlace de la ejecución de CI que cubra el código aprobado | La fase 4 está cubierta por CI en `d4b3f8a` (ejecución nº 4, 694 / 992 según el usuario); la aprobación manual sigue pendiente. El código de la fase 5 es posterior |
| P21 | ~~Confirmar con CI las pruebas de la 4.1, la 4.2, I1–I6 y la protección de 17.13~~ **Hecho en `d4b3f8a`** (ejecución nº 4). Pruebas de la fase 5: **hecho en `1772b59`** (ejecución nº 7) | Ver 15.5 |

#### D. Criterios de aceptación de la fase 4 (sección 12)

| Criterio | Evaluación |
|---|---|
| El foco no cambia | **Cumplido en lo probado**: estilos y `GetForegroundWindow` sin cambios (`--overlay-check`) y atajos recibidos por la aplicación de destino (M4, inferencia). Sin medir con aplicaciones concretas ni elevadas (P2, P9) |
| Transparencia, click-through y no activación **a la vez** | **Parcial**: transparencia y no activación comprobadas (estilos, aspecto, M1); el click-through, solo como estilo (P1) |
| Se ejecuta una acción real | **Cumplido** (M4–M7) |
| El atajo espera a soltar Ctrl y vence a los 5 s | **Espera: cumplida en el uso normal (inferencia de M4). Vencimiento: solo automático** (P3) |
| DPI y monitores comprobados o documentados como pendientes | **Documentados como pendientes** (P7, P8) |

#### E. Estado de la fase 4

Implementada, con validación manual básica correcta y con las pruebas automáticas de la 4.1 y la 4.2 **pendientes de confirmación por CI** (única ejecución verificada: verde sobre `349fbde`, anterior a la 4.1; cifras 622 / 522 comunicadas por el usuario). **Pendiente de aprobación expresa del usuario** (reglas de trabajo de CLAUDE.md): el estado pasará a «aprobada» solo cuando el usuario lo confirme, indicando la versión (P20). No se avanza a la fase 5 hasta entonces. Las limitaciones P1–P19 y P21 no se consideran resueltas por este cierre. *(Actualizado en 17.12: el cierre técnico posterior a la auditoría cambia el código; ver allí qué hay escrito, qué no se ha ejecutado y qué sigue pendiente; y en 17.13 la protección contra la navegación no soportada).*

### 17.12 Cierre técnico de la fase 4: auditoría I1–I6 (v0.16)

**Alcance y procedencia.** Tras la auditoría de las fases 4.1/4.2 se corrigieron los hallazgos que pertenecen al cierre de la fase 4 (I1, I2, I4, I5 y la parte inmediata de I6), se resolvió I3 con una decisión documentada y se auditaron foco, tiempos, overlay, DPI y apagado. **Estado de verificación, sin mezclar:**

| Qué | Estado |
|---|---|
| Compilación Release de la solución (0 avisos, 0 errores) | **Comprobada en el equipo de desarrollo** (salida a una carpeta temporal, porque `Radial.App` estaba en ejecución y bloqueaba su `bin`) |
| Pruebas automáticas nuevas de este cierre (33 métodos, 56 casos con `InlineData`; sin contar las de I1/I2 anteriores; detalle en la tabla) | **Escritas; según el informe de cierre compiladas, no ejecutadas** (Smart App Control impide lanzarlas en este equipo, 17.8) y **NO confirmadas por CI**. No se dan cifras totales nuevas: el recuento estático de atributos no equivale al que informa el ejecutor de pruebas |
| Comportamiento real (hooks, `SendInput`, overlay, apagado) | **Sin probar a mano** tras estos cambios |
| Fase 3 | `GlobalInputService` y `GestureEvent` reciben **un campo aditivo** (I5, abajo). `LowLevelHookHost`, `ChordTracker`, `GuardedSyntheticInput`, `SyntheticMarker` y la autoprueba **no se modificaron** |

**Hallazgos y decisiones**

| Id | Decisión | Implementación | Pruebas nuevas |
|---|---|---|---|
| **I1** lector del canal | Resuelto (ya hecho antes de esta sección) | `GestureEventReader`: un fallo por evento se notifica y el bucle sigue; N fallos seguidos (10) lo detienen y se comunican; la cancelación es el final esperado. `MainWindow` supervisa la tarea y muestra su estado | 14 (`GestureEventReaderTests`) |
| **I2** suscriptores | Resuelto (ya hecho antes) | `Completed` y `ActionFinished` se invocan suscriptor a suscriptor; `SubscriberFailed` los comunica | 8 (coordinador) |
| **I3** UIPI / elevación | **No se implementa detección. Limitación documentada** | Ver «I3» abajo. Cambio de redacción: la ventana de diagnóstico dice «aceptada por Windows (no se comprueba que la aplicación la procese)», no «correcta» | — |
| **I4** teclas atascadas | Resuelto | `SendInputKeyboardInjector`: la liberación se comprueba, se reintenta UNA vez la parte que falte y, si sigue sin entrar, se informa (`KeyInjectionResult.ReleaseFailed` → código `KeyReleaseFailed`). Si la llamada lanza, se sueltan todas las teclas que la secuencia pulsa y se propaga la excepción | 10 (inyector) + 3 (ejecutor) |
| **I5** autoprueba | Resuelto con una barrera central | `GestureEvent.SyntheticWindow`, fijado por el servicio al publicar | 5 (servicio) + 4 (coordinador) |
| **I6** destinos | **Parte inmediata resuelta; el resto bloqueado para la fase 5** | `TargetPolicy` en los ejecutores. Ver «I6» abajo | 26 + 4 + 1 |

**I3 — por qué no hay detección.** La documentación de `SendInput` dice que está sujeta a UIPI y que ni el valor devuelto ni `GetLastError` indican que fue UIPI quien lo bloqueó. Radial solo puede saber que **Windows aceptó** la secuencia. Detectar un destino elevado exigiría abrir el proceso de la ventana en primer plano y leer su token; desde un proceso sin elevar, esa lectura falla con «acceso denegado» tanto para una aplicación elevada como para procesos protegidos, de otro usuario o del sistema. Convertir «no se pudo leer» en «elevada» sería una heurística que **no se ha podido verificar** en este equipo y que bloquearía atajos legítimos con falsos positivos; no se presenta como garantía. **Comportamiento correcto hoy:** Radial envía, informa «aceptada por Windows» y **nunca afirma que la aplicación procesó la entrada**. Queda en P9 con su procedimiento de prueba (abajo). Si el producto necesitara avisar, la vía sería una decisión de diseño de la fase 5/7 con prueba manual con una aplicación elevada real (p. ej. el Administrador de tareas).

**I4 — qué se garantiza y qué no.** `SendInput` devuelve cuántos eventos insertó, contando desde el primero. Con la secuencia `+Ctrl +C -C -Ctrl` en una sola llamada: si entran 0 no hay nada que soltar; si entran 1 a 3 se envían las liberaciones de lo pulsado en orden inverso; se comprueba cuántas entraron y se reintenta una vez las que falten (nunca en bucle). **No se puede garantizar:** (1) el teclado limpio si el sistema rechaza también la liberación y su reintento (entonces `KeyReleaseFailed` pide al usuario pulsar y soltar los modificadores); (2) que un modificador que el usuario vuelva a pulsar físicamente en los milisegundos entre la espera y el envío no se mezcle con el atajo; (3) cancelar una vez enviado. Soltar una tecla que no estaba pulsada es inocuo, por eso en el caso de excepción (se ignora cuántas entraron) se sueltan todas las de la secuencia. No se hicieron pruebas con teclado real: todo con una llamada al sistema falsa.

**I5 — análisis y barrera.** Hasta ahora la protección era un booleano de la interfaz (`Isolate()`/`Resume()` más el descarte por marca de tiempo): correcto pero dependiente de que cada ruta de código lo recordara. Con `AcceptOwnSyntheticInput` activo, los eventos de la autoprueba producen `GestureEvent` idénticos a los reales. **Nueva barrera:** `GlobalInputService.Write` copia `AcceptOwnSyntheticInput` en `GestureEvent.SyntheticWindow` **dentro del mismo cerrojo** con el que el tracker procesó el evento; el campo viaja con el evento en el canal, así que aunque se lea después de apagar la aceptación (aislamiento levantado «demasiado pronto», eventos pendientes en el canal, parada del servicio durante la autoprueba) `RadialOverlayCoordinator.Handle` lo ignora. Las tres barreras (marca del evento, `Isolate`, descarte por marca de tiempo) son independientes y se conservan. Es un campo **aditivo con valor por defecto**; el contrato de la fase 3 no cambia. Efecto colateral aceptado: un gesto físico real durante la ventana de la autoprueba también se ignora (la autoprueba pide no tocar nada). Cierre de la aplicación: el lector se cancela, `Dismiss` cancela la acción pendiente y oculta el overlay y después se libera el servicio, y el overlay se cierra en un `finally`.

**I6 — qué se resuelve ahora.** En la fase 4 los destinos están en código (`ProvisionalTargets`, rutas absolutas de carpetas conocidas; URL fija): no hay un vector inmediato. Aun así, `TargetPolicy` fija el mínimo en el último punto antes de llamar al sistema, independiente de la configuración: **aplicaciones** → ruta absoluta o nombre de programa sin ruta ni «:» (`notepad.exe`); se rechazan rutas relativas, de unidad relativa, de dispositivo (`\\?\`, `\\.\`) y protocolos (`ms-settings:`, `shell:`, `file:`, `http:`) con `TargetNotAllowed`; el directorio de trabajo, si se indica, debe ser absoluto. **Archivos y carpetas** → una ruta relativa se trata como inexistente (`TargetNotFound`, nunca se resuelve contra el directorio de trabajo del proceso: `.` ya no abre «la carpeta actual») y una ruta de dispositivo da `TargetNotAllowed`. `http`/`https` en las URL no cambia. **Bloqueado para la fase 5 (importación/configuración):** política de qué tipos de archivo se pueden abrir (`OpenFile` ejecuta `.exe`, `.bat`, `.lnk`, `.js`... por asociación), qué hacer con rutas de red (UNC: absolutas, se aceptan hoy; pueden provocar autenticación en red), validación equivalente en `GetProblems()` de Core al cargar, advertencia al importar y nombres sin ruta (se resuelven con el orden de búsqueda del sistema; hay que decidir si la configuración los permite).

**Foco y destino del atajo (auditoría, sin cambio de política).** El destino es la ventana en primer plano al empezar la ejecución, cuando el overlay ya se ocultó y nunca se activó (`WS_EX_NOACTIVATE`, `ShowActivated=false`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`; `--overlay-check` midió «sin cambios» en el equipo de desarrollo, no con aplicaciones concretas). Se vuelve a comprobar justo antes de enviar; si cambió, `ForegroundChanged` y no se envía. **Decisión:** se mantiene la política «la ventana que tiene el foco al confirmar», no «la que lo tenía al activar el gesto»: capturar al activar obligaría a cambiar el contrato `IActionExecutor` o a cancelar acciones que no dependen del foco (abrir una URL). **Corrección:** ahora un primer plano nulo (`GetForegroundWindow() == 0`: transición de foco, ventana recién cerrada) hace fallar con `ForegroundChanged` en lugar de enviar a ninguna parte.

**Tiempos y cancelación (auditoría, sin cambio de política).** Espera de 5 s por defecto, configurable, de Ctrl, Mayús, Alt y Win con `GetAsyncKeyState` fuera de los hooks; nunca se pulsa ni suelta un modificador; la cancelación (nuevo gesto, aislamiento, cierre) se atiende en cada sondeo y justo al terminar la espera; nada se envía tras una cancelación. Las pruebas existentes lo cubren con reloj y pausa falsos. Sin carreras nuevas encontradas.

**Overlay y click-through (auditoría).** Estilos `WS_EX_LAYERED` (WPF), `NOACTIVATE`, `TOOLWINDOW` y `TRANSPARENT`, aplicados con `SetWindowPos(…SWP_FRAMECHANGED)` y releídos para comprobarlos; `HWND_TOPMOST` en cada colocación; se crea una vez, se oculta y se cierra solo al salir. El coordinador no conserva referencias entre gestos más allá del motor y la acción pendiente (cancelada al empezar otro gesto, aislar o descartar). **No verificado:** el efecto real del click-through (P1) y que `WindowFromPoint` ignore la ventana en la práctica: no se prueba con clics sintéticos. **Procedimiento manual pendiente (P1):** con el gesto mantenido sobre una ventana con un elemento claramente pulsable bajo un sector, soltar y comprobar que no se activó ningún elemento ni cambió el foco (el botón central ya lo consume el hook); repetir con el cursor sobre otra aplicación.

**DPI y varios monitores (auditoría).** La colocación es un cálculo puro (`OverlayPlacement`) con pruebas para 100/125/150/175/200 %, coordenadas negativas (monitor a la izquierda o arriba) y desbordamiento en el borde (sin ajuste, E-1 opción A). La escala se fija una vez al mostrar (`Show` devuelve la del monitor donde quedó) y no cambia durante el gesto aunque el cursor cambie de monitor: es la decisión existente y se mantiene. `OnDpiChanged` recoloca con el cálculo propio. No hay hallazgos nuevos. **Sin probar en hardware:** 100/150/200 %, monitor secundario, cambio de monitor a mitad del gesto y `WM_DPICHANGED` real (P7, P8).

**Apagado (auditoría).** Orden al cerrar la ventana: se detienen los temporizadores, se cancela el lector, `Dismiss` cancela la acción pendiente y oculta el overlay, se libera el servicio (hooks) y el overlay se cierra en un `finally`, de modo que un fallo al liberar el servicio ya no deja una ventana abierta que mantendría vivo el proceso. Cerrar a mitad de la autoprueba sigue aplazando el cierre (fase 3). **Sin probar a mano** (no se comprobó que el proceso termine en todos los casos).

**Procedimientos manuales pendientes** (el usuario, con hardware): (1) autoprueba (`--selftest`) y comprobar que el radial no aparece y no se ejecuta ninguna acción (P6); (2) Ctrl+C hacia una aplicación **elevada** (Administrador de tareas con «Ejecutar como administrador»): esperado, Radial informa «aceptada por Windows» y no ocurre nada en la aplicación (P9); (3) ver la fila «Lector de eventos» de la ventana de diagnóstico; (4) vencimiento de 5 s con Ctrl mantenido (P3); (5) cerrar la aplicación durante un gesto y comprobar en el Administrador de tareas que `Radial.App.exe` desaparece.

**Criterios de la misión y su estado.** Implementado en código, con pruebas escritas (su compilación y ejecución las valida CI): I1, I2, I4, I5, I6 (parte inmediata), `ForegroundChanged` ante primer plano nulo, apagado robusto. **Documentado como limitación:** I3. **Pendiente de confirmación por CI** (P21) y de las pruebas manuales anteriores. La fase 4 sigue **pendiente de aprobación expresa**.



*Nota de la v0.19: esta protección existía porque el overlay solo representaba el menú raíz. En la fase 5 el overlay representa los cambios de menú y la protección se convirtió en la red de seguridad de 18.2: solo se admiten «misma pila», «un nivel más» y «un nivel menos» y cualquier otro estado cancela el gesto sin ejecutar nada. El evento `UnsupportedNavigation` se conserva con ese significado. Lo que sigue describe el estado de la v0.17.*

**Hallazgo de la auditoría.** `RadialEngine` ya sabe navegar (entrar en un submenú por permanencia, recentrar, volver por el centro armado; sección 14.5, pruebas desde la fase 1), pero `RadialOverlayCoordinator`, `IRadialPresenter`, `OverlayWindow` y `RadialView` solo representan el menú raíz: el coordinador llama a `Show` una vez, con el raíz, y no mira qué menú tiene el motor. Con un grupo en el menú, tras la permanencia el motor pasaría al submenú mientras la ventana seguía dibujando el raíz: el usuario vería resaltado un elemento del raíz y al soltar se ejecutaría otro, el del submenú. **Hoy no se alcanza en la aplicación** (el menú provisional tiene ocho acciones y ningún grupo), pero la API del coordinador acepta cualquier configuración válida.

**Estado de los submenús, sin ambigüedad:** *implementados en el núcleo* (motor, validador, pruebas de Core); *no integrados en el overlay ni en la interfaz* (cambio de menú, recentrado de la ventana, indicador de progreso de la permanencia y vuelta atrás visibles): **fase 5**. Esta sección no los implementa.

**Protección.** Después de cada actualización del cursor, `RadialOverlayCoordinator.Update` comprueba que el menú actual del motor es el que se presentó (misma instancia). Si no lo es:
1. el motor se abandona y el overlay se oculta (`Close`);
2. no se ejecuta nada y no se produce `Completed` ni `Aborted` para ese gesto;
3. se avisa con el evento nuevo `UnsupportedNavigation` (con el identificador del menú al que habría pasado; la ventana de diagnóstico lo registra), siempre después de ocultar y soltar el motor, con el mismo aislamiento de suscriptores que el resto de eventos (I2);
4. la selección del otro menú **no se comunica nunca al presentador**: la comprobación va antes de `SetSelection`.

La comprobación también se hace con la posición del evento de soltar: `Confirm` aplica esa posición **antes** de confirmar y, si con ella el motor cambia de menú (la permanencia se completa en ese mismo instante), no confirma. Los eventos de soltar el botón central o Ctrl que lleguen después encuentran el motor inactivo y se ignoran como cualquier evento sin gesto activo, así que ninguna liberación puede ejecutar una acción de un menú que no se vio. Un gesto nuevo funciona con normalidad. Consecuencia aceptada: mantener el cursor sobre un grupo más del tiempo de permanencia (600 ms por defecto) cancela el gesto en lugar de abrir el submenú; mientras no haya grupos en el menú provisional, no ocurre.

**Pruebas** (`RadialOverlayCoordinatorNavigationTests`, 11 métodos, coordinador con presentador, ejecutor y reloj falsos y un menú de dos niveles): entrar en el submenú cancela y oculta; antes de la permanencia no se bloquea; la selección del submenú no llega al presentador; soltar el botón central tras el bloqueo no ejecuta ni confirma; soltar Ctrl tras el bloqueo no informa nada más; sondear tras el bloqueo no hace nada; el evento de soltar que completa la permanencia no se confirma; soltar sobre un grupo antes de la permanencia no ejecuta nada y no es navegación no soportada; una acción del menú raíz se sigue ejecutando; un gesto nuevo tras el bloqueo funciona; un suscriptor defectuoso del evento nuevo se comunica sin romper la cancelación. **Estado: escritas, sin compilar ni ejecutar en el equipo de desarrollo (regla de 15.5) y sin confirmar por CI** hasta que se anote la ejecución sobre el commit que las contiene.

**Cuándo retirarla.** Al integrar la navegación en el overlay (fase 5), esta cancelación se sustituye por la representación real del cambio de menú, con pruebas de esa navegación. Hasta entonces no debe quitarse.

### 17.14 Fallo seguro del ciclo de vida de la captura (v0.18)

**Problema (auditoría de la fase 4).** El canal de eventos del servicio no tiene límite y los hooks siguen instalados mientras el servicio esté en marcha: si el lector del canal terminaba sin que se hubiera pedido (canal cerrado o con error, o demasiados fallos seguidos, I1 en 17.12), la aplicación solo lo mostraba en el diagnóstico y los hooks seguían consumiendo Ctrl+botón central (y Esc con un gesto activo) sin que nadie procesara los gestos.

**Diseño.**
- `GlobalInputService.Fail(reason)` (Radial.Windows/Input): quita los hooks, cancela el gesto activo (`ServiceStopped`) y deja el estado en `Faulted` con el motivo en los errores del diagnóstico. Comparte con `Stop` la liberación (`ReleaseCapture`). **Idempotente:** solo la primera llamada hace algo; con la captura ya detenida solo marca el fallo; tras `Dispose` no hace nada. Un indicador interno de «puede haber hooks instalados» evita liberar dos veces (`Stop` o `Dispose` posteriores a un `Fail` no vuelven a llamar al host). Una `Stop` posterior no borra el fallo; el siguiente `Start` lo olvida. No es una recuperación automática: no se reinstala nada.
- `GestureReaderSupervisor` (Radial.Windows/Input): ejecuta el lector y distingue el final esperado (cancelación por el cierre de la aplicación, o cualquier final cuando el cierre ya se pidió) del inesperado. Ante un final inesperado, **una sola vez**: (1) `failCapture` (hooks), (2) `dismissOverlay` (overlay y gesto, sin informar de un gesto confirmado ni cancelado), (3) evento `Failed` con el motivo y los pasos de limpieza que fallaron. Cada paso se intenta aunque el anterior falle; un suscriptor defectuoso no impide avisar a los demás. Se inyecta con dos delegados (`GlobalInputService.Fail` y `RadialOverlayCoordinator.Dismiss` en la aplicación), así que se prueba sin WPF.
- `MainWindow`: usa el supervisor, registra `[captura] ERROR: …` en la ventana de diagnóstico y **deshabilita «Iniciar captura»** (el lector ya no existe: reiniciar los hooks dejaría el mismo problema); la ventana sigue abierta, sin captura, y hay que cerrar y volver a abrir Radial. El cierre normal (cancelar el lector, ocultar el overlay y liberar el servicio, en ese orden) no genera ningún aviso de fallo.
- No se modificó el motor radial, `ChordTracker`, `LowLevelHookHost` ni la autoprueba; no hay submenús.

**Pruebas** (`GestureReaderSupervisorTests`, 11 métodos, y `GlobalInputServiceFailTests`, 9 métodos más una teoría de 2 casos): final inesperado sin gesto y durante un gesto activo (hooks fuera, estado `Faulted`, gesto cancelado, overlay oculto sin eventos de gesto terminado); demasiados fallos del manejador; canal cerrado por sí solo; fallo gestionado una sola vez y sin segunda liberación al hacer `Dispose`; cierre normal y cierre con el canal terminando a la vez (sin fallo, captura intacta); cada paso de la limpieza se intenta aunque falle el otro; suscriptor defectuoso; `Fail` con captura en marcha, con gesto activo, repetido, ya detenida, sin iniciar, tras `Dispose`, seguido de `Dispose`/`Stop`/`Start`, y sin motivo. **Estado: escritas, sin compilar ni ejecutar en el equipo de desarrollo (regla de 15.5) y sin confirmar por CI** hasta que se anote la ejecución sobre el commit que las contiene.

**Límites.** Los hooks reales solo se han probado con dobles; el fallo del lector no se ha provocado a mano (no hay forma de forzarlo desde la interfaz) y el cierre de la aplicación tras un fallo no se ha comprobado en ejecución. `Fail` llama a `LowLevelHookHost.Stop` desde el hilo de la interfaz, que espera hasta 3 s al hilo de los hooks (igual que el cierre).

## 18. Fase 5: submenús en el overlay, configuración persistente y bandeja (v0.19)

### 18.1 Estado: implementada; pruebas en verde en CI; sin validar a mano

| Qué | Estado |
|---|---|
| Código (submenús en el overlay, JSON de la configuración, almacén en disco, definiciones y colocaciones, menú predeterminado, bandeja) | **Implementado** |
| Compilación Release del producto (`dotnet build src/Radial.App/Radial.App.csproj -c Release`) | **Correcta, 0 advertencias y 0 errores**, en el equipo de desarrollo |
| Pruebas nuevas: 119 métodos de prueba (más casos con `InlineData`) en 7 archivos: navegación del coordinador (26), JSON de la configuración (37), definiciones y colocaciones (10), acciones desconocidas en el motor (5), almacén en disco (25), configuración predeterminada (9) y recorrido de extremo a extremo (7) | **No se compilan ni ejecutan en el equipo de desarrollo (Smart App Control, 15.5). En CI: la ejecución nº 5 (`6eb9977`) falló al compilar por un `using` que faltaba en una prueba; la nº 6 (`0932f25`) por el aviso `xUnit2029` (`Assert.Empty` sobre un `Where`); la nº 7 (`1772b59`, <https://github.com/xrbrr24/Radial/actions/runs/42001660093>) terminó en verde: compilación, Core y Windows.** El recuento es de métodos, no el que informe el ejecutor de pruebas; las cifras por suite no se han podido leer sin autenticación (se pueden ver en el resumen de la ejecución) |
| Comprobaciones sin entrada ni hooks en el equipo de desarrollo | `--render-preview`: dibujo de grupos, progreso de entrada, flecha y anillo de retroceso y acción desconocida, revisado en el PNG. `--overlay-check`: con la ventana ya visible, `ShowMenu` la recoloca con el centro en el punto pedido (±1 px), sigue visible, no activa y sin cambiar el foco ni los estilos; un monitor al 125 % |
| Gesto real con grupos, retroceso, tres niveles, reinicio, archivo corrupto, bandeja y «Salir» | **Sin probar a mano** (procedimiento en 18.8) |

### 18.2 Submenús en el overlay

El motor (`RadialEngine`, fase 1) ya decidía todo (permanencia, recentrado, retroceso armado, progreso); lo que faltaba era reflejarlo en la ventana. Se mantiene la separación: el motor no sabe de ventanas y el coordinador no decide qué es un grupo.

**Estado de presentación.** `IRadialPresenter` representa: el menú actual y su nivel (`Show` para el raíz, `ShowMenu(menú, nivel, centro)` para los siguientes), el centro (píxeles físicos), el sector resaltado (`SetSelection`) y el progreso (`SetProgress(RadialProgress)`: entrada en un grupo, retroceso y retroceso armado). `ShowMenu` cambia el contenido y mueve la ventana **ya visible** en un solo paso (`OverlayWindowInterop.RecenterVisible`: un único `SetWindowPos` con el DPI que tenía, y una corrección solo si Windows la deja en un monitor con otro DPI), sin ocultarla, recrearla ni pasar por la ventana mínima que usa la primera aparición (`PlaceCentered`), para no parpadear.

**Origen geométrico del motor frente al centro físico de la ventana.** El motor mide en un único sistema de DIP durante toda la interacción (el del primer centro). El coordinador convierte con un **ancla** (el píxel físico de un punto y la coordenada del motor de ese punto) y la escala del monitor donde está el radial: `motor = ancla.motor + (píxel − ancla.píxel) / escala`. Al entrar en un grupo o volver atrás, el motor pone su origen (`Origin`) en el cursor. El centro **físico** de la ventana es ese origen convertido de vuelta con el mapa viejo (`píxel = ancla.píxel + (motor − ancla.motor) · escala`), que es el propio punto del cursor; ahí se re-ancla (`ancla.píxel` = ese punto, `ancla.motor` = `Origin`) con la escala que devuelve `ShowMenu`. Consecuencias: el origen no se resta dos veces; las coordenadas del motor son continuas aunque la ventana cambie de sitio; y si el menú nuevo cae en un monitor con otra escala, las distancias siguientes se miden con la escala nueva sin saltos (prueba con 125 % y 200 %). No hay algoritmo multimonitor propio ni ajuste a los bordes: la limitación E-1 (opción A) sigue como estaba, sin probar.

**Dibujo (`RadialView`).** Un grupo se distingue de una acción por una banda de acento en el borde exterior de su sector y un chevrón; mientras se permanece sobre él, un arco por fuera del sector se completa con el progreso de la permanencia. Dentro de un submenú el centro muestra una flecha de volver; al salir del centro el retroceso queda armado (flecha y borde del centro se iluminan) y al volver a él un anillo alrededor del centro se completa. Una acción de tipo desconocido se dibuja apagada, con un glifo de aviso, y no se resalta.

**Red de seguridad (sustituye a la protección temporal de 17.13).** Tras cada actualización del cursor se compara la pila de menús que muestra la ventana con la del motor: igual, un nivel más (entrar) o un nivel menos (volver) son las únicas transiciones admitidas (`RadialOverlayCoordinator.Classify`). Cualquier otra (pila vacía, otro menú, un salto de varios niveles, otra instancia) cancela el gesto de forma segura: se oculta el overlay, se abandona el motor, no se ejecuta nada y se avisa con `UnsupportedNavigation`. No debería ocurrir y solo se puede probar de forma directa sobre `Classify`. Un fallo del presentador al cambiar de menú también cancela el gesto y se propaga (la ventana principal lo contiene y descarta el overlay). La posición del evento de soltar se aplica antes de confirmar: si con ella se completa una permanencia, el menú cambia y el gesto termina sin ejecutar nada («confirmado sin selección»).

**Las protecciones anteriores siguen**: Esc, soltar Ctrl, aislamiento durante la autoprueba (tres barreras) y cancelación de la acción pendiente funcionan en cualquier nivel (pruebas con cancelación y aislamiento dentro de un submenú).

**Pruebas (con reloj simulado, sin esperas reales):** entrada tras completar la permanencia y no antes; progreso gradual (0,25 / 0,5 / 0,75); reinicio del progreso al salir del grupo; cambio de menú sin ocultar ni volver a mostrar (el registro del presentador no contiene `hide` y solo un `show`); selección vacía tras entrar; medición contra el centro nuevo (el punto de activación cae al sur del centro del submenú); cambio de escala de monitor; retroceso no permitido sin armar, armado al salir del centro, completado tras la permanencia y anulado si se vuelve a salir; tres niveles hacia dentro y hacia fuera; confirmar en el tercer nivel y en un submenú (la acción ejecutada pertenece al último menú mostrado, comprobado para los tres niveles); cancelar (Esc y Ctrl) y aislar dentro de un submenú; soltar justo al completarse la permanencia; soltar sobre un grupo o sobre una acción desconocida; gesto nuevo tras navegar; fallo del presentador; y `Classify` con todas las transiciones válidas y los estados anómalos.

### 18.3 Configuración en JSON (`Radial.Core/Storage`)

`ConfigurationFile.Serialize/Parse` convierte entre `RadialConfiguration` y el JSON **sin tocar el disco** y sin lanzar por el contenido. Se usa `System.Text.Json` con contexto **source-generated** (`ConfigurationJsonContext`; propiedades en camelCase, sangrías, sin escribir nulos) sobre DTOs de campos opcionales; la acción de cada elemento es un `JsonObject` abierto que se resuelve a mano (`ActionJson`), por 7.1.

| Tema | Comportamiento |
|---|---|
| `schemaVersion` | Entero ≥ 1. Ausente o inválido: corrupto. **Mayor que la que conoce esta versión: `FutureVersion`, sin interpretar el resto.** La actual es la 1 |
| Migraciones | `ConfigurationMigrator`: una migración por versión sobre el árbol JSON, encadenadas hasta la versión de destino; falta un paso intermedio = error controlado (`config.migration-failed`). Hoy no hay ninguna (la primera versión es la 1); las pruebas usan migradores de 3 versiones con migraciones de prueba |
| Límites | 1 MiB y 32 niveles de profundidad (un acción desconocida anidada más hondo hace corrupto el archivo entero). Se acepta un BOM UTF-8. No se admiten comentarios ni comas finales |
| Validación | Tras convertir al modelo se aplica `ConfigurationValidator`. Errores → `Invalid`; advertencias (acciones desconocidas, menús inalcanzables) acompañan a una configuración válida |
| Errores de lectura | Posición o icono no reconocidos, tiempos absurdos, propiedades de una acción conocida con el tipo JSON equivocado, objeto de acción sin `type` o con `type` que no es texto: configuración `Invalid` (o `Corrupt` si la estructura general no corresponde). **Se rechaza la configuración entera**, no el elemento suelto: así no se descarta nada a escondidas y el original queda en cuarentena. Una propiedad obligatoria *ausente* en una acción conocida la señala la validación con su ruta |
| Escritura | Siempre la versión actual; las acciones desconocidas se reescriben tal cual. Las propiedades desconocidas de la estructura conocida se ignoran al leer y **no se conservan** al reescribir (solo las acciones desconocidas completas) |

**Acciones desconocidas (7.1, implementadas).** `UnknownAction(Type, RawJson)` conserva el discriminador y el objeto JSON original íntegro (incluidas propiedades anidadas y matrices) y se reescribe tal cual (equivalencia semántica, comprobada con `JsonNode.DeepEquals`). Genera una advertencia (`action.unknown-type`), **no se ejecuta** (ningún ejecutor la admite; el motor devuelve `CancelReason.UnsupportedAction` en lugar de un resultado ejecutable; no puede ser una definición del repertorio) y se dibuja apagada. Defensa en capas: ni el motor ni los ejecutores ni el modelo del catálogo la aceptan.

**Seguridad.** Nada se ejecuta durante la lectura ni la validación. Las acciones conocidas mantienen sus políticas de destino, de URL (`http`/`https`) y de ejecución (17.7 y 17.12); esta fase no añade ejecución de scripts, comandos de consola ni ningún mecanismo para eludir las políticas de Windows. Siguen **bloqueados para antes de importar configuraciones de terceros (fase 8)**: la política de tipos de archivo ejecutables por asociación, las rutas de red, la validación equivalente a `TargetPolicy` en `GetProblems()` y los nombres de programa sin ruta (17.12, «I6»).

### 18.4 Almacén en disco (`Radial.Windows/Storage/FileSettingsStore`)

`ISettingsStore` (en Core) tiene `LoadOrCreate()` y `Save()`; ninguna operación lanza por el contenido ni por el disco. Carpeta: `%LOCALAPPDATA%\Radial\` con `config.json`, `backups\`, `icons\` (reservada) y `logs\` (registros de errores desde S2, sección 20).

| Situación al cargar | Resultado |
|---|---|
| No existe `config.json` (primer inicio) | Se crean las carpetas, se genera la configuración predeterminada, se **valida** y se guarda por el camino de creación (`File.Move` sin sobrescribir) |
| Existe y es válido | Se carga; las advertencias van al diagnóstico. No se reescribe |
| Corrupto o inválido (JSON roto, demasiado grande o profundo, sin estructura, con errores de validación) | El archivo se renombra a `config.corrupt-aaaaMMdd-HHmmss-fff.json` (cuarentena, como mucho 10); se restaura la copia de `backups\` **válida** más reciente (las copias rotas o de versión futura se omiten) o, si no hay, se escribe la predeterminada. Nunca se restaura una copia sin validar |
| **Versión futura** | **El archivo no se toca ni se pone en cuarentena.** Se usan los valores predeterminados en memoria (`CanSave = false`) y `Save` se rechaza durante todo el proceso: no se destruyen datos que una versión más nueva sí entiende |
| No se puede leer (bloqueo, permisos) o no se puede crear la carpeta | No es corrupción: no se toca el archivo; valores predeterminados en memoria y aviso |

**Escritura (`Save`).** Valida primero (una configuración inválida o mayor de 1 MiB no se guarda). Se escribe un temporal en la misma carpeta (`FileMode.CreateNew`, volcado a disco) y: si el destino no existe, `File.Move` sin sobrescribir; si existe y el contenido es distinto, `File.Replace(temporal, destino, copia)`, que deja la versión anterior en `backups\config-aaaaMMdd-HHmmss-fff.json` (se conservan las 10 más recientes); si es idéntico, no se hace nada y no se crea copia. Los temporales huérfanos de una caída se borran al arrancar. El nombre de las copias lleva milisegundos (el diseño inicial decía segundos) para que el orden alfabético sea el cronológico y no haya colisiones.

**Pruebas** (carpeta temporal real, reloj que avanza un segundo por lectura): creación del primer inicio, carga idéntica sin reescribir, archivo editado a mano, varios tipos de corrupción, inválido pero parseable, sobredimensionado, restauración de la copia válida más reciente saltando las rotas, copias solo inválidas o de versión futura, límite de cuarentena, versión futura intacta con `Save` rechazado, archivo bloqueado, carpeta imposible de crear, guardado con copia, primera escritura, guardado idéntico, rotación a 10 copias, configuración inválida rechazada, persistencia entre instancias y conservación íntegra de una acción desconocida tras cargar y guardar.

### 18.5 Definiciones de acción y colocaciones

Antes, `ActionDefinition.ToItem` usaba el identificador de la definición como identificador del elemento, de modo que una acción no se podía reutilizar en dos menús. Ahora hay dos identificadores: el **de la definición** (`ActionDefinition.Id`, estable: `edit.copy`) y el **de la colocación** (`RadialItem.Id`, único en toda la configuración). `RadialItem.DefinitionId` (opcional) recuerda de qué definición salió un elemento y se guarda en el JSON (`definitionId`), pensando en el editor de la fase 6. `ToItem(ItemId, posición)` recibe el identificador de la colocación; `ToItem(MenuId, posición)` lo deriva como `{menú}.{definición}` (único mientras la definición se coloque una vez por menú). Los grupos (`OpenMenuAction`) son estructura del árbol de menús: no tienen definición y no pueden serlo (`ActionDefinition.GetProblems()` lo rechaza). El catálogo no se hizo más complejo; la persistencia de una biblioteca de definiciones propias del usuario es de la fase 6. La validación comprueba que el `definitionId` no esté vacío ni supere los 64 caracteres. Pruebas: misma definición en dos menús con identificadores distintos y configuración válida; dos veces en el mismo menú exige identificador explícito; la definición no se modifica al reutilizarla; los grupos y las acciones desconocidas no pueden ser definiciones.

### 18.6 Menú predeterminado

`Radial.Windows/Storage/DefaultConfiguration` genera la configuración inicial a partir del repertorio (`ActionCatalog`): **las ocho acciones de siempre** (Copiar, Pegar, Deshacer, Seleccionar todo y Guardar en el menú principal; Carpeta, Web y Navegador) con la misma disposición de los cinco atajos. Cambio respecto a las fases 4.1/4.2: las tres aperturas están dentro del grupo «Abrir» (oeste), de modo que el primer inicio ya enseña la navegación entre menús y se puede probar sin editar el archivo. `ProvisionalMenu` y `ProvisionalTargets` desaparecieron; el resto (destinos de carpetas conocidas del sistema, sin inventar rutas) se conserva. Pruebas: es válida y sin advertencias, conserva las ocho acciones, tiene un grupo con su submenú, identificadores únicos, definiciones del repertorio y ida y vuelta por el JSON.

### 18.7 Bandeja y arranque

`Radial.App/Tray/TrayIcon` (WPF no tiene bandeja): `NotifyIcon` de Windows Forms, que forma parte del entorno de ejecución de escritorio (sin paquetes). Solo ese tipo lo usa: el proyecto activa `UseWindowsForms`, quita los usings implícitos de Windows Forms y Drawing para que `Application`, `Brushes`, `Point` o `Size` no sean ambiguos y silencia el analizador WFO0003 (que pide quitar el DPI del manifiesto: la interfaz es WPF y `PerMonitorV2` en `app.manifest` es una decisión fijada). Opciones: «Mostrar ventana de diagnóstico» y «Salir de Radial» (y doble clic para mostrar; en la fase 6 la primera opción pasó a llamarse «Mostrar Radial» y muestra el centro de control, 19.6). La X de la ventana la oculta y Radial sigue en la bandeja; «Salir» ejecuta el cierre de siempre (fin del lector, ocultar el overlay, liberar los hooks, cerrar el overlay y retirar el icono) y, si hay una autoprueba en curso, la cancela y completa el cierre al terminar. Al cerrarse la sesión de Windows el cierre se permite (una ventana que se niega a cerrar bloquearía el apagado). El icono se dibuja en tiempo de ejecución; el identificador del icono nativo no se libera (uno solo durante toda la vida del proceso).

**Arranque.** `App.OnStartup` carga la configuración con `FileSettingsStore`; un fallo inesperado nunca tumba la aplicación (valores predeterminados en memoria y aviso en el registro). La autoprueba no toca el disco. Los avisos de la carga se muestran en el registro (`[config]`). **La captura empieza sola** al cargar la ventana (antes había que pulsar «Iniciar captura»), salvo en la autoprueba. Sin autoarranque con Windows ni instancia única (fase 7).

### 18.8 Qué hay que probar a mano (sin hacer)

Con el gesto real y `dotnet build src/Radial.App/Radial.App.csproj -c Release` + ejecutar `Radial.App`: (1) primer inicio con `%LOCALAPPDATA%\Radial` borrado o renombrado: se crea `config.json` y el overlay muestra el menú; (2) entrar en «Abrir» (permanecer sobre el sector oeste): arco de progreso, cambio de menú sin parpadeo, recentrado en el cursor; (3) ejecutar Carpeta, Web y Navegador desde el submenú; (4) retroceso: salir del centro, volver a él y esperar el anillo; (5) tres niveles editando `config.json` (README); (6) Esc y soltar Ctrl dentro de un submenú; (7) reiniciar y comprobar la misma configuración; (8) dañar `config.json` y comprobar la cuarentena y la recuperación; (9) un `schemaVersion` mayor: el archivo no cambia; (10) una acción de `type` desconocido: aviso, se ve apagada y no hace nada; (11) la bandeja: ocultar con la X, mostrar, «Salir» y comprobar en el Administrador de tareas que `Radial.App.exe` desaparece; (12) además, todo lo pendiente de la fase 4 (17.11), DPI distintos, varios monitores y bordes de pantalla.

### 18.9 Qué queda para las fases 6, 7 y 8

- **Fase 6:** ventana de ajustes, editor visual del radial con vista previa, biblioteca de acciones (persistencia de las definiciones del usuario), arrastrar y soltar, editor de grupos.
- **Fase 7:** apariencia y temas, colores, autoarranque con Windows, instancia única, notificaciones de fallo de acciones, iconos de archivo (`icons\`).
- **Fase 8:** importar y exportar (con la política de rutas y tipos de archivo de 17.12), robustez multimonitor y DPI mixto, empaquetado, firma de código y Smart App Control (15), validación en Windows 11 estable y checklist manual completa.

## 19. Fase 6: biblioteca de acciones persistente y editor radial (v0.20)

### 19.1 Estado: implementada; pruebas automáticas en verde en CI; sin validar a mano

Rama `feature/fase-6-action-library-editor`, creada sobre `9f11800` (fase 5 con CI en verde). Hitos: (1) biblioteca y migración, (2) modelo de atajos y captura, (3) centro de control, (4) biblioteca en la interfaz, (5) editor radial, (6) integración y pruebas.

- **CI en verde** en `5ed2a5b` (ejecución nº 12, <https://github.com/xrbrr24/Radial/actions/runs/42016665068>: lógica de los hitos 1 y 2), en `e7c7671` (ejecución nº 13, <https://github.com/xrbrr24/Radial/actions/runs/42022325594>: hitos 3 a 5) y en `abca8b2` (ejecución nº 14, <https://github.com/xrbrr24/Radial/actions/runs/42023689947>: último commit de código de la fase, con el flujo de aceptación de punta a punta y la documentación), con compilación, `Radial.Core.Tests` y `Radial.Windows.Tests` en `success`, verificado en la API de GitHub). **Las cifras por suite no se han podido leer** sin autenticación (están en el resumen de la ejecución y en los artefactos TRX). Las ejecuciones nº 9 a 11 fallaron al compilar o en dos pruebas escritas sin poder ejecutarlas en el equipo de desarrollo (avisos `xUnit2029` y `xUnit2013`, un valor nulo conservado a propósito de una acción desconocida y la longitud de un mensaje que ahora lista 118 teclas): errores de las pruebas, corregidos en `8c40b43` y `5ed2a5b` sin tocar el producto. Para poder leerlos, el workflow emite desde `de2b410` cada prueba fallada como anotación de error.
- **Compilación local:** `dotnet build src/Radial.App/Radial.App.csproj -c Release` sin avisos ni errores. Las pruebas no se compilan ni se ejecutan en el equipo de desarrollo (Smart App Control, 15.5 y 17.8).
- **No validado a mano:** arrastrar y soltar, el grabador de atajos con teclado real, el foco y la navegación con teclado, el tamaño de ventana, DPI distintos del 125 %, varios monitores, la bandeja con la ventana nueva, la autoprueba tras mover su lógica a `DiagnosticsView`/`CaptureHost`, y todo lo que ya figuraba pendiente de las fases 4 y 5 (17.11, 18.8). **Las fases 4 y 5 siguen sin estar aprobadas manualmente.**
- **Comprobado en este equipo sin entrada ni hooks:** `Radial.App.exe --render-ui <carpeta>` (herramienta de desarrollo nueva) dibuja el centro de control en PNG con la configuración predeterminada en memoria: editor (sin selección, sector con acción, sector vacío, grupo, dentro de un grupo, ventana estrecha), biblioteca (lista, búsqueda, detalle, formularios de atajo y de aplicación), ajustes, diálogo y aviso. Se arrancó además `Radial.App` unos segundos: la ventana «Radial» (1320×840) se muestra y la aplicación responde.

### 19.2 Una sola fuente de verdad: biblioteca y colocaciones

`RadialConfiguration.Actions` es la **biblioteca**: cada `ActionDefinition` tiene `Id` estable, nombre, descripción, categoría (`ActionCategory`, abierta: `editing`, `browser`, `applications`, `files`, `system`, `multimedia`, `custom` y las que añada el usuario), etiquetas, icono, la acción y si es de fábrica. Una definición puede estar sin colocar, colocada en varios menús o varias veces. Un `RadialItem` es una **colocación**: guarda su sector, el id de su definición y, si el usuario lo cambia, su propio nombre (`LabelOverride`) o icono (`IconOverride`); el resto lo toma de la definición. Los grupos (`OpenMenuAction`) son estructura del árbol de menús, nunca definiciones. Editar una definición cambia a la vez todas sus colocaciones (`LibraryResolver.Materialize`/`Resolve`); borrarla pide quitar antes sus colocaciones o confirmar que se eliminen también.

`LibraryResolver.Normalize` cierra las incoherencias de una configuración cargada (colocación suelta sin definición, referencia colgante): crea la definición que falta o conserva el elemento y lo comunica, y no descarta nada. Una acción de tipo desconocido (7.1) se conserva en la biblioteca y en los menús, no se puede colocar, ejecutar, crear ni editar.

### 19.3 Esquema versión 2 y migración desde la 1

`ConfigurationFile.CurrentSchemaVersion = 2`. El archivo lleva `actions` (la biblioteca) y cada elemento de un menú referencia su definición con `actionId` (más `label` e `icon` solo si son propios); los grupos y los elementos sin definición llevan su `action` en línea. `SchemaV1ToV2Migration` (encadenada en `ConfigurationMigrator`, sobre el árbol JSON) convierte un archivo de la versión 1 sin perder nada: conserva menús, posiciones, grupos y parámetros; recupera las definiciones por `definitionId`; no duplica las idénticas (misma acción y mismo nombre e icono comparten definición); un `definitionId` repetido con otra acción crea una nueva, uno con formato no válido se sustituye; todo ello se comunica como avisos de migración (`ConfigurationCodes.MigrationNote`). Las acciones desconocidas y los elementos malformados quedan como estaban. Se mantienen la cuarentena de archivos corruptos, la copia válida más reciente y el comportamiento de «versión futura» (el archivo no se toca, `Save` se rechaza). Al guardar por primera vez una configuración migrada, `File.Replace` deja el archivo de la versión 1 en `backups\`.

Límites nuevos (`ConfigurationLimits`): 500 caracteres de descripción, 12 etiquetas de 24 caracteres y 2000 definiciones.

### 19.4 Teclas y captura de atajos (sustituye al vocabulario cerrado de 66 teclas de 17.5 y 17.6)

`Radial.Core.Model.KeyVocabulary` sigue siendo la **única fuente de verdad**, ahora con **118 nombres estables**: letras, dígitos, F1–F24, Enter, Tab, Espacio, Esc, Retroceso, Supr, Insertar, Inicio, Fin, RePág, AvPág, flechas, teclado numérico (incluido `NumpadEnter`), `PrintScreen`, `Pause`, `ContextMenu`, las teclas de signos nombradas por su tecla virtual (`Oem1`, `OemPlus`, `OemComma`, `Oem3`…) y las multimedia y de navegador. Los modificadores son Ctrl, Mayús, Alt y Win. **La identidad guardada es la tecla, no el carácter:** en un teclado español `Oem3` es la Ñ y en uno estadounidense la tilde, y la misma configuración envía la misma tecla física con cualquier distribución. Cómo se llama una tecla en el teclado activo lo da `IKeyLabelProvider` (`Win32KeyLabelProvider`: `GetKeyboardLayout`, `MapVirtualKeyEx`, `GetKeyNameText`, solo lectura), nunca la configuración. `ShortcutParser` (Windows) sigue siendo la tabla de nombre a código `VK_*` y se comprueba por reflexión contra el vocabulario. `ShortcutRules` rechaza lo que no se puede enviar (Ctrl+Alt+Supr). `KeyChordText` da el texto de una combinación y las formas en las que se busca.

**Captura:** `KeyCapture.Evaluate` (lógica pura, sin teclado real) decide si una pulsación es utilizable: solo modificadores = seguir esperando; teclas de bloqueo, pulsaciones de un IME o teclas fuera del vocabulario = rechazo **con explicación** y no se guarda nada. `KeyCaptureViewModel` + `KeyCaptureBox` la usan **solo mientras el usuario activa «Grabar» y el campo tiene el foco** de la ventana de Radial: no hay captura global ni permanente (los hooks del radial no intervienen). Para lo que Windows se queda (p. ej. Win+E) hay una elección manual (modificadores y lista de las 118 teclas con el nombre del teclado activo) que pasa por la misma validación. Esc cancela la grabación.

### 19.5 Edición y reglas

`ConfigurationEditor` (Core, sin interfaz) es el único camino de edición: añadir, actualizar, duplicar y borrar definiciones; colocar, quitar, mover (intercambiando si el destino está ocupado), duplicar y sustituir colocaciones; nombre e icono propios; crear, renombrar, convertir y eliminar grupos (rechaza o confirma si tienen contenido); ajustes de navegación. **Cada operación se aplica sobre una copia y se valida con `ConfigurationValidator`: si la configuración resultante no es válida, no cambia nada** y devuelve un `EditResult` con código estable (`EditCodes`: ocupado, duplicado, en uso, ciclo, demasiado profundo, no permitido…) y un mensaje para el usuario. Así el editor impide por construcción ids duplicados, grupos huérfanos, referencias rotas, ciclos, profundidad superior a la máxima y dos elementos en un mismo sector. `ActionSearch` busca (sin distinguir mayúsculas ni tildes) por nombre, descripción, categoría, etiquetas, destino y combinación de teclas (se encuentra escrita como «ctrl+s», «ctrl s» o «ctrl mayús s», y por el nombre de la tecla en el teclado activo), con filtros por tipo, categoría, de fábrica o propia y colocada o no. `ConfigurationSession` lleva la configuración en edición sobre un `ISettingsStore`: «cambios sin guardar» es una **comparación del contenido que se escribiría**, no una marca (deshacer a mano deja la sesión limpia y no hay escrituras innecesarias); un guardado fallido no pierde nada y se puede reintentar; descartar vuelve a lo guardado.

### 19.6 Presentación: modelos de vista sin WPF y vistas

- **`Radial.Windows/Presentation`** (sin WPF; solo `INotifyPropertyChanged` e `ICommand` de la biblioteca base, y la prueba de arquitectura lo sigue exigiendo): `ShellViewModel` (secciones, recorrido, estado de guardado, confirmación al cerrar, búsqueda global de comandos y navegación), `LibraryViewModel`/`ActionFormViewModel`/`KeyCaptureViewModel` (biblioteca, formulario por tipo, grabación), `EditorViewModel` (árbol, ocho sectores, inspector, selector de acciones y todas las operaciones), `SettingsViewModel`, `InAppDialogs` (diálogos y avisos dentro de la ventana, sin cuadros de mensaje del sistema) y `UiText` (textos). Los selectores de archivos y los diálogos son interfaces: las pruebas usan dobles.
- **`Radial.App`**: composición. `Theme/Tokens.xaml` es el **único sitio con valores visuales** (colores, tipografía, radios, espaciado, sombra, duraciones) y `Theme/Controls.xaml` los estilos y plantillas propias (botones, texto, desplegable, deslizador, árbol, listas, barras): sin el aspecto predeterminado de WPF, sin paquetes visuales, con los pinceles como `DynamicResource` para que la fase 7 cambie de tema sin tocar nada más. Paleta: grafito, pizarra y un acento frío; iconos de Segoe Fluent Icons por nombre (`GlyphCatalog`, con los puntos de código como números). `Views/` (`ShellView`, `EditorView`, `LibraryView`, `SettingsView`, `DiagnosticsView`), `Controls/` (`RadialPreview`, `KeyCaptureBox`, `IconPicker`, `ActionPicker`, `GlyphIcon`) y `Runtime/CaptureHost`, que reúne sin interfaz lo que antes vivía en la ventana (captura global, lector y su supervisor, overlay, coordinador, acción pendiente, autoprueba y cierre ordenado). **La vista previa del editor es el mismo `RadialView` del overlay** (no hay una segunda implementación del dibujo), escalado, con los sectores seleccionados según `RadialGeometry.Select`; no instala hooks ni ejecuta nada.
- **Aplicar lo guardado:** al guardar, `CaptureHost.ApplyConfiguration` recrea el coordinador con la configuración nueva (oculta el overlay, cancela la acción pendiente y empieza de cero); durante la autoprueba se aplaza hasta que termina. El supervisor del lector llama al coordinador vigente. Se mantienen las barreras de la autoprueba (marca del evento, `Isolate`, marca de tiempo).
- **Ciclo de vida:** la X oculta la ventana y los cambios sin guardar siguen en memoria; «Salir de Radial» pregunta (seguir editando, descartar y salir, guardar y salir) si los hay y no cierra si guardar falla; al cerrarse la sesión de Windows no pregunta. La captura sigue empezando sola. La barra de título es oscura con atributos documentados de DWM (`WindowStyling`); no se usa ningún material ni desenfoque.

### 19.7 Verificación

- **Core (CI):** esquema 2, migración 1→2 (incluidas incoherencias, acciones desconocidas, archivo corrupto y versión futura), persistencia independiente de las definiciones, reutilización, vocabulario de teclas, reglas de atajos, texto y formas de búsqueda, editor (colocar, mover, duplicar, sustituir, grupos, borrado seguro, ciclos, profundidad, referencias rotas), búsqueda y filtros, sesión.
- **Windows (CI):** tabla de teclas y consistencia con el vocabulario, captura con teclado español, etiquetas, configuración predeterminada con biblioteca y, en `Presentation/`: captura, formulario por tipo (nunca guarda una definición inválida), biblioteca (búsqueda, filtros, indicador de colocada, crear, duplicar, eliminar en uso), editor (colocar, intercambiar, grupos, profundidad máxima, borrado con confirmación, actualización de la vista previa, cambios externos, descartar), ajustes, centro de control (guardado, fallo de guardado, solo lectura, confirmación al cerrar, búsqueda de comandos) y el **flujo de aceptación de punta a punta sobre el almacén real en una carpeta temporal** (crear atajo grabado y acción web, guardarlas sin colocar, reutilizar, crear un grupo, organizar, guardar, «reiniciar» y recuperar).
- **No cubierto por pruebas automáticas** (WPF, sin proyecto de pruebas de interfaz): plantillas, enlaces de XAML, arrastrar y soltar, el grabador con teclado real, `CaptureHost.ApplyConfiguration` y el cierre de la ventana. Solo se ha comprobado con `--render-ui` y el arranque descrito en 19.1.

### 19.8 Qué hay que probar a mano (sin hacer)

(1) Primer inicio con `%LOCALAPPDATA%\Radial` borrado: se crea `config.json` y aparece el centro de control; (2) migrar un `config.json` de la versión 1 (hay una copia en `backups\` tras guardar); (3) crear un atajo grabando la combinación, con Ñ y símbolos en el teclado español, y comprobar que ejecuta la tecla correcta; (4) crear acciones de aplicación, archivo, carpeta y web con los selectores; (5) arrastrar acciones de la biblioteca a los sectores y entre sectores, y al árbol; (6) crear y eliminar grupos con contenido; (7) guardar, cerrar con «Salir de Radial», volver a abrir y usar el radial con el gesto real (el menú guardado es el que sale); (8) cambios sin guardar con la X, con «Salir» y con la ventana oculta; (9) ventana estrecha y maximizada, DPI distintos y varios monitores; (10) `Radial.App.exe --selftest <archivo>` (la lógica de la autoprueba se movió) y todo lo pendiente de 17.11 y 18.8.

### 19.9 Qué queda

- **Fase 7:** temas (los tokens ya están centralizados), autoarranque con Windows, instancia única, notificaciones de fallo de acciones, iconos de archivo (`icons\`), tamaño y posición de la ventana, ajustes avanzados del gesto.
- **Fase 8:** importar y exportar (con la política de rutas y tipos de archivo de 17.12), multimonitor y DPI mixto, empaquetado, firma y Smart App Control (15).
- Sin hacer a propósito en esta fase: macros, scripts, accesibilidad completa del editor para lectores de pantalla (hay navegación con teclado en la vista previa, pero no se ha validado con ninguno), pruebas automáticas de la interfaz WPF.


## 20. S2: fiabilidad y gestión de errores (v0.21)

### 20.1 Estado: implementado; compilación del producto correcta; pruebas escritas y SIN ejecutar; sin validar a mano

| Qué | Estado |
|---|---|
| Código (`Radial.Core/Diagnostics`, `Radial.Windows/Diagnostics`, integración en `Radial.App`) | **Implementado** |
| Compilación Release del producto (`dotnet build src/Radial.App/Radial.App.csproj -c Release`) | **Correcta, 0 advertencias y 0 errores**, en el equipo de desarrollo |
| Pruebas nuevas (Core y Windows) | **Escritas; no se compilan ni se ejecutan en el equipo de desarrollo (Smart App Control, 15.5).** Pendiente la ejecución de CI |
| Comportamiento real (globo de bandeja, banner, cierre por error grave, rotación real de los archivos, hilo de escritura) | **Sin probar a mano** (20.8) |

### 20.2 Qué puede hacer ahora el usuario

Un fallo ya no se pierde: se escribe en un registro en disco y, según su gravedad, se avisa aunque Radial esté oculto en la bandeja. Una acción que falla se registra en disco y se dice dentro de la ventana de Radial (avisarlo por la bandeja con la ventana oculta es de la fase 7, pendiente); la captura global que se detiene o un hook de ratón que Windows deja de entregar generan un globo de la bandeja; los problemas que comprometen el funcionamiento dejan además un banner en la ventana, visible al abrirla. Pulsar el globo abre Diagnóstico. El icono de la bandeja tiene una opción nueva, «Abrir carpeta de registros». Si Radial terminó por un error grave, el siguiente arranque lo avisa.

### 20.3 Arquitectura

```
Origen del error ──▶ ErrorReporter.Report / ReportProblem ──▶ 1 clasifica (ErrorClassifier + ErrorBudget)
 (Radial.App,            (Radial.Windows/Diagnostics)           2 registra   ──▶ IRadialLog ──▶ FileRadialLog ──▶ %LOCALAPPDATA%\Radial\logs\
  Radial.Windows)                                               3 avisa      ──▶ NotificationThrottle ──▶ NotificationRouter ──┬─▶ ShellNoticeSink (banner / aviso en la ventana)
                                                                4 si es fatal ──▶ FileFatalMarker + TerminationRequested        └─▶ TrayNoticeSink (globo de la bandeja, TrayIcon)
```

- **`Radial.Core/Diagnostics`** (sin Windows ni WPF; pura y con pruebas de Core): `IRadialLog` (por donde cualquier proyecto deja constancia, sin saber dónde se guarda), `LogEntry`, `LogLevel`, `LogFormatter` (formato), `LogSanitizer` (datos sensibles), `ErrorClassifier`/`ErrorImpact`/`ErrorOrigin` (política de recuperación), `ErrorBudget` (bucles de errores), `NotificationThrottle` (tormentas de avisos) y `LogRetention` (nombres y retención).
- **`Radial.Windows/Diagnostics`** (sin WPF): `FileRadialLog` (el registro en disco), `ErrorReporter` (el punto único), `NotificationRouter` y los contratos `IUserNotifier`, `IUserPresence`, `INoticeSink`, `OperationTrail` (qué se estaba haciendo) y `FileFatalMarker`.
- **`Radial.App`**: `Runtime/ErrorSystem` (composición y captura global), `Runtime/NoticeSinks` (los dos canales y la presencia de la ventana) y la integración en `App`, `MainWindow`, `CaptureHost` y `TrayIcon`. `FileSettingsStore` recibe un `IRadialLog` opcional para sus fallos de disco.
- **No hay un segundo sistema de avisos:** se reutilizan el banner y los avisos de la ventana (`ShellViewModel.Banner`, `InAppDialogs`) y el `NotifyIcon` de la bandeja.

### 20.4 Captura global (qué se captura y qué se hace)

| Contexto | Mecanismo | Decisión |
|---|---|---|
| Hilo de interfaz (manejadores, comandos, enlaces, `async void`) | `Application.DispatcherUnhandledException` | Se marca como controlada siempre (decide el reportero). Se sigue, salvo tipo fatal o más de 5 errores en 60 s (presupuesto): entonces cierre ordenado |
| Otro hilo (pool, temporizador) | `AppDomain.UnhandledException` | El runtime termina el proceso de todas formas. Se registra con vaciado a disco y se deja el marcador. **No** se intenta un cierre ordenado desde un hilo arbitrario con el estado posiblemente corrupto (podría bloquear el cierre) |
| Tarea con fallo que nadie observó | `TaskScheduler.UnobservedTaskException` | Recuperable: se registra, se avisa y se marca como observada |
| Trabajo lanzado y olvidado (`RunGuardedAsync`) | `try/catch` propio | Lector de eventos de la captura y «salir de Radial» (antes el primero tragaba cualquier excepción en silencio) |
| Comandos asíncronos de la interfaz | `AsyncCommand.Failed` | Recuperable |
| Arranque | `try/catch` de `App.OnStartup` | Fatal: se registra, se deja el marcador y se termina |
| Cierre | `try/catch` en `MainWindow.ShutdownOnce` | Recuperable: se registra y se sigue cerrando |
| Fallos de la captura (iniciar, hook silencioso, errores del servicio, lector detenido) | `CaptureHost` (`CheckHealth` cada 5 s mientras corre) | Degradado: globo + banner |
| Almacenamiento | `FileSettingsStore` (registro) y los resultados que ya devolvía (aviso en la ventana) | Registro en disco; al usuario, como antes |

**Clasificación (`ErrorClassifier`).** Son fatales en cualquier contexto `OutOfMemory`, `AccessViolation`, `StackOverflow`, `InvalidProgram`, `BadImageFormat`, `SEHException`, la caída de un dominio y `TypeInitializationException` (o cualquiera de ellas como interna): el proceso puede tener el estado corrupto y seguir «como si nada» lo ocultaría. Por origen: arranque y otro hilo, fatal; almacenamiento y captura, degradado; interfaz, tarea sin observar, acción y cierre, recuperable. Un error del propio aviso nunca escala ni vuelve a avisar (no hay bucle).

**Cierre por error grave.** En el hilo de interfaz: se registra (nivel `Critical`, vaciado a disco), se deja `last-fatal.txt`, se suelta la captura global (`CaptureHost.Shutdown`: coordinador, hooks, overlay, bandeja), y se llama a `Application.Shutdown(1)`. Soltar los hooks ANTES de terminar importa: un proceso a medias con los hooks puestos dejaría el botón central consumido. El marcador solo guarda origen, tipo de excepción y operación, nunca el mensaje.

### 20.5 Registro persistente

- **Ubicación:** `%LOCALAPPDATA%\Radial\logs\radial-aaaaMMdd-NNN.log` (la carpeta ya estaba reservada, 7). Se crea si no existe. UTF-8 sin BOM, abiertos con uso compartido (se pueden leer mientras Radial corre).
- **Formato:** `fecha ISO-8601 nivel [origen] t<hilo> op=<operación> | mensaje`, y debajo (sangrado) la excepción completa (tipo, mensaje y traza de cada una, hasta 6 niveles y 8 excepciones) y el rastro de las últimas 8 operaciones (`OperationTrail`: arranque, procesar gesto, aplicar configuración, acciones...). La operación en curso se toma del flujo de ejecución si quien informa no la da.
- **Sin datos del usuario:** quien registra no pasa nombres ni rutas (de una acción se registra su identificador y el código del error); además `LogSanitizer` sustituye la carpeta de perfil por `%USERPROFILE%`, recorta de las direcciones web lo que sigue a `?` o `#` y limita la longitud.
- **Escritura:** hilo propio y cola acotada (2000): `Write` no bloquea ni lanza; si la cola se llena se descartan entradas y se anota cuántas. Las entradas `Critical` esperan hasta 2 s a estar escritas. `Dispose` vacía la cola (espera hasta 3 s). Un fallo del propio registro (carpeta imposible, disco lleno, archivo bloqueado) se cuenta y se ignora: no hay fallo en cascada.
- **Retención:** un archivo por día; al pasar de 1 MiB, el siguiente número. Se conservan los 14 más recientes y, además, como mucho 8 MiB en total; nunca se borra el archivo en uso ni otros archivos de la carpeta (`last-fatal.txt` incluido).

### 20.6 Avisos al usuario

- **Dos tipos:** `Notice` (una operación falló y Radial sigue entero; p. ej. una acción) y `Problem` (una función quedó comprometida o Radial va a cerrarse). Texto siempre escrito para personas; nunca el de la excepción. Los `Problem` añaden «Más detalles en la sección Diagnóstico y en la carpeta de registros.».
- **Enrutado (`NotificationRouter`):** `Notice` dentro de la ventana si el usuario la está mirando (visible, no minimizada y **activa**; una ventana abierta detrás de otra aplicación no cuenta) y, si no, globo de la bandeja; con fallback al otro canal si falla o no existe. `Problem`: siempre los dos (globo para enterarse con la ventana oculta; banner para que se vea al abrirla). Si no hay canal (la bandeja aún no existe durante el arranque), se guarda (hasta 8) y se entrega al cargar la ventana.
- **Tormentas (`NotificationThrottle`):** el mismo aviso no se repite antes de 30 s (el siguiente que se muestre dice «se repitió N veces»), y como mucho 3 avisos por minuto en total. Solo limita los avisos: el registro en disco no pasa por aquí.
- **Sin bloqueos:** los canales llevan el aviso al hilo de interfaz con `Dispatcher.BeginInvoke` y no esperan; ningún aviso es un cuadro de mensaje del sistema ni un diálogo modal, y nada se avisa desde un hilo de hooks.
- **Si el aviso falla**, el error ya está en el registro; el fallo del canal se anota y se prueba el otro.

### 20.7 Decisiones de recuperación no evidentes

1. **Siempre `Handled = true` en la interfaz**, con presupuesto: dejar la excepción sin controlar mataría el proceso sin soltar los hooks ni escribir nada; marcarla y seguir sin límite escondería un estado inestable. El presupuesto (5 errores por minuto, reloj monotónico) corta los bucles.
2. **Error de otro hilo: no se intenta cerrar con orden.** El runtime ya termina el proceso; hacer `Service.Dispose()` (espera hasta 3 s al hilo de hooks) desde un hilo con estado dudoso podría colgar justo el cierre. Se escribe, se vacía a disco y se deja el marcador; los hooks desaparecen con el proceso.
3. **Fallo al arrancar: sin cuadro de mensaje.** Mantiene la regla del proyecto (nada de cuadros del sistema). Consecuencia: el usuario solo se entera por el aviso del siguiente arranque o mirando los registros (20.9).
4. **Aplicar la configuración al radial** (`CaptureHost.ApplyConfiguration`) crea el coordinador nuevo ANTES de soltar el viejo: si falla, el radial sigue con el anterior y el aviso dice que los cambios se guardaron pero no se aplicaron; antes la excepción salía por el flujo de guardado con los cambios ya en disco.
5. **Los fallos de las acciones NO usan el sistema de avisos** (corrección de alcance, v0.27): avisarlos por la bandeja con la ventana oculta es una función de la fase 7 no aprobada. `CaptureHost.OnActionFinished` los registra como advertencia con `ErrorReporter.RecordProblem` (identificador y código, no nombre ni destino; sin aviso) y los comunica con `ActionCompleted`, que la ventana convierte en un aviso dentro de ella, como antes de la entrega. Un fallo de acción no cuenta para el limitador de avisos. El resto de fallos (arranque, configuración, captura, aplicación) sí pasan por `NotificationRouter`.
6. **La autoprueba (`--selftest`) escribe registros** (no config). Las herramientas de desarrollo (`--render-ui`, `--render-preview`, `--overlay-check`) no crean el sistema de errores y siguen sin tocar el disco.

### 20.8 Qué hay que probar a mano (sin hacer)

(1) Una acción que falla (p. ej. una carpeta que no existe) → aviso dentro de la ventana de Radial (visible al abrirla) y una línea de advertencia en el registro; **no** hay globo en la bandeja (fase 7). (2) Con la ventana oculta, un fallo de la captura (p. ej. impedir iniciar los hooks) → globo en la bandeja; pulsarlo abre Diagnóstico. (3) Ventana abierta pero detrás de otra aplicación, mismo fallo de captura → también globo. (4) Ventana activa → aviso dentro de la ventana, sin globo. (5) Repetir un fallo de captura varias veces → un solo aviso y luego «se repitió N veces». (6) «Abrir carpeta de registros» en el menú de la bandeja y comprobar el contenido. (7) Impedir la escritura de `logs\` (permisos) y comprobar que Radial sigue funcionando. (8) Simular un fallo grave (depuración) y comprobar: archivo `last-fatal.txt`, hooks liberados (el botón central vuelve a funcionar), aviso en el arranque siguiente. (9) Bloquear con otra herramienta `logs\radial-*.log` y comprobar que no hay cascada. (10) Rotación real con muchos registros. (11) Quitar el hook de ratón (la sonda lo detecta, 16.6) y comprobar el aviso.

### 20.9 Limitaciones

- Un fallo durante el arranque, antes de que exista la bandeja, no se puede comunicar en el momento: queda en el registro y se avisa en el siguiente arranque.
- Un error no controlado en otro hilo no permite aviso en el momento (el proceso termina); solo registro y marcador.
- No se instalan filtros de excepciones de primera oportunidad (`FirstChanceException`): solo lo que alcanza el límite del hilo o del trabajo lanzado.
- El registro no está cifrado ni se envía a ningún sitio (sin telemetría): es local; el usuario decide si lo comparte.
- Los hilos de los hooks (`LowLevelHookBase`) siguen sin registrar por sí mismos (callbacks mínimos): se revisan sus contadores desde `CheckHealth` cada 5 s.
- Se han dejado sin cambiar los `catch` vacíos documentados de la fase 3 (`GestureEventReader`, `GestureReaderSupervisor`): su fallo ya llega al sistema por otros caminos y no tienen acceso al registro sin tocar los hooks.

## 21. Acciones útiles además de atajos y versión portátil (v0.28)

### 21.1 Estado: implementado; compilación y publicación correctas en el equipo de desarrollo; pruebas escritas y SIN ejecutar allí; sin validar a mano

| Qué | Estado |
|---|---|
| Código (`Radial.Core/Model`, `Storage/ActionJson`, `Radial.Windows/{TextInput,Audio,WindowControl}`, formulario del editor, `Radial.App`) | **Implementado** |
| `dotnet build src/Radial.App/Radial.App.csproj -c Release` | **Correcto, 0 advertencias y 0 errores** (13-nov-2026) |
| `tools/publish-portable.ps1` (mismo comando, ejecutado a mano) | **Correcto, 0 advertencias**: un único `Radial.App.exe` de unos 72 MiB |
| `Radial.App.exe --config-check` sobre el `.exe` publicado con `docs/examples/config-acciones-nuevas.json` | **Correcto**: 9 acciones, los cuatro tipos nuevos reconocidos, sin avisos |
| `Radial.App.exe --render-ui` sobre el `.exe` publicado (formularios nuevos dibujados fuera de pantalla) | **Correcto** (WPF funciona dentro del archivo único) |
| Pruebas nuevas (Core y Windows) | **Escritas; no se compilan ni se ejecutan en el equipo de desarrollo (Smart App Control, 15.5).** Las valida CI: **ejecución nº 21 sobre `63223a7`, en verde** (compilación de la solución, Core, Windows y trabajo `portable`; <https://github.com/xrbrr24/Radial/actions/runs/43412919169>; estado verificado en la API de GitHub, cifras por suite sin comprobar). La nº 20 (`ef2825a`) falló solo en la prueba de guarda del número de códigos de error (11 → 16) y se corrigió sin tocar el producto |
| Ejecución real (texto escrito en otra aplicación, volumen de Windows, cerrar una ventana ajena) | **Sin probar a mano** (21.9) |

### 21.2 Modelo y `config.json`

Los tres tipos nuevos son acciones de la biblioteca como las demás (`actions` + `actionId`), con su categoría e icono por defecto, y se dibujan en el radial como cualquier otra. `schemaVersion` sigue en 2 (los campos son aditivos).

| `type` | Campos | Reglas (`GetProblems()`, única fuente) |
|---|---|---|
| `typeText` | `text` | 1–1000 caracteres; se admiten saltos de línea (`\r\n`, `\n`, `\r`) y tabuladores; se rechazan los demás caracteres de control (incluido `\0`) y los pares sustitutos incompletos. |
| `volume` | `operation` (`increase`, `decrease`, `setLevel`, `toggleMute`), `percent` | `increase`/`decrease`: diferencia 1–100; `setLevel`: nivel 0–100; `toggleMute` no usa número (no se escribe). |
| `windowControl` | `operation` (`minimize`, `maximize`, `restore`, `close`) | Operación conocida. |
| `launchApplication` | `path`, `arguments` (**lista de textos**), `workingDirectory` | Como antes, más: como máximo 64 argumentos, 8192 caracteres cada uno y 30 000 en total; sin `\0` ni elementos nulos. |

- **Argumentos como lista.** `LaunchApplicationAction.Arguments` era un texto y ahora es `IReadOnlyList<string>`: cada elemento llega al programa tal cual, sin comillas que escribir y sin intérprete de comandos. La igualdad del `record` compara elemento a elemento (`null` y lista vacía son «sin argumentos»). **Compatibilidad:** un `config.json` con `"arguments": "--x \"y z\""` (texto) se sigue leyendo y se divide con las reglas de `CommandLineToArgvW` (`CommandLineArguments.Split`, solo traduce); al guardar se reescribe como lista. Un tipo JSON equivocado (número, objeto, un elemento que no es texto) es un error ESTRUCTURAL y el archivo va a cuarentena como siempre.
- **Operación desconocida o número fuera de rango** (`"operation": "explode"`, `percent: 500`): no es un error de lectura; la validación del modelo lo señala y la carga tolerante la neutraliza (`UnknownAction` con su JSON íntegro, aviso `config.action-neutralized`, colocaciones enlazadas), igual que cualquier otra acción inválida (5/6).
- **Los mensajes y el registro nunca repiten el texto** de `typeText` (puede ser una contraseña o datos personales). Aun así queda en `config.json` en claro: el formulario lo dice y no es lugar para secretos.

### 21.3 Insertar texto (`TypeTextActionExecutor`)

Orden fijo, el de los atajos (3.4, 17.x): tipo → cancelado → `GetProblems()` → hay ventana en primer plano → espera a que se suelten los cuatro modificadores físicos (`ModifierReleaseWaiter`, extraído del ejecutor de atajos y compartido; mismo tiempo de espera y mismo criterio) → la ventana en primer plano sigue siendo la misma → UNA llamada a `SendInput` con todo el texto. Códigos: `InvalidAction`, `ForegroundChanged`, `ModifiersNotReleased`, `InputInjectionFailed`, `KeyReleaseFailed`.

- **Sin portapapeles.** Cada unidad UTF-16 va como `KEYEVENTF_UNICODE` (pulsar y soltar, `wVk = 0`, el carácter en `wScan`): no depende de la distribución del teclado ni de los modificadores; los pares sustitutos se envían unidad a unidad, como documenta Windows. Un salto de línea es UNA pulsación de Intro y un tabulador, una de Tab (muchas aplicaciones ignoran esas teclas si llegan como carácter Unicode). `KeyboardEvent` ganó un campo aditivo `Unicode`; `ShortcutSequence.ReleasesFor` distingue una tecla virtual de un carácter Unicode con el mismo código.
- **Mismo inyector que los atajos** (`SendInputKeyboardInjector`, sin la marca de la autoprueba: los hooks descartan sus eventos por inyectados). Si Windows acepta solo parte del texto, se suelta lo pulsado y se informa («puede que se haya escrito solo una parte»); no se reintenta el envío.
- **Límites sin solución:** aplicación de destino elevada (UIPI: Windows descarta la entrada sin avisar; el éxito significa «Windows aceptó la entrada», no «la aplicación la procesó»); aplicaciones que filtran la entrada Unicode (algunos juegos, escritorios remotos y máquinas virtuales); 1000 caracteres como tope para que una sola llamada no ocupe un tiempo apreciable.

### 21.4 Volumen (`VolumeActionExecutor`)

Windows Core Audio sobre el dispositivo de salida PREDETERMINADO (rol multimedia): `IMMDeviceEnumerator` → `IMMDevice.Activate` → `IAudioEndpointVolume`, interfaces COM declaradas a mano (`CoreAudioEndpointProvider`, sin paquetes, sin teclas multimedia, sin mezclador ni diálogos). Cada ejecución abre el dispositivo y lo libera al terminar (el predeterminado puede cambiar en cualquier momento). Se hace **en el pool**, no en el hilo de interfaz (COM con el servicio de audio ocupado puede tardar).

- Subir/bajar: nivel actual (entero 0–100, redondeado) ± diferencia, acotado a 0–100; **subir** también quita el silencio. Fijar: el nivel indicado; si es > 0 y había silencio, lo quita (0 no). Alternar silencio: invierte el silencio sin tocar el nivel.
- Códigos: `AudioUnavailable` (no hay dispositivo, se desconectó o el servicio de audio no responde: `E_NOTFOUND`, `AUDCLNT_E_DEVICE_INVALIDATED`, `AUDCLNT_E_SERVICE_NOT_RUNNING`), `AudioControlFailed` (Windows rechazó la operación). Los mensajes no incluyen nombres de dispositivo.
- El orden de los métodos de las interfaces COM es el de `mmdeviceapi.h` y `endpointvolume.h`: un error ahí no lo detecta el compilador ni las pruebas con dispositivo falso. **Es lo primero que hay que comprobar a mano** (21.9).

### 21.5 Controlar la ventana activa (`WindowControlActionExecutor`)

El destino es la ventana con el foco al ejecutar (el overlay no se activa, así que sigue siendo la que había debajo). Se rechaza con `WindowNotAvailable` si no hay ninguna, ya no existe, no es visible, **pertenece al proceso de Radial** (centro de control, overlay, diálogos: se compara el identificador de proceso) o es el escritorio o una barra de tareas (clases `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`). Se vuelve a comprobar justo antes de actuar. Una operación que no procede (minimizar lo minimizado, maximizar lo maximizado, restaurar lo que ya tiene tamaño normal, ventana sin botón de minimizar o maximizar) da `WindowOperationNotSupported`; cerrar se permite siempre.

- Se pide con `WM_SYSCOMMAND` (`SC_MINIMIZE`, `SC_MAXIMIZE`, `SC_RESTORE`, `SC_CLOSE`) por `PostMessage`: lo mismo que hacen los botones de la barra de título, la ventana decide y **no se espera a que responda** (una ventana colgada no bloquea a Radial). **Cerrar es un cierre normal:** la aplicación puede preguntar si guardar; no se mata ningún proceso.
- Proceso elevado: Windows (UIPI) rechaza el mensaje con «acceso denegado» y se informa (`WindowOperationFailed`, mensaje sin nombre de programa ni título de ventana).
- Todo el trabajo va en el pool; el éxito significa «Windows aceptó la orden», no «la aplicación la atendió».

### 21.6 Iniciar aplicación con argumentos y directorio de trabajo

`ProcessLaunchRequest.Arguments` es la lista; `ShellProcessLauncher.CreateStartInfo` la copia a `ProcessStartInfo.ArgumentList` (nunca se compone una línea ni se pasa por `cmd`/PowerShell; `UseShellExecute` sigue en `true`). **Comprobado en el código fuente del runtime** (`SafeProcessHandle.Windows.cs`, `StartWithShellExecute`): los parámetros de `ShellExecuteEx` salen de `startInfo.BuildArguments()`, que incluye `ArgumentList`, y no se rechaza con `UseShellExecute`. **No se ha ejecutado:** Smart App Control bloqueó (`0x800711C7`) el programa de comprobación que se compiló para ello y, según 15.5, no se insiste. El entrecomillado lo hace .NET con las reglas de `CommandLineToArgvW`; un programa que interprete su línea de comandos de otra manera verá otra cosa (límite del programa, no de Radial).

**Formulario.** «Argumentos (uno por línea)»: las líneas vacías se ignoran y cada línea se recorta; un argumento con espacios va en una sola línea. Un argumento con saltos de línea (solo posible editando `config.json`) no se puede escribir en este formulario: guardar queda bloqueado con una explicación hasta que se edite o borre el campo, para no alterarlo en silencio.

### 21.7 Editor, biblioteca y radial

`ActionKind` ganó `Text`, `Volume` y `Window`; «Nueva acción» ofrece «Insertar texto», «Volumen» y «Ventana activa» (también la paleta de comandos y el filtro por tipo). `ActionFormViewModel` valida el número en el propio formulario (entero 0–100 o 1–100, mensaje por operación) y deja el resto a `GetProblems()`. `UiText.Summary` describe cada acción en una línea (el texto se acorta a 40 caracteres y los saltos de línea se muestran como «↵»). La búsqueda de la biblioteca encuentra el texto de una acción de insertar texto y los argumentos de una aplicación. `ActionExecutors.Create` registra los tres ejecutores en el `CompositeActionExecutor`.

### 21.8 Versión portátil

- **Comando (única definición, `tools/publish-portable.ps1`):** `dotnet publish src/Radial.App/Radial.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false`. Salida local: `artifacts\portable\win-x64\Radial.App.exe` (`artifacts\` está excluido de Git). Sin recorte (WPF no lo admite de forma segura).
- **Sin firmar.** No es un instalador y no se firma (fase 8). Un `.exe` sin firmar puede ser bloqueado por Smart App Control (15) o avisado por SmartScreen; **que se ejecute aquí no demuestra que se ejecute en otro equipo**. Las bibliotecas nativas de WPF se extraen la primera vez en una carpeta temporal del usuario (`IncludeNativeLibrariesForSelfExtract`).
- **Identidad de la compilación en archivo único:** `Assembly.Location` está vacío dentro de un archivo único; `BuildIdentity` usa entonces `Environment.ProcessPath` (la fecha que se muestra es la del `.exe`).
- **CI (`.github/workflows/radial-windows-validation.yml`, trabajo `portable`):** `needs: validate`, así que solo corre si la compilación de la solución y las dos suites pasaron. Publica con el mismo script, comprueba que el `.exe` publicado arranca (`--config-check` sobre una carpeta temporal con el ejemplo de configuración: debe cargar 1 acción de texto, 4 de volumen, 3 de ventana y 1 aplicación), calcula el SHA-256 y sube el artefacto `radial-portable-win-x64` (`Radial.App.exe` y `Radial.App.exe.sha256`, 30 días). Descarga: pestaña Actions del repositorio → ejecución → «Artifacts» (requiere sesión en GitHub), o `gh run download <id> -n radial-portable-win-x64`.
- `Radial.App.exe --config-check <informe.txt> --root <carpeta>` (herramienta de desarrollo y de comprobación): carga o crea `config.json` en `<carpeta>`, la recarga y escribe un informe; sin hooks, sin ventanas, sin acciones. `--root` es obligatorio: nunca usa la configuración personal.

### 21.9 Pendiente de validar a mano

Ninguna de las cuatro acciones se ha ejecutado contra Windows real. En orden de riesgo: (1) volumen (orden de los métodos COM); (2) texto en un editor normal, con tildes, ñ, símbolos, un emoji y un salto de línea, y que no se pegue el Ctrl del gesto; (3) minimizar, maximizar, restaurar y cerrar sobre una ventana ajena, y que nunca actúe sobre Radial; (4) una aplicación con argumentos con espacios; (5) el `.exe` portátil en otro equipo (Smart App Control o SmartScreen pueden bloquearlo). Los fallos de estas acciones se dicen dentro de la ventana de Radial (Diagnóstico) y se registran en disco, no por la bandeja (v0.27).
