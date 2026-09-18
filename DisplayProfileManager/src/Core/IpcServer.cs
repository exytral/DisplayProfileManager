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
    internal enum IpcAuthorityKind
    {
        Normal,
        Dev
    }

    public static class IpcServer
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private const string PipeNameBase = "DPM_IpcPipe";
        private const string DevPipeNameBase = "DPM_IpcPipe.Dev";
        public static string PipeName { get; } = BuildPipeName(Process.GetCurrentProcess().SessionId, IpcAuthorityKind.Normal);
        internal static string DevPipeName { get; } = BuildPipeName(Process.GetCurrentProcess().SessionId, IpcAuthorityKind.Dev);
        public static string BuildPipeName(int sessionId) => BuildPipeName(sessionId, IpcAuthorityKind.Normal);
        internal static string BuildPipeName(int sessionId, IpcAuthorityKind authority) => $"{(authority == IpcAuthorityKind.Dev ? DevPipeNameBase : PipeNameBase)}.{sessionId}";

        internal static IpcAuthorityKind[] GetProbeOrder(bool devMode, bool isExit) => devMode ? new[] { IpcAuthorityKind.Dev } : isExit ? new[] { IpcAuthorityKind.Normal, IpcAuthorityKind.Dev } : new[] { IpcAuthorityKind.Normal };

        public static void StartListening(CancellationToken token, Func<string, Task> onMessage) => StartListening(token, onMessage, IpcAuthorityKind.Normal);

        internal static void StartListening(CancellationToken token, Func<string, Task> onMessage, IpcAuthorityKind authority)
        {
            Task.Run(async () =>
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServer(authority);

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
                            server = CreateServer(authority);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally { server?.Dispose(); }
            }, token);
        }

        public static Task<bool> SendAsync(string message) => SendAsync(message, IpcAuthorityKind.Normal, 2000);

        internal static Task<bool> SendAsync(string message, int connectTimeoutMilliseconds) => SendAsync(message, IpcAuthorityKind.Normal, connectTimeoutMilliseconds);

        internal static async Task<bool> SendAsync(string message, IpcAuthorityKind authority, int connectTimeoutMilliseconds = 2000)
        {
            NamedPipeClientStream client = null;
            try
            {
                string pipeName = authority == IpcAuthorityKind.Dev ? DevPipeName : PipeName;
                client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(connectTimeoutMilliseconds);

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

        private static NamedPipeServerStream CreateServer(IpcAuthorityKind authority) =>
            new NamedPipeServerStream(authority == IpcAuthorityKind.Dev ? DevPipeName : PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }
}