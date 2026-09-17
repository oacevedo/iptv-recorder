using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace IptvRecorder;

/// <summary>Reproductor incrustado basado en LibVLC para la vista previa de canales.</summary>
public class EmbeddedPreview : IDisposable
{
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private readonly VideoView _view;
    private string _userAgent;

    public bool IsPlaying => _player?.IsPlaying == true;
    public event Action<string>? Error;

    public EmbeddedPreview(VideoView view, string userAgent)
    {
        _view = view;
        _userAgent = userAgent;
    }

    private void EnsureInitialized()
    {
        if (_player != null) return;
        Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--network-caching=1500", "--quiet");
        _player = new MediaPlayer(_libVlc);
        _player.EncounteredError += (_, _) => Error?.Invoke(Loc.Get("Err_VlcPlay"));
        _view.MediaPlayer = _player;
    }

    public void Play(Channel channel, int volume, bool mute)
    {
        EnsureInitialized();
        using var media = new Media(_libVlc!, new Uri(channel.Url));
        if (channel.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            media.AddOption(":http-user-agent=" + _userAgent);
        _player!.Volume = volume;
        _player.Mute = mute;
        _player.Play(media);
    }

    public void Stop()
    {
        if (_player == null) return;
        // Stop() bloquea si se llama desde el hilo de la UI mientras VLC decodifica; usar hilo aparte.
        var p = _player;
        Task.Run(() => { try { p.Stop(); } catch { } });
    }

    public void SetVolume(int volume)
    {
        if (_player != null) _player.Volume = volume;
    }

    public void SetMute(bool mute)
    {
        if (_player != null) _player.Mute = mute;
    }

    public void SetUserAgent(string ua) => _userAgent = ua;

    public void Dispose()
    {
        try
        {
            if (_player != null)
            {
                _view.MediaPlayer = null;
                _player.Stop();
                _player.Dispose();
            }
            _libVlc?.Dispose();
        }
        catch { }
        _player = null;
        _libVlc = null;
    }
}
