using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IptvRecorder;

public class Channel
{
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Logo { get; set; } = "";
    public string Url { get; set; } = "";

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
    public bool ConvertToMp4 { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    /// <summary>Impedir la suspensión mientras se graba y despertar el equipo para grabar.</summary>
    public bool PreventSleep { get; set; } = true;
    /// <summary>Código de idioma ("es", "en"). Vacío = el del sistema.</summary>
    public string Language { get; set; } = "";
}
