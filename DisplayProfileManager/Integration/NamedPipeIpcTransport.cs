using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Integration
{
    internal enum IpcAuthorityKind
    {
        Normal,
        Dev
    }

    public static class NamedPipeIpcTransport
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private const int MaxMessageChars = 65536;
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

        private const string PipeNameBase = "DPM_IpcPipe";
        private const string DevPipeNameBase = "DPM_IpcPipe.Dev";
        public static string PipeName { get; } = BuildPipeName(Process.GetCurrentProcess().SessionId, IpcAuthorityKind.Normal);
        internal static string DevPipeName { get; } = BuildPipeName(Process.GetCurrentProcess().SessionId, IpcAuthorityKind.Dev);
        public static string BuildPipeName(int sessionId) => BuildPipeName(sessionId, IpcAuthorityKind.Normal);
        internal static string BuildPipeName(int sessionId, IpcAuthorityKind authority) => $"{(authority == IpcAuthorityKind.Dev ? DevPipeNameBase : PipeNameBase)}.{sessionId}";

        internal static IpcAuthorityKind[] GetProbeOrder(bool devMode, bool isExit) => devMode ? [IpcAuthorityKind.Dev] : isExit ? [IpcAuthorityKind.Normal, IpcAuthorityKind.Dev] : [IpcAuthorityKind.Normal];

        public static void StartListening(CancellationToken token, Func<string, Task> onMessage) => StartListening(token, onMessage, IpcAuthorityKind.Normal);

        internal static void StartListening(CancellationToken token, Func<string, Task> onMessage, IpcAuthorityKind authority) =>
            StartListening(token, onMessage, authority == IpcAuthorityKind.Dev ? DevPipeName : PipeName);

        internal static void StartListening(CancellationToken token, Func<string, Task> onMessage, string pipeName)
        {
            Task.Run(async () =>
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServer(pipeName);

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await server.WaitForConnectionAsync(token);

                            using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
                            {
                                using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                                readDeadline.CancelAfter(ReadTimeout);
                                var buffer = new char[4096];
                                var message = new StringBuilder();
                                int count;
                                while ((count = await reader.ReadAsync(buffer.AsMemory(), readDeadline.Token)) > 0)
                                {
                                    if (message.Length + count > MaxMessageChars)
                                        throw new InvalidDataException("IPC input exceeds the message size limit.");
                                    message.Append(buffer, 0, count);
                                }
                                if (message.Length > 0)
                                    await onMessage(message.ToString());
                            }

                            server.Disconnect();
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "IPC pipe listener error");
                            server.Dispose();
                            server = null;
                            if (token.IsCancellationRequested)
                                break;
                            try
                            {
                                server = CreateServer(pipeName);
                            }
                            catch (Exception recreateError)
                            {
                                _logger.Error(recreateError, "IPC pipe listener recreation failed");
                                await Task.Delay(100, token);
                                server = CreateServer(pipeName);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.Error(ex, "IPC pipe listener terminated unexpectedly"); }
                finally { server?.Dispose(); }
            }, token);
        }

        public static Task<bool> SendAsync(string message) => SendAsync(message, IpcAuthorityKind.Normal, 2000);

        internal static Task<bool> SendAsync(string message, int connectTimeoutMilliseconds) => SendAsync(message, IpcAuthorityKind.Normal, connectTimeoutMilliseconds);

        internal static Task<bool> SendAsync(string message, IpcAuthorityKind authority, int connectTimeoutMilliseconds = 2000) =>
            SendAsync(message, authority == IpcAuthorityKind.Dev ? DevPipeName : PipeName, connectTimeoutMilliseconds);

        internal static async Task<bool> SendAsync(string message, string pipeName, int connectTimeoutMilliseconds = 2000)
        {
            NamedPipeClientStream client = null;
            try
            {
                client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(connectTimeoutMilliseconds);

                using var writer = new StreamWriter(client);
                await writer.WriteAsync(message);
                await writer.FlushAsync();

                return true;
            }
            catch
            {
                client?.Dispose();
                return false;
            }
        }

        private static NamedPipeServerStream CreateServer(string pipeName) =>
            new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }
}
