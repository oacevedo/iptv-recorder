using System.Text.RegularExpressions;

namespace IptvRecorder;

public static partial class M3uParser
{
    [GeneratedRegex("([A-Za-z0-9\\-]+)=\"([^\"]*)\"")]
    private static partial Regex AttrRegex();

    public static List<Channel> Parse(string content) => Parse(content, out _);

    /// <summary>Además de los canales, devuelve la dirección de la guía si la cabecera
    /// de la lista la anuncia (<c>url-tvg</c> o <c>x-tvg-url</c>).</summary>
    public static List<Channel> Parse(string content, out string epgUrl)
    {
        epgUrl = "";
        var channels = new List<Channel>();
        Channel? pending = null;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in AttrRegex().Matches(line))
                {
                    var key = m.Groups[1].Value.ToLowerInvariant();
                    if (key is "url-tvg" or "x-tvg-url" && epgUrl.Length == 0)
                        epgUrl = m.Groups[2].Value.Split(',')[0].Trim();
                }
                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pending = ParseExtInf(line);
                continue;
            }

            if (line.StartsWith('#')) continue;

            if (pending != null)
            {
                pending.Url = line;
                channels.Add(pending);
                pending = null;
            }
        }

        return channels;
    }

    private static Channel ParseExtInf(string line)
    {
        var ch = new Channel();
        var comma = line.LastIndexOf(',');
        var header = comma >= 0 ? line[..comma] : line;
        ch.Name = comma >= 0 ? line[(comma + 1)..].Trim() : "";

        foreach (Match m in AttrRegex().Matches(header))
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            var val = m.Groups[2].Value;
            switch (key)
            {
                case "group-title": ch.Group = val; break;
                case "tvg-logo": ch.Logo = val; break;
                case "tvg-name": if (ch.Name.Length == 0) ch.Name = val; break;
                case "tvg-id": ch.TvgId = val; break;
            }
        }

        if (ch.Name.Length == 0) ch.Name = "(sin nombre)";
        if (ch.Group.Length == 0) ch.Group = "Sin grupo";
        return ch;
    }
}
