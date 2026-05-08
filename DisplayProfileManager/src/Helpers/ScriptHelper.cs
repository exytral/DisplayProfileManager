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

                bool useShell = extension == ".lnk"; // .lnk files require the Windows Shell to execute

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
                else if (extension == ".py")
                {
                    fileName = "python.exe";
                    finalArguments = $"\"{filePath}\" {cmdArgs}";
                }
                else
                {
                    finalArguments = cmdArgs;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = finalArguments.Trim(),
                    UseShellExecute = useShell, // Set to true if it's a shortcut
                    CreateNoWindow = !useShell, // CreateNoWindow must be false if UseShellExecute is true
                    RedirectStandardError = !useShell // Cannot redirect error stream if using shell execute
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

        public static (string Path, string Args) ParseScriptString(string fullScript)
        {
            if (string.IsNullOrWhiteSpace(fullScript)) return (string.Empty, string.Empty);

            string input = fullScript.Trim();

            if (input.StartsWith("\""))
            {
                int closingQuote = input.IndexOf("\"", 1);
                if (closingQuote > 0)
                {
                    return (
                        input.Substring(1, closingQuote - 1), // Path
                        input.Substring(closingQuote + 1).Trim() // Args
                    );
                }
            }

            int firstSpace = input.IndexOf(' ');
            if (firstSpace == -1) return (input.Replace("\"", ""), string.Empty);

            return (
                input.Substring(0, firstSpace).Replace("\"", ""),
                input.Substring(firstSpace).Trim()
            );
        }
    }
}