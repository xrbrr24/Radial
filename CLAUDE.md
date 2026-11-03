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

## Ejecutores de acciones (fase 4, paso 3 — sin verificar)

- `Radial.Windows/Actions`: `IProcessLauncher` (destino, argumentos y directorio de trabajo **separados**), `ShellProcessLauncher` (`Process.Start` con `UseShellExecute`; **nunca** `cmd.exe`, PowerShell ni scripts) y los ejecutores `LaunchApplicationActionExecutor`, `OpenFileActionExecutor`, `OpenFolderActionExecutor`, `OpenUrlActionExecutor` sobre la base `ProcessActionExecutor<T>`. Orden fijo: tipo no admitido → cancelado → `GetProblems()` de Core → comprobaciones propias (existencia, esquema) → lanzamiento. No hay expansión de variables de entorno ni resolución de rutas relativas.
- Códigos de error: `UnsupportedAction`, `InvalidAction`, `TargetNotFound`, `LaunchFailed`, `UrlNotAllowed`. Los mensajes no incluyen rutas ni direcciones.
- **`OpenUrlAction` solo admite `http` y `https`**, con una única definición en Core (`OpenUrlAction.AllowedSchemes`); `mailto:` y el resto se rechazan al validar. El ejecutor de Windows reutiliza esa lista en su comprobación defensiva independiente justo antes de lanzar. No ampliarla sin decisión expresa.
- Las pruebas de estos ejecutores usan un lanzador falso y no inician ningún proceso; `ShellProcessLauncher` se prueba con un arranque inyectado. El lanzador real solo está sin probar de extremo a extremo.

## DPI y overlay

- `PerMonitorV2` (ver `app.manifest`). No usar `GetDpiForMonitor`; usar `GetDpiForWindow`, `VisualTreeHelper.GetDpi` y gestionar `WM_DPICHANGED`.
- **Overlay (ADR-001, aceptado): ventana layered de WPF** — `AllowsTransparency=true`, fondo oscuro translúcido, forma circular, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `WS_EX_TRANSPARENT` para el click-through, `Topmost`, mostrar sin activar. Se crea una vez al arrancar. Hay que comprobar que transparencia, click-through y no activación funcionan **a la vez**.
- **Sin blur.** No usar Acrylic, Mica ni técnicas DWM (`DWMWA_SYSTEMBACKDROP_TYPE`, `SetWindowRgn` sobre material) en producción. **`SetWindowCompositionAttribute` no es una API pública documentada: no se usa.** La captura de pantalla + `BlurEffect` (variante E) está archivada como posible experimento futuro: no implementarla ni depender de ella. El desenfoque no es requisito del MVP.
- `WM_NCHITTEST → HTTRANSPARENT` no deja pasar el clic a otro proceso: no usarlo.
- La investigación de la fase 2 fue **parcial**: un monitor al 125 %, una ejecución. DPI 100/150/200 %, varios monitores, cambio de monitor, `WM_DPICHANGED` real y Windows 11 estable 25H2 siguen **pendientes de validar**; no afirmar que están probados.
- Las causas de los resultados de las variantes B, C y D **se desconocen**. No presentarlas como explicadas.

## Estética

Oscura, translúcida, minimalista, grafito con acento frío. Sin exceso de gradientes ni brillos.

## Comandos

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/Radial.App
```

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

Fases 0 y 1 completadas. Fase 2 cerrada con cobertura parcial y decisión aceptada (ADR-001). **Fase 3 aprobada** (401 pruebas automáticas en verde: 252 Core + 149 Windows; autoprueba sintética; validación manual con hardware y cierre durante la autoprueba realizados por el usuario; auditoría D1–D3 corregida). Sin validar al cierre: bloqueo/suspensión reales, otros DPI, varios monitores, Windows 11 25H2, Ctrl atascado (hipótesis); ver 16.12 del documento técnico. **Fase 4 en curso, NO aprobada.** Pasos 1 y 2 hechos y verificados (contratos de ejecución de acciones y reloj de producción en Core; parser de atajos en Windows con el vocabulario de teclas unificado en Core; 916 pruebas en verde al cerrar el paso 2). **Paso 3 (ejecutores de aplicación, archivo, carpeta y URL) escrito y compilado, pero SIN VERIFICAR y NO aprobado:** Smart App Control bloqueó las pruebas de Windows y las de Core con el código actual; la última ejecución de Core (571 en verde) es anterior al ajuste de URL; las de Windows nunca se han ejecutado (documento técnico, 17.8). Ver las secciones 10, 12, 14, 15, 16 y 17.

Roadmap: fase 3 captura global · fase 4 overlay real (variante A) y acciones · fase 5 submenús y configuración (MVP) · fase 6 ajustes y editor visual · fase 7 apariencia y sistema · fase 8 importar/exportar y distribución.
