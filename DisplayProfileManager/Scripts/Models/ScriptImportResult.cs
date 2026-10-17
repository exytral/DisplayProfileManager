using System.Collections.Generic;

namespace DisplayProfileManager.Scripts.Models
{
    internal sealed class ScriptImportResult(string sourcePath, string importedFileName)
    {
        public string SourcePath { get; } = sourcePath;
        public string ImportedFileName { get; } = importedFileName;
        public bool Success => !string.IsNullOrWhiteSpace(ImportedFileName);
    }

    internal sealed class ScriptImportBatchResult
    {
        public List<ScriptImportResult> Results { get; } = [];
        public int ImportedCount { get; internal set; }
        public int FailedCount { get; internal set; }
    }
}
