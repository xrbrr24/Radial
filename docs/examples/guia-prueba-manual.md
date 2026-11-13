# Guía de prueba manual: texto, volumen, ventana activa y argumentos

Prueba guiada de las cuatro acciones nuevas con `config-acciones-nuevas.json`. Dura unos minutos y no usa nada fuera de lo que
ves en pantalla. Ninguna de las acciones se ha ejecutado todavía contra Windows real: esta guía es esa primera validación.

## Qué contiene el ejemplo

```
Radial (menú principal)
├─ Norte  → Texto de prueba (escribe tildes, ñ, un emoji y un salto de línea)
├─ Este   → Volumen   ├─ Norte: Subir 10 · Este: Fijar al 30 % · Sur: Bajar 10 · Oeste: Silencio
├─ Sur    → Ventana   ├─ Norte: Maximizar · Este: Restaurar · Sur: Minimizar · Oeste: Cerrar ventana
└─ Oeste  → Bloc de notas con el archivo «C:\Temp\prueba radial.txt» (el nombre lleva un espacio)
```

Cómo se usa el radial: **mantén Ctrl, pulsa el botón central, mueve hacia el sector y suelta el botón central** (después puedes soltar
Ctrl). Para entrar en un grupo (Volumen, Ventana), mantén el cursor sobre su sector unos 0,5 s: el submenú se abre en el cursor;
mueve al sector de la acción y suelta el botón central. Esc cancela. Soltar sin moverse no ejecuta nada.

## Preparación (una vez)

1. **Copia de seguridad.** Cierra Radial (bandeja → «Salir de Radial») y copia `%LOCALAPPDATA%\Radial\config.json` a un lugar seguro
   (por ejemplo, a `config.guardada.json` en el Escritorio).
2. **Archivo para el argumento con espacios.** Crea la carpeta `C:\Temp` si no existe y, dentro, un archivo de texto llamado
   exactamente `prueba radial.txt` (con un espacio) y con cualquier contenido, por ejemplo «hola». Guárdalo.
3. **Instala el ejemplo.** Copia `docs\examples\config-acciones-nuevas.json` sobre `%LOCALAPPDATA%\Radial\config.json`.
4. **Abre la compilación** (`artifacts\portable\win-x64\Radial.App.exe` o la descargada de CI). El título de la ventana muestra la
   fecha de compilación: comprueba que es la que esperas. En **Diagnóstico** se verá el resultado de cada acción
   («Última acción» y el registro).
5. Prueba con aplicaciones normales (Bloc de notas, el Explorador). No lo hagas sobre juegos, pantalla completa ni programas
   ejecutados como administrador (Windows descarta ahí el teclado y las órdenes de ventana: es un límite conocido, no un fallo).

## Pruebas (la secuencia más corta)

**1. Volumen** (no depende de la ventana activa). Abre el control de volumen de Windows o pon un sonido.
Este → Norte (sube 10 puntos), Este → Sur (baja 10), Este → Este (fija el 30 %), Este → Oeste (silencio; repítelo para quitarlo).
*Esperado:* el control de volumen de Windows refleja cada cambio. Subir o fijar con el sonido silenciado, además, lo reactiva.
Al terminar, deja el volumen como te guste.

**2. Texto Unicode.** Abre el Bloc de notas con un documento nuevo y vacío, haz clic dentro y lanza Norte.
*Esperado:* aparece exactamente
```
Prueba de Radial: ñandú, canción, ¿qué tal? ¡Sí! 😀
Segunda línea: añadir, pingüino, Ñ.
```
Si salen atajos (Ctrl+letra) en vez de texto, o faltan caracteres, anota qué ocurrió. **No lo lances sobre campos de contraseña ni
terminales**: el texto se escribe donde esté el foco.

**3. Argumento con espacios.** Oeste.
*Esperado:* se abre el Bloc de notas con «prueba radial.txt» (el título dice `prueba radial.txt`). **Fallo** si el Bloc de notas
pregunta si quiere crear «C:\Temp\prueba» o abre dos archivos: el argumento se habría partido por el espacio.

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
`ForegroundChanged`, `ModifiersNotReleased`, `InputInjectionFailed`…). Los textos no incluyen lo que escribiste ni nombres de ventanas.

## Al terminar

Cierra Radial (bandeja → «Salir de Radial») y devuelve tu `config.json` desde la copia de seguridad. Puedes borrar
`C:\Temp\prueba radial.txt`.
