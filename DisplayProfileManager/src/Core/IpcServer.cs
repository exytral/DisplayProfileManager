using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Core
{
    public static class IpcServer
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private const string PipeNameBase = "DPM_IpcPipe";
        public static string PipeName { get; } = BuildPipeName(Process.GetCurrentProcess().SessionId);
        public static string BuildPipeName(int sessionId) => $"{PipeNameBase}.{sessionId}";

        public static void StartListening(CancellationToken token, Func<string, Task> onMessage)
        {
            Task.Run(async () =>
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServer();

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await server.WaitForConnectionAsync(token);

                            using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
                            {
                                string receivedValue = await reader.ReadToEndAsync();
                                if (!string.IsNullOrEmpty(receivedValue))
                                    await onMessage(receivedValue);
                            }

                            server.Disconnect();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "IPC pipe listener error");
                            server.Dispose();
                            server = CreateServer();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally { server?.Dispose(); }
            }, token);
        }

        public static async Task<bool> SendAsync(string message)
        {
            NamedPipeClientStream client = null;
            try
            {
                client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                await client.ConnectAsync(2000);

                using (var writer = new StreamWriter(client))
                {
                    await writer.WriteAsync(message);
                    await writer.FlushAsync();
                }

                return true;
            }
            catch
            {
                client?.Dispose();
                return false;
            }
        }

        private static NamedPipeServerStream CreateServer() => new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }
}