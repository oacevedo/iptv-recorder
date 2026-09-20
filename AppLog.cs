using System.IO;
using System.Text;

namespace IptvRecorder;

/// <summary>
/// Registro en disco de lo que va pasando. La barra de estado solo guarda la última
/// línea, así que sin esto un fallo de madrugada no deja rastro que consultar.
///
/// Un único archivo que se rota al llegar a 1 MB, conservando el anterior.
/// </summary>
public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string Path1 = System.IO.Path.Combine(Store.Dir, "iptv-recorder.log");
    private static readonly string Path2 = System.IO.Path.Combine(Store.Dir, "iptv-recorder.1.log");
    private const long MaxBytes = 1_000_000;

    public static string FilePath => Path1;

    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                Rotate();
                File.AppendAllText(
                    Path1,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    private static void Rotate()
    {
        try
        {
            var info = new FileInfo(Path1);
            if (!info.Exists || info.Length < MaxBytes) return;
            if (File.Exists(Path2)) File.Delete(Path2);
            File.Move(Path1, Path2);
        }
        catch { }
    }
}
