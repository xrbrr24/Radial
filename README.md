# Radial

Launcher personalizable basado en menús radiales para Windows 11.

Mantén **Ctrl**, pulsa el **botón central** del ratón, mueve el cursor hacia un sector y suelta el botón para ejecutar una acción. Esc o soltar Ctrl antes de confirmar cancela.

> **Estado:** en desarrollo. Fases 0 a 3 completadas (esqueleto, núcleo lógico, investigación del overlay con cobertura parcial —[ADR-001](docs/adr/ADR-001-overlay-rendering.md)— y captura global de teclado y ratón). **La fase 4 (overlay real y acciones) está implementada y pendiente de aprobación**: la validación manual básica la hizo el usuario en un equipo, y las pruebas automáticas se validan en GitHub Actions (qué está confirmado y qué no, en [docs/technical-design.md](docs/technical-design.md), 15.5 y 17.11). Aún no hay configuración editable, bandeja ni instalador.

## Qué hace hoy la aplicación

Al ejecutar `Radial.App` se abre una **ventana de diagnóstico** (no es el panel de ajustes definitivo). Con el botón «Iniciar captura» se instalan los hooks globales y el gesto funciona sobre cualquier aplicación:

- **Overlay radial** de ocho sectores centrado en el cursor, oscuro y translúcido, que no roba el foco, deja pasar el ratón y se oculta al confirmar o cancelar. Sin desenfoque (ADR-001).
- **Ocho acciones fijas y provisionales** (el menú aún no se carga de ningún archivo):

  | Acción | Qué hace |
  |---|---|
  | Copiar, Pegar, Deshacer, Seleccionar todo, Guardar | Envían el atajo (Ctrl+C, V, Z, A, S) a la ventana que tiene el foco, esperando a que se suelten los modificadores (5 s por defecto) |
  | Carpeta | Abre la carpeta Documentos en el Explorador |
  | Web | Abre `https://www.wikipedia.org` en el navegador predeterminado |
  | Navegador | Lanza Microsoft Edge |

- Los fallos de una acción (destino inexistente, atajo vencido, ventana que cambió...) se registran en la ventana de diagnóstico; todavía no hay notificaciones.
- «Aceptada por Windows» no significa «procesada por la aplicación»: hacia una aplicación ejecutada como administrador, Windows puede descartar el teclado sin avisar.

## Qué existe en el motor y todavía no está en la aplicación

El núcleo (`Radial.Core`) ya implementa y prueba la **navegación entre submenús** (entrada por permanencia, recentrado, vuelta atrás por el centro, progreso), la validación de configuraciones y los tipos de acción (aplicación, archivo, carpeta, URL, atajo, abrir menú). **Nada de eso está conectado todavía al overlay ni a la interfaz** —**los submenús no están disponibles en la aplicación**—, no hay carga de `config.json` y los ejecutores de archivo y de aplicaciones con argumentos no se usan en el menú provisional. Si el motor llegara a cambiar de menú, el overlay cancela el gesto sin ejecutar nada (protección temporal, 17.13 del documento técnico). La integración, la configuración persistente y el editor son las fases 5 y 6.

## Requisitos

- Windows 11 x64, versiones estables 25H2 o posteriores.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.403 o compatible) para compilar.

## Compilar y probar

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/Radial.App
```

En el equipo de desarrollo del proyecto, Smart App Control bloquea la carga de los ensamblados de pruebas recién compilados, así que allí solo se compila el producto (`dotnet build src/Radial.App/Radial.App.csproj -c Release`) y las pruebas las ejecuta [GitHub Actions](.github/workflows/radial-windows-validation.yml) en cada push y pull request. Detalles en [docs/technical-design.md](docs/technical-design.md), 15 y 17.8.

## Estructura

| Ruta | Contenido |
|---|---|
| `src/Radial.Core` | Lógica pura, independiente de Windows y WPF. |
| `src/Radial.Windows` | Integración con Windows: hooks, SendInput, DPI, ejecución de acciones y coordinación del overlay (almacenamiento en disco: fase 5). |
| `src/Radial.App` | Aplicación WPF: ventana de diagnóstico y overlay. |
| `tests/` | Pruebas unitarias (xUnit). |
| `docs/` | Documentación técnica. |

Detalles en [docs/technical-design.md](docs/technical-design.md).

## Principios

Todo es local: sin cuentas, sin servidores, sin servicios de pago. La configuración vivirá en `%LOCALAPPDATA%\Radial\` (todavía no se escribe nada allí).
