using System.IO;

namespace IptvRecorder;

/// <summary>
/// Estimaciones de espacio para no acabar con un partido cortado a la mitad.
///
/// No se conoce el bitrate real de un canal hasta que se graba, así que la estimación
/// previa usa un valor típico de televisión en alta definición. Durante la grabación la
/// comprobación sí es exacta: se mira el espacio que queda de verdad.
/// </summary>
public static class DiskSpace
{
    /// <summary>Por debajo de esto no se empieza a grabar.</summary>
    public const long StartFloorBytes = 1L << 30;

    /// <summary>Por debajo de esto se detiene una grabación en curso, para que el
    /// archivo quede utilizable en vez de cortarse a medio escribir.</summary>
    public const long StopFloorBytes = 512L << 20;

    /// <summary>Bitrate supuesto para la estimación previa, en megabits por segundo.</summary>
    private const double AssumedMbps = 6.0;

    /// <summary>Espacio libre en el disco de esa carpeta, o null si no se puede saber
    /// (por ejemplo en una ruta de red).</summary>
    public static long? Free(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch { return null; }
    }

    /// <summary>Espacio que hace falta para una grabación de esos minutos. Con conversión
    /// a MP4 se cuenta el doble: el .ts y el .mp4 conviven en disco unos segundos.</summary>
    public static long Estimate(int minutes, bool convertToMp4)
    {
        var bytes = (long)(minutes * 60.0 * AssumedMbps * 1_000_000.0 / 8.0);
        return convertToMp4 ? bytes * 2 : bytes;
    }

    public static string Format(long bytes)
    {
        var gb = bytes / 1024.0 / 1024.0 / 1024.0;
        return gb >= 1 ? $"{gb:0.0} GB" : $"{bytes / 1024.0 / 1024.0:0} MB";
    }
}
