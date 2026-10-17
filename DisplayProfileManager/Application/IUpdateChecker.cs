using System.Threading.Tasks;

namespace DisplayProfileManager.Application
{
    public interface IUpdateChecker
    {
        Task<UpdateCheckResult> CheckAsync();
    }
}
