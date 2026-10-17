namespace DisplayProfileManager.UI.ViewModels
{
    public class ScriptListEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
    }
}
