using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DisplayProfileManager.Helpers
{
    public class ScriptHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        public async Task ExecuteScriptAsync(string filePath, string cmdArgs = "")
        {
            try
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
                    finalArguments = cmdArgs;

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = finalArguments.Trim(),
                    UseShellExecute = useShell,
                    CreateNoWindow = !useShell,
                    RedirectStandardError = !useShell
                };

                await Task.Run(() =>
                {
                    Process.Start(psi);
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Script execution error: {filePath}");
            }
        }
    }
}