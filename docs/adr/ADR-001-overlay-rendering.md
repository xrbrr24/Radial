# ADR-001: Selection of the radial overlay rendering strategy

- **Estado:** Accepted
- **Fecha:** 2026-11-02
- **Fase:** 2 (spike técnico). Datos: `spikes/Radial.BlurSpike/results/run1-results.md` y `.json`.

## Contexto

El overlay de Radial debe ser circular, translúcido y oscuro, estar siempre encima mientras se usa, no robar el foco, dejar pasar el ratón y rendir bien. Se quiso comprobar si Windows 11 permite además un desenfoque real del fondo, sin asumir que ninguna técnica funcionaba.

Se construyó un prototipo con cinco variantes (A a E) y se midió foco, click-through con clics reales inyectados contra un receptor en otro proceso, aspecto y latencia.

**Cobertura de la investigación: parcial.** Una sola ejecución válida, en un monitor de 1920×1200 al 125 % (120 DPI), una GPU integrada Intel, 60 Hz y Windows 11 26H2 (build 26300). Después, Smart App Control bloqueó el prototipo; se decidió no desactivarlo ni eludirlo y cerrar la fase con los datos disponibles. Los experimentos pendientes no se ejecutaron.

## Decisión

**El overlay de producción será una ventana layered de WPF** (variante A):

- `AllowsTransparency=true`, `WindowStyle=None`, fondo transparente.
- Disco oscuro translúcido con borde suave. Sin blur.
- `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, y `WS_EX_TRANSPARENT` para el click-through cuando corresponda.
- `Topmost` y visualización sin activar (`ShowActivated=false`, equivalente a `SW_SHOWNOACTIVATE`).
- Se crea una vez al arrancar y se muestra y oculta después.

No se incorporan Acrylic, Mica ni ninguna otra técnica DWM a la arquitectura de producción. **El desenfoque no es requisito del MVP.**

La implementación definitiva debe comprobar que transparencia, click-through y no activación siguen funcionando juntas.

## Motivos

Observados en `run1`:

| Criterio | A | E |
|---|---|---|
| Forma circular con borde suave | Sí | Sí |
| Transparencia | Sí | Sí |
| Click-through real (`WS_EX_TRANSPARENT`) | Sí: el clic llegó al receptor y no al overlay | Sí |
| Foreground sin cambios al mostrar | Sí | Sí |
| El clic sobre el overlay no lo activa | Sí | Sí |
| Aparición visible, mediana / P95 | **27.55 / 34.79 ms** | 68.90 / 87.20 ms |
| Blur | No | Estático (de una foto) |

A y E fueron las únicas variantes que cumplieron a la vez forma circular, click-through real y foco intacto. De las dos, A tiene mejor latencia y es sustancialmente más simple: no captura la pantalla ni usa efectos de píxel.

## Alternativas descartadas

| Alternativa | Por qué no se adopta |
|---|---|
| **B. DWM Acrylic** (`DWMWA_SYSTEMBACKDROP_TYPE`) | Se observó un relleno gris uniforme, sin blur apreciable, rectangular, y el click-through no se demostró. La latencia P95 (795.52 ms con 12 muestras perdidas) no es representativa. Las causas **no se determinaron**. |
| **C. DWM + `SetWindowRgn`** | La ventana siguió viéndose rectangular y no se consiguió click-through en los modos probados. No se registró el resultado de `SetWindowRgn`; la causa **no se determinó**. |
| **D. `SetWindowCompositionAttribute`** | Rectángulo oscuro sin blur apreciable ni forma circular, y ningún modo entregó el clic. La causa **no se determinó**. Además es una API **no documentada** como interfaz pública estable: puede cambiar o desaparecer sin aviso. No se usará en producción. |
| **E. Captura de pantalla + `BlurEffect`** | Funcionó, pero es el blur de **una foto estática** tomada antes de mostrar, no del fondo en vivo. Es la variante más lenta (68.90 ms de mediana al mostrar), añade una captura de la pantalla en memoria (bitmap de 2.41 MB y ~16 ms por captura) y complica el comportamiento al mover o cuando el fondo cambia. Queda **archivada como posible experimento futuro**; no se implementa ni se convierte en dependencia. |

B, C y D no se adoptan por no cumplir los requisitos con lo observado, no porque se haya demostrado que sean imposibles: las causas de sus resultados siguen sin explicar.

## Consecuencias

**Positivas**
- Arquitectura simple, basada en WPF y APIs documentadas, sin dependencias nuevas.
- Click-through y no activación demostrados en este equipo.

**Negativas y riesgos**
- **Sin desenfoque.** La estética premium dependerá de un buen diseño del panel translúcido (contraste, borde, sombra), no del material del sistema.
- **Aparición en frío lenta:** la primera aparición de A tardó 163.92 ms frente a 27.55 ms de mediana después. Hay que crear la ventana al arrancar y dejarla preparada.
- **Cobertura incompleta, pendiente de validar:**
  - DPI al 100, 150 y 200 %, y monitores con escalados distintos.
  - Varios monitores y cambio de monitor a mitad de interacción.
  - `WM_DPICHANGED` real y la forma circular tras un cambio de DPI.
  - Windows 11 estable 25H2 (solo se probó la 26H2).
  - Aplicaciones reales, pantalla completa exclusiva y ventanas elevadas.
- **Ventana layered de WPF:** su coste de renderizado a tamaños y DPI mayores no se ha medido. Las cifras de CPU y fotogramas del spike incluyen un contador de fotogramas que fuerza el render continuo y **no son representativas**.
- **Smart App Control** puede bloquear compilaciones sin firmar. Es un riesgo de distribución, tratado en `docs/technical-design.md` sección 15.
- Alt+Tab se evaluó con una heurística sobre estilos de ventana, no observando la lista real.

## Revisión

Reabrir esta decisión si en la fase 4 o posteriores se detecta que la combinación de transparencia, click-through y no activación falla en otro DPI, otro monitor o en Windows 11 estable. El desenfoque podría reconsiderarse como mejora futura con un nuevo estudio; no hay que partir de las causas de B, C y D, que siguen desconocidas.
