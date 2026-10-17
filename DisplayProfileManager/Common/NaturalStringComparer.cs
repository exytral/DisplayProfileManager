using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Common
{
    public sealed class NaturalStringComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);

        public static NaturalStringComparer Instance { get; } = new NaturalStringComparer();

        public int Compare(string x, string y)
        {
            if (x == null)
            {
                return y == null ? 0 : -1;
            }
            if (y == null)
            {
                return 1;
            }

            try
            {
                return StrCmpLogicalW(x, y);
            }
            catch (DllNotFoundException)
            {
                return string.Compare(x, y, StringComparison.CurrentCulture);
            }
        }
    }
}
