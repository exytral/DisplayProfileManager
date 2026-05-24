using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    public class ScriptManager
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private static readonly Lazy<ScriptManager> _instance =
            new Lazy<ScriptManager>(() => new ScriptManager());

        public static ScriptManager Instance => _instance.Value;
        public string ScriptsFolderPath => _scriptsFolderPath;

        private readonly string _scriptsFolderPath;
        private readonly ScriptHelper _scriptHelper;

        private ScriptManager()
        {
            _scriptHelper = new ScriptHelper();

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DisplayProfileManager"
            );

            _scriptsFolderPath = Path.Combine(appData, "Scripts");

            if (!Directory.Exists(_scriptsFolderPath))
            {
                Directory.CreateDirectory(_scriptsFolderPath);
            }
        }

        private void EnsureScriptsFolderExists()
        {
            if (!Directory.Exists(_scriptsFolderPath))
            {
                Directory.CreateDirectory(_scriptsFolderPath);
                logger.Info("Re-created missing scripts folder.");
            }
        }

        public string FormatCommand(string fileName, string args)
        {
            string formattedName = fileName.Contains(" ") ? $"\"{fileName}\"" : fileName;
            return string.IsNullOrWhiteSpace(args) ? formattedName : $"{formattedName} {args.Trim()}";
        }

        public void ExecuteScript(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            var parts = ScriptHelper.ParseScriptString(command);
            string name = parts.Path;
            string args = parts.Args;

            string path = Path.Combine(_scriptsFolderPath, name);

            if (File.Exists(path))
            {
                string argsLog = !string.IsNullOrEmpty(args) ? " " + args : "";
                _ = _scriptHelper.ExecuteScriptAsync(path, args);
                logger.Info("Executed: " + name + argsLog);
            }
            else
            {
                logger.Warn("Script not found: " + name);
            }
        }

        public void AddScript(List<string> scripts, string fileName, string cmdArgs = "")
        {
            EnsureScriptsFolderExists();
            string newFullCommand = FormatCommand(fileName, cmdArgs);

            if (!scripts.Contains(newFullCommand, StringComparer.OrdinalIgnoreCase))
            {
                scripts.Add(newFullCommand);
            }
        }

        public void RemoveScript(List<string> scripts, string fileName, string cmdArgs = "")
        {
            string commandToRemove = FormatCommand(fileName, cmdArgs);
            scripts.RemoveAll(c => string.Equals(c, commandToRemove, StringComparison.OrdinalIgnoreCase));
        }

        public void SortScripts(List<string> scripts)
        {
            scripts.Sort(StringComparer.OrdinalIgnoreCase);
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
                if (string.Equals(
                    Path.GetFullPath(Path.GetDirectoryName(sourcePath)).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(_scriptsFolderPath).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(sourcePath);
                }

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
                    {
                        CreateShortcut(destinationPath, sourcePath);
                    }
                    else
                    {
                        File.Copy(sourcePath, destinationPath);
                    }
                });

                return Path.GetFileName(destinationPath);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to import script to sandbox.");
                return null;
            }
        }

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