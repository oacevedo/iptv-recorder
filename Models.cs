using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace IptvRecorder;

public class Channel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Logo { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Identificador del canal en la lista (tvg-id), si lo trae.</summary>
    public string TvgId { get; set; } = "";

    /// <summary>Clave con la que se recuerdan los favoritos. Se prefiere el identificador
    /// de la lista porque sobrevive a los cambios de nombre del proveedor.</summary>
    public string Key => TvgId.Length > 0 ? TvgId : Name;

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { _isFavorite = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite))); }
    }

    private ImageSource? _logoImage;
    /// <summary>Logotipo ya descargado, o null mientras no lo esté.</summary>
    public ImageSource? LogoImage
    {
        get => _logoImage;
        set { _logoImage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogoImage))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Nombre que exponen los lectores de pantalla y la automatización de la interfaz.
    public override string ToString() => Name;
}

public enum RecordingStatus
{
    Pending,
    Recording,
    Converting,
    Completed,
    Failed,
    Cancelled
}

public class Recording : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string ChannelUrl { get; set; } = "";
    public DateTime Start { get; set; }
    public int DurationMinutes { get; set; }

    [JsonIgnore]
    public DateTime End => Start.AddMinutes(DurationMinutes);

    private RecordingStatus _status;
    [JsonConverter(typeof(RecordingStatusJsonConverter))]
    public RecordingStatus Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsActive)); } }
    }

    private string _outputFile = "";
    public string OutputFile
    {
        get => _outputFile;
        set { _outputFile = value; OnPropertyChanged(); }
    }

    private string _lastLog = "";
    public string LastLog
    {
        get => _lastLog;
        set { _lastLog = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public bool IsActive => Status is RecordingStatus.Recording or RecordingStatus.Converting;

    private string _liveUrl = "";
    /// <summary>Dirección local para ver la grabación en curso; vacía si no se está grabando.</summary>
    [JsonIgnore]
    public string LiveUrl
    {
        get => _liveUrl;
        set { _liveUrl = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AppSettings
{
    public string M3uUrl { get; set; } = "";
    public string FfmpegPath { get; set; } = "";
    public string PlayerPath { get; set; } = "";
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "IPTV");
    public string UserAgent { get; set; } = "VLC/3.0.20 LibVLC/3.0.20";
    public int LeadSeconds { get; set; } = 60;
    /// <summary>Minutos extra grabados al final, para cubrir descuentos y prórrogas.</summary>
    public int TailMinutes { get; set; } = 10;
    public bool ConvertToMp4 { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>Avisar en la bandeja cuando una grabación termina o falla.</summary>
    public bool Notifications { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    /// <summary>Impedir la suspensión mientras se graba y despertar el equipo para grabar.</summary>
    public bool PreventSleep { get; set; } = true;
    /// <summary>Código de idioma ("es", "en"). Vacío = el del sistema.</summary>
    public string Language { get; set; } = "";
}
