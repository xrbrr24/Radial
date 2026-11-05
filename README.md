# Radial

Launcher personalizable basado en menús radiales para Windows 11.

Mantén **Ctrl**, pulsa el **botón central** del ratón, mueve el cursor hacia un sector y suelta el botón para ejecutar una acción. Un sector puede ser un **grupo**: permanece sobre él y se abre su submenú, recentrado donde está el cursor; sal del centro y vuelve a él para retroceder. Esc o soltar Ctrl antes de confirmar cancela.

> **Estado:** en desarrollo. Fases 0 a 3 completadas. **Fase 4 (overlay y acciones): implementada y validada automáticamente en CI; su validación manual completa sigue pendiente y NO está aprobada.** **Fase 5 (submenús, configuración persistente, bandeja): implementada; sus pruebas están pendientes de confirmación en CI y su validación manual no se ha hecho.** El detalle de qué está confirmado y qué no está en [docs/technical-design.md](docs/technical-design.md) (15.5, 17.11 y sección 18). Todavía no hay editor visual, ajustes, temas, autoarranque, importar/exportar ni instalador.

## Qué hace hoy la aplicación

Al ejecutar `Radial.App` se carga la configuración, se instala la captura global y aparece el icono de **bandeja del sistema** (menú: «Mostrar ventana de diagnóstico» y «Salir de Radial»). Se abre también una **ventana de diagnóstico** (no es el panel de ajustes definitivo); cerrarla con la X la oculta y Radial sigue en la bandeja. Salir de verdad: «Salir de Radial» en el icono.

- **Overlay radial** de ocho sectores centrado en el cursor, oscuro y translúcido, que no roba el foco, deja pasar el ratón y se oculta al confirmar o cancelar. Sin desenfoque (ADR-001).
- **Submenús y grupos, conectados al overlay:** los grupos se distinguen por una banda de acento y un chevrón; el arco por fuera del sector muestra el progreso de la permanencia; al completarse, el menú cambia dentro de la misma ventana y se recentra en el cursor; en un submenú el centro muestra una flecha de volver, que se «arma» al salir del centro y cuyo anillo de progreso se completa al regresar. Se pueden anidar (3 niveles por defecto). Las permanencias y la profundidad máxima son las de la configuración.
- **Configuración persistente en JSON:** `%LOCALAPPDATA%\Radial\config.json`. En el primer inicio se genera la predeterminada (las ocho acciones de antes: Copiar, Pegar, Deshacer, Seleccionar todo y Guardar en el menú principal, y Carpeta, Web y Navegador dentro del grupo «Abrir», al oeste). A partir de ahí manda el archivo: se puede editar a mano (ver abajo).
- **Tipos de acción:** aplicación, archivo, carpeta, URL (`http`/`https`), atajo de teclado y abrir menú (grupo). Una acción de un tipo que esta versión no conoce se **conserva** en el archivo con todos sus datos, se avisa y **nunca se ejecuta**.
- **Robustez:** un `config.json` corrupto no impide arrancar (se pone en cuarentena y se recupera una copia válida o la predeterminada); uno de una versión futura no se toca ni se sobrescribe; se guardan copias de seguridad (las 10 últimas) y se escribe de forma atómica.
- Los fallos de una acción se registran en la ventana de diagnóstico; todavía no hay notificaciones.
- «Aceptada por Windows» no significa «procesada por la aplicación»: hacia una aplicación ejecutada como administrador, Windows puede descartar el teclado sin avisar.

### Estructura de la carpeta de datos

```
%LOCALAPPDATA%\Radial\
├─ config.json          configuración activa
├─ backups\             config-aaaaMMdd-HHmmss-fff.json (las 10 últimas)
├─ icons\               (reservada: caché de iconos, fase 7)
├─ logs\                (reservada)
└─ config.corrupt-*.json   archivos dañados puestos en cuarentena (los 10 últimos)
```

### Editar la configuración a mano

El formato es JSON con `schemaVersion`. Ejemplo mínimo con un grupo:

```json
{
  "schemaVersion": 1,
  "rootMenuId": "main",
  "geometry": { "deadZoneRadius": 40, "hysteresisDegrees": 6 },
  "navigation": { "submenuDwellMs": 600, "backDwellMs": 800, "maxDepth": 3 },
  "menus": [
    { "id": "main", "name": "Principal", "items": [
      { "id": "main.copy", "name": "Copiar", "position": "North",
        "icon": { "source": "glyph", "value": "Copy" }, "definitionId": "edit.copy",
        "action": { "type": "sendKeys", "modifiers": ["Control"], "key": "C" } },
      { "id": "main.more", "name": "Más", "position": "West",
        "icon": { "source": "glyph", "value": "Apps" },
        "action": { "type": "openMenu", "targetMenuId": "more" } }
    ] },
    { "id": "more", "name": "Más", "items": [
      { "id": "more.web", "name": "Web", "position": "North",
        "icon": { "source": "glyph", "value": "Link" },
        "action": { "type": "openUrl", "url": "https://www.wikipedia.org" } }
    ] }
  ]
}
```

Posiciones: `North`, `NorthEast`, `East`, `SouthEast`, `South`, `SouthWest`, `West`, `NorthWest`. Tipos de acción: `launchApplication` (`path`, `arguments`, `workingDirectory`), `openFile` (`path`), `openFolder` (`path`), `openUrl` (`url`), `sendKeys` (`modifiers`, `key`) y `openMenu` (`targetMenuId`). Si el archivo no es válido, Radial lo pone en cuarentena y arranca con una copia válida o con la predeterminada (el motivo aparece en la ventana de diagnóstico). El editor visual llegará en la fase 6.

## Qué falta (fases 6 a 8)

- **Fase 6:** ventana de ajustes y editor visual del radial con biblioteca de acciones.
- **Fase 7:** apariencia y temas, autoarranque con Windows, instancia única.
- **Fase 8:** importar y exportar, robustez con varios monitores y DPI mixto, empaquetado, evaluación de la firma de código y validación en Windows 11 estable.

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
| `src/Radial.Core` | Lógica pura, independiente de Windows y WPF: geometría, motor, modelo, validación, JSON de la configuración, migraciones. |
| `src/Radial.Windows` | Integración con Windows: hooks, SendInput, DPI, ejecución de acciones, coordinación del overlay y almacén de la configuración en disco. |
| `src/Radial.App` | Aplicación WPF: ventana de diagnóstico, overlay y bandeja. |
| `tests/` | Pruebas unitarias y de integración (xUnit). |
| `docs/` | Documentación técnica. |

Detalles en [docs/technical-design.md](docs/technical-design.md).

## Principios

Todo es local: sin cuentas, sin servidores, sin servicios de pago. La configuración vive en `%LOCALAPPDATA%\Radial\`.
