using k8s;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using XtermBlazor;
using System.Runtime.ConstrainedExecution;

namespace Blazor.KubeXTerm.Services
{
    internal class KubeXTermConnectionManager : IAsyncDisposable
    {
        private IKubernetes _k8SContext;
        private string _namespace = "default";
        public Xterm WebTerminal;
        private Stream _stdinStream;
        WebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private bool _disposedValue;


        public KubeXTermConnectionManager(Xterm term, IKubernetes k8SContext, string @namespace)
        {
            WebTerminal = term;
            this._k8SContext = k8SContext;
            _namespace = @namespace;
        }


        /// <summary>
        /// Sends the resize command to the kubernetes websocket on channel 4
        /// Kinda irritating this is not a function in the .net K8s api already
        /// </summary>
        /// <param name="webSocket"></param>
        /// <param name="cols"></param>
        /// <param name="rows"></param>
        /// <returns></returns>
        public async Task SendResizeCommandAsync(int rows, int cols)
        {
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    // Kubernetes expects JSON payload for resize
                    var resizePayload = new
                    {
                        Height = rows,
                        Width = cols
                    };

                    // Convert the payload to JSON
                    var jsonPayload = JsonSerializer.Serialize(resizePayload);

                    // Create the message for channel 4 (resize)
                    var message = new byte[1 + Encoding.UTF8.GetByteCount(jsonPayload)];
                    message[0] = 4; // Channel 4 for resize
                    Encoding.UTF8.GetBytes(jsonPayload, 0, jsonPayload.Length, message, 1);

                    // Send the message as a binary frame
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Binary,
                        true,
                        CancellationToken.None
                    );
                }
            }
            catch (Exception ex)
            {   
                Console.WriteLine($"Error sending resize command: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a /bin/bash in the target pod
        ///  ////TODO Change to id of some kind instead of name
        /// </summary>
        /// <param name="podname"></param>
        /// <returns></returns>
        public async Task ExecInPod(string podname, string containerName, string[] cmd)
        {
            //var cmd = new[] { "/bin/bash" }; // Use an interactive shell for command execution
            _webSocket = await _k8SContext.WebSocketNamespacedPodExecAsync(
                name: podname,
                @namespace: _namespace,
                container: containerName,
                command: cmd,
                stderr: true,
                stdin: true,
                stdout: true,
                tty: true // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            // Get stdin, stdout, and stderr streams
            byte stdinIndex = 0;
            _stdinStream = demux.GetStream(stdinIndex, stdinIndex); // Stdin channel
            var stdoutStream = demux.GetStream(1, 1); // Stdout channel
            var stderrStream = demux.GetStream(2, 2); // Stderr channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!_disposedValue)
                try
                {
                    await WebTerminal.WriteLine("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
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
                    await WebTerminal.WriteLine("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
                }
        }

        /// <summary>
        /// Only displays stderr in the XTerm window
        /// </summary>
        /// <param name="podname"></param>
        /// <param name="containerName"></param>
        /// <returns></returns>
        public async Task StdErrInPod(string podname, string containerName)
        {
            _webSocket = await _k8SContext.WebSocketNamespacedPodAttachAsync(
                name: podname,
                @namespace: _namespace,
                container: containerName,
                stderr: true,
                stdin: false,
                stdout: false,
                tty: false // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            // Get stdin
            var stderrStream = demux.GetStream(2, 1); // Stdout channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask).ConfigureAwait(false);

            if (!_disposedValue)
                try
                {
                    await WebTerminal.WriteLine("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
                }
        }

        /// <summary>
        /// Get logs from the very beginning //TODO configure a limit?
        /// </summary>
        /// <param name="podname"></param>
        /// <param name="containerName"></param>
        /// <returns></returns>
        public async Task AllLogsAsync(string podname, string containerName)
        {
            var taskStream = _k8SContext.CoreV1.ReadNamespacedPodLogAsync(podname, _namespace, follow: true);

            // Await the task to get the actual stream
            Stream logStream = await taskStream;

            // Start tasks for reading stdout and stderr
            var logTask = Task.Run(async () => await ReadStream(logStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(logTask).ConfigureAwait(false);

            if (!_disposedValue)
                try
                {
                    await WebTerminal.WriteLine("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
                }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="podname"></param>
        /// <returns></returns>
        public async Task LogsInPodAync(string podname, string containerName)
        {

            _webSocket = await _k8SContext.WebSocketNamespacedPodAttachAsync(
                name: podname,
                @namespace: _namespace,
                container: containerName,
                stderr: true,
                stdin: false,
                stdout: true,
                tty: false // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(_webSocket);
            demux.Start();

            // Get stdin, stdout, and stderr streams
            var stdoutStream = demux.GetStream(1, 1); // Stdout channel
            var stderrStream = demux.GetStream(2, 1); // Stderr channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!_disposedValue)
                try
                {
                    await WebTerminal.WriteLine("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
                }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public async Task WriteStream(string input)
        {
            try
            {
                // Write input to the stdin stream
                var inputBytes = Encoding.UTF8.GetBytes(input);
                await _stdinStream.WriteAsync(inputBytes, 0, inputBytes.Length).ConfigureAwait(false);
                await _stdinStream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WebTerminal.WriteLine($"Error writing to stream: {ex.Message}");

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public async Task WriteByte(byte b)
        {
            try
            {
                // Write input to the stdin stream, if sdin is null, we are just looking at logs
                if (_stdinStream == null)
                    return;
                _stdinStream.WriteByte(b);
                await _stdinStream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WebTerminal.WriteLine($"Error writing byte to stream: {ex.Message}");

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task CloseStreams()
        {
            //TODO test if streams are open first?
            _stdinStream?.Close();
            _stdinStream?.Dispose();
            _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", _cancellationTokenSource.Token);
            _webSocket?.Dispose();
        }

        /// <summary>
        /// Reads a stream and immediately writes it in the web terminal
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private async Task ReadStream(System.IO.Stream stream)
        {
            var buffer = new byte[4096];
            while (true)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        // Stream might have closed
                        break;
                    }

                    var outputText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    await WebTerminal.Write(outputText);
                }
                catch (Exception ex)
                {
                    await WebTerminal.WriteLine($"Error reading stream: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// Dispose of all the streams etc. //TODO: figure out known issue with the zombie bash 
        /// processes left behind when closing the web terminals. 
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            if (!_disposedValue)
            {
                try
                {
                    //Apparently this is not how kubectl even does this. Zombies are just ok?
                    // Send Ctrl+C (SIGINT) to interrupt the process in the terminal
                    //await WriteByte(0x03); // Ctrl+C (SIGINT)
                    // Send Ctrl+D (EOF) to simulate EOF or exit signal
                    //await WriteByte(0x04); // Ctrl+D (EOF)
                    _disposedValue = true;
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that might occur during stream write operations
                    Console.WriteLine($"Error while sending Ctrl signals: {ex.Message}");
                }
                finally
                {
                    _disposedValue = true;
                    await CloseStreams();
                }
            }
        }
    }
}
