# IPTV Recorder

Grabador de canales IPTV para Windows. Carga una lista M3U, busca el canal y programa la grabación con fecha, hora y duración. La app lanza `ffmpeg` sola cuando llega la hora y guarda el resultado en MP4.

## Requisitos

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (ya lo tienes si tienes el SDK)
- `ffmpeg` en el PATH o indicado en Ajustes (`winget install ffmpeg`)

## Uso

1. Ejecuta `dist\IptvRecorder.exe`.
2. Pega el enlace M3U arriba y pulsa **Cargar lista**. La lista queda en caché, no hace falta volver a cargarla cada vez.
3. Busca el canal por nombre o filtra por grupo y selecciónalo.
4. Para comprobar que es el canal correcto, pulsa **Vista previa** (o doble clic en el canal). Se reproduce dentro de la app, en el panel superior derecho, con control de volumen y silencio. **Abrir en VLC** lo abre en una ventana aparte si prefieres pantalla completa. La app detiene la vista previa sola cuando empieza una grabación, para no ocupar la conexión del proveedor.
5. Rellena título, fecha, hora (formato 24h, por ejemplo `20:55`) y duración en minutos.
6. Pulsa **Programar**. O **Grabar ahora** para empezar al instante.

La app debe seguir abierta a la hora de la grabación. Al cerrarla o minimizarla se queda en la bandeja del sistema y las grabaciones se hacen igualmente. Para cerrarla del todo: clic derecho en el icono de la bandeja y **Salir**.

## Idioma

La interfaz está en español e inglés. Por defecto usa el idioma de Windows; se puede fijar en Ajustes y cambia al instante sin reiniciar.

Para añadir otro idioma: copia `Strings\Strings.en.xaml` como `Strings\Strings.xx.xaml` (código ISO de dos letras), traduce los textos y añade el idioma a la lista `Available` en `Localization.cs`.

## Ajustes

- **Idioma**: automático (el de Windows), español o inglés.

- **Carpeta de salida**: por defecto `Vídeos\IPTV`.
- **Reproductor**: el que se usa para "Abrir en VLC". Vacío significa VLC si está instalado, y si no, ffplay. La vista previa incrustada no depende de esto: usa LibVLC, que va incluido con la app.
- **Empezar antes**: segundos de margen antes de la hora indicada (por defecto 60) para que el stream enganche.
- **Convertir a MP4**: al terminar, remuxea el `.ts` a `.mp4` sin recodificar y borra el `.ts`.
- **Iniciar con Windows**: arranca minimizado en la bandeja al iniciar sesión, para no perder grabaciones.
- **User-Agent**: algunos proveedores solo aceptan reproductores conocidos. Por defecto se identifica como VLC.

## Limitaciones

- Casi todos los proveedores limitan las conexiones simultáneas. Una grabación cuenta como una conexión: no veas otro canal del mismo servicio mientras grabas.
- Si el PC está apagado o suspendido a la hora programada, la grabación no se hace. Desactiva la suspensión automática si programas de noche.
- Las URLs de la lista cambian a veces. Si una grabación falla con error de conexión, vuelve a cargar la lista y programa de nuevo.

## Compilar

```powershell
.\build.ps1
```

Genera `dist\IptvRecorder.exe` junto con la carpeta `dist\libvlc`, que contiene el motor de VLC para la vista previa. Hay que copiar las dos cosas juntas. Requiere el runtime de .NET 10.

## Datos

Ajustes, grabaciones programadas y caché de la lista se guardan en `%APPDATA%\IptvRecorder`.
