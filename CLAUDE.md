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
- Sin coste: nada de APIs de pago, cloud, servidores, cuentas ni licencias comerciales. Todo local.

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
- `Radial.Windows` (`net10.0-windows`, sin WPF): hooks, SendInput, DWM, DPI, ejecutores, almacenamiento en disco. **Nunca `Radial.Infrastructure`.**
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

## DPI y overlay

- `PerMonitorV2` (ver `app.manifest`). No usar `GetDpiForMonitor`; usar `GetDpiForWindow`, `VisualTreeHelper.GetDpi` y gestionar `WM_DPICHANGED`.
- Overlay: no activable, transparente a clics, sin robar el foco.
- Blur: solo si el spike de la fase 2 lo avala. Si no es fiable, panel translúcido oscuro sin blur. La estabilidad prima sobre los efectos.

## Estética

Oscura, translúcida, minimalista, grafito con acento frío. Sin exceso de gradientes ni brillos.

## Comandos

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/Radial.App
```

## Estado

Ver la sección de fases del documento técnico. Fase 0 completada; esperar aprobación para la fase 1.
