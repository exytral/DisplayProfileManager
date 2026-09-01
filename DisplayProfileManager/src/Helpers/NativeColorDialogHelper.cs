using System;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Helpers
{
    internal static class NativeColorDialogHelper
    {
        #region P/Invoke

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChooseColor(ref CHOOSECOLOR lpcc);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CHOOSECOLOR
        {
            public int LStructSize;
            public IntPtr HwndOwner;
            public IntPtr HInstance;
            public uint RgbResult;
            public IntPtr LpCustColors;
            public int Flags;
            public IntPtr LCustData;
            public IntPtr LpfnHook;
            public IntPtr LpTemplateName;
        }

        #endregion

        #region Constants

        private const int CcRgbInit = 0x00000001;
        private const int CcFullOpen = 0x00000002;

        #endregion

        public static bool TryChooseColor(IntPtr owner, uint initialColor, out uint selectedColor)
        {
            selectedColor = initialColor;
            var customColors = new uint[16];
            var handle = GCHandle.Alloc(customColors, GCHandleType.Pinned);

            try
            {
                var dialog = new CHOOSECOLOR
                {
                    LStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
                    HwndOwner = owner,
                    RgbResult = initialColor,
                    LpCustColors = handle.AddrOfPinnedObject(),
                    Flags = CcRgbInit | CcFullOpen
                };

                if (!ChooseColor(ref dialog))
                {
                    return false;
                }

                selectedColor = dialog.RgbResult;
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}