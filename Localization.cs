using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace IptvRecorder;

/// <summary>Idiomas de la interfaz. Para añadir uno: copiar Strings/Strings.en.xaml, traducirlo y registrarlo aquí.</summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Available =
    {
        ("en", "English"),
        ("es", "Español"),
    };

    private const string Fallback = "en";
    private static ResourceDictionary? _current;

    /// <summary>Cultura con la que arrancó Windows, antes de que la app la cambie.</summary>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;

    public static string Current { get; private set; } = Fallback;

    /// <summary>Idioma que deben usar las ventanas para dar formato a fechas y números.
    /// WPF no mira la cultura del hilo: va por <see cref="FrameworkElement.Language"/>.</summary>
    public static XmlLanguage WindowLanguage { get; private set; } =
        XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

    public static string Resolve(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Available.Any(l => l.Code == code) ? code : Fallback;
    }

    public static void Apply(string code)
    {
        code = Resolve(code);
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Strings/Strings.{code}.xaml", UriKind.Absolute)
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Current = code;
        ApplyCulture(code);
    }

    /// <summary>Alinea el formato de fechas y números con el idioma elegido, para que en
    /// español no salgan fechas al estilo estadounidense. Si el idioma coincide con el de
    /// Windows se conserva la cultura del sistema, que lleva las preferencias del usuario.</summary>
    private static void ApplyCulture(string code)
    {
        CultureInfo culture;
        try
        {
            culture = SystemCulture.TwoLetterISOLanguageName.Equals(code, StringComparison.OrdinalIgnoreCase)
                ? SystemCulture
                : CultureInfo.CreateSpecificCulture(code);
        }
        catch { culture = SystemCulture; }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;

        WindowLanguage = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        foreach (Window window in Application.Current.Windows) window.Language = WindowLanguage;
    }

    public static string Get(string key, params object[] args)
    {
        var s = Application.Current?.TryFindResource(key) as string ?? key;
        return args.Length == 0 ? s : string.Format(s, args);
    }
}

/// <summary>Muestra un RecordingStatus con el texto del idioma actual.</summary>
public class RecordingStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is RecordingStatus s ? Loc.Get("Rec_" + s) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Serializa el estado por nombre y acepta los nombres en español de versiones anteriores.</summary>
public class RecordingStatusJsonConverter : JsonConverter<RecordingStatus>
{
    private static readonly Dictionary<string, RecordingStatus> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pendiente"] = RecordingStatus.Pending,
        ["Grabando"] = RecordingStatus.Recording,
        ["Convirtiendo"] = RecordingStatus.Converting,
        ["Completada"] = RecordingStatus.Completed,
        ["Fallida"] = RecordingStatus.Failed,
        ["Cancelada"] = RecordingStatus.Cancelled,
    };

    public override RecordingStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? "";
        if (Enum.TryParse<RecordingStatus>(s, ignoreCase: true, out var v)) return v;
        return Legacy.TryGetValue(s, out var legacy) ? legacy : RecordingStatus.Failed;
    }

    public override void Write(Utf8JsonWriter writer, RecordingStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
