# Resultados automáticos — 2026-11-02 12:29

Microsoft Windows 10.0.26300 · .NET 10.0.14 · Intel(R) Graphics (integrada) · 1 monitor(es), 1920x1200 px (primario) · 120 DPI · overlay 300 DIP

## A — WPF layered window
- Notas: WPF AllowsTransparency=true (WS_EX_LAYERED lo añade WPF). Sin blur real.
- Estilos extendidos: 0x08080088; tamaño real: 375x375 px a 120 DPI (pedido 300 DIP => 375 px)
- Foco: antes=receptor, después=receptor, sin cambios=True; elegible para Alt+Tab (heurística)=False
- Aspecto: nitidez 1 (referencia sin overlay 1), rango de contraste 31, luminancia media 32.03 => nítido (el fondo NO está desenfocado); esquina transparente=True
- Click-through:
  - None: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=nítido (el fondo NO está desenfocado) (nitidez 1); foco=receptor; estilos 0x08080088 
  - ExTransparent: receptor clics=1, movs=48; overlay clics=0; **pasa=True**; blur=nítido (el fondo NO está desenfocado) (nitidez 1); foco=receptor; estilos 0x080800A8 
  - ExLayeredTransparent: receptor clics=1, movs=90; overlay clics=0; **pasa=True**; blur=nítido (el fondo NO está desenfocado) (nitidez 1); foco=receptor; estilos 0x080800A8 
  - HitTestTransparent: receptor clics=0, movs=0; overlay clics=0; **pasa=False**; blur=nítido (el fondo NO está desenfocado) (nitidez 1); foco=receptor; estilos 0x08080088 
- Modo elegido para rendimiento: ExTransparent
- Aparición en frío: Show()=145.14 ms, visible en pantalla=163.92 ms
- 30 aperturas: visible mediana=27.55 ms, p95=34.79 ms; llamada Show() mediana=5.81 ms, p95=11.89 ms; primer render WPF mediana=6.54 ms; ocultar mediana=45.47 ms, p95=65.95 ms; muestras perdidas=0
- CPU (de un núcleo): oculto=0.52%, visible=20.31%; memoria privada 145.28 -> 141.69 MB; working set visible 216.05 MB
- Movimiento: 168 fotogramas, intervalo mediana=16.67 ms, p95=33.33 ms, máx=33.33 ms, >25 ms=10

## B — DWM system backdrop
- Notas: DWMWA_SYSTEMBACKDROP_TYPE=3 (Acrylic) -> HRESULT 0x0; ExtendFrame=0x0; DarkMode=0x0; Corner=0x0; BorderNone=0x0
- Estilos extendidos: 0x08000088; tamaño real: 375x375 px a 120 DPI (pedido 300 DIP => 375 px)
- Foco: antes=receptor, después=receptor, sin cambios=True; elegible para Alt+Tab (heurística)=False
- Aspecto: nitidez 0 (referencia sin overlay 1), rango de contraste 0, luminancia media 71 => uniforme (no se distingue el fondo); esquina transparente=False
- Click-through:
  - None: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x08000088 
  - ExTransparent: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x080000A8 
  - ExLayeredTransparent: receptor clics=0, movs=0; overlay clics=0; **pasa=False**; blur=nítido (el fondo NO está desenfocado) (nitidez 0.65); foco=; estilos 0x080000A8 WindowFromPoint devolvió una ventana ajena (pid 33208); no se inyectó el clic.
  - HitTestTransparent: receptor clics=0, movs=0; overlay clics=0; **pasa=False**; blur=nítido (el fondo NO está desenfocado) (nitidez 0.65); foco=; estilos 0x08000088 WindowFromPoint devolvió una ventana ajena (pid 33208); no se inyectó el clic.
- Modo elegido para rendimiento: None
- Aparición en frío: Show()=32.06 ms, visible en pantalla=57.91 ms
- 30 aperturas: visible mediana=29.92 ms, p95=795.52 ms; llamada Show() mediana=6.43 ms, p95=13.6 ms; primer render WPF mediana=6.65 ms; ocultar mediana=32.72 ms, p95=49.7 ms; muestras perdidas=12
- CPU (de un núcleo): oculto=0%, visible=9.37%; memoria privada 122.07 -> 122.07 MB; working set visible 199.61 MB
- Movimiento: 163 fotogramas, intervalo mediana=16.67 ms, p95=33.33 ms, máx=33.33 ms, >25 ms=15

## C — DWM backdrop + SetWindowRgn
- Notas: DWMWA_SYSTEMBACKDROP_TYPE=3 (Acrylic) -> HRESULT 0x0; ExtendFrame=0x0; DarkMode=0x0; Corner=0x0; BorderNone=0x0
- Estilos extendidos: 0x08000088; tamaño real: 375x375 px a 120 DPI (pedido 300 DIP => 375 px)
- Foco: antes=receptor, después=receptor, sin cambios=True; elegible para Alt+Tab (heurística)=False
- Aspecto: nitidez 0 (referencia sin overlay 1), rango de contraste 0, luminancia media 71 => uniforme (no se distingue el fondo); esquina transparente=False
- Click-through:
  - None: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x08000088 
  - ExTransparent: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x080000A8 
  - ExLayeredTransparent: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x080000A8 
  - HitTestTransparent: receptor clics=0, movs=0; overlay clics=0; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x08000088 
- Modo elegido para rendimiento: None
- Aparición en frío: Show()=46.21 ms, visible en pantalla=80.34 ms
- 30 aperturas: visible mediana=43.83 ms, p95=69.91 ms; llamada Show() mediana=11.38 ms, p95=17.39 ms; primer render WPF mediana=11.99 ms; ocultar mediana=46.09 ms, p95=50.27 ms; muestras perdidas=0
- CPU (de un núcleo): oculto=0.52%, visible=21.87%; memoria privada 121.24 -> 121.16 MB; working set visible 199.06 MB
- Movimiento: 168 fotogramas, intervalo mediana=16.67 ms, p95=33.33 ms, máx=33.33 ms, >25 ms=10

## D — SetWindowCompositionAttribute
- Notas: SetWindowCompositionAttribute(WCA_ACCENT_POLICY, EnableAcrylicBlurBehind) -> 1 (0 = fallo). API NO documentada.
- Estilos extendidos: 0x08000088; tamaño real: 375x375 px a 120 DPI (pedido 300 DIP => 375 px)
- Foco: antes=receptor, después=receptor, sin cambios=True; elegible para Alt+Tab (heurística)=False
- Aspecto: nitidez 0 (referencia sin overlay 1), rango de contraste 0, luminancia media 21 => uniforme (no se distingue el fondo); esquina transparente=False
- Click-through:
  - None: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x08000088 
  - ExTransparent: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x080000A8 
  - ExLayeredTransparent: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x080000A8 
  - HitTestTransparent: receptor clics=0, movs=0; overlay clics=0; **pasa=False**; blur=uniforme (no se distingue el fondo) (nitidez 0); foco=receptor; estilos 0x08000088 
- Modo elegido para rendimiento: None
- Aparición en frío: Show()=43.57 ms, visible en pantalla=61.81 ms
- 30 aperturas: visible mediana=39.7 ms, p95=56.69 ms; llamada Show() mediana=10.08 ms, p95=15.71 ms; primer render WPF mediana=10.85 ms; ocultar mediana=43.62 ms, p95=55.47 ms; muestras perdidas=0
- CPU (de un núcleo): oculto=0%, visible=19.79%; memoria privada 125.69 -> 125.62 MB; working set visible 203.96 MB
- Movimiento: 172 fotogramas, intervalo mediana=16.67 ms, p95=16.67 ms, máx=33.33 ms, >25 ms=6

## E — Captura + BlurEffect
- Notas: Captura GDI (BitBlt) + System.Windows.Media.Effects.BlurEffect. Blur de una FOTO estática, no en vivo.
- Estilos extendidos: 0x08080088; tamaño real: 375x375 px a 120 DPI (pedido 300 DIP => 375 px)
- Foco: antes=receptor, después=receptor, sin cambios=True; elegible para Alt+Tab (heurística)=False
- Aspecto: nitidez 0.05 (referencia sin overlay 1), rango de contraste 129, luminancia media 77.38 => desenfocado; esquina transparente=True
- Click-through:
  - None: receptor clics=0, movs=0; overlay clics=1; **pasa=False**; blur=desenfocado (nitidez 0.05); foco=receptor; estilos 0x08080088 
  - ExTransparent: receptor clics=1, movs=1; overlay clics=0; **pasa=True**; blur=desenfocado (nitidez 0.05); foco=receptor; estilos 0x080800A8 
  - ExLayeredTransparent: receptor clics=1, movs=1; overlay clics=0; **pasa=True**; blur=desenfocado (nitidez 0.05); foco=receptor; estilos 0x080800A8 
  - HitTestTransparent: receptor clics=0, movs=0; overlay clics=0; **pasa=False**; blur=desenfocado (nitidez 0.05); foco=receptor; estilos 0x08080088 
- Modo elegido para rendimiento: ExTransparent
- Aparición en frío: Show()=144.4 ms, visible en pantalla=174.15 ms
- 30 aperturas: visible mediana=68.9 ms, p95=87.2 ms; llamada Show() mediana=49.54 ms, p95=63.71 ms; primer render WPF mediana=50.66 ms; ocultar mediana=37.38 ms, p95=66.09 ms; muestras perdidas=0
- CPU (de un núcleo): oculto=0%, visible=27.07%; memoria privada 177.74 -> 174.78 MB; working set visible 259.95 MB
- Movimiento: 158 fotogramas, intervalo mediana=16.67 ms, p95=33.33 ms, máx=33.33 ms, >25 ms=20
- Captura: mediana=16.35 ms, p95=19.64 ms, bitmap 2.41 MB
