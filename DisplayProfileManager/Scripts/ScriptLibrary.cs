using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Scripts.Interop;
using DisplayProfileManager.Scripts.Models;

namespace DisplayProfileManager.Scripts
{
    internal sealed class ScriptLibrary
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly string _scriptsFolderPath;

        public string ScriptsFolderPath => _scriptsFolderPath;

        public ScriptLibrary(string appDataFolder)
        {
            if (string.IsNullOrWhiteSpace(appDataFolder)) throw new ArgumentException("Scripts storage folder is required.", nameof(appDataFolder));

            _scriptsFolderPath = Path.Combine(appDataFolder, "Scripts");
            EnsureScriptsFolderExists();
        }

        private void EnsureScriptsFolderExists()
        {
            if (!Directory.Exists(_scriptsFolderPath))
                Directory.CreateDirectory(_scriptsFolderPath);
        }

        internal bool TryResolveSandboxedScriptPath(string fileName, out string resolvedPath) =>
            TryResolveSandboxedScriptPath(_scriptsFolderPath, fileName, out resolvedPath);

        internal static bool TryResolveSandboxedScriptPath(string scriptsFolderPath, string fileName, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(scriptsFolderPath) || string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            try
            {
                string sandbox = Path.GetFullPath(scriptsFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string sandboxPrefix = sandbox + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(Path.Combine(sandbox, fileName));

                if (!candidate.StartsWith(sandboxPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                resolvedPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> ImportScriptAsync(string sourcePath)
        {
            try
            {
                EnsureScriptsFolderExists();
                if (!File.Exists(sourcePath))
                {
                    return null;
                }

                string originalFileName = Path.GetFileName(sourcePath);
                string ext = Path.GetExtension(originalFileName).ToLower();
                bool isExe = ext == ".exe";
                string fileName = isExe
                    ? Path.GetFileNameWithoutExtension(originalFileName) + ".lnk"
                    : originalFileName;

                string destinationPath = Path.Combine(_scriptsFolderPath, fileName);

                _logger.Debug($"ImportScript sandbox check: dir='{Path.GetFullPath(Path.GetDirectoryName(sourcePath)).TrimEnd(Path.DirectorySeparatorChar)}' sandbox='{Path.GetFullPath(_scriptsFolderPath).TrimEnd(Path.DirectorySeparatorChar)}'");

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
                        WindowsShortcutWriter.CreateShortcut(destinationPath, sourcePath);
                    else
                        File.Copy(sourcePath, destinationPath);
                });

                return Path.GetFileName(destinationPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to import script to sandbox.");
                return null;
            }
        }

        internal Task<ScriptImportBatchResult> ImportScriptsAsync(IEnumerable<string> sourcePaths) =>
            ImportScriptsAsync(sourcePaths, ImportScriptAsync);

        internal static async Task<ScriptImportBatchResult> ImportScriptsAsync(
            IEnumerable<string> sourcePaths,
            Func<string, Task<string>> importOneAsync)
        {
            var batch = new ScriptImportBatchResult();
            if (sourcePaths == null || importOneAsync == null)
            {
                return batch;
            }

            foreach (var sourcePath in sourcePaths)
            {
                string importedFileName = null;
                try
                {
                    importedFileName = await importOneAsync(sourcePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to import script '{sourcePath}' in batch");
                }

                var result = new ScriptImportResult(sourcePath, importedFileName);
                batch.Results.Add(result);
                if (result.Success) batch.ImportedCount++;
                else batch.FailedCount++;
            }

            return batch;
        }
    }
}
