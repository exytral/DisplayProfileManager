using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Integration
{
    internal interface IProfileShortcutStore
    {
        IReadOnlyList<ProfileShortcutSnapshot> ReadAll();
        void Write(ProfileShortcutSpec shortcut);
        void Delete(string fileName);
        void DeleteFolderIfEmpty();
    }

    internal sealed class StartMenuProfileShortcutStore : IProfileShortcutStore
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly string _folderPath;

        public StartMenuProfileShortcutStore(string programsFolder = null)
        {
            programsFolder ??= Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            _folderPath = Path.Combine(programsFolder, "Display Profile Manager", "Profiles");
        }

        public IReadOnlyList<ProfileShortcutSnapshot> ReadAll()
        {
            if (!Directory.Exists(_folderPath))
            {
                return [];
            }

            var snapshots = new List<ProfileShortcutSnapshot>();
            foreach (string path in Directory.GetFiles(_folderPath, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    snapshots.Add(ReadShortcut(path));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not read managed profile shortcut '{path}'");
                    snapshots.Add(new ProfileShortcutSnapshot
                    {
                        FileName = Path.GetFileName(path),
                        IsReadable = false
                    });
                }
            }

            return snapshots;
        }

        public void Write(ProfileShortcutSpec shortcut)
        {
            if (shortcut == null) throw new ArgumentNullException(nameof(shortcut));

            string path = ProfileShortcutPlanner.GetManagedShortcutPath(_folderPath, shortcut.FileName);
            Directory.CreateDirectory(_folderPath);
            ShellLinkFile.Write(path, shortcut);
        }

        public void Delete(string fileName)
        {
            string path = ProfileShortcutPlanner.GetManagedShortcutPath(_folderPath, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        public void DeleteFolderIfEmpty()
        {
            if (Directory.Exists(_folderPath) && Directory.GetFileSystemEntries(_folderPath).Length == 0)
                Directory.Delete(_folderPath);
        }

        private static ProfileShortcutSnapshot ReadShortcut(string path)
        {
            var link = ShellLinkFile.Read(path);
            return new ProfileShortcutSnapshot
            {
                FileName = Path.GetFileName(path),
                TargetPath = link.TargetPath,
                Arguments = link.Arguments,
                WorkingDirectory = link.WorkingDirectory,
                Description = link.Description,
                IconPath = link.IconPath,
                IsReadable = true
            };
        }
    }

    internal static class ShellLinkFile
    {
        public static ProfileShortcutSnapshot Read(string path)
        {
            object instance = new ShellLinkComObject();
            try
            {
                var shellLink = (IShellLinkW)instance;
                ((IPersistFile)instance).Load(path, 0);

                var target = new StringBuilder(32768);
                var arguments = new StringBuilder(32768);
                var workingDirectory = new StringBuilder(32768);
                var description = new StringBuilder(1024);
                var iconPath = new StringBuilder(32768);

                shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                shellLink.GetArguments(arguments, arguments.Capacity);
                shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
                shellLink.GetDescription(description, description.Capacity);
                shellLink.GetIconLocation(iconPath, iconPath.Capacity, out _);

                return new ProfileShortcutSnapshot
                {
                    FileName = Path.GetFileName(path),
                    TargetPath = target.ToString(),
                    Arguments = arguments.ToString(),
                    WorkingDirectory = workingDirectory.ToString(),
                    Description = description.ToString(),
                    IconPath = iconPath.ToString(),
                    IsReadable = true
                };
            }
            finally
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }

        public static void Write(string path, ProfileShortcutSpec shortcut)
        {
            object instance = new ShellLinkComObject();
            try
            {
                var shellLink = (IShellLinkW)instance;
                shellLink.SetPath(shortcut.TargetPath);
                shellLink.SetArguments(shortcut.Arguments);
                shellLink.SetWorkingDirectory(shortcut.WorkingDirectory);
                shellLink.SetDescription(shortcut.Description);
                shellLink.SetIconLocation(shortcut.IconPath, 0);
                ((IPersistFile)instance).Save(path, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private sealed class ShellLinkComObject { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRelative, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
