using k8s;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using XtermBlazor;

namespace MudBlazor.KubeXTerm.Services
{
    internal class KubeXTermConnectionManager : IAsyncDisposable
    {
        private IKubernetes K8sContext;
        private string Namespace = "default";
        public Xterm webTerminal;
        private Stream stdinStream;
        WebSocket webSocket;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private bool disposedValue;


        public KubeXTermConnectionManager(Xterm term, IKubernetes k8sContext, string @namespace)
        {
            webTerminal = term;
            this.K8sContext = k8sContext;
            Namespace = @namespace;
        }


        /// <summary>
        /// Sends the resize command to the kubernetes websocket on channel 4
        /// Kinda irritating this is noto a function in the .net K8s api already
        /// </summary>
        /// <param name="webSocket"></param>
        /// <param name="cols"></param>
        /// <param name="rows"></param>
        /// <returns></returns>
        public async Task SendResizeCommandAsync(int rows, int cols)
        {
            if (webSocket != null && webSocket.State == WebSocketState.Open)
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
                await webSocket.SendAsync(
                    new ArraySegment<byte>(message),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None
                );
            }
        }

        /// <summary>
        /// Executes a /bin/bash in the target pod
        ///  ////TODO Change to id of some kind instead of name
        /// </summary>
        /// <param name="podname"></param>
        /// <returns></returns>
        public async Task ExecInPod(string podname, string containerName)
        {
            //TODO: use a custom non-root user
            //var cmd = new[] {"/bin/bash", "-c", "gosu customuser /bin/bash" }; // Use an interactive shell for command execution
            var cmd = new[] { "/bin/bash" }; // Use an interactive shell for command execution
            webSocket = await K8sContext.WebSocketNamespacedPodExecAsync(
                name: podname,
                @namespace: Namespace,
                container: containerName,
                command: cmd,
                stderr: true,
                stdin: true,
                stdout: true,
                tty: true // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(webSocket);
            demux.Start();

            // Get stdin, stdout, and stderr streams
            byte stdinIndex = 0;
            stdinStream = demux.GetStream(stdinIndex, stdinIndex); // Stdin channel
            var stdoutStream = demux.GetStream(1, 1); // Stdout channel
            var stderrStream = demux.GetStream(2, 2); // Stderr channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!disposedValue)
                try
                {
                    await webTerminal.WriteLine("WebSocket connection closed.");
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
            webSocket = await K8sContext.WebSocketNamespacedPodAttachAsync(
                name: podname,
                @namespace: Namespace,
                container: containerName,
                stderr: false,
                stdin: false,
                stdout: true,
                tty: false // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(webSocket);
            demux.Start();

            // Get stdin
            var stdoutStream = demux.GetStream(1, 1); // Stdout channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask).ConfigureAwait(false);

            if (!disposedValue)
                try
                {
                    await webTerminal.WriteLine("WebSocket connection closed.");
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
            webSocket = await K8sContext.WebSocketNamespacedPodAttachAsync(
                name: podname,
                @namespace: Namespace,
                container: containerName,
                stderr: true,
                stdin: false,
                stdout: false,
                tty: false // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(webSocket);
            demux.Start();

            // Get stdin
            var stderrStream = demux.GetStream(2, 1); // Stdout channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask).ConfigureAwait(false);

            if (!disposedValue)
                try
                {
                    await webTerminal.WriteLine("WebSocket connection closed.");
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
            var taskStream = K8sContext.CoreV1.ReadNamespacedPodLogAsync(podname, Namespace, follow: true);

            // Await the task to get the actual stream
            Stream logStream = await taskStream;

            // Start tasks for reading stdout and stderr
            var logTask = Task.Run(async () => await ReadStream(logStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(logTask).ConfigureAwait(false);

            if (!disposedValue)
                try
                {
                    await webTerminal.WriteLine("WebSocket connection closed.");
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

            webSocket = await K8sContext.WebSocketNamespacedPodAttachAsync(
                name: podname,
                @namespace: Namespace,
                container: containerName,
                stderr: true,
                stdin: false,
                stdout: true,
                tty: false // TTY is true to enable interactive terminal

            ).ConfigureAwait(false);

            var demux = new StreamDemuxer(webSocket);
            demux.Start();

            // Get stdin, stdout, and stderr streams
            var stdoutStream = demux.GetStream(1, 1); // Stdout channel
            var stderrStream = demux.GetStream(2, 1); // Stderr channel

            // Start tasks for reading stdout and stderr
            var stdoutTask = Task.Run(async () => await ReadStream(stdoutStream).ConfigureAwait(false));
            var stderrTask = Task.Run(async () => await ReadStream(stderrStream).ConfigureAwait(false));

            // Wait for any one of the tasks to complete or error. This can happen if a user exits bash or if there is an error
            await Task.WhenAny(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!disposedValue)
                try
                {
                    await webTerminal.WriteLine("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while writing to web terminal, tab is probably closed");
                }
        }

        /// <summary>
        /// Reads a stream and immediately writes it in the web terminal
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public async Task ReadStream(System.IO.Stream stream)
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
                    await webTerminal.Write(outputText);
                }
                catch (Exception ex)
                {
                    await webTerminal.WriteLine($"Error reading stream: {ex.Message}");
                    break;
                }
            }
        }

        public async Task WriteStream(string input)
        {
            try
            {
                // Write input to the stdin stream
                var inputBytes = Encoding.UTF8.GetBytes(input);
                await stdinStream.WriteAsync(inputBytes, 0, inputBytes.Length).ConfigureAwait(false);
                await stdinStream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await webTerminal.WriteLine($"Error writing to stream: {ex.Message}");

            }
        }

        public async Task WriteByte(byte b)
        {
            try
            {
                // Write input to the stdin stream, if sdin is null, we are just looking at logs
                if (stdinStream == null)
                    return;
                stdinStream.WriteByte(b);
                await stdinStream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await webTerminal.WriteLine($"Error writing byte to stream: {ex.Message}");

            }
        }

        public async Task CloseStreams()
        {
            //TODO test if streams are open first?
            stdinStream?.Close();
            stdinStream?.Dispose();
            webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", _cancellationTokenSource.Token);
            webSocket?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!disposedValue)
                {
                    // Send Ctrl+C (SIGINT) to interrupt the process in the terminal
                    await WriteByte(0x03); // Ctrl+C (SIGINT)
                    // Send Ctrl+D (EOF) to simulate EOF or exit signal
                    await WriteByte(0x04); // Ctrl+D (EOF)
                    disposedValue = true;

                    await CloseStreams();
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that might occur during stream write operations
                Console.WriteLine($"Error while sending Ctrl signals: {ex.Message}");
            }
        }
    }
}
