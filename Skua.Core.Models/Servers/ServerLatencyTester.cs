using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Skua.Core.Models.Servers;

/// <summary>
/// Measures AQW server latency using the same verChk/apiOK handshake used by the Manager.
/// </summary>
public static class ServerLatencyTester
{
    private const int TimeoutMs = 2500;
    private const int SampleCount = 3;
    private static readonly byte[] VersionCheck = Encoding.UTF8.GetBytes("<msg t='sys'><body action='verChk' r='0'><ver v='165' /></body></msg>\0");

    /// <summary>
    /// Measures each server concurrently and stores its median valid handshake latency and status indicator.
    /// </summary>
    public static async Task MeasureAsync(IEnumerable<Server> servers, int thresholdMs = ServerLatencyClassifier.DefaultThresholdMs, int nearThresholdPercent = ServerLatencyClassifier.DefaultNearThresholdPercent, CancellationToken cancellationToken = default)
    {
        int safeThreshold = Math.Clamp(thresholdMs, 50, 2000);
        int safeNearPercent = Math.Clamp(nearThresholdPercent, 50, 95);
        Task[] tasks = servers.Select(server => MeasureServerAsync(server, safeThreshold, safeNearPercent, cancellationToken)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task MeasureServerAsync(Server server, int thresholdMs, int nearThresholdPercent, CancellationToken cancellationToken)
    {
        if (!server.Online || string.IsNullOrWhiteSpace(server.IP) || server.Port <= 0 || server.Port > 65535)
        {
            SetUnavailable(server);
            return;
        }

        List<double> samples = new(SampleCount);

        for (int sample = 0; sample < SampleCount && !cancellationToken.IsCancellationRequested; sample++)
        {
            try
            {
                // Measure the complete connection and AQW handshake, not just the server's response after send.
                Stopwatch stopwatch = Stopwatch.StartNew();
                using TcpClient client = new();
                await client.ConnectAsync(server.IP, server.Port).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs), cancellationToken).ConfigureAwait(false);

                using NetworkStream stream = client.GetStream();
                await stream.WriteAsync(VersionCheck.AsMemory(), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeoutMs);
                byte[] buffer = new byte[1];
                StringBuilder response = new();

                while (!timeout.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;

                    if (buffer[0] != 0)
                    {
                        response.Append((char)buffer[0]);
                        continue;
                    }

                    string message = response.ToString();
                    response.Clear();

                    if (message.Contains("action='apiOK'", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("action=\"apiOK\"", StringComparison.OrdinalIgnoreCase))
                    {
                        stopwatch.Stop();
                        samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                        break;
                    }

                    if (message.Contains("action='apiKO'", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("action=\"apiKO\"", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A timed-out sample is invalid; try the next sample.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // A failed connection or missing protocol response is not a valid latency sample.
            }
        }

        if (samples.Count == 0)
        {
            SetUnavailable(server);
            return;
        }

        samples.Sort();
        server.Ping = (long)Math.Round(samples[samples.Count / 2], MidpointRounding.AwayFromZero);
        server.LatencyStatus = ServerLatencyClassifier.Classify(server.Ping, thresholdMs, nearThresholdPercent);
    }

    private static void SetUnavailable(Server server)
    {
        server.Ping = ServerLatencyClassifier.FailedPing;
        server.LatencyStatus = ServerLatencyStatus.Unavailable;
    }
}
