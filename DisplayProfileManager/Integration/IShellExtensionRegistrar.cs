namespace DisplayProfileManager.Integration
{
    public enum ShellRegistrationMode
    {
        Normal,
        Developer
    }

    public interface IShellExtensionRegistrar
    {
        bool IsRegistered();
        ShellRegistrationMode GetRegistrationMode() => ShellRegistrationMode.Normal;
        bool Register();
        bool Register(ShellRegistrationMode mode) =>
            mode == ShellRegistrationMode.Normal && Register();
        bool Unregister();
        bool RestartExplorer();
    }
}
