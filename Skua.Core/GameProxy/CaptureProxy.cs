using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Skua.Core.GameProxy;

public partial class CaptureProxy : ObservableRecipient, ICaptureProxy
{
    // Generous local safety limit, not a server protocol limit.
    private const int MaxDecompressedPacketBytes = 16 * 1024 * 1024;

    private readonly IDiagnosticsService _diagnostics;
    private long _outboundBytes;
    private long _inboundBytes;
    private long _outboundPackets;
    private long _inboundPackets;
    private long _interceptorInvocations;
    private long _interceptorElapsedTicks;
    private long _forwardingDrops;
    private long _forwardingExceptions;
    private long _activeStreams;
    private CancellationTokenSource? _captureProxyCTS;
    private readonly object _lifecycleLock = new();

    /// <summary>
    /// The default port for the capture proxy to run on.
    /// </summary>
    public const int DefaultPort = 5588;

    public IPEndPoint? Destination { get; set; }
    public List<IInterceptor> Interceptors { get; } = new();

    private Thread? _thread;
    private TcpListener? _listener;
    private TcpClient? _forwarder;
    private TcpClient? _client;
    private int _listenPort = DefaultPort;

    public CaptureProxy(IDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool _running;

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (Running || Destination is null)
                return;

            ResetDiagnosticsCounters();
            _listenPort = Destination.Port;
            CancellationTokenSource captureProxyCTS = new();
            _captureProxyCTS = captureProxyCTS;
            TcpListener listener = new(IPAddress.Loopback, _listenPort);
            _listener = listener;

            try
            {
                listener.Start();
            }
            catch
            {
                try { listener.Stop(); } catch { }
                _listener = null;
                captureProxyCTS.Dispose();
                _captureProxyCTS = null;
                return;
            }

            Running = true;
            CancellationToken token = captureProxyCTS.Token;
            _thread = new(() =>
            {
                try
                {
                    _Listen(token);
                }
                finally
                {
                    lock (_lifecycleLock)
                    {
                        _captureProxyCTS?.Dispose();
                        _captureProxyCTS = null;
                        _listener = null;
                        _thread = null;
                        Running = false;
                    }
                    RecordDiagnosticsSummary();
                }
            })
            { Name = "Capture Proxy" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? listenerThread;
        lock (_lifecycleLock)
        {
            _captureProxyCTS?.Cancel();
            try { _listener?.Stop(); } catch { }

            // TcpClient.Connected is only a snapshot; always close sockets to interrupt pending reads/writes.
            try { _forwarder?.Close(); } catch { }
            try { _forwarder?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            try { _client?.Dispose(); } catch { }

            listenerThread = _thread;
        }

        if (listenerThread is not null && listenerThread != Thread.CurrentThread)
            listenerThread.Join(TimeSpan.FromSeconds(2));
    }

    private void _Listen(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? localClient = null;
            TcpClient? localForwarder = null;
            try
            {
                localClient = _listener?.AcceptTcpClient();
                if (localClient == null)
                    break;
                localClient.NoDelay = true;
                localForwarder = new TcpClient
                {
                    NoDelay = true
                };
                localForwarder.ConnectAsync(Destination!).WaitAsync(token).GetAwaiter().GetResult();

                _client = localClient;
                _forwarder = localForwarder;
                TcpClient client = localClient;
                TcpClient forwarder = localForwarder;

                Interlocked.Add(ref _activeStreams, 2);
                // Do not pass the cancellation token to StartNew: a pre-cancelled task would
                // never enter _DataInterceptor and would leave ActiveStreams permanently elevated.
                Task.Factory.StartNew(() => _DataInterceptor(client, forwarder, true, token));
                Task.Factory.StartNew(() => _DataInterceptor(forwarder, client, false, token));
            }
            catch
            {
                localClient?.Close();
                localClient?.Dispose();
                localForwarder?.Close();
                localForwarder?.Dispose();
            }
        }

        try { _listener?.Stop(); } catch { }
    }
    private async Task _DataInterceptor(TcpClient target, TcpClient destination, bool outbound, CancellationToken token)
    {
        byte[] messageBuffer = new byte[4096];
        List<byte> cpacket = new();
        NetworkStream? targetStream = null;
        NetworkStream? destStream = null;

        try
        {
            // Socket shutdown can race task startup; acquire streams inside the guarded block
            // so every started interceptor reaches the cleanup path and decrements ActiveStreams.
            targetStream = target.GetStream();
            destStream = destination.GetStream();

            while (!token.IsCancellationRequested && target.Connected && destination.Connected)
            {
                int read = await targetStream.ReadAsync(messageBuffer, token).ConfigureAwait(false);

                if (read == 0)
                    break;

                for (int i = 0; i < read; i++)
                {
                    if (token.IsCancellationRequested)
                        break;

                    byte b = messageBuffer[i];
                    if (b > 0)
                    {
                        cpacket.Add(b);
                        continue;
                    }

                    if (cpacket.Count == 0)
                    {
                        await destStream.WriteAsync(messageBuffer.AsMemory(i, 1), token).ConfigureAwait(false);
                        continue;
                    }

                    byte[] data = cpacket.ToArray();
                    cpacket.Clear();

                    string content = Encoding.UTF8.GetString(data);
                    bool compressed = !outbound && _TryUnpack(content, out content);
                    MessageInfo message = new(content);
                    if (outbound)
                    {
                        Interlocked.Increment(ref _outboundPackets);
                        Interlocked.Add(ref _outboundBytes, data.Length);
                    }
                    else
                    {
                        Interlocked.Increment(ref _inboundPackets);
                        Interlocked.Add(ref _inboundBytes, data.Length);
                    }

                    IInterceptor[] currentInterceptors;
                    lock (Interceptors)
                    {
                        currentInterceptors = Interceptors.Count == 0
                            ? Array.Empty<IInterceptor>()
                            : Interceptors.OrderBy(i => i.Priority).ToArray();
                    }

                    if (currentInterceptors.Length > 0)
                    {
                        long interceptorStart = Stopwatch.GetTimestamp();
                        foreach (IInterceptor interceptor in currentInterceptors)
                        {
                            interceptor.Intercept(message, outbound);
                            Interlocked.Increment(ref _interceptorInvocations);
                        }
                        Interlocked.Add(ref _interceptorElapsedTicks, Stopwatch.GetTimestamp() - interceptorStart);
                    }

                    if (token.IsCancellationRequested || !target.Connected || !destination.Connected)
                        break;

                    if (message.Send)
                    {
                        byte[] contentBytes = message.Content == content ? data
                            : compressed ? _Pack(message.Content) : Encoding.UTF8.GetBytes(message.Content);
                        byte[] msg = new byte[contentBytes.Length + 1];
                        Buffer.BlockCopy(contentBytes, 0, msg, 0, contentBytes.Length);
                        await destStream.WriteAsync(msg, token).ConfigureAwait(false);
                    }
                    else
                    {
                        Interlocked.Increment(ref _forwardingDrops);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* Cancelled */
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            /* Connection closed. */
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _forwardingExceptions);
        }
        finally
        {
            Interlocked.Decrement(ref _activeStreams);
            targetStream?.Dispose();
            destStream?.Dispose();
            try { target.Close(); } catch { }
            try { destination.Close(); } catch { }
        }
    }

    private void ResetDiagnosticsCounters()
    {
        Interlocked.Exchange(ref _outboundBytes, 0);
        Interlocked.Exchange(ref _inboundBytes, 0);
        Interlocked.Exchange(ref _outboundPackets, 0);
        Interlocked.Exchange(ref _inboundPackets, 0);
        Interlocked.Exchange(ref _interceptorInvocations, 0);
        Interlocked.Exchange(ref _interceptorElapsedTicks, 0);
        Interlocked.Exchange(ref _forwardingDrops, 0);
        Interlocked.Exchange(ref _forwardingExceptions, 0);
        Interlocked.Exchange(ref _activeStreams, 0);
    }

    private void RecordDiagnosticsSummary()
    {
        _diagnostics.RecordEvent("Proxy", "OutboundBytes", Interlocked.Read(ref _outboundBytes), "bytes");
        _diagnostics.RecordEvent("Proxy", "InboundBytes", Interlocked.Read(ref _inboundBytes), "bytes");
        _diagnostics.RecordEvent("Proxy", "OutboundPackets", Interlocked.Read(ref _outboundPackets), "packets");
        _diagnostics.RecordEvent("Proxy", "InboundPackets", Interlocked.Read(ref _inboundPackets), "packets");
        _diagnostics.RecordEvent("Proxy", "InterceptorInvocations", Interlocked.Read(ref _interceptorInvocations), "calls");
        long elapsedTicks = Interlocked.Read(ref _interceptorElapsedTicks);
        double elapsedMilliseconds = elapsedTicks * 1000d / Stopwatch.Frequency;
        _diagnostics.RecordEvent("Proxy", "InterceptorTime", elapsedMilliseconds, "ms");
        _diagnostics.RecordEvent("Proxy", "ForwardingDrops", Interlocked.Read(ref _forwardingDrops), "messages");
        _diagnostics.RecordEvent("Proxy", "ForwardingExceptions", Interlocked.Read(ref _forwardingExceptions), "exceptions");
        _diagnostics.RecordEvent("Proxy", "ActiveStreams", Interlocked.Read(ref _activeStreams), "streams");
    }

    private static bool _TryUnpack(string message, out string content)
    {
        content = message;
        if (!message.StartsWith('Z'))
            return false;

        try
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            int end = message.Length;
            while (end > 1 && message[end - 1] == '=')
                end--;

            byte[] compressed = new byte[(end - 1) * 3 / 4];
            if (compressed.Length < 8)
                return false;
            int bits = 0, bitCount = 0, count = 0;
            for (int i = 1; i < end; i++)
            {
                if (message[i] > 255)
                    throw new InvalidDataException("Invalid compressed packet character.");
                // Game4000 maps unknown Base64 characters to zero and permits missing padding.
                bits = bits << 6 | Math.Max(0, alphabet.IndexOf(message[i]));
                bitCount += 6;
                if (bitCount >= 8)
                {
                    bitCount -= 8;
                    compressed[count++] = (byte)(bits >> bitCount);
                }
            }

            using MemoryStream input = new(compressed, 0, count);
            using ZLibStream zlib = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (read > MaxDecompressedPacketBytes - output.Length)
                    throw new PacketTooLargeException();
                output.Write(buffer, 0, read);
            }
            content = Encoding.UTF8.GetString(output.GetBuffer().AsSpan(0, (int)output.Length));
            return true;
        }
        catch (Exception e) when ((e is InvalidDataException or IOException) && e is not PacketTooLargeException)
        {
            // Leave malformed envelopes intact for the game to handle.
            return false;
        }
    }

    // Propagates to the interceptor's IOException handler so oversized packets are not forwarded.
    private sealed class PacketTooLargeException : IOException { }

    private static byte[] _Pack(string content)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(Encoding.UTF8.GetBytes(content));
        return Encoding.ASCII.GetBytes("Z" + Convert.ToBase64String(output.ToArray()));
    }
}