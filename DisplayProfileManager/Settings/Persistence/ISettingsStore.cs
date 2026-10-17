using System.Threading.Tasks;

using DisplayProfileManager.Settings.Models;

namespace DisplayProfileManager.Settings.Persistence
{
    internal interface ISettingsStore
    {
        string SettingsFilePath { get; }
        string AppDataFolder { get; }
        Task<AppSettings> LoadAsync();
        Task<bool> SaveAsync(AppSettings settings);
    }
}
