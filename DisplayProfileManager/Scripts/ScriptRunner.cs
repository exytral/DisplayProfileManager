using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Scripts.Models;

namespace DisplayProfileManager.Scripts
{
    public sealed class ScriptRunner : IScriptRunner
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly ScriptLibrary _library;

        internal ScriptRunner(ScriptLibrary library)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
        }

        public void ExecuteScript(Script script)
        {
            if (script == null || !script.IsEnabled || string.IsNullOrWhiteSpace(script.FileName)) return;

            if (!_library.TryResolveSandboxedScriptPath(script.FileName, out string path))
            {
                _logger.Warn("Rejected script path outside sandbox: " + script.FileName);
                return;
            }

            if (!File.Exists(path))
            {
                _logger.Warn("Script not found: " + script.FileName);
                return;
            }

            string argsLog = !string.IsNullOrEmpty(script.Arguments) ? " " + script.Arguments : "";
            _ = ExecuteScriptAsync(path, script.Arguments);
            _logger.Info("Executed: " + script.FileName + argsLog);
        }

        public async Task ExecuteScriptAsync(string filePath, string cmdArgs = "")
        {
            try
            {
                var psi = BuildProcessStartInfo(filePath, cmdArgs);
                await Task.Run(() => StartProcess(psi));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Script execution error: {filePath}");
            }
        }

        internal static ProcessStartInfo BuildProcessStartInfo(string filePath, string cmdArgs)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            string fileName = filePath;
            string finalArguments = "";
            bool useShell = extension == ".lnk";

            if (extension == ".ps1")
            {
                fileName = "powershell.exe";
                finalArguments = $"-ExecutionPolicy Bypass -File \"{filePath}\" {cmdArgs}";
            }
            else if (extension == ".bat" || extension == ".cmd")
            {
                fileName = "cmd.exe";
                finalArguments = $"/c \"{filePath}\" {cmdArgs}";
            }
            else if (extension == ".vbs" || extension == ".js")
            {
                fileName = "cscript.exe";
                finalArguments = $"/nologo \"{filePath}\" {cmdArgs}";
            }
            else if (extension == ".py")
            {
                fileName = "python.exe";
                finalArguments = $"\"{filePath}\" {cmdArgs}";
            }
            else if (extension == ".ahk")
            {
                fileName = "autohotkey.exe";
                finalArguments = $"\"{filePath}\" {cmdArgs}";
            }
            else
            {
                finalArguments = cmdArgs;
            }

            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = finalArguments.Trim(),
                UseShellExecute = useShell,
                CreateNoWindow = !useShell
            };
        }

        internal static void StartProcess(
            ProcessStartInfo startInfo,
            Func<ProcessStartInfo, IDisposable> startProcess = null)
        {
            using var process = (startProcess ?? DefaultStartProcess)(startInfo);
        }

        private static IDisposable DefaultStartProcess(ProcessStartInfo startInfo) =>
            Process.Start(startInfo);
    }
}
