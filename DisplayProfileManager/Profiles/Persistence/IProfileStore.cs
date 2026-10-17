using System.Threading.Tasks;

namespace DisplayProfileManager.Profiles.Persistence
{
    internal interface IProfileStore
    {
        string AppDataFolder { get; }
        string[] EnumerateProfileFiles();
        Task<string> ReadProfileTextAsync(string path);
        Task<bool> WriteProfileTextAsync(string profileId, string json);
        Task<bool> DeleteProfileAsync(string profileId);
    }
}
