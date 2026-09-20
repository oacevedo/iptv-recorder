using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Xml;

namespace IptvRecorder;

/// <summary>Un programa de la guía.</summary>
public class EpgProgramme
{
    public string ChannelId { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime Stop { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    public int DurationMinutes => Math.Max(1, (int)Math.Round((Stop - Start).TotalMinutes));

    /// <summary>Texto de la hora tal y como se ve en la lista.</summary>
    public string TimeRange => Stop > Start ? $"{Start:HH:mm} - {Stop:HH:mm}" : $"{Start:HH:mm}";

    public bool IsOnNow => DateTime.Now >= Start && DateTime.Now < Stop;

    // Lo que anuncian los lectores de pantalla y la automatización de la interfaz.
    public override string ToString() => $"{TimeRange}  {Title}";
}

/// <summary>
/// Lector de guías en formato XMLTV, que es el que usan los proveedores de IPTV.
///
/// Los archivos pueden tener decenas de megas, así que se leen en flujo y solo se
/// conservan los programas de los canales que están en la lista del usuario y dentro de
/// la ventana de tiempo que interesa.
/// </summary>
public static class XmltvParser
{
    public static Dictionary<string, List<EpgProgramme>> Parse(
        Stream xml, HashSet<string>? wanted, DateTime from, DateTime to)
    {
        var result = new Dictionary<string, List<EpgProgramme>>(StringComparer.OrdinalIgnoreCase);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CheckCharacters = false,
        };

        using var reader = XmlReader.Create(xml, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "programme") continue;

            var channel = reader.GetAttribute("channel") ?? "";
            if (channel.Length == 0) continue;
            if (wanted != null && !wanted.Contains(channel)) continue;

            var start = ParseTime(reader.GetAttribute("start"));
            if (start == null || start < from || start > to) continue;
            var stop = ParseTime(reader.GetAttribute("stop")) ?? start.Value.AddMinutes(30);

            string title = "", desc = "";
            using (var sub = reader.ReadSubtree())
            {
                sub.Read();
                while (sub.Read())
                {
                    if (sub.NodeType != XmlNodeType.Element) continue;
                    if (sub.Name == "title" && title.Length == 0) title = sub.ReadElementContentAsString();
                    else if (sub.Name == "desc" && desc.Length == 0) desc = sub.ReadElementContentAsString();
                }
            }

            if (title.Length == 0) continue;

            if (!result.TryGetValue(channel, out var list))
                result[channel] = list = new List<EpgProgramme>();

            list.Add(new EpgProgramme
            {
                ChannelId = channel,
                Start = start.Value,
                Stop = stop,
                Title = title.Trim(),
                Description = desc.Trim(),
            });
        }

        foreach (var list in result.Values) list.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    /// <summary>Convierte "20260920200000 +0200" a hora local. Sin desfase se toma como local.</summary>
    internal static DateTime? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();

        var digits = text.Length >= 14 ? text[..14] : text;
        if (!DateTime.TryParseExact(digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            // Algunas guías omiten los segundos.
            if (text.Length >= 12 && DateTime.TryParseExact(text[..12], "yyyyMMddHHmm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) { }
            else return null;
        }

        var space = text.IndexOf(' ');
        if (space < 0) return parsed;

        var offsetText = text[(space + 1)..].Trim();
        var offset = ParseOffset(offsetText);
        if (offset == null) return parsed;

        return new DateTimeOffset(parsed, offset.Value).LocalDateTime;
    }

    private static TimeSpan? ParseOffset(string text)
    {
        if (text.Length < 5) return null;
        var sign = text[0] switch { '+' => 1, '-' => -1, _ => 0 };
        if (sign == 0) return null;
        if (!int.TryParse(text.AsSpan(1, 2), out var hours)) return null;
        if (!int.TryParse(text.AsSpan(3, 2), out var minutes)) return null;
        var span = new TimeSpan(hours, minutes, 0);
        // DateTimeOffset no admite desfases mayores de 14 horas.
        return span > TimeSpan.FromHours(14) ? null : span * sign;
    }
}

/// <summary>
/// Guarda la guía descargada y la sirve por canal.
///
/// El emparejamiento es siempre por identificador (tvg-id): emparejar por nombre parece
/// tentador, pero confunde las versiones Este y Oeste del mismo canal, que emiten con
/// horas de diferencia, y una grabación programada con esos datos grabaría otra cosa.
/// </summary>
public class EpgStore
{
    private Dictionary<string, List<EpgProgramme>> _byChannel = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string CachePath = Path.Combine(Store.Dir, "epg.xml");
    private static readonly string StampPath = Path.Combine(Store.Dir, "epg.stamp");

    /// <summary>Ventana que se conserva: algo de pasado para ver qué hay ahora, y una semana por delante.</summary>
    private static readonly TimeSpan Past = TimeSpan.FromHours(6);
    private static readonly TimeSpan Future = TimeSpan.FromDays(8);

    public int ProgrammeCount { get; private set; }
    public int ChannelCount => _byChannel.Count;
    public DateTime? LastUpdated { get; private set; }

    public IReadOnlyList<EpgProgramme> For(string channelId)
    {
        if (channelId.Length > 0 && _byChannel.TryGetValue(channelId, out var list))
        {
            var cutoff = DateTime.Now.AddHours(-1);
            return list.Where(p => p.Stop > cutoff).ToList();
        }
        return Array.Empty<EpgProgramme>();
    }

    /// <summary>Carga la guía guardada en disco, si no es demasiado vieja.</summary>
    public async Task<bool> LoadCacheAsync(HashSet<string> wanted, TimeSpan maxAge)
    {
        try
        {
            if (!File.Exists(CachePath) || !File.Exists(StampPath)) return false;
            var stampText = await File.ReadAllTextAsync(StampPath);
            if (!DateTime.TryParse(stampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp))
                return false;
            if (DateTime.Now - stamp > maxAge) return false;

            await using var file = File.OpenRead(CachePath);
            Adopt(XmltvParser.Parse(file, wanted, DateTime.Now - Past, DateTime.Now + Future), stamp);
            return ProgrammeCount > 0;
        }
        catch { return false; }
    }

    /// <summary>Descarga la guía y la guarda. Acepta XML normal o comprimido en gzip.</summary>
    public async Task RefreshAsync(string url, HashSet<string> wanted, string userAgent)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        byte[] raw;
        if (File.Exists(url))
            raw = await File.ReadAllBytesAsync(url);
        else
            raw = await http.GetByteArrayAsync(url);

        Directory.CreateDirectory(Store.Dir);

        await using (var source = new MemoryStream(raw))
        await using (var plain = Decompress(source))
        await using (var target = File.Create(CachePath))
        {
            await plain.CopyToAsync(target);
        }

        await using (var file = File.OpenRead(CachePath))
        {
            Adopt(XmltvParser.Parse(file, wanted, DateTime.Now - Past, DateTime.Now + Future), DateTime.Now);
        }

        await File.WriteAllTextAsync(StampPath, DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
    }

    private void Adopt(Dictionary<string, List<EpgProgramme>> data, DateTime stamp)
    {
        _byChannel = data;
        ProgrammeCount = data.Values.Sum(v => v.Count);
        LastUpdated = stamp;
    }

    /// <summary>Devuelve el contenido sin comprimir, detectando gzip por su firma.</summary>
    private static Stream Decompress(Stream source)
    {
        var isGzip = source.Length > 2 && source.ReadByte() == 0x1F && source.ReadByte() == 0x8B;
        source.Position = 0;
        return isGzip ? new GZipStream(source, CompressionMode.Decompress, leaveOpen: true) : source;
    }
}
