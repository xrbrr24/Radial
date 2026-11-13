# Guía de prueba manual: texto, volumen, ventana activa y argumentos

Prueba guiada de las cuatro acciones nuevas con `config-acciones-nuevas.json`. Dura unos minutos y no usa nada fuera de lo que
ves en pantalla.

## Resultado de la primera ronda (2026-11-13) y qué cambió

| Acción | Primera ronda | Estado |
|---|---|---|
| Volumen (subir, bajar, fijar, silencio) | Funcionó | Sin cambios |
| Ventana: minimizar, maximizar, restaurar, cerrar | Funcionó | Sin cambios |
| Radial no actúa sobre sí mismo | Funcionó | Sin cambios |
| Insertar texto | **Falló**: letras repetidas, caracteres de reemplazo, resultado distinto en cada intento (también en el Bloc de notas) | Corregido en código; **pendiente de repetir con la compilación nueva** |
| Bloc de notas con argumento | **Falló**: «no encuentra la ruta» | Ejemplo y guía corregidos; **pendiente de repetir** |

Lo que se comprobó (y lo que no) al investigar:

- **Texto.** El registro de Radial de esa sesión muestra UNA ejecución por gesto (un «Confirmado» → una acción, `Succeeded`), y el
  código envía cada carácter una sola vez, así que no era una doble ejecución. La estructura de `SendInput` (carácter Unicode, pulsar
  y soltar, los dos pares del emoji) es la documentada. **La causa exacta no se ha podido reproducir ni demostrar sin el equipo**:
  el texto se enviaba de golpe (más de 200 eventos en una sola llamada) y algunas aplicaciones lo procesan de forma asíncrona. La
  compilación nueva envía el texto en lotes de 4 caracteres completos (el emoji nunca se parte), con una pausa breve entre lotes,
  y se detiene si cambia la ventana activa o se vuelve a pulsar Ctrl/Mayús/Alt/Win. Es una corrección razonada, **no una causa
  demostrada**: la prueba manual de abajo es la que lo confirma.
- **Argumento.** Radial lanzó `notepad.exe` con éxito (el registro dice `Succeeded`): el mensaje de «ruta» lo da el propio Bloc de
  notas. Ese mensaje coincide con el que da Windows cuando la carpeta del archivo no existe, y `C:\Temp` no existe en una instalación
  normal de Windows (ni en el equipo de desarrollo cuando se investigó); la guía anterior pedía crearla a mano y no lo comprobaba.
  **No está demostrado que ese fuera el estado en tu prueba**: lo confirma la comprobación nueva del paso 2. El valor de `arguments` llega a `ProcessStartInfo.ArgumentList` como
  UN único argumento (prueba automática nueva); el ejemplo ya no fija un directorio de trabajo y ahora incluye una **acción de
  control** con un archivo que existe en cualquier Windows (`C:\Windows\win.ini`), para separar «falla Radial» de «falta tu archivo».

## Qué contiene el ejemplo

```
Radial (menú principal)
├─ Norte  → Texto de prueba (escribe tildes, ñ, un emoji y un salto de línea)
├─ Este   → Volumen   ├─ Norte: Subir 10 · Este: Fijar al 30 % · Sur: Bajar 10 · Oeste: Silencio
├─ Sur    → Ventana   ├─ Norte: Maximizar · Este: Restaurar · Sur: Minimizar · Oeste: Cerrar ventana
└─ Oeste  → Bloc de notas ├─ Norte: win.ini (control, sin espacios) · Este: «C:\Temp\prueba radial.txt» (con espacio)
```

Cómo se usa el radial: **mantén Ctrl, pulsa el botón central, mueve hacia el sector y suelta el botón central** (después puedes soltar
Ctrl). Para entrar en un grupo (Volumen, Ventana, Bloc de notas), mantén el cursor sobre su sector unos 0,5 s: el submenú se abre en
el cursor; mueve al sector de la acción y suelta el botón central. Esc cancela. Soltar sin moverse no ejecuta nada.

## Preparación (una vez)

1. **Copia de seguridad.** Cierra Radial (bandeja → «Salir de Radial») y copia `%LOCALAPPDATA%\Radial\config.json` a un lugar seguro
   (por ejemplo, a `config.guardada.json` en el Escritorio).
2. **Archivo para el argumento con espacios.** Abre PowerShell (no hace falta como administrador) y pega estas tres líneas:
   ```powershell
   New-Item -ItemType Directory -Force -Path C:\Temp | Out-Null
   Set-Content -LiteralPath 'C:\Temp\prueba radial.txt' -Value 'hola'
   Get-ChildItem -LiteralPath C:\Temp
   ```
   **Comprueba** que la lista muestra exactamente `prueba radial.txt` (con el espacio y una sola extensión `.txt`). Si el
   Explorador oculta las extensiones, un archivo creado a mano como «prueba radial.txt» puede llamarse en realidad
   `prueba radial.txt.txt`: por eso se crea con el comando y se comprueba en la lista.
3. **Instala el ejemplo.** Copia `docs\examples\config-acciones-nuevas.json` sobre `%LOCALAPPDATA%\Radial\config.json`.
4. **Abre la compilación nueva** (la carpeta y el SHA-256 te los da el informe de la entrega). El título de la ventana muestra la
   fecha de compilación: comprueba que es la de la compilación nueva, y que **no queda otra instancia de Radial en la bandeja**
   (Radial aún no tiene instancia única: la vieja seguiría con su código y sus hooks). En **Diagnóstico** se verá el resultado de
   cada acción («Última acción» y el registro).
5. Prueba con aplicaciones normales (Bloc de notas, el Explorador). No lo hagas sobre juegos, pantalla completa ni programas
   ejecutados como administrador (Windows descarta ahí el teclado y las órdenes de ventana: es un límite conocido, no un fallo).

## Pruebas (la secuencia más corta)

**1. Volumen** (no depende de la ventana activa). Abre el control de volumen de Windows o pon un sonido.
Este → Norte (sube 10 puntos), Este → Sur (baja 10), Este → Este (fija el 30 %), Este → Oeste (silencio; repítelo para quitarlo).
*Esperado:* el control de volumen de Windows refleja cada cambio. Subir o fijar con el sonido silenciado, además, lo reactiva.
Al terminar, deja el volumen como te guste.

**2. Texto Unicode (la que falló).** Abre el Bloc de notas con un documento nuevo y vacío, haz clic dentro y lanza Norte.
Radial espera a que sueltes Ctrl y escribe en lotes pequeños: tarda un instante (unos 0,3 s), no es instantáneo.
*Esperado:* aparece exactamente
```
Prueba de Radial: ñandú, canción, ¿qué tal? ¡Sí! 😀
Segunda línea: añadir, pingüino, Ñ.
```
Repítelo 3 o 4 veces (borra el texto entre una y otra con Ctrl+A y Supr): antes el resultado cambiaba en cada intento. Si algo
sale mal, **anota qué apareció, en qué aplicación y si ocurre siempre igual**, y repite en otra aplicación (WordPad o un campo de
texto del navegador) para saber si depende del Bloc de notas. En Diagnóstico debe constar UNA acción por gesto. **No lo lances
sobre campos de contraseña ni terminales**: el texto se escribe donde esté el foco. Si cambias de ventana mientras escribe, se
detiene y Diagnóstico lo dice (`ForegroundChanged`); el resto del texto no se escribe en la otra ventana.

**3. Argumentos (la que falló).** Dos pasos:

- **3a. Control.** Oeste → Norte. *Esperado:* se abre el Bloc de notas con `win.ini`. Esto prueba que Radial lanza el programa y
  le pasa un argumento, sin depender de ningún archivo tuyo. Si falla, el problema no es tu archivo: copia la línea de Diagnóstico.
- **3b. Con espacio.** Oeste → Este. *Esperado:* se abre el Bloc de notas con «prueba radial.txt» (el título dice `prueba radial.txt`).

*Si 3b falla y 3a funciona*, mira qué dice el Bloc de notas (Diagnóstico solo dirá «aceptada por Windows»: significa que Windows
arrancó el programa, no que encontrara el archivo):

| Qué ves | Qué significa |
|---|---|
| «El sistema no puede encontrar la ruta especificada» | La carpeta `C:\Temp` no existe: repite el paso 2 de la preparación. |
| «No se encuentra el archivo… ¿desea crearlo?» con el nombre `prueba radial.txt` | La carpeta existe pero el archivo no (o se llama distinto, p. ej. `prueba radial.txt.txt`): repite el paso 2 y comprueba la lista. |
| Se abre o se ofrece crear un archivo llamado `prueba` (o dos pestañas: `C:\Temp\prueba` y `radial.txt`) | **Fallo de Radial**: el argumento se partió por el espacio. Anótalo tal cual. |
| Diagnóstico: `TargetNotFound` | Windows no encontró `notepad.exe` (el programa en sí). |

**4. Ventana activa.** Con esa ventana del Bloc de notas en primer plano:
Sur → Norte (se maximiza) · Sur → Este (vuelve a su tamaño normal) · Sur → Sur (se minimiza; recupérala desde la barra de tareas).
*Restaurar* solo actúa sobre una ventana maximizada (una minimizada no puede tener el foco); por eso se prueba justo después de maximizar.
Si lo lanzas sobre una ventana que ya está en su estado, Radial lo dice en Diagnóstico («ya está maximizada», etc.) y no hace nada.

**5. Radial nunca actúa sobre sí mismo.** Haz clic en la ventana de Radial para ponerla en primer plano y lanza Sur → Sur.
*Esperado:* la ventana de Radial NO se minimiza y Diagnóstico dice que la ventana activa es de Radial.

**6. Cerrar ventana (al final).** **Guarda antes el contenido** de la ventana que vayas a cerrar (Ctrl+S en el Bloc de notas). Sur → Oeste.
*Esperado:* la ventana se cierra con un cierre normal. Si hubiera cambios sin guardar, la aplicación preguntará si guardar: es lo
correcto, Radial no mata procesos.

## Si algo falla

Abre **Diagnóstico** y copia las últimas líneas («Última acción» y el registro): cada fallo lleva un código estable
(`AudioUnavailable`, `AudioControlFailed`, `WindowNotAvailable`, `WindowOperationNotSupported`, `WindowOperationFailed`,
`ForegroundChanged`, `ModifiersNotReleased`, `InputInjectionFailed`, `TargetNotFound`…). Los textos no incluyen lo que escribiste
ni nombres de ventanas. El registro en disco (`%LOCALAPPDATA%\Radial\logs\`) guarda el identificador de la acción y el código de
los fallos, nunca el texto.

## Al terminar

Cierra Radial (bandeja → «Salir de Radial») y devuelve tu `config.json` desde la copia de seguridad. Puedes borrar
`C:\Temp\prueba radial.txt` (y `C:\Temp` si la creaste solo para esto).
