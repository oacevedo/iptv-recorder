using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IptvRecorder;

/// <summary>
/// Logotipos de los canales, descargados bajo demanda.
///
/// La lista puede tener miles de canales, así que solo se piden los que se ven: la lista
/// está virtualizada y cada fila pide el suyo al aparecer. Lo descargado se guarda en
/// disco para que la siguiente vez sea instantáneo. Bastantes direcciones devuelven 404,
/// así que los fallos también se recuerdan y no se reintentan.
/// </summary>
public static class LogoCache
{
    private static readonly string Dir = Path.Combine(Store.Dir, "logos");
    private static readonly Dictionary<string, Task<ImageSource?>> Tasks = new();
    private static readonly SemaphoreSlim Gate = new(6);

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    /// <summary>Alto al que se decodifican, suficiente para la lista y barato en memoria.</summary>
    private const int DecodeHeight = 32;

    /// <summary>Pide el logotipo de un canal. Vuelve enseguida; la imagen aparece cuando
    /// está lista. Debe llamarse desde el hilo de la interfaz.</summary>
    public static async void Request(Channel channel)
    {
        if (channel.LogoImage != null || channel.Logo.Length == 0) return;

        try
        {
            var image = await GetAsync(channel.Logo);
            if (image != null) channel.LogoImage = image;
        }
        catch { }
    }

    private static Task<ImageSource?> GetAsync(string url)
    {
        lock (Tasks)
        {
            if (Tasks.TryGetValue(url, out var existing)) return existing;
            var task = LoadAsync(url);
            Tasks[url] = task;
            return task;
        }
    }

    private static async Task<ImageSource?> LoadAsync(string url)
    {
        // Algunas listas apuntan a imágenes del propio disco en vez de a una dirección web.
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var local = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(url).LocalPath
                    : url;
                return File.Exists(local) ? Decode(await File.ReadAllBytesAsync(local)) : null;
            }
            catch { return null; }
        }

        var path = CachePath(url);

        if (File.Exists(path))
        {
            var cached = Decode(await File.ReadAllBytesAsync(path));
            if (cached != null) return cached;
            try { File.Delete(path); } catch { }
        }

        await Gate.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            var image = Decode(bytes);
            if (image == null) return null;

            try
            {
                Directory.CreateDirectory(Dir);
                await File.WriteAllBytesAsync(path, bytes);
            }
            catch { }

            return image;
        }
        catch
        {
            // Direcciones rotas y tiempos de espera: se recuerdan como nulo y no se
            // vuelven a intentar mientras la aplicación siga abierta.
            return null;
        }
        finally { Gate.Release(); }
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelHeight = DecodeHeight;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private static string CachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
        return Path.Combine(Dir, hash + ".img");
    }
}
