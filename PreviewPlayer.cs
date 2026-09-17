using System.Diagnostics;
using System.IO;

namespace IptvRecorder;

public class PreviewPlayer
{
    private Process? _proc;

    public bool IsRunning => _proc != null && !_proc.HasExited;

    public static string? ResolvePlayer(string configured, string ffmpegConfigured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        var ffmpeg = RecordingScheduler.ResolveFfmpeg(ffmpegConfigured);
        if (ffmpeg != null)
        {
            var ffplay = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffplay.exe");
            if (File.Exists(ffplay)) return ffplay;
        }
        return null;
    }

    public string Start(Channel channel, AppSettings settings)
    {
        Stop();

        var player = ResolvePlayer(settings.PlayerPath, settings.FfmpegPath);
        if (player == null)
            throw new InvalidOperationException(Loc.Get("Err_NoPlayer"));

        var psi = new ProcessStartInfo { FileName = player, UseShellExecute = false };
        var a = psi.ArgumentList;
        var isVlc = Path.GetFileName(player).Equals("vlc.exe", StringComparison.OrdinalIgnoreCase);

        if (isVlc)
        {
            a.Add("--http-user-agent=" + settings.UserAgent);
            a.Add("--meta-title=" + channel.Name);
            a.Add("--play-and-exit");
            a.Add("--no-video-title-show");
            a.Add(channel.Url);
        }
        else
        {
            a.Add("-hide_banner"); a.Add("-loglevel"); a.Add("error");
            a.Add("-window_title"); a.Add(channel.Name);
            a.Add("-x"); a.Add("960");
            if (channel.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                a.Add("-user_agent"); a.Add(settings.UserAgent);
            }
            a.Add("-i"); a.Add(channel.Url);
        }

        _proc = Process.Start(psi);
        return Path.GetFileNameWithoutExtension(player).ToUpperInvariant();
    }

    public void Stop()
    {
        try
        {
            if (_proc != null && !_proc.HasExited)
            {
                _proc.CloseMainWindow();
                if (!_proc.WaitForExit(2000)) _proc.Kill(true);
            }
        }
        catch { }
        finally { _proc = null; }
    }
}
