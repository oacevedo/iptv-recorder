using System.Net;
using System.Net.Sockets;

namespace IptvRecorder;

/// <summary>
/// Permite ver una grabación en curso sin abrir una segunda conexión al proveedor.
///
/// ffmpeg envía una copia del flujo a un puerto local (además del archivo). Si nadie
/// escuchara ese puerto, Windows respondería "puerto inalcanzable" y ffmpeg descartaría
/// esa salida, así que esta clase mantiene un receptor vivo durante toda la grabación
/// y reenvía cada datagrama a un segundo puerto, del que lee el reproductor.
/// </summary>
public sealed class LiveRelay : IDisposable
{
    private readonly Socket _in;
    private readonly Socket _out;
    private readonly CancellationTokenSource _cts = new();
    private readonly EndPoint _target;

    /// <summary>Puerto al que ffmpeg envía la copia del flujo.</summary>
    public int SourcePort { get; }

    /// <summary>URL que el reproductor abre para ver la grabación en curso.</summary>
    public string PlaybackUrl { get; }

    public LiveRelay()
    {
        _in = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _in.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        SourcePort = ((IPEndPoint)_in.LocalEndPoint!).Port;
        _in.ReceiveBufferSize = 1 << 20;

        // El reproductor necesita poder enlazar el puerto de salida, así que solo se
        // reserva uno libre y se suelta enseguida.
        int targetPort;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            targetPort = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        _target = new IPEndPoint(IPAddress.Loopback, targetPort);
        _out = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        PlaybackUrl = $"udp://@127.0.0.1:{targetPort}";

        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[65536];
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            int n;
            try
            {
                n = await _in.ReceiveAsync(buffer, SocketFlags.None, token);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            if (n <= 0) continue;

            try
            {
                // Mientras nadie esté viendo la grabación no hay nadie enlazado al puerto
                // de salida; el envío falla y se descarta sin afectar a la grabación.
                _out.SendTo(buffer, n, SocketFlags.None, _target);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _in.Dispose(); } catch { }
        try { _out.Dispose(); } catch { }
        try { _cts.Dispose(); } catch { }
    }
}
