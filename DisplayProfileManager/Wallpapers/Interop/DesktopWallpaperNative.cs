using System;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Wallpapers.Interop
{
    internal static class DesktopWallpaperNative
    {

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
            void GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
            void GetAttributes(uint dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems(out IntPtr ppenumShellItems);
        }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            [PreserveSig]
            int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(DesktopWallpaperPosition position);
            DesktopWallpaperPosition GetPosition();
            void SetSlideshow(IShellItemArray items);
            IShellItemArray GetSlideshow();
            void SetSlideshowOptions(DesktopSlideshowOptions options, uint slideshowTick);
            void GetSlideshowOptions(out DesktopSlideshowOptions options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, uint direction);
            DesktopSlideshowState GetStatus();
            void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }

        internal enum DesktopWallpaperPosition
        {
            Center = 0,
            Tile = 1,
            Stretch = 2,
            Fit = 3,
            Fill = 4,
            Span = 5,
        }

        [Flags]
        internal enum DesktopSlideshowState : uint
        {
            Enabled = 0x01,
            Slideshow = 0x02,
            DisabledByRemoteSession = 0x04,
        }

        [Flags]
        internal enum DesktopSlideshowOptions : uint
        {
            None = 0x00,
            ShuffleImages = 0x01,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
        internal const string ClsidDesktopWallpaper = "C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD";

        internal const uint SigdnFileSysPath = 0x80058000;

        internal const uint WmSettingChange = 0x001A;

        internal const uint SmtoAbortIfHung = 0x0002;

        internal const uint SpiSetDeskWallpaper = 0x0014;

        internal const uint SpifUpdateIniFile = 0x0001;

        internal const uint SpifSendChange = 0x0002;

        internal static readonly IntPtr HwndBroadcast = new(0xffff);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        internal static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [DllImport("shell32.dll", PreserveSig = false)]
        internal static extern void SHCreateShellItemArrayFromShellItem(
            IShellItem psi, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            UIntPtr wParam,
            [MarshalAs(UnmanagedType.LPWStr)] string lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            [MarshalAs(UnmanagedType.LPWStr)] string pvParam,
            uint fWinIni);

    }
}
