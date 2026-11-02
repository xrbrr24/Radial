# Radial — Documento técnico v0.3

Estado: **Fase 0 completada.** Pendiente de revisión; la fase 1 no ha comenzado.

Convención: **[Comprobado]** = verificado con documentación oficial o en este equipo. **[Por validar]** = propuesta que debe confirmarse con un prototipo o prueba.

## 0. Historial de cambios

| Versión | Cambios |
|---|---|
| v0.2 | Ctrl primero obligatorio, geometría normalizada, DPI con `GetDpiForWindow`, hooks con eventos críticos en orden, almacenamiento en `%LOCALAPPDATA%`, spike de blur, fases 0 a 8. |
| **v0.3** | Compatibilidad oficial: **Windows 11 x64 estable 25H2+**, sin Windows 10. Corrección: la build 26300 es Windows 11 **26H2**, disponible en el canal de disponibilidad general desde el 29-sep-2026, no una build Insider. E-1 (bordes) vuelve a **abierto** con alternativas documentadas. Timeout de atajos 5 s configurable. Primera escritura por creación y siguientes por reemplazo atómico. Estrategia explícita para acciones de tipo desconocido. Eliminada la resincronización con `GetAsyncKeyState` en callbacks. Resultados de la fase 0. |

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

Fuera de la solución: `spikes/Radial.BlurSpike` (fase 2, desechable).

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
- **Estado de Ctrl obsoleto:** si se pierde un evento de liberación de Ctrl, el tracker puede creer que sigue pulsado. Como no se consulta `GetAsyncKeyState` dentro del callback, la defensa es el **watchdog**: un temporizador fuera de los callbacks (~50 ms) que, mientras el chord está activo, contrasta con `GetAsyncKeyState` y cancela si el estado físico no coincide. Esto protege contra estados atascados, no contra la pérdida del hook.
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
r = θ - 360·floor(θ/360)            → [0, 360)      (sin usar %)
if (r >= 360) r = 0                 → caso -1e-20 + 360
sector = floor((r + 22.5) / 45) mod 8     (0 = Norte, 1 = NE … 7 = NO)
```

`%` no se usa para normalizar porque en C# conserva el signo del dividendo. Límites en intervalos `[inicio, fin)` en sentido horario. Zona muerta: `distancia < deadZone`, y el vector nulo también lo es. Se rechazan NaN e infinitos. La histéresis angular solo se aplica cuando ya hay un sector seleccionado.

**Pruebas obligatorias:** las 8 direcciones exactas; para cada uno de los 8 límites (22.5° + 45°·k) un punto a ±0.01°, que debe caer en sectores contiguos distintos (con píxeles enteros un límite exacto es inalcanzable, así que no se afirma qué sector gana en él); normalización de `-0.0`, `-1e-20`, `±180`, `360`, `720` y ángulos grandes; barrido 0°–359.9° cada 0.1° contra tabla de referencia; zona muerta (dentro, fuera, nulo); histéresis sin parpadeo.

## 6. Overlay, DPI y monitores

### 6.1 Ventana

- Se crea una vez y se reutiliza (mostrar y ocultar). Una misma ventana sirve a todos los niveles de submenú.
- `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`, `Topmost`, `SW_SHOWNOACTIVATE`. Comprobación de foco con `GetForegroundWindow` antes y después. **[Por validar]** en el spike.

### 6.2 DPI (PerMonitorV2)

El manifiesto de `Radial.App` ya declara `PerMonitorV2` **[Comprobado]**: la ventana de prueba reporta 120 DPI al 125 %. No se usa `GetDpiForMonitor`.

Secuencia al mostrar: (1) `SetWindowPos` en píxeles físicos con tamaño mínimo y centrada en el cursor, para que quede en el monitor correcto; (2) `GetDpiForWindow`; (3) tamaño físico = lógico × dpi / 96 con `SetWindowPos`; (4) la ventana permanece invisible hasta tener posición y tamaño definitivos. Se atiende `WM_DPICHANGED` ignorando el rectángulo sugerido y recalculando con la regla anterior. Escala interna con `VisualTreeHelper.GetDpi`. Los límites de monitor se obtienen con `MonitorFromPoint` + `GetMonitorInfo`.

**[Por validar]:** que las coordenadas del hook lleguen en píxeles físicos con PerMonitorV2, y el cambio de monitor a mitad de interacción con DPI mixto.

### 6.3 Rendimiento

Redibujado solo desde `CompositionTarget.Rendering`. Objetivos propuestos a validar en el spike: del chord al primer fotograma ≤ 100 ms, y consumo nulo con el overlay oculto.

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
- **Manual, con checklist por fase:** hooks reales, foco, blur, DPI y monitores.
- **Guardas de arquitectura:** ya existen en la fase 0 (sección 12). Con ensamblados todavía vacíos son una red de seguridad para el futuro y solo se vuelven realmente discriminantes cuando haya código.

## 10. Spike de desenfoque (fase 2)

Prototipo desechable en `spikes/Radial.BlurSpike`, fuera de `Radial.sln`. Sin sistema visual complejo. Se conservan las conclusiones en un ADR (`docs/adr/`).

| Variante | Técnica |
|---|---|
| A | `AllowsTransparency` + pincel translúcido (base y alternativa garantizada). |
| B | Sin `AllowsTransparency`, `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`. Documentada, Windows 11 22H2+. |
| C | B + `SetWindowRgn` circular, para comprobar el borde. |
| D | `SetWindowCompositionAttribute` (no documentada), solo para conocer sus limitaciones. No se adoptaría. |
| E | Captura de la pantalla bajo el cursor + `BlurEffect` (falso blur). |

Se mide: transparencia, si el blur es real y en vivo o falso, ventana no activable (el foreground no cambia), click-through real, comportamiento en estado inactivo, latencia al mostrar, consumo con la ventana oculta, coste por fotograma al 100 % y 200 %, bordes y DPI mixto.

**Regla de decisión:** el blur entra solo si es estable, no roba el foco y funciona en Windows 11 estable y en 26H2. Si no, el panel translúcido oscuro sin blur es la solución definitiva. La estabilidad prima sobre los efectos.

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
| **1** Núcleo | Geometría, motor, máquina de estados con submenús y retroceso, modelo, validación, serialización con acciones desconocidas. | Pruebas de las secciones 4, 5 y 7 en verde. | Pendiente |
| **2** Spike de ventana | Variantes A–E y ADR. | Tabla de resultados completa y decisión registrada. | Pendiente |
| **3** Captura global | Hilo de hooks, `ChordTracker`, watchdog, diagnóstico. | Ctrl→botón activa; botón→Ctrl no activa y el clic llega a la aplicación; la rueda no activa; Esc y soltar Ctrl cancelan; sin retraso perceptible del ratón. | Pendiente |
| **4** Overlay real + acciones | Ventana no activable, `RadialView`, ejecutores (app, archivo, carpeta, URL, atajo diferido). | El foco no cambia; se ejecuta una acción real; el atajo espera a soltar Ctrl y vence a los 5 s. | Pendiente |
| **5** Submenús + configuración | Dwell con progreso, recentrado, retroceso armado, `config.json` en `%LOCALAPPDATA%`, bandeja con "Salir". **→ MVP.** | Dos niveles estables; archivo corrupto no rompe la app; E-1 evaluado con los criterios de 6.4. | Pendiente |
| **6** Ajustes y editor | Ventana de ajustes, editor del radial con vista previa. | El ejemplo completo del enunciado se configura solo desde la interfaz. | Pendiente |
| **7** Apariencia y sistema | Temas, colores, blur según el ADR, autoarranque, instancia única. | Funciona con y sin blur. | Pendiente |
| **8** Importar/exportar y entrega | Importar, exportar, copias, robustez multimonitor y DPI, README, empaquetado. | Checklist manual completa, con DPI mixto y **en Windows 11 estable**. | Pendiente |

### Criterios de aceptación del MVP

1. Ctrl + clic central abre el radial de 8 sectores.
2. Botón central sin Ctrl previo **no** abre nada y el clic llega a la aplicación de debajo.
3. Soltar el botón ejecuta; Esc cancela; soltar Ctrl antes de confirmar cancela.
4. El foco de la aplicación activa no cambia y el radial no interfiere con el movimiento del ratón.
5. Funcionan `.exe`, carpeta, URL y atajo diferido (con timeout de 5 s configurable).
6. Un submenú abre por permanencia con indicador de progreso, se recentra y se puede volver por el centro armado.
7. La configuración se carga desde `%LOCALAPPDATA%\Radial\config.json` con validación.
8. El estado del hook es visible en el diagnóstico y no se promete recuperación automática.
9. Panel translúcido oscuro; con blur solo si el spike lo avala.
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
