using System;
using System.IO;

namespace DisplayProfileManager.Scripts.Interop
{
    internal static class WindowsShortcutWriter
    {
        public static void CreateShortcut(string shortcutPath, string targetPath)
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
