using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    public class ScriptManager
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private static readonly Lazy<ScriptManager> _instance = new Lazy<ScriptManager>(() => new ScriptManager());
        private readonly string _scriptsFolderPath;
        private readonly ScriptHelper _scriptHelper;
        public static ScriptManager Instance => _instance.Value;
        public string ScriptsFolderPath => _scriptsFolderPath;

        private ScriptManager()
        {
            _scriptHelper = new ScriptHelper();

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager");

            _scriptsFolderPath = Path.Combine(appData, "Scripts");
            if (!Directory.Exists(_scriptsFolderPath))
                Directory.CreateDirectory(_scriptsFolderPath);
        }

        private void EnsureScriptsFolderExists()
        {
            if (!Directory.Exists(_scriptsFolderPath))
                Directory.CreateDirectory(_scriptsFolderPath);
        }

        #region Public Methods

        public void ExecuteScript(Script script)
        {
            if (script == null || !script.IsEnabled || string.IsNullOrWhiteSpace(script.FileName)) return;

            string path = Path.Combine(_scriptsFolderPath, script.FileName);
            if (File.Exists(path))
            {
                string argsLog = !string.IsNullOrEmpty(script.Arguments) ? " " + script.Arguments : "";
                _ = _scriptHelper.ExecuteScriptAsync(path, script.Arguments);
                logger.Info("Executed: " + script.FileName + argsLog);
            }
            else
                logger.Warn("Script not found: " + script.FileName);
        }

        public async Task<string> ImportScriptAsync(string sourcePath)
        {
            try
            {
                EnsureScriptsFolderExists();
                if (!File.Exists(sourcePath)) return null;

                string originalFileName = Path.GetFileName(sourcePath);
                string ext = Path.GetExtension(originalFileName).ToLower();
                bool isExe = ext == ".exe";

                string fileName = isExe
                    ? Path.GetFileNameWithoutExtension(originalFileName) + ".lnk"
                    : originalFileName;

                string destinationPath = Path.Combine(_scriptsFolderPath, fileName);

                logger.Debug($"ImportScript sandbox check: dir='{Path.GetFullPath(Path.GetDirectoryName(sourcePath)).TrimEnd(Path.DirectorySeparatorChar)}' sandbox='{Path.GetFullPath(_scriptsFolderPath).TrimEnd(Path.DirectorySeparatorChar)}'");

                // Early-return if already in sandbox
                if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(sourcePath)).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(_scriptsFolderPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(sourcePath);

                int counter = 1;
                while (File.Exists(destinationPath))
                {
                    string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                    destinationPath = Path.Combine(_scriptsFolderPath, $"{nameOnly} ({counter}){(isExe ? ".lnk" : ext)}");
                    counter++;
                }

                await Task.Run(() =>
                {
                    if (isExe)
                        CreateShortcut(destinationPath, sourcePath);
                    else
                        File.Copy(sourcePath, destinationPath);
                });

                return Path.GetFileName(destinationPath);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to import script to sandbox.");
                return null;
            }
        }

        #endregion

        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Save();
        }
    }
}