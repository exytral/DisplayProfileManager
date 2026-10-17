using DisplayProfileManager.Scripts.Models;

namespace DisplayProfileManager.Scripts
{
    public interface IScriptRunner
    {
        void ExecuteScript(Script script);
    }
}
