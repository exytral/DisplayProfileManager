using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Vrr.Amd.Interop
{
    // ADLX is a COM-style C++ SDK with no C exports beyond init/terminate, so the interface vtables are walked by slot
    // Slot numbers and signatures come from the C vtable structs in the public ADLX headers (SDK/Include)
    // Untested on hardware: written without an AMD GPU, see docs/wiki/profiles.md
    internal static class AdlxNative
    {
        #region Function Pointers

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeDelegate(ulong version, out IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int TerminateDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int QueryFullVersionDelegate(out ulong version);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDelegate(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterfaceDelegate(IntPtr self, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint SizeDelegate(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AtDelegate(IntPtr self, uint location, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFreeSyncDelegate(IntPtr self, IntPtr display, out IntPtr freeSync);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBoolDelegate(IntPtr self, out byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetBoolDelegate(IntPtr self, byte value);

        #endregion

        #region Constants

        internal const int AdlxOk = 0;
        internal const int AdlxAlreadyEnabled = 1;
        internal const int AdlxAlreadyInitialized = 2;

        // ADLXVersion.h 2.0.0.125 packed as ADLX_MAKE_FULL_VER (ADLXDefines.h), used only when the driver cannot report its own version
        private const ulong HeaderVersion = (2UL << 48) | 125UL;

        // IADLXInterface (ADLXDefines.h): Acquire, Release, QueryInterface; every refcounted interface inherits them first
        private const int ReleaseSlot = 1;

        // IADLXSystemVtbl (ISystem.h): no inherited IADLXInterface slots, it is a singleton owned by ADLX
        // 0 GetHybridGraphicsType, 1 GetGPUs, 2 QueryInterface, 3 GetDisplaysServices
        private const int SystemGetDisplaysServicesSlot = 3;

        // IADLXDisplayServicesVtbl (IDisplays.h): 0-2 IADLXInterface, 3 GetNumberOfDisplays, 4 GetDisplays,
        // 5 Get3DLUT, 6 GetGamut, 7 GetGamma, 8 GetDisplayChangedHandling, 9 GetFreeSync
        private const int ServicesGetDisplaysSlot = 4;
        private const int ServicesGetFreeSyncSlot = 9;

        // IADLXDisplayListVtbl (IDisplays.h): 0-2 IADLXInterface, 3 Size, 4 Empty, 5 Begin, 6 End, 7 At, 8 Clear,
        // 9 Remove_Back, 10 Add_Back, 11 At_DisplayList
        private const int ListSizeSlot = 3;
        private const int ListAtDisplaySlot = 11;

        // IADLXFreeSyncVtbl (IDisplaySettings.h): 0-2 IADLXInterface, 3 IsSupported, 4 IsEnabled, 5 SetEnabled
        private const int FreeSyncIsSupportedSlot = 3;
        private const int FreeSyncIsEnabledSlot = 4;
        private const int FreeSyncSetEnabledSlot = 5;

        #endregion

        internal readonly record struct FreeSyncDisplay(IntPtr FreeSync, bool Supported, bool Enabled);

        // One init/terminate bracket; every interface acquired through it is released before ADLXTerminate
        internal sealed class Session : IDisposable
        {
            private readonly IntPtr _module;
            private readonly TerminateDelegate _terminate;
            private readonly IntPtr _system;
            private readonly List<IntPtr> _acquired = [];
            private bool _disposed;

            private Session(IntPtr module, TerminateDelegate terminate, IntPtr system)
            {
                _module = module;
                _terminate = terminate;
                _system = system;
            }

            // Returns null with the reason when ADLX is absent (no AMD driver) or refuses to start
            internal static Session Open(out string error)
            {
                string library = Environment.Is64BitProcess ? "amdadlx64.dll" : "amdadlx32.dll";
                if (!NativeLibrary.TryLoad(library, typeof(AdlxNative).Assembly, DllImportSearchPath.System32, out IntPtr module))
                {
                    error = $"{library} is not installed";
                    return null;
                }

                if (!NativeLibrary.TryGetExport(module, "ADLXInitialize", out IntPtr initAddress)
                    || !NativeLibrary.TryGetExport(module, "ADLXTerminate", out IntPtr terminateAddress))
                {
                    NativeLibrary.Free(module);
                    error = $"{library} does not export the ADLX entry points";
                    return null;
                }

                var initialize = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(initAddress);
                var terminate = Marshal.GetDelegateForFunctionPointer<TerminateDelegate>(terminateAddress);

                // Ask for the driver's own version so a newer header than the driver cannot cause a version mismatch
                ulong version = HeaderVersion;
                if (NativeLibrary.TryGetExport(module, "ADLXQueryFullVersion", out IntPtr queryAddress))
                {
                    var query = Marshal.GetDelegateForFunctionPointer<QueryFullVersionDelegate>(queryAddress);
                    if (query(out ulong driverVersion) == AdlxOk)
                        version = driverVersion;
                }

                int status = initialize(version, out IntPtr system);
                if (!IsSuccess(status) || system == IntPtr.Zero)
                {
                    NativeLibrary.Free(module);
                    error = $"ADLX initialization failed ({status})";
                    return null;
                }

                error = null;
                return new Session(module, terminate, system);
            }

            // Every display with a FreeSync interface, with its supported and enabled state
            internal List<FreeSyncDisplay> GetFreeSyncDisplays(out string error)
            {
                var result = new List<FreeSyncDisplay>();

                if (!GetInterface(_system, SystemGetDisplaysServicesSlot, out IntPtr services, out error))
                    return result;
                Track(services);

                if (!GetInterface(services, ServicesGetDisplaysSlot, out IntPtr list, out error))
                    return result;
                Track(list);

                uint count = Slot<SizeDelegate>(list, ListSizeSlot)(list);
                var at = Slot<AtDelegate>(list, ListAtDisplaySlot);
                var getFreeSync = Slot<GetFreeSyncDelegate>(services, ServicesGetFreeSyncSlot);

                for (uint i = 0; i < count; i++)
                {
                    if (at(list, i, out IntPtr display) != AdlxOk || display == IntPtr.Zero) continue;
                    Track(display);

                    // A display without a FreeSync interface is simply skipped
                    if (getFreeSync(services, display, out IntPtr freeSync) != AdlxOk || freeSync == IntPtr.Zero) continue;
                    Track(freeSync);

                    bool supported = ReadBool(freeSync, FreeSyncIsSupportedSlot, out bool isSupported) && isSupported;
                    bool enabled = supported && ReadBool(freeSync, FreeSyncIsEnabledSlot, out bool isEnabled) && isEnabled;
                    result.Add(new FreeSyncDisplay(freeSync, supported, enabled));
                }

                error = null;
                return result;
            }

            internal bool IsEnabled(IntPtr freeSync) => ReadBool(freeSync, FreeSyncIsEnabledSlot, out bool enabled) && enabled;

            internal bool SetEnabled(IntPtr freeSync, bool enabled) =>
                IsSuccess(Slot<SetBoolDelegate>(freeSync, FreeSyncSetEnabledSlot)(freeSync, enabled ? (byte)1 : (byte)0));

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                for (int i = _acquired.Count - 1; i >= 0; i--)
                    Slot<ReleaseDelegate>(_acquired[i], ReleaseSlot)(_acquired[i]);
                _acquired.Clear();

                _terminate();
                NativeLibrary.Free(_module);
            }

            private void Track(IntPtr acquired) => _acquired.Add(acquired);

            private static bool GetInterface(IntPtr self, int slot, out IntPtr result, out string error)
            {
                int status = Slot<GetInterfaceDelegate>(self, slot)(self, out result);
                bool ok = IsSuccess(status) && result != IntPtr.Zero;
                error = ok ? null : $"ADLX call failed ({status})";
                return ok;
            }

            private static bool ReadBool(IntPtr self, int slot, out bool value)
            {
                int status = Slot<GetBoolDelegate>(self, slot)(self, out byte raw);
                value = raw != 0;
                return IsSuccess(status);
            }
        }

        private static bool IsSuccess(int status) => status == AdlxOk || status == AdlxAlreadyEnabled || status == AdlxAlreadyInitialized;

        // COM-style object: first pointer is the vtable, slot n is the nth function pointer in it
        private static T Slot<T>(IntPtr self, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(self);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }
    }
}
