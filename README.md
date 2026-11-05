# Radial

Launcher personalizable basado en menús radiales para Windows 11.

Mantén **Ctrl**, pulsa el **botón central** del ratón, mueve el cursor hacia un sector y suelta el botón para ejecutar una acción. Un sector puede ser un **grupo**: permanece sobre él y se abre su submenú, recentrado donde está el cursor; sal del centro y vuelve a él para retroceder. Esc o soltar Ctrl antes de confirmar cancela.

> **Estado:** en desarrollo. Fases 0 a 3 completadas. **Fase 4 (overlay y acciones): implementada y validada automáticamente en CI; su validación manual completa sigue pendiente y NO está aprobada.** **Fase 5 (submenús, configuración persistente, bandeja): implementada; pruebas automáticas en verde en CI; su validación manual no se ha hecho.** **Fase 6 (biblioteca de acciones y editor radial): implementada; compilación y pruebas automáticas en verde en CI (commit `e7c7671`, ejecución nº 13); sin validar a mano.** El detalle de qué está confirmado y qué no está en [docs/technical-design.md](docs/technical-design.md) (15.5, 17.11 y secciones 18 y 19). Todavía no hay temas, autoarranque, importar/exportar ni instalador.

## Qué hace hoy la aplicación

Al ejecutar `Radial.App` se carga la configuración, se instala la captura global y aparece el icono de **bandeja del sistema** (menú: «Mostrar Radial» y «Salir de Radial»). Se abre el **centro de control**, con cuatro secciones: **Editor radial**, **Biblioteca de acciones**, **Ajustes** y **Diagnóstico**. Cerrarlo con la X lo oculta y Radial sigue en la bandeja; salir de verdad: «Salir de Radial» en el icono (si hay cambios sin guardar, pregunta antes). El radial usa lo último que se **guarda** (botón Guardar o Ctrl+S).

### Centro de control (fase 6)

- **Biblioteca de acciones:** las acciones existen por sí mismas, independientes de dónde se coloquen. Cada una tiene nombre, descripción, categoría (Edición, Navegador, Aplicaciones, Archivos, Sistema, Multimedia y las tuyas), etiquetas, icono y su acción (atajo de teclado, aplicación con argumentos y carpeta de trabajo, archivo, carpeta o dirección web `http`/`https`). Se pueden crear, editar, duplicar y eliminar (avisa si está colocada), guardar sin colocar y reutilizar en varios menús o varias veces. Hay unas 50 de fábrica. La búsqueda es propia y no bloquea: por nombre, descripción, categoría, etiquetas, destino o combinación (`ctrl s`), con filtros por tipo, categoría y si están colocadas.
- **Atajos por captura:** «Grabar» recoge la combinación solo mientras lo activas; también se puede elegir a mano (para lo que Windows se queda, como Win+E). Se guarda la tecla física (no el carácter) y se muestra con el nombre de tu teclado (la Ñ del español, por ejemplo). Las combinaciones que no se pueden enviar se rechazan con una explicación.
- **Editor radial:** a la izquierda la jerarquía de menús y grupos; en el centro la vista previa, que dibuja el mismo radial que verás en pantalla; a la derecha el inspector del sector elegido. Se puede añadir una acción desde la biblioteca (doble clic o arrastrando), mover y duplicar sectores, quitar una colocación sin borrar la acción, convertir entre acción y grupo, crear, renombrar, mover y eliminar grupos (avisa del contenido) y moverse por la jerarquía. El editor no deja ids duplicados, ciclos, grupos huérfanos, referencias rotas, más profundidad que la máxima ni dos acciones en un sector.
- **Ajustes:** tiempo para entrar en un grupo, tiempo para volver atrás y profundidad máxima.
- **Cambios:** el estado de guardado se ve arriba (todo guardado, cambios sin guardar, no se pudo guardar, solo lectura); un guardado fallido no pierde nada y se puede reintentar; solo se escribe si algo cambió. Búsqueda global de comandos y navegación con **Ctrl+K**.

- **Overlay radial** de ocho sectores centrado en el cursor, oscuro y translúcido, que no roba el foco, deja pasar el ratón y se oculta al confirmar o cancelar. Sin desenfoque (ADR-001).
- **Submenús y grupos, conectados al overlay:** los grupos se distinguen por una banda de acento y un chevrón; el arco por fuera del sector muestra el progreso de la permanencia; al completarse, el menú cambia dentro de la misma ventana y se recentra en el cursor; en un submenú el centro muestra una flecha de volver, que se «arma» al salir del centro y cuyo anillo de progreso se completa al regresar. Se pueden anidar (3 niveles por defecto). Las permanencias y la profundidad máxima son las de la configuración.
- **Configuración persistente en JSON:** `%LOCALAPPDATA%\Radial\config.json`. En el primer inicio se genera la predeterminada (las ocho acciones de antes: Copiar, Pegar, Deshacer, Seleccionar todo y Guardar en el menú principal, y Carpeta, Web y Navegador dentro del grupo «Abrir», al oeste). A partir de ahí manda el archivo: se puede editar a mano (ver abajo).
- **Tipos de acción:** aplicación, archivo, carpeta, URL (`http`/`https`), atajo de teclado y abrir menú (grupo). Una acción de un tipo que esta versión no conoce se **conserva** en el archivo con todos sus datos, se avisa y **nunca se ejecuta**.
- **Robustez:** un `config.json` corrupto no impide arrancar (se pone en cuarentena y se recupera una copia válida o la predeterminada); uno de una versión futura no se toca ni se sobrescribe; se guardan copias de seguridad (las 10 últimas) y se escribe de forma atómica.
- Los fallos de una acción se avisan dentro del centro de control (si está visible) y se registran en Diagnóstico; todavía no hay notificaciones del sistema.
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

El formato es JSON con `schemaVersion` (hoy 2; un archivo de la versión 1 se migra solo, sin perder nada, y al guardar queda una copia en `backups\`). La biblioteca va en `actions` y los elementos de los menús la referencian con `actionId`. Ejemplo mínimo con un grupo:

```json
{
  "schemaVersion": 2,
  "rootMenuId": "main",
  "geometry": { "deadZoneRadius": 40, "hysteresisDegrees": 6 },
  "navigation": { "submenuDwellMs": 600, "backDwellMs": 800, "maxDepth": 3 },
  "actions": [
    { "id": "edit.copy", "name": "Copiar", "category": "editing", "builtIn": true,
      "icon": { "source": "glyph", "value": "Copy" },
      "action": { "type": "sendKeys", "modifiers": ["Control"], "key": "C" } },
    { "id": "web.wikipedia", "name": "Wikipedia", "category": "browser",
      "icon": { "source": "glyph", "value": "Link" },
      "action": { "type": "openUrl", "url": "https://www.wikipedia.org" } }
  ],
  "menus": [
    { "id": "main", "name": "Principal", "items": [
      { "id": "main.copy", "position": "North", "actionId": "edit.copy" },
      { "id": "main.more", "name": "Más", "position": "West",
        "icon": { "source": "glyph", "value": "Apps" },
        "action": { "type": "openMenu", "targetMenuId": "more" } }
    ] },
    { "id": "more", "name": "Más", "items": [
      { "id": "more.web", "position": "North", "actionId": "web.wikipedia" }
    ] }
  ]
}
```

Posiciones: `North`, `NorthEast`, `East`, `SouthEast`, `South`, `SouthWest`, `West`, `NorthWest`. Tipos de acción: `launchApplication` (`path`, `arguments`, `workingDirectory`), `openFile` (`path`), `openFolder` (`path`), `openUrl` (`url`), `sendKeys` (`modifiers`, `key`) y `openMenu` (`targetMenuId`). Si el archivo no es válido, Radial lo pone en cuarentena y arranca con una copia válida o con la predeterminada (el motivo aparece en la ventana de diagnóstico). Lo normal es usar el editor y la biblioteca del centro de control; editar el archivo a mano sigue siendo posible.

## Qué falta (fases 7 y 8)

- **Fase 7:** apariencia y temas (los colores y medidas ya están centralizados en `src/Radial.App/Theme`), autoarranque con Windows, instancia única.
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
| `src/Radial.Core` | Lógica pura, independiente de Windows y WPF: geometría, motor, modelo, biblioteca de acciones, edición, búsqueda, validación, JSON de la configuración, migraciones. |
| `src/Radial.Windows` | Integración con Windows (hooks, SendInput, DPI, ejecución de acciones, coordinación del overlay, almacén en disco, nombres de teclas) y los modelos de vista del centro de control (`Presentation`, sin WPF). |
| `src/Radial.App` | Aplicación WPF: centro de control (vistas, tema, controles), overlay, bandeja y composición. `Radial.App.exe --render-ui <carpeta>` dibuja la interfaz en PNG (herramienta de desarrollo, sin hooks ni entrada). |
| `tests/` | Pruebas unitarias y de integración (xUnit). |
| `docs/` | Documentación técnica. |

Detalles en [docs/technical-design.md](docs/technical-design.md).

## Principios

Todo es local: sin cuentas, sin servidores, sin servicios de pago. La configuración vive en `%LOCALAPPDATA%\Radial\`.
