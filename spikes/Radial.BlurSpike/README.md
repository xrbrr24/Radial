# Radial.BlurSpike — prototipo de la fase 2

**Estado: investigación cerrada con cobertura parcial.** Prototipo desechable: no forma parte de `Radial.sln`, no referencia a `Radial.Core`, `Radial.Windows` ni `Radial.App`, y no usa paquetes NuGet. De aquí solo se conservan las conclusiones; la decisión está en [ADR-001](../../docs/adr/ADR-001-overlay-rendering.md).

> **El prototipo no puede ejecutarse hoy en el equipo de desarrollo.** Smart App Control bloqueó la DLL compilada tras la primera ejecución válida (ver sección 9). Todos los datos de este documento proceden de esa única ejecución, `results/run1-results.md` y `results/run1-results.json`. No se ha repetido ningún experimento.

## 1. Objetivo

Averiguar qué técnica de ventana permite un overlay circular y translúcido, siempre encima, que no robe el foco, deje pasar el ratón y rinda bien en Windows 11. Se probaron cinco técnicas sin asumir que ninguna funcionaba.

Orden de prioridad de la decisión: fiabilidad, click-through, ausencia de cambio de foco, rendimiento, calidad visual, compatibilidad.

## 2. Arquitectura del prototipo

```
Radial.BlurSpike.exe
├─ Harness/    ControlWindow (selector de variante, Show/Hide, tamaño, opacidad, blur, click-through)
│              ReceiverWindow + ReceiverClient (receptor en OTRO PROCESO: --receiver)
├─ Overlay/    OverlayWindowBase + WindowA … WindowE, VariantCatalog
├─ Diagnostics/ AutoRunner (batería), Probes (latencia, nitidez), Report (modelo de resultados)
└─ Native/     Win32 (APIs documentadas), ScreenGrabber, ExperimentalComposition (solo variante D)
```

- **Receptor en otro proceso.** `HTTRANSPARENT` solo atraviesa ventanas del mismo hilo; un receptor en el mismo proceso habría dado falsos positivos. El receptor muestra franjas blancas y negras y publica sus contadores de clics y movimientos en el título de su ventana.
- **Clic real.** El clic se inyecta con `SendInput` en el centro del overlay. Antes de inyectarlo se comprueba con `WindowFromPoint` que el punto pertenece a una ventana del prototipo; si no, se omite.
- **Foco.** Se registra `GetForegroundWindow` antes y después de mostrar el overlay, antes de cualquier clic.
- **Latencia.** Un hilo sondea `GetPixel` del DC de pantalla sobre un punto de color de acento en el centro del overlay hasta que aparece (o desaparece) en la imagen compuesta. La resolución práctica la limita el refresco de 60 Hz (~16.7 ms).
- **Blur.** Heurística propia: relación entre el mayor escalón de luminancia entre píxeles contiguos y el contraste total de una fila de franjas. Un escalón nítido da ≈ 1 aunque haya un tinte; un desenfoque da valores bajos; un contraste menor de 8 se clasifica como "uniforme".
- **Modos de click-through probados en cada variante:** `None`, `ExTransparent` (solo `WS_EX_TRANSPARENT`), `ExLayeredTransparent` (`WS_EX_LAYERED | WS_EX_TRANSPARENT`) y `HitTestTransparent` (`WM_NCHITTEST` → `HTTRANSPARENT`).
- Estilos comunes a todas las variantes: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `Topmost`, `ShowActivated=false`, y `WM_MOUSEACTIVATE → MA_NOACTIVATE`.
- El overlay se coloca con `SetWindowPos` en **píxeles físicos**; el DPI se lee con `GetDpiForWindow` tras situar la ventana en el monitor de destino.

## 3. Variantes

| Variante | Técnica | Qué es |
|---|---|---|
| **A** | `AllowsTransparency=true`, elipse translúcida oscura, `DropShadowEffect` | Ventana layered de WPF. **Sin blur**: el escritorio se ve nítido, solo oscurecido. |
| **B** | Sin `AllowsTransparency`; `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE = 3)` (`DWMSBT_TRANSIENTWINDOW`), `DwmExtendFrameIntoClientArea(-1)`, modo oscuro, borde nulo | Material Acrylic del sistema sobre una ventana no layered, rectangular. |
| **C** | B + `SetWindowRgn(CreateEllipticRgn)` recalculada tras cada colocación | Intento de forma circular sobre el material de B. |
| **D** | `SetWindowCompositionAttribute(WCA_ACCENT_POLICY, ACCENT_ENABLE_ACRYLICBLURBEHIND)` + región elíptica | **API no documentada.** Aislada en `Native/ExperimentalComposition.cs`. Experimental. |
| **E** | `BitBlt` de la pantalla (antes de mostrar) + `BlurEffect` de WPF recortado a una elipse | **Blur de una foto estática**, no en vivo. La captura vive solo en memoria y se descarta al ocultar. |

Se diferencia explícitamente entre blur en vivo, blur de una captura estática y fondo meramente translúcido: **ninguna variante produjo blur en vivo**; **E** produjo blur estático; **A** es solo translúcida.

## 4. Resultados (run1, 2026-11-02)

Entorno: Windows 11 26H2 (10.0.26300), .NET 10.0.14, Intel(R) Graphics integrada, **1 monitor de 1920×1200 al 125 % (120 DPI), 60 Hz**, tema oscuro con transparencia activada. Overlay de 300 DIP = 375 px.

### 4.1 Resumen

| Variante | Blur | Circular | Foco intacto | Click-through | Aparición visible: mediana / P95 |
|---|---|---|---|---|---|
| A | No (translúcido) | Sí | Sí | **Sí** (`ExTransparent`, `ExLayeredTransparent`) | 27.55 / 34.79 ms |
| B | No (gris uniforme) | No | Sí | **No demostrado** | 29.92 / 795.52 ms (12 muestras perdidas: **no comparable**) |
| C | No (gris uniforme) | No | Sí | No | 43.83 / 69.91 ms |
| D | No (rectángulo oscuro) | No | Sí | No | 39.70 / 56.69 ms |
| E | Estático (de una captura) | Sí | Sí | **Sí** (`ExTransparent`, `ExLayeredTransparent`) | 68.90 / 87.20 ms |

"No" en C y D significa que ninguno de los cuatro modos entregó el clic al receptor; pero en B, C y D la combinación `WS_EX_LAYERED | WS_EX_TRANSPARENT` no llegó a quedar aplicada (sección 4.2), así que **no se descarta que esas variantes pudieran dejar pasar el ratón con otra configuración**. Para B queda "no demostrado".

"Foco intacto": en las cinco variantes el foreground era el receptor antes y después de mostrar el overlay. "Circular": según el análisis de la esquina de la ventana (transparente en A y E, no en B, C ni D) y la inspección de las capturas.

### 4.2 Click-through por modo (clic real inyectado)

Cada celda: clics recibidos por el receptor / clics recibidos por el overlay.

| Variante | `None` | `ExTransparent` | `ExLayeredTransparent` | `HitTestTransparent` |
|---|---|---|---|---|
| A | 0 / 1 | **1 / 0** | **1 / 0** | 0 / 0 |
| B | 0 / 1 | 0 / 1 | omitido* | omitido* |
| C | 0 / 1 | 0 / 1 | 0 / 1 | 0 / 0 |
| D | 0 / 1 | 0 / 1 | 0 / 1 | 0 / 0 |
| E | 0 / 1 | **1 / 0** | **1 / 0** | 0 / 0 |

\* Omitido: `WindowFromPoint` devolvió una ventana ajena (pid 33208) en el punto central y no se inyectó el clic. **No se identificó esa ventana.**

- En el modo `None` el overlay absorbió el clic y **el foreground siguió siendo el receptor**: hacer clic sobre el overlay no lo activó (en las cinco variantes).
- En `HitTestTransparent` el clic no llegó ni al receptor ni al overlay; **no se observó su destino**.
- Estilos extendidos observados: A y E `0x08080088` (sin click-through) y `0x080800A8` (con `WS_EX_TRANSPARENT`); B, C y D `0x08000088` y `0x080000A8`. Es decir: `TOPMOST | LAYERED | TOOLWINDOW | NOACTIVATE | TRANSPARENT` en las variantes que funcionaron.
- B, C y D **no mostraron `WS_EX_LAYERED` en ningún modo**: con `ExLayeredTransparent` el estilo registrado fue `0x080000A8`, que no incluye `WS_EX_LAYERED` (`0x00080000`) aunque el prototipo intentaba añadirlo. No se investigó por qué; por tanto, **la combinación layered + transparente no se llegó a probar de verdad en B, C y D**.

### 4.3 Aspecto

| Variante | Nitidez (1 = sin desenfoque) | Rango de contraste | Luminancia media | Esquina transparente | Observación visual de la captura |
|---|---|---|---|---|---|
| Referencia sin overlay | 1 | — | — | — | Franjas blancas y negras |
| A | 1 | 31 | 32.03 | Sí | Disco oscuro translúcido, franjas visibles atenuadas, borde circular suave, sombra |
| B | 0 | 0 | 71 | No | Rectángulo de esquinas redondeadas, gris plano, con sombra |
| C | 0 | 0 | 71 | No | Igual que B; se ve además el disco del tinte dentro del rectángulo |
| D | 0 | 0 | 21 | No | Rectángulo oscuro plano |
| E | 0.05 | 129 | 77.38 | Sí | Disco con franjas desenfocadas, borde circular suave |

La "observación visual" es la inspección de las capturas guardadas durante la ejecución (carpeta de capturas fuera del repositorio). Las capturas son de diagnóstico del harness; las variantes no escriben nada en disco.

### 4.4 Latencia y fluidez

| Variante | Primera aparición en frío (visible) | Llamada `Show()` mediana | Primer render WPF mediana | Ocultar mediana / P95 | Fotogramas en movimiento >25 ms |
|---|---|---|---|---|---|
| A | 163.92 ms | 5.81 ms | 6.54 ms | 45.47 / 65.95 ms | 10 de 168 |
| B | 57.91 ms | 6.43 ms | 6.65 ms | 32.72 / 49.70 ms | 15 de 163 |
| C | 80.34 ms | 11.38 ms | 11.99 ms | 46.09 / 50.27 ms | 10 de 168 |
| D | 61.81 ms | 10.08 ms | 10.85 ms | 43.62 / 55.47 ms | 6 de 172 |
| E | 174.15 ms | 49.54 ms | 50.66 ms | 37.38 / 66.09 ms | 20 de 158 |

- 30 aperturas por variante tras la primera en frío. Percentil 95 por rango más próximo.
- **A: la primera aparición cuesta 164 ms frente a una mediana de 27.6 ms en las siguientes.** La ventana debe crearse al arrancar la aplicación.
- **E:** la captura de la región tarda 16.35 ms de mediana (P95 19.64 ms) y el bitmap ocupa 2.41 MB. Esa captura está incluida en su `Show()`.
- Intervalo de fotogramas durante el movimiento: mediana 16.67 ms en todas; P95 33.33 ms salvo D (16.67 ms).

### 4.5 CPU y memoria — NO representativos

| Variante | CPU oculto | CPU visible | Memoria privada oculto → visible | Working set visible |
|---|---|---|---|---|
| A | 0.52 % | 20.31 % | 145.28 → 141.69 MB | 216.05 MB |
| B | 0 % | 9.37 % | 122.07 → 122.07 MB | 199.61 MB |
| C | 0.52 % | 21.87 % | 121.24 → 121.16 MB | 199.06 MB |
| D | 0 % | 19.79 % | 125.69 → 125.62 MB | 203.96 MB |
| E | 0 % | 27.07 % | 177.74 → 174.78 MB | 259.95 MB |

**No presentar estos valores como el consumo del overlay definitivo.** El overlay de prueba contiene un contador de fotogramas que obliga a renderizar de forma continua, la CPU es la del proceso del harness (porcentaje de un núcleo) y la memoria es la del proceso completo, no la del overlay. Tampoco los valores de fotogramas por segundo son una medición definitiva del rendimiento de producción.

## 5. Tipo de cada medida

| Medida | Tipo |
|---|---|
| Latencia de aparición y ocultación | Automatizada (sondeo de píxel a 60 Hz; incluye cuantización de fotograma) |
| Foco, click-through, estilos, tamaño real | Automatizada (APIs de Windows y clics inyectados) |
| Nitidez / "blur" | Automatizada, con una heurística propia |
| Captura de E (tiempo, tamaño) | Automatizada |
| CPU, memoria, intervalos de fotograma | Automatizada, no representativa (sección 4.5) |
| Alt+Tab | **Heurística** sobre estilos de ventana (no se observó la lista real); resultado: no elegible en las cinco |
| Aspecto visual, calidad de los bordes | **Observación visual** de capturas |
| Estimaciones | Ninguna |

## 6. Anomalías observadas

| Variante | Hecho observado |
|---|---|
| **B** | Fondo gris uniforme (#464646 en la captura), sin blur apreciable. Click-through no demostrado: dos modos sin paso de clic y dos omitidos. Doce muestras de latencia perdidas (aparición u ocultación; no se registró cuál) y P95 de 795.52 ms: **no es representativo**. |
| **C** | La ventana siguió viéndose rectangular con su sombra. La ejecución pidió una región circular, pero **no se registró el resultado de `SetWindowRgn`**. |
| **D** | Rectángulo oscuro plano, sin blur apreciable y sin la forma circular que se pedía. Ninguno de los cuatro modos entregó el clic al receptor. |
| **B, C, D** | `ExLayeredTransparent` dejó los estilos en `0x080000A8` (sin `WS_EX_LAYERED`). |

## 7. Hipótesis no confirmadas

Ninguna de estas se ha comprobado; no deben citarse como conclusiones.

- El gris plano de B y D podría deberse al estado de la ventana (inactiva) o a su tipo de ventana (un popup sin marco). No se probó.
- La falta de recorte en C y D podría deberse a varias causas. No se registró el resultado de la llamada a `SetWindowRgn`, así que no se sabe si falló, si se aplicó solo a una parte del dibujado o si tuvo otro efecto. Dato observado: en la captura de C se ve dentro del rectángulo un disco más oscuro con la forma de la elipse pedida; no se determinó qué parte del dibujado responde a la región.
- El estilo `WS_EX_LAYERED` no quedó aplicado en B, C ni D con `ExLayeredTransparent` (sección 4.2). No se investigó la causa.
- Los 12 fallos de B y el pid 33208 podrían ser una ventana ajena puntual o un efecto de la propia variante. No se identificó.
- La diferencia de click-through entre A/E (layered) y B/C/D (no layered) coincide con la documentación de `WS_EX_LAYERED`, pero **no se verificó la causa**.

## 8. Limitaciones de las pruebas

- **Un monitor al 125 % (120 DPI).** No se probaron 100, 150 ni 200 %, ni monitores con escalados distintos, ni el cambio de monitor, ni la forma circular o las regiones tras un cambio de DPI, ni `WM_DPICHANGED` real. El prototipo calcula el tamaño físico a partir de `GetDpiForWindow`, pero ese código solo se ejercitó a 120 DPI.
- Una sola GPU (Intel integrada), un solo monitor de 60 Hz, una sola build (26H2 26300). **No se probó en Windows 11 estable 25H2.**
- Una sola ejecución completa. Treinta aperturas por variante dan mediana y P95, pero sin repetir la batería.
- Resolución temporal de la latencia ~16.7 ms.
- El receptor fue una ventana WPF propia: no se probó con aplicaciones reales, juegos, pantalla completa exclusiva ni ventanas elevadas.
- La heurística de nitidez es propia y no está contrastada con otra herramienta.

## 9. Interrupción por Smart App Control

- Smart App Control está en **modo de aplicación** en el equipo de desarrollo.
- Tras la primera ejecución válida, `Radial.BlurSpike.dll` fue bloqueada con el error `0x800711C7` ("An Application Control policy has blocked this file"). Los eventos de Code Integrity indican que no cumplía el nivel de firma exigido.
- El primer intento sí funcionó; los siguientes fueron bloqueados, incluido un reintento sin cambios.
- **No se modificó ninguna configuración de seguridad** ni se buscó un método alternativo de ejecución. El responsable del proyecto decidió expresamente no hacerlo.
- Consecuencia: no se ejecutaron los experimentos de la sección 10, y la fase 2 se cierra con los datos de `run1`.

Detalles y decisiones en `docs/technical-design.md`, sección 15.

## 10. Código añadido después de run1: compilado, nunca ejecutado

Los resultados de este documento proceden de una compilación **anterior** a los siguientes cambios. Compilan en Release sin avisos, pero **no están validados** y no hay resultados de ellos:

| Cambio | Dónde |
|---|---|
| Variante **B2** (B con `WindowChrome`) | `Overlay/WindowB.cs` (parámetro `useWindowChrome`), `Overlay/VariantCatalog.cs` |
| Experimento de activación del overlay (`ActivationExperimentAsync`) y sus campos en el informe | `Diagnostics/AutoRunner.cs`, `Diagnostics/Report.cs`, `Overlay/OverlayWindowBase.cs` (`AllowActivation`) |
| Registro del resultado de `SetWindowRgn` (`RegionLog`, `GetWindowRgnBox`) | `Overlay/OverlayWindowBase.cs`, `Native/Win32.cs` |
| Desglose de muestras perdidas (`ShowMisses`, `HideMisses`) y título de la ventana ajena en clics omitidos | `Diagnostics/AutoRunner.cs`, `Diagnostics/Report.cs` |
| `--only` y registro a archivo (`run.log`) | `App.xaml.cs`, `Harness/ControlWindow.xaml.cs` |

Los comentarios `NO VALIDADO` en el código marcan estos puntos. No se han eliminado.

## 11. Decisión técnica definitiva

**El overlay de producción será la variante A: ventana layered de WPF** (`AllowsTransparency=true`), con fondo oscuro translúcido y forma circular, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_TRANSPARENT` cuando corresponda, `Topmost` y `SW_SHOWNOACTIVATE` (en WPF, `ShowActivated=false`).

Es la opción más sencilla de las dos que cumplieron los requisitos principales (forma circular, transparencia, click-through real, foco intacto) y la de mejor latencia entre ambas. **E** (captura + `BlurEffect`) queda archivada como posible experimento futuro y no se adopta. **B, C y D no se adoptan.**

**El desenfoque no es requisito del MVP.** Ningún DWM, Acrylic ni Mica entra en la arquitectura de producción. Ver [ADR-001](../../docs/adr/ADR-001-overlay-rendering.md).

La implementación definitiva debe volver a comprobar que transparencia, click-through y ausencia de activación funcionan **a la vez**, y debe validar DPI distintos del 125 % y varios monitores, que este prototipo no cubrió.
