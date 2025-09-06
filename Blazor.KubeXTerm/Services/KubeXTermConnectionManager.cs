using System.Collections.Concurrent;
using k8s;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using XtermBlazor;

namespace Blazor.KubeXTerm.Services
{
    public class KubeXTermConnectionManager : IAsyncDisposable
    {
        private readonly IKubernetes _k8SContext;
        private readonly string _namespace;
        private Xterm? _attachedTerminal;
        private Stream? _stdinStream;
        private WebSocket? _webSocket;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _disposedValue;

        // Simple ring buffer of recent output (bytes capped)
        private readonly ConcurrentQueue<string> _history = new();
        private const int MaxHistoryBytes = 256 * 1024;
        private int _historyBytes;

        // When true, we are in an interactive TTY session (e.g., vim/htop). In this mode
        // we should not buffer/replay output because TUIs send incremental escape sequences.
        private bool _isInteractiveTty;

        // Add this field near your other private fields
        private string _lineRemainder = string.Empty;

        public KubeXTermConnectionManager(IKubernetes k8SContext, string @namespace)
        {
            _k8SContext = k8SContext;
            _namespace = @namespace;
        }

        // Back-compat ctor (kept; delegates to new pattern)
        public KubeXTermConnectionManager(Xterm term, IKubernetes k8SContext, string @namespace) : this(k8SContext, @namespace)
        {
            AttachTerminal(term);
        }

        public async Task AttachTerminal(Xterm term)
        {
            _attachedTerminal = term;

            Console.WriteLine($"AttachTerminal: _isInteractiveTty={_isInteractiveTty}, History count={_history.Count}, History bytes={_historyBytes}");

            // For non-interactive sessions, restore history immediately in a single write
            if (!_isInteractiveTty && _history.Count > 0)
            {
                try
                {
                    var sb = new StringBuilder();
                    foreach (var chunk in _history)
                        sb.Append(chunk);

                    // Include any partial trailing line that hasn't been terminated yet
                    if (!string.IsNullOrEmpty(_lineRemainder))
                        sb.Append(_lineRemainder);

                    await _attachedTerminal.Write(sb.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error replaying history: {ex.Message}");
                }
            }

            // For interactive sessions (TTY/TUIs), do nothing here.
        }

        public void DetachTerminal()
        {
            _attachedTerminal = null;
        }

        private void AppendHistory(string chunk)
        {
            // Do not buffer interactive TTY output; TUIs use incremental escape sequences.
            if (_isInteractiveTty) return;

            // Accumulate by lines so we can repaint cleanly on reattach
            var text = (_lineRemainder ?? string.Empty) + (chunk ?? string.Empty);
            _lineRemainder = string.Empty;

            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    // Include the newline in the stored line
                    string line = text.Substring(start, i - start + 1);

                    _history.Enqueue(line);
                    _historyBytes += Encoding.UTF8.GetByteCount(line);

                    // Trim by bytes cap
                    while (_historyBytes > MaxHistoryBytes && _history.TryDequeue(out var removed))
                    {
                        _historyBytes -= Encoding.UTF8.GetByteCount(removed);
                    }

                    start = i + 1;
                }
            }

            // Keep any trailing partial line to be completed by a future chunk
            if (start < text.Length)
            {
                _lineRemainder = text.Substring(start);
            }
        }

        public async Task SendResizeCommandAsync(int rows, int cols)
        {
            try
            {
                if (_webSocket is { State: WebSocketState.Open })
                {
                    var resizePayload = JsonSerializer.Serialize(new { Height = rows, Width = cols });
                    var message = new byte[1 + Encoding.UTF8.GetByteCount(resizePayload)];
                    message[0] = 4; // resize channel
                    Encoding.UTF8.GetBytes(resizePayload, 0, resizePayload.Length, message, 1);
                    await _webSocket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending resize command: {ex.Message}");
            }
        }

        public async Task ExecInPod(string podname, string? containerName, string[] cmd)
        {
            _isInteractiveTty = true; // interactive TTY session
            _webSocket = await _k8SContext.WebSocketNamespacedPodExecAsync(
                name: podname, @namespace: _namespace, container: containerName,
                command: cmd, stderr: true, stdin: true, stdout: true, tty: true
            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            // Get stdin, stdout, and stderr streams
            byte stdinIndex = 0;

            _stdinStream = demux.GetStream(stdinIndex, stdinIndex);
            var stdoutStream = demux.GetStream(1, 1);
            var stderrStream = demux.GetStream(2, 2);

            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));
            await Task.WhenAny(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!_disposedValue)
            {
                AppendHistory("\r\nWebSocket connection closed.\r\n");
                var t = _attachedTerminal;
                if (t != null) { try { await t.WriteLine("WebSocket connection closed."); } catch { } }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="podname"></param>
        /// <returns></returns>
        public async Task StdOutInPod(string podname, string containerName)
        {
            _webSocket = await _k8SContext.WebSocketNamespacedPodAttachAsync(
                name: podname,
                @namespace: _namespace,
                container: containerName,
                stderr: false,
                stdin: false,
                stdout: true,
                tty: false // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            // Get stdin
            var stdoutStream = demux.GetStream(1, 1); // Stdout channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask).ConfigureAwait(false);

            if (!_disposedValue)
            try
            {
                await _attachedTerminal.WriteLine("WebSocket connection closed.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
            }
        }

        public async Task StdErrInPod(string podname, string? containerName)
        {
            _isInteractiveTty = false; // stderr only (non-interactive)
            _webSocket = await _k8SContext.WebSocketNamespacedPodAttachAsync(
                name: podname, @namespace: _namespace, container: containerName,
                stderr: true, stdin: false, stdout: false, tty: false
            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            var stderrStream = demux.GetStream(2, 2);
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));
            await Task.WhenAny(stderrTask).ConfigureAwait(false);

            if (!_disposedValue)
            {
                AppendHistory("\r\nWebSocket connection closed.\r\n");
                var t = _attachedTerminal;
                if (t != null) { try { await t.WriteLine("WebSocket connection closed."); } catch { } }
            }
        }

        public async Task AllLogsAsync(string podname, string? containerName)
        {
            _isInteractiveTty = false; // log stream (non-interactive)
            var taskStream = _k8SContext.CoreV1.ReadNamespacedPodLogAsync(podname, _namespace, follow: true);
            Stream logStream = await taskStream;
            var logTask = Task.Run(async () => await ReadStream(logStream).ConfigureAwait(false));
            await Task.WhenAny(logTask).ConfigureAwait(false);

            if (!_disposedValue)
            {
                AppendHistory("\r\nLog stream closed.\r\n");
                var t = _attachedTerminal;
                if (t != null) { try { await t.WriteLine("WebSocket connection closed."); } catch { } }
            }
        }

        public async Task LogsInPodAync(string podname, string? containerName)
        {
            _isInteractiveTty = false; // combined stdout+stderr logs (non-interactive)
            _webSocket = await _k8SContext.WebSocketNamespacedPodAttachAsync(
                name: podname, @namespace: _namespace, container: containerName,
                stderr: true, stdin: false, stdout: true, tty: false
            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            var stdoutStream = demux.GetStream(1, 1);
            var stderrStream = demux.GetStream(2, 2);
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));
            await Task.WhenAny(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!_disposedValue)
            {
                AppendHistory("\r\nWebSocket connection closed.\r\n");
                var t = _attachedTerminal;
                if (t != null) { try { await t.WriteLine("WebSocket connection closed."); } catch { } }
            }
        }

        public async Task WriteStream(string input)
        {
            try
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                if (_stdinStream != null)
                {
                    await _stdinStream.WriteAsync(inputBytes, 0, inputBytes.Length).ConfigureAwait(false);
                    await _stdinStream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppendHistory($"\r\nError writing to stream: {ex.Message}\r\n");
                var t = _attachedTerminal;
                if (t != null) { try { await t.WriteLine($"Error writing to stream: {ex.Message}"); } catch { } }
            }
        }

        public async Task WriteByte(byte b)
        {
            try
            {
                if (_stdinStream == null) return;
                _stdinStream.WriteByte(b);
                await _stdinStream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendHistory($"\r\nError writing byte to stream: {ex.Message}\r\n");
                var t = _attachedTerminal;
                if (t != null) { try { await t.WriteLine($"Error writing byte to stream: {ex.Message}"); } catch { } }
            }
        }

        public async Task CloseStreams()
        {
            try
            {
                _stdinStream?.Dispose();
                if (_webSocket != null)
                {
                    try { await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", _cancellationTokenSource.Token); } catch { }
                    _webSocket.Dispose();
                }
            }
            catch { /* ignore */ }
        }

        private async Task ReadStream(Stream stream)
        {
            var buffer = new byte[4096];
            while (true)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (bytesRead == 0) break;
                    var outputText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    AppendHistory(outputText);
                    var t = _attachedTerminal;
                    if (t != null)
                    {
                        try { await t.Write(outputText); } catch { /* view may be gone; ignore */ }
                    }
                }
                catch (Exception ex)
                {
                    AppendHistory($"\r\nError reading stream: {ex.Message}\r\n");
                    var t = _attachedTerminal;
                    if (t != null) { try { await t.WriteLine($"Error reading stream: {ex.Message}"); } catch { } }
                    break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposedValue) return;
            _disposedValue = true;
            try
            {
                await CloseStreams();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dispose error: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
        }

        // Add this new method
        public async Task ForceRedrawAsync()
        {
            if (_attachedTerminal != null && _isInteractiveTty && _webSocket is { State: WebSocketState.Open })
            {
                try
                {
                    int rows = await _attachedTerminal.GetRows();
                    int cols = await _attachedTerminal.GetColumns();
                    
                    // Force TUI redraw by sending a slight resize then correct size
                    await SendResizeCommandAsync(rows - 1, cols);
                    await Task.Delay(50);
                    await SendResizeCommandAsync(rows, cols);
                }
                catch { /* ignore */ }
            }
        }
    }
}
