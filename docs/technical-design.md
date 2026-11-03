# Radial — Documento técnico v0.9

Estado: **Fases 0, 1, 2 y 3 cerradas** (la fase 3 aprobada tras la validación manual con hardware y la auditoría final; limitaciones sin validar en 16.12). La fase 2 se cerró con cobertura parcial (sección 10, [ADR-001](adr/ADR-001-overlay-rendering.md)). **La fase 4 está en curso y NO está aprobada: solo se ha hecho su paso 1** (contratos de ejecución de acciones y reloj de producción, sección 17). Lo implementado está en las secciones 14 (núcleo), 16 (captura global) y 17 (paso 1 de la fase 4); todo lo demás sigue siendo diseño.

Convención: **[Comprobado]** = verificado con documentación oficial o en este equipo. **[Por validar]** = propuesta que debe confirmarse con un prototipo o prueba.

## 0. Historial de cambios

| Versión | Cambios |
|---|---|
| v0.2 | Ctrl primero obligatorio, geometría normalizada, DPI con `GetDpiForWindow`, hooks con eventos críticos en orden, almacenamiento en `%LOCALAPPDATA%`, spike de blur, fases 0 a 8. |
| **v0.9** | Fase 4, **paso 1** (de varios): contratos de ejecución de acciones (`IActionExecutor`, `ActionExecutionResult`, `ActionErrorCodes`) y reloj de producción (`MonotonicClock`) en `Radial.Core`. Sin ejecutores concretos ni cambios en `Radial.Windows` ni `Radial.App`. La fase 4 no está aprobada. Nueva sección 17. |
| v0.8 | Fase 3 **aprobada**. Se registra el resultado de la validación manual con hardware y del paso 14 (cierre durante la autoprueba), realizados por el usuario (16.9 y 16.10), y lo que sigue sin validar (16.12). Solo documentación: sin cambios de código respecto a la v0.7. |
| v0.7 | Correcciones de la auditoría final de la fase 3 (D1, D2, D3): la autoprueba comprueba cursor, ventana bajo el cursor y ventana en primer plano antes de **cada** inyección, marca sus eventos (`dwExtraInfo`, 32 bits) y solo acepta los propios; un segundo servicio ya no deja el estado en `Starting`; cerrar la ventana durante la autoprueba la cancela y libera de forma síncrona lo que pulsó. Secciones 16.1, 16.2, 16.4, 16.6, 16.7, 16.9 y 16.11. |
| v0.6 | Fase 3 implementada: captura global (`GlobalInputService`, hooks de bajo nivel en un hilo dedicado, `ChordTracker`, watchdog, sonda de salud, diagnóstico) en `Radial.Windows`, y ventana de diagnóstico en `Radial.App`. **Cambio de diseño respecto a v0.5/3.3: el watchdog ya no comprueba el botón central con `GetAsyncKeyState`**, porque la autoprueba demostró que Windows no refleja un evento consumido por un hook (sección 16.6). Nueva sección 16. |
| v0.5 | Fase 2 cerrada con cobertura parcial (un monitor al 125 %, una ejecución válida). Overlay de producción: **ventana layered de WPF (variante A)**, sin Acrylic, Mica ni técnicas DWM; el blur deja de ser requisito del MVP ([ADR-001](adr/ADR-001-overlay-rendering.md)). Nueva sección 10 con resultados y anomalías sin explicar, y sección 15 sobre Smart App Control. Fases 4 y 7 y el criterio 9 del MVP actualizados. |
| v0.4 | Fase 1 implementada: modelo de dominio, geometría, histéresis, motor, submenús y validador en `Radial.Core`. La fórmula de normalización de la sección 5 se corrige para reflejar el código (`%` con corrección explícita). Nueva sección 14. Cambios de diseño respecto a v0.3 listados en 14.6. |
| v0.3 | Compatibilidad oficial: **Windows 11 x64 estable 25H2+**, sin Windows 10. Corrección: la build 26300 es Windows 11 **26H2**, disponible en el canal de disponibilidad general desde el 29-sep-2026, no una build Insider. E-1 (bordes) vuelve a **abierto** con alternativas documentadas. Timeout de atajos 5 s configurable. Primera escritura por creación y siguientes por reemplazo atómico. Estrategia explícita para acciones de tipo desconocido. Eliminada la resincronización con `GetAsyncKeyState` en callbacks. Resultados de la fase 0. |

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
| **Radial.Windows** (`net10.0-windows`, sin WPF) | Hooks, `ChordTracker`, `SendInput`, DWM, DPI y monitores, ejecutores de acciones, almacenamiento en disco, autoarranque. |
| **Radial.App** (`net10.0-windows`, WPF) | Composición manual de dependencias, overlay, `RadialView`, ViewModels, ajustes, bandeja, instancia única. |
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
- **[Por validar en la fase 4]** que transparencia, click-through y no activación siguen funcionando juntas en la implementación definitiva.
- **No se usa** Acrylic, Mica ni ninguna técnica DWM (`DWMWA_SYSTEMBACKDROP_TYPE`, `SetWindowRgn` sobre material, `SetWindowCompositionAttribute`). **No hay blur.**

### 6.2 DPI (PerMonitorV2)

El manifiesto de `Radial.App` ya declara `PerMonitorV2` **[Comprobado]**: la ventana de prueba reporta 120 DPI al 125 %. No se usa `GetDpiForMonitor`.

Secuencia al mostrar: (1) `SetWindowPos` en píxeles físicos con tamaño mínimo y centrada en el cursor, para que quede en el monitor correcto; (2) `GetDpiForWindow`; (3) tamaño físico = lógico × dpi / 96 con `SetWindowPos`; (4) la ventana permanece invisible hasta tener posición y tamaño definitivos. Se atiende `WM_DPICHANGED` ignorando el rectángulo sugerido y recalculando con la regla anterior. Escala interna con `VisualTreeHelper.GetDpi`. Los límites de monitor se obtienen con `MonitorFromPoint` + `GetMonitorInfo`.

**[Por validar]:** que las coordenadas del hook lleguen en píxeles físicos con PerMonitorV2, y el cambio de monitor a mitad de interacción con DPI mixto.

### 6.3 Rendimiento

Redibujado solo desde `CompositionTarget.Rendering`. Referencia del spike (variante A, 125 %, 60 Hz, GPU integrada): aparición visible con mediana de 27.55 ms y P95 de 34.79 ms tras la primera, midiendo con un sondeo de píxel a 60 Hz (resolución ~16.7 ms). Objetivo propuesto, **sin validar en producción**: del chord al primer fotograma ≤ 100 ms. El consumo de CPU y los fotogramas por segundo del spike **no son representativos** (el overlay de prueba incluía un contador que forzaba el render continuo); el consumo real del overlay definitivo hay que medirlo en la fase 4.

### 6.4 Bordes de pantalla (E-1) — ABIERTO

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

## 7. Configuración y almacenamiento

```
%LOCALAPPDATA%\Radial\
├─ config.json
├─ backups\config-YYYYMMDD-HHmmss.json     (rotación, últimas N = 10)
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
3. Validación estructural: posiciones 0–7 sin duplicados, ids únicos, referencias de menú existentes, sin ciclos, profundidad máxima, longitudes, esquemas de URL permitidos (`http`, `https`, `mailto`), rutas bien formadas.
4. `ValidationReport` y resumen de acciones al usuario, con atención a ejecutables y atajos. Las `UnknownAction` se listan como no ejecutables.
5. Solo si no hay errores: copia de la configuración activa y aplicación. Nunca se ejecuta nada durante la validación.

## 8. Ejecución de acciones

`IActionExecutor` por tipo, resueltos por discriminador en un registro. Añadir un tipo es una clase más y un registro.

- **App, archivo, carpeta, URL:** `Process.Start` con `UseShellExecute=true`; nunca por `cmd`. Argumentos y directorio de trabajo.
- **Teclado:** `SendInput` con la política de la sección 3.4.
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
| **4** Overlay real + acciones | Overlay con la **variante A** (ventana layered de WPF, sección 6.1), `RadialView`, ejecutores (app, archivo, carpeta, URL, atajo diferido). | El foco no cambia; transparencia, click-through y no activación funcionan **a la vez**; se ejecuta una acción real; el atajo espera a soltar Ctrl y vence a los 5 s; DPI y monitores comprobados o documentados como pendientes. | Pendiente |
| **5** Submenús + configuración | Interfaz del dwell con progreso, serialización JSON con acciones desconocidas (7.1), `config.json` en `%LOCALAPPDATA%`, bandeja con "Salir". **→ MVP.** | Dos niveles estables; archivo corrupto no rompe la app; E-1 evaluado con los criterios de 6.4. | Pendiente |
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

Comprueba: raíz vacía o inexistente; identificadores vacíos, demasiado largos o duplicados (menús, y elementos en toda la configuración); nombres vacíos o de más de 64 caracteres; posiciones inválidas o duplicadas; iconos; acciones ausentes o con datos obligatorios vacíos; URLs que no sean absolutas `http`, `https` o `mailto`; referencias a submenús inexistentes; **ciclos** (incluso entre menús inalcanzables); profundidad por encima de `MaxDepth`; zona muerta, histéresis, tiempos (50 ms a 10 s) y profundidad (1 a 10) fuera de rango. Un menú inalcanzable desde la raíz es una **advertencia**, no un error.

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

- Los bordes de pantalla (E-1) siguen abiertos. El motor trabaja con el origen que le dan.
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

## 17. Fase 4, paso 1: contratos de ejecución de acciones y reloj de producción

**Estado: solo el paso 1 de la fase 4 está implementado. La fase 4 no está aprobada** y sus criterios de aceptación (sección 12) no han cambiado ni se han evaluado. Este paso no es visible para el usuario: define contratos y un reloj, sin ejecutar nada.

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

Ejecutores concretos (aplicación, archivo, carpeta, URL, atajo), `IProcessLauncher`, conversión de atajos a teclas, espera de modificadores, coordinador, canal de eventos para varios lectores, overlay, `RadialView`, DPI y monitores, configuración provisional, notificaciones y cambios de ciclo de vida. Son pasos posteriores de la fase 4. **Nada de la fase 3 se ha modificado** (hooks, `ChordTracker`, servicio de entrada, autoprueba).

### 17.4 Verificación de este paso

**Automática (421 pruebas en verde: 272 Core + 149 Windows; 20 nuevas, las 401 anteriores sin modificar; build sin advertencias):**
- `ActionExecutionResultTests`: éxito, fallo con código y mensaje, cancelación sin error, rechazo de código o mensaje vacíos, los tres resultados distinguibles, igualdad por valor, códigos estables.
- `ExecutionContractIndependenceTests`: Core no referencia ensamblados de procesos, red, Win32 ni WPF; la API pública de `Execution` solo expone tipos de Core o de la biblioteca base; la firma se integra con `RadialAction` y `CancellationToken`. Se comprobó que fallan si se introduce un `System.Diagnostics.Process` en la API (mutación hecha a mano y revertida).
- `MonotonicClockTests`: es un `IClock`, no retrocede (100 000 lecturas), avanza sin que nadie lo avance, mide el tiempo real en las unidades correctas (cota inferior estricta de 20 ms, superior muy holgada) y el motor funciona con él. Son las únicas pruebas de Core con tiempo real, y solo ~20 ms.
- **No hay prueba de `IActionExecutor` en sí**: es una interfaz sin implementación. Se probará con cada ejecutor concreto.

**Manual:** ninguna; este paso no cambia el comportamiento de la aplicación.
