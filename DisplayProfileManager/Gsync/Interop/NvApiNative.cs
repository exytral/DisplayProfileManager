using System;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Gsync.Interop
{
    // NVAPI exports only nvapi_QueryInterface; every other entry point is resolved by its interface ID
    internal static class NvApiNative
    {
        #region Function Pointers

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr QueryInterfaceDelegate(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int InitializeDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int CreateSessionDelegate(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int SessionDelegate(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int GetBaseProfileDelegate(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int GetSettingDelegate(IntPtr session, IntPtr profile, uint settingId, [In, Out] byte[] setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int SetSettingDelegate(IntPtr session, IntPtr profile, [In] byte[] setting);

        #endregion

        #region Constants

        internal const int NvapiOk = 0;
        internal const int NvapiSettingNotFound = -160;

        private const uint InitializeId = 0x0150E828;
        private const uint CreateSessionId = 0x0694D52E;
        private const uint DestroySessionId = 0xDAD9CFF8;
        private const uint LoadSettingsId = 0x375DBD6B;
        private const uint SaveSettingsId = 0xFCBC7E14;
        private const uint GetBaseProfileId = 0xDA8466A0;
        private const uint GetSettingId = 0x73BF8338;
        private const uint SetSettingId = 0x577DD202;

        // NVDRS_SETTING_V1: version, settingName[2048 wchar], id, type, location, two flags, then two 4100-byte value unions
        internal const int SettingSize = 12320;
        internal const uint SettingVersion = SettingSize | (1u << 16);
        internal const int SettingIdOffset = 4100;
        internal const int SettingTypeOffset = 4104;
        internal const int CurrentValueOffset = 8220;
        internal const uint DwordType = 0;

        #endregion

        internal sealed class Api
        {
            public InitializeDelegate Initialize;
            public CreateSessionDelegate CreateSession;
            public SessionDelegate DestroySession;
            public SessionDelegate LoadSettings;
            public SessionDelegate SaveSettings;
            public GetBaseProfileDelegate GetBaseProfile;
            public GetSettingDelegate GetSetting;
            public SetSettingDelegate SetSetting;
        }

        private static readonly Lazy<Api> _api = new(Load);

        internal static Api TryGetApi() => _api.Value;

        private static Api Load()
        {
            string library = Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll";
            if (!NativeLibrary.TryLoad(library, typeof(NvApiNative).Assembly, DllImportSearchPath.System32, out IntPtr handle))
            {
                return null;
            }

            if (!NativeLibrary.TryGetExport(handle, "nvapi_QueryInterface", out IntPtr queryAddress))
            {
                return null;
            }

            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryAddress);
            T Resolve<T>(uint id) where T : Delegate
            {
                IntPtr address = query(id);
                return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
            }

            var api = new Api
            {
                Initialize = Resolve<InitializeDelegate>(InitializeId),
                CreateSession = Resolve<CreateSessionDelegate>(CreateSessionId),
                DestroySession = Resolve<SessionDelegate>(DestroySessionId),
                LoadSettings = Resolve<SessionDelegate>(LoadSettingsId),
                SaveSettings = Resolve<SessionDelegate>(SaveSettingsId),
                GetBaseProfile = Resolve<GetBaseProfileDelegate>(GetBaseProfileId),
                GetSetting = Resolve<GetSettingDelegate>(GetSettingId),
                SetSetting = Resolve<SetSettingDelegate>(SetSettingId)
            };

            bool complete = api.Initialize != null && api.CreateSession != null && api.DestroySession != null
                && api.LoadSettings != null && api.SaveSettings != null && api.GetBaseProfile != null
                && api.GetSetting != null && api.SetSetting != null;
            return complete ? api : null;
        }

        internal static byte[] CreateSetting(uint settingId = 0)
        {
            var buffer = new byte[SettingSize];
            BitConverter.TryWriteBytes(buffer.AsSpan(0), SettingVersion);
            BitConverter.TryWriteBytes(buffer.AsSpan(SettingIdOffset), settingId);
            return buffer;
        }

        internal static byte[] CreateDwordSetting(uint settingId, uint value)
        {
            var buffer = CreateSetting(settingId);
            BitConverter.TryWriteBytes(buffer.AsSpan(SettingTypeOffset), DwordType);
            BitConverter.TryWriteBytes(buffer.AsSpan(CurrentValueOffset), value);
            return buffer;
        }

        internal static uint ReadSettingType(byte[] setting) => BitConverter.ToUInt32(setting, SettingTypeOffset);
        internal static uint ReadCurrentValue(byte[] setting) => BitConverter.ToUInt32(setting, CurrentValueOffset);
    }
}
