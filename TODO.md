# Tareas pendientes

Lista de mejoras por orden de prioridad.

## Hecho

- [x] **1. Que el PC no se duerma a mitad de la grabación**
  Mientras hay una grabación en curso se le pide a Windows que no suspenda el sistema
  (la pantalla puede apagarse igualmente). Para las grabaciones programadas se arma un
  temporizador que despierta el equipo dos minutos antes del margen de arranque. Se
  puede desactivar en Ajustes. Si el plan de energía no permite los temporizadores de
  reactivación, la app lo avisa en la barra de estado. Ver `PowerManager.cs`.

- [x] **2. Margen al final de la grabación**
  Ajuste nuevo `TailMinutes`, 10 minutos por defecto. Se suma a la hora de fin al
  calcular la duración que recibe ffmpeg, así que la duración que escribe el usuario
  sigue siendo la del partido. Al programar, la barra de estado dice hasta qué hora se
  grabará de verdad. Ver `RecordingScheduler.EndWithTail`.

- [x] **3. Aviso al terminar o al fallar**
  El planificador lanza un evento `Finished` al llegar a un estado final y la ventana
  principal muestra una notificación junto al reloj. No avisa cuando la detiene el
  usuario. Al pulsar el aviso se abre la carpeta con el archivo seleccionado. Se puede
  desactivar en Ajustes.

- [x] **4. Comprobar el espacio en disco**
  Tres comprobaciones en `DiskSpace.cs`: aviso al programar si la estimación supera el
  espacio libre, negativa a empezar por debajo de 1 GB, y parada ordenada si durante la
  grabación el disco baja de 512 MB. La estimación supone 6 Mbps y cuenta el doble
  cuando se convierte a MP4, porque el .ts y el .mp4 conviven unos segundos.
  Pendiente: las dos últimas no se han podido probar llenando un disco de verdad.

- [x] **5. Logotipos de los canales**
  `LogoCache.cs` los descarga solo cuando la fila aparece (la lista está virtualizada),
  los guarda en `%APPDATA%\IptvRecorder\logos` y recuerda los que fallan para no
  reintentarlos: alrededor del 40% de las direcciones del proveedor dan 404. Casi todos
  los logotipos son blancos sobre fondo transparente, así que van sobre una pastilla
  oscura; sin logotipo la pastilla es invisible y los nombres siguen alineados.

- [x] **Iconos en los botones**
  Con la tipografía `Segoe MDL2 Assets` que trae Windows, mediante la propiedad adjunta
  `Icon.Glyph` y una plantilla compartida en `App.xaml`. Sin dependencias ni archivos
  de imagen.

## Siguientes

- [ ] **6. Favoritos**
  Marcar los canales que se usan de verdad y poder filtrar solo por ellos.

- [ ] **7. Formato de fecha según el idioma**
  Con la interfaz en español las fechas salen en formato estadounidense, porque el
  idioma de la app no cambia la cultura de formato. Hay que fijarla en `Loc.Apply`
  comprobando que no rompe el selector de fecha ni la lectura de la hora.

- [ ] **8. Registro en archivo**
  Hoy un fallo solo deja una línea en la columna Detalle. Un log en disco permite
  saber qué pasó en una grabación de madrugada.

## Proyecto grande

- [ ] **9. Guía de programación (EPG)**
  Todos los canales de la lista traen `tvg-id`, así que el proveedor tiene datos de
  programación. Permitiría elegir el partido de una guía en vez de escribir la hora a
  mano, rellenando canal, inicio y duración solos.

## Descartado por ahora

- **Firmar el ejecutable.** Windows muestra una advertencia al abrir el programa porque
  no está firmado. Un certificado cuesta dinero; de momento basta con avisar a quien
  reciba el enlace.
