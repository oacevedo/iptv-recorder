using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace IptvRecorder;

public class RecordingScheduler : IDisposable
{
    private readonly ObservableCollection<Recording> _recordings;
    private readonly Func<AppSettings> _settings;
    private readonly Dictionary<Guid, Process> _procs = new();
    private readonly Dictionary<Guid, LiveRelay> _relays = new();
    private readonly HashSet<Guid> _stopRequested = new();
    private readonly DispatcherTimer _timer;

    public event Action<string>? Log;
    public event Action? Changed;
    public event Action<Recording>? Starting;
    /// <summary>Una grabación ha terminado, bien o mal. No se lanza si el usuario la detuvo.</summary>
    public event Action<Recording>? Finished;

    public bool AnyActive => _procs.Count > 0;

    public RecordingScheduler(ObservableCollection<Recording> recordings, Func<AppSettings> settings)
    {
        _recordings = recordings;
        _settings = settings;

        // Si la app se cerró mientras grababa, esos registros quedan huérfanos.
        foreach (var r in _recordings.Where(r => r.IsActive))
        {
            r.Status = RecordingStatus.Failed;
            r.LastLog = Loc.Get("Log_ClosedDuringRecording");
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Tick()
    {
        var now = DateTime.Now;
        var lead = _settings().LeadSeconds;

        foreach (var r in _recordings.ToList())
        {
            if (r.Status != RecordingStatus.Pending) continue;

            if (now >= r.End)
            {
                r.Status = RecordingStatus.Failed;
                r.LastLog = Loc.Get("Log_Missed");
                Changed?.Invoke();
                Finished?.Invoke(r);
                continue;
            }

            if (now >= r.Start.AddSeconds(-lead))
                Start(r);
        }

        UpdatePower();
    }

    /// <summary>Minutos de antelación con los que se despierta el equipo.</summary>
    private const int WakeMarginMinutes = 2;

    private bool _wakeWarned;

    /// <summary>Mantiene el equipo despierto mientras se graba y arma el despertador para
    /// la siguiente grabación programada.</summary>
    private void UpdatePower()
    {
        var s = _settings();
        if (!s.PreventSleep)
        {
            PowerManager.Release();
            return;
        }

        PowerManager.KeepAwake(_procs.Count > 0);

        var now = DateTime.Now;
        var next = _recordings
            .Where(r => r.Status == RecordingStatus.Pending)
            .Select(r => r.Start.AddSeconds(-s.LeadSeconds))
            .Where(t => t > now)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();

        if (next == DateTime.MinValue)
        {
            PowerManager.CancelWake();
            return;
        }

        var wakeAt = next.AddMinutes(-WakeMarginMinutes);
        if (wakeAt <= now) return;

        if (!PowerManager.ScheduleWake(wakeAt) && !_wakeWarned)
        {
            _wakeWarned = true;
            Log?.Invoke(Loc.Get("Log_WakeUnavailable"));
        }
    }

    public static string? ResolveFfmpeg(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        // Un ffmpeg.exe junto al ejecutable (así se distribuye en el zip del release).
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;

        // PATH del proceso, y después el PATH del registro por si la app arrancó
        // desde un entorno anterior a una instalación o actualización de ffmpeg.
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "",
        };
        foreach (var path in paths)
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        // Instalaciones de winget (Gyan.FFmpeg) en ámbito de usuario o de máquina.
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinGet", "Packages"),
        };
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var pkg in Directory.GetDirectories(root, "*FFmpeg*"))
                {
                    var found = Directory.GetFiles(pkg, "ffmpeg.exe", SearchOption.AllDirectories)
                                         .OrderByDescending(f => f).FirstOrDefault();
                    if (found != null) return found;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>Hora a la que se detiene realmente la grabación, con el margen final.</summary>
    public static DateTime EndWithTail(Recording r, AppSettings s)
        => r.End.AddMinutes(Math.Clamp(s.TailMinutes, 0, MaxTailMinutes));

    public const int MaxTailMinutes = 120;

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return s.Length == 0 ? "recording" : s;
    }

    public void Start(Recording r)
    {
        if (_procs.ContainsKey(r.Id)) return;

        var s = _settings();
        var ffmpeg = ResolveFfmpeg(s.FfmpegPath);
        if (ffmpeg == null)
        {
            r.Status = RecordingStatus.Failed;
            r.LastLog = Loc.Get("Log_FfmpegMissing");
            Changed?.Invoke();
            Finished?.Invoke(r);
            return;
        }

        try { Directory.CreateDirectory(s.OutputFolder); }
        catch (Exception ex)
        {
            r.Status = RecordingStatus.Failed;
            r.LastLog = Loc.Get("Log_OutputFolderError", ex.Message);
            Changed?.Invoke();
            Finished?.Invoke(r);
            return;
        }

        var baseName = $"{r.Start:yyyy-MM-dd_HHmm} {SafeFileName(r.Title.Length > 0 ? r.Title : r.ChannelName)}";
        var tsFile = Path.Combine(s.OutputFolder, baseName + ".ts");
        var i = 1;
        while (File.Exists(tsFile)) tsFile = Path.Combine(s.OutputFolder, $"{baseName} ({i++}).ts");

        // Se graba más allá de la hora de fin indicada: los partidos se alargan.
        var seconds = Math.Max(30, (int)(EndWithTail(r, s) - DateTime.Now).TotalSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var a = psi.ArgumentList;
        a.Add("-hide_banner"); a.Add("-loglevel"); a.Add("warning"); a.Add("-stats"); a.Add("-y");
        if (r.ChannelUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            a.Add("-user_agent"); a.Add(s.UserAgent);
            a.Add("-reconnect"); a.Add("1");
            a.Add("-reconnect_streamed"); a.Add("1");
            a.Add("-reconnect_delay_max"); a.Add("5");
            a.Add("-rw_timeout"); a.Add("20000000");
        }
        a.Add("-i"); a.Add(r.ChannelUrl);

        // Salida principal: el archivo. La selección de pistas es la de siempre.
        a.Add("-c"); a.Add("copy");
        a.Add("-t"); a.Add(seconds.ToString());
        a.Add("-f"); a.Add("mpegts");
        a.Add(tsFile);

        // Segunda salida: una copia a un puerto local, para poder ver la grabación en
        // curso sin abrir otra conexión al proveedor.
        LiveRelay? relay = null;
        try { relay = new LiveRelay(); } catch { }
        if (relay != null)
        {
            a.Add("-c"); a.Add("copy");
            a.Add("-t"); a.Add(seconds.ToString());
            a.Add("-f"); a.Add("mpegts");
            a.Add($"udp://127.0.0.1:{relay.SourcePort}?pkt_size=1316");
        }

        Starting?.Invoke(r);

        Process proc;
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Exited += (_, _) => OnExited(r, proc, tsFile);
            proc.Start();
        }
        catch (Exception ex)
        {
            relay?.Dispose();
            r.Status = RecordingStatus.Failed;
            r.LastLog = Loc.Get("Log_FfmpegStartError", ex.Message);
            Changed?.Invoke();
            Finished?.Invoke(r);
            return;
        }

        _procs[r.Id] = proc;
        if (relay != null)
        {
            _relays[r.Id] = relay;
            r.LiveUrl = relay.PlaybackUrl;
        }
        r.OutputFile = tsFile;
        r.Status = RecordingStatus.Recording;
        r.LastLog = Loc.Get("Log_Connecting");
        Changed?.Invoke();
        UpdatePower();
        Log?.Invoke(Loc.Get("Log_Recording", r.ChannelName, tsFile));

        _ = Task.Run(() => PumpStderr(r, proc));
        _ = proc.StandardOutput.ReadToEndAsync();
    }

    private void PumpStderr(Recording r, Process proc)
    {
        try
        {
            var reader = proc.StandardError;
            var buf = new char[512];
            var line = new StringBuilder();
            int n;
            while ((n = reader.Read(buf, 0, buf.Length)) > 0)
            {
                for (var k = 0; k < n; k++)
                {
                    var c = buf[k];
                    if (c == '\r' || c == '\n')
                    {
                        if (line.Length > 0)
                        {
                            var text = line.ToString().Trim();
                            line.Clear();
                            if (text.Length > 0)
                                Application.Current?.Dispatcher.BeginInvoke(() => r.LastLog = Shorten(text));
                        }
                    }
                    else line.Append(c);
                }
            }
        }
        catch { }
    }

    private static string Shorten(string text)
    {
        // Las líneas de progreso de ffmpeg son largas; nos quedamos con tiempo y tamaño.
        if (text.StartsWith("size=") || text.StartsWith("frame="))
        {
            var size = Extract(text, "size=");
            var time = Extract(text, "time=");
            return $"{time}   {size}";
        }
        return text.Length > 160 ? text[..160] : text;
    }

    private static string Extract(string text, string key)
    {
        var i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "";
        var rest = text[(i + key.Length)..].TrimStart();
        var end = rest.IndexOf(' ');
        return key.TrimEnd('=') + " " + (end < 0 ? rest : rest[..end]);
    }

    private void OnExited(Recording r, Process proc, string tsFile)
    {
        var code = proc.ExitCode;
        var requested = _stopRequested.Contains(r.Id);
        long size = 0;
        try { if (File.Exists(tsFile)) size = new FileInfo(tsFile).Length; } catch { }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _procs.Remove(r.Id);
            _stopRequested.Remove(r.Id);
            if (_relays.Remove(r.Id, out var relay)) relay.Dispose();
            r.LiveUrl = "";
            UpdatePower();

            if (size < 200_000)
            {
                r.Status = requested ? RecordingStatus.Cancelled : RecordingStatus.Failed;
                if (!requested) r.LastLog = Loc.Get("Log_ExitNoData", code, r.LastLog);
                try { if (size == 0 && File.Exists(tsFile)) File.Delete(tsFile); } catch { }
                Changed?.Invoke();
                Log?.Invoke(Loc.Get("Log_Failed", r.ChannelName, r.LastLog));
                if (!requested) Finished?.Invoke(r);
                return;
            }

            var s = _settings();
            if (s.ConvertToMp4)
            {
                r.Status = RecordingStatus.Converting;
                r.LastLog = Loc.Get("Log_Converting");
                Changed?.Invoke();
                _ = Task.Run(() => ConvertToMp4(r, tsFile));
            }
            else
            {
                Finish(r, tsFile, size, requested);
            }
        });
    }

    private void ConvertToMp4(Recording r, string tsFile)
    {
        var s = _settings();
        var ffmpeg = ResolveFfmpeg(s.FfmpegPath)!;
        var mp4 = Path.ChangeExtension(tsFile, ".mp4");
        var ok = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
            };
            var a = psi.ArgumentList;
            a.Add("-hide_banner"); a.Add("-loglevel"); a.Add("error"); a.Add("-y");
            a.Add("-i"); a.Add(tsFile);
            a.Add("-c"); a.Add("copy");
            a.Add("-movflags"); a.Add("+faststart");
            a.Add(mp4);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            ok = p.ExitCode == 0 && File.Exists(mp4) && new FileInfo(mp4).Length > 0;
            if (!ok) Application.Current?.Dispatcher.Invoke(() => Log?.Invoke(Loc.Get("Log_ConvertFailed", err.Trim())));
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() => Log?.Invoke(Loc.Get("Log_ConvertFailed", ex.Message)));
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (ok)
            {
                try { File.Delete(tsFile); } catch { }
                Finish(r, mp4, new FileInfo(mp4).Length, false);
            }
            else
            {
                try { if (File.Exists(mp4)) File.Delete(mp4); } catch { }
                Finish(r, tsFile, new FileInfo(tsFile).Length, false, Loc.Get("Log_SavedAsTs"));
            }
        });
    }

    private void Finish(Recording r, string file, long size, bool requested, string? note = null)
    {
        r.OutputFile = file;
        r.Status = RecordingStatus.Completed;
        var mb = (size / 1024.0 / 1024.0).ToString("0");
        r.LastLog = note ?? Loc.Get(requested ? "Log_StoppedManually" : "Log_Completed", mb);
        Changed?.Invoke();
        Log?.Invoke(Loc.Get("Log_Finished", r.ChannelName, file));
        Finished?.Invoke(r);
    }

    public void Stop(Recording r)
    {
        if (!_procs.TryGetValue(r.Id, out var proc)) return;
        _stopRequested.Add(r.Id);
        try
        {
            proc.StandardInput.Write("q");
            proc.StandardInput.Flush();
        }
        catch { }

        _ = Task.Run(() =>
        {
            try { if (!proc.WaitForExit(8000)) proc.Kill(true); } catch { }
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var p in _procs.Values.ToList())
        {
            try { p.StandardInput.Write("q"); p.StandardInput.Flush(); } catch { }
            try { if (!p.WaitForExit(5000)) p.Kill(true); } catch { }
        }
        _procs.Clear();
        foreach (var relay in _relays.Values.ToList()) relay.Dispose();
        _relays.Clear();
        PowerManager.Release();
    }
}
