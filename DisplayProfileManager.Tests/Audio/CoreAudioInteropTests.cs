using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Audio;
using static DisplayProfileManager.Audio.Interop.CoreAudioNative;

namespace DisplayProfileManager.Tests.Audio
{
    // Hand-built native vtable so calls cross the real CLR COM interop boundary without audio hardware
    internal sealed class NativeComFake
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceMethod(IntPtr self, ref Guid riid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint RefCountMethod(IntPtr self);

        private const int ENoInterface = unchecked((int)0x80004002);
        private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");

        // Fakes stay reachable and allocated for the process lifetime so a late RCW release never reaches freed memory
        private static readonly List<NativeComFake> _live = [];

        private readonly Guid _iid;
        private readonly List<Delegate> _methods = [];
        private int _refCount = 1;

        internal NativeComFake(Guid iid, params Delegate[] interfaceMethods)
        {
            _iid = iid;
            _methods.Add(new QueryInterfaceMethod(QueryInterface));
            _methods.Add(new RefCountMethod(AddRef));
            _methods.Add(new RefCountMethod(Release));
            _methods.AddRange(interfaceMethods);

            IntPtr vtable = Marshal.AllocHGlobal(IntPtr.Size * _methods.Count);
            for (int i = 0; i < _methods.Count; i++)
                Marshal.WriteIntPtr(vtable, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_methods[i]));

            Pointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, vtable);

            lock (_live)
                _live.Add(this);
        }

        internal IntPtr Pointer { get; }
        internal int RefCount => Volatile.Read(ref _refCount);

        internal T CreateRcw<T>() where T : class => (T)Marshal.GetObjectForIUnknown(Pointer);

        internal IntPtr AddRefForCaller()
        {
            AddRef(Pointer);
            return Pointer;
        }

        private int QueryInterface(IntPtr self, ref Guid riid, out IntPtr ppv)
        {
            if (riid == IidIUnknown || riid == _iid)
            {
                AddRef(self);
                ppv = self;
                return 0;
            }

            ppv = IntPtr.Zero;
            return ENoInterface;
        }

        private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref _refCount);

        private uint Release(IntPtr self) => (uint)Interlocked.Decrement(ref _refCount);
    }

    [TestClass]
    public class CoreAudioInteropTests
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CountMethod(IntPtr self, out uint count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetAtMethod(IntPtr self, uint index, IntPtr key);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ValueMethod(IntPtr self, IntPtr key, IntPtr value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CommitMethod(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ActivateMethod(IntPtr self, IntPtr iid, uint clsCtx, IntPtr activationParams, out IntPtr instance);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int OpenPropertyStoreMethod(IntPtr self, uint access, out IntPtr store);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIdMethod(IntPtr self, out IntPtr id);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetStateMethod(IntPtr self, out uint state);

        private const int SOk = 0;
        private const int SFalse = 1;
        private const int EFail = unchecked((int)0x80004005);
        private const int ENotImpl = unchecked((int)0x80004001);
        private const ushort VtEmpty = 0;
        private const ushort VtUi4 = 19;

        private static readonly Guid IidPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
        private static readonly Guid IidMMDevice = new("D666063F-1587-4E43-81F1-B948E807363F");

        private static int NativePropVariantSize => IntPtr.Size == 8 ? 24 : 16;

        private static NativeComFake CreatePropertyStore(Func<IntPtr, IntPtr, int> getValue) => new(
            IidPropertyStore,
            new CountMethod((IntPtr self, out uint count) => { count = 1; return SOk; }),
            new GetAtMethod((self, index, key) => ENotImpl),
            new ValueMethod((self, key, value) => getValue(key, value)),
            new ValueMethod((self, key, value) => ENotImpl),
            new CommitMethod(self => ENotImpl));

        private static NativeComFake CreateDevice(NativeComFake store, int openResult = SOk) => new(
            IidMMDevice,
            new ActivateMethod((IntPtr self, IntPtr iid, uint clsCtx, IntPtr activationParams, out IntPtr instance) => { instance = IntPtr.Zero; return ENotImpl; }),
            new OpenPropertyStoreMethod((IntPtr self, uint access, out IntPtr result) =>
            {
                result = openResult >= 0 && store != null ? store.AddRefForCaller() : IntPtr.Zero;
                return openResult;
            }),
            new GetIdMethod((IntPtr self, out IntPtr id) => { id = Marshal.StringToCoTaskMemUni("{0.0.0.00000000}.{device}"); return SOk; }),
            new GetStateMethod((IntPtr self, out uint state) => { state = DeviceStateActive; return SOk; }));

        private static string ReadFriendlyName(NativeComFake device)
        {
            var rcw = device.CreateRcw<IMMDevice>();
            try
            {
                return AudioDeviceService.GetDeviceFriendlyName(rcw);
            }
            finally
            {
                Marshal.FinalReleaseComObject(rcw);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PropVariant_Layout_MatchesNativeWindowsAbi()
        {
            Assert.AreEqual(NativePropVariantSize, Marshal.SizeOf<PROPVARIANT>());
            Assert.AreEqual(0, (int)Marshal.OffsetOf<PROPVARIANT>(nameof(PROPVARIANT.vt)));
            Assert.AreEqual(8, (int)Marshal.OffsetOf<PROPVARIANT>(nameof(PROPVARIANT.pwszVal)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CoreAudioInterfaces_HresultMethods_PreserveNativeSignature()
        {
            var interfaces = new[] { typeof(IMMDeviceEnumerator), typeof(IMMDeviceCollection), typeof(IMMDevice), typeof(IPropertyStore) };

            var mismatches = interfaces
                .SelectMany(type => type.GetMethods())
                .Where(method => method.ReturnType != typeof(int) || (method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) == 0)
                .Select(method => $"{method.DeclaringType.Name}.{method.Name}")
                .ToList();

            Assert.AreEqual(0, mismatches.Count, string.Join(", ", mismatches));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PropertyStoreGetValue_FailureHresult_ReturnsHresultWithoutThrowing()
        {
            var fake = CreatePropertyStore((key, value) => EFail);
            var store = fake.CreateRcw<IPropertyStore>();
            var key = PKEY_Device_FriendlyName;

            try
            {
                int hr = store.GetValue(ref key, out _);

                Assert.AreEqual(EFail, hr);
            }
            finally
            {
                Marshal.FinalReleaseComObject(store);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PropertyStoreGetValue_SuccessCode_ReturnsNativeHresult()
        {
            var fake = CreatePropertyStore((key, value) =>
            {
                Marshal.WriteInt16(value, 0, unchecked((short)VtEmpty));
                return SFalse;
            });
            var store = fake.CreateRcw<IPropertyStore>();
            var key = PKEY_Device_FriendlyName;

            try
            {
                int hr = store.GetValue(ref key, out var value);

                Assert.AreEqual(SFalse, hr);
                Assert.AreEqual(VtEmpty, value.vt);
            }
            finally
            {
                Marshal.FinalReleaseComObject(store);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PropertyStoreGetValue_ProviderWritesFullNativeSize_TailReachesManagedValue()
        {
            // Writes every byte of the native PROPVARIANT; an undersized managed value cannot receive the tail
            IntPtr canary = new(0x5A5A5A5A);
            var fake = CreatePropertyStore((key, value) =>
            {
                for (int offset = 0; offset < NativePropVariantSize; offset++)
                    Marshal.WriteByte(value, offset, 0);

                Marshal.WriteInt16(value, 0, unchecked((short)VtUi4));
                Marshal.WriteIntPtr(value, NativePropVariantSize - IntPtr.Size, canary);
                return SOk;
            });
            var store = fake.CreateRcw<IPropertyStore>();
            var key = PKEY_Device_FriendlyName;

            try
            {
                int hr = store.GetValue(ref key, out var value);

                Assert.AreEqual(SOk, hr);
                Assert.AreEqual(VtUi4, value.vt);
                Assert.AreEqual(canary, value.unionTail);
            }
            finally
            {
                Marshal.FinalReleaseComObject(store);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PropVariantClear_LpwstrValue_ReleasesStringAndResetsValue()
        {
            var value = new PROPVARIANT { vt = VtLpwstr, pwszVal = Marshal.StringToCoTaskMemUni("Device") };

            int hr = PropVariantClear(ref value);

            Assert.AreEqual(SOk, hr);
            Assert.AreEqual(VtEmpty, value.vt);
            Assert.AreEqual(IntPtr.Zero, value.pwszVal);
            Assert.AreEqual(IntPtr.Zero, value.unionTail);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDeviceFriendlyName_LpwstrValue_ReturnsNameAndReleasesStore()
        {
            Guid requestedFormat = Guid.Empty;
            uint requestedProperty = 0;
            var store = CreatePropertyStore((key, value) =>
            {
                requestedFormat = Marshal.PtrToStructure<Guid>(key);
                requestedProperty = (uint)Marshal.ReadInt32(key, 16);
                Marshal.WriteInt16(value, 0, unchecked((short)VtLpwstr));
                Marshal.WriteIntPtr(value, 8, Marshal.StringToCoTaskMemUni("Device"));
                return SOk;
            });
            var device = CreateDevice(store);

            string name = ReadFriendlyName(device);

            Assert.AreEqual("Device", name);
            Assert.AreEqual(PKEY_Device_FriendlyName.fmtid, requestedFormat);
            Assert.AreEqual(PKEY_Device_FriendlyName.pid, requestedProperty);
            Assert.AreEqual(1, store.RefCount, "The property store reference handed to the caller must be released");
            Assert.AreEqual(1, device.RefCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDeviceFriendlyName_NonStringValue_ReturnsNull()
        {
            var store = CreatePropertyStore((key, value) =>
            {
                Marshal.WriteInt16(value, 0, unchecked((short)VtUi4));
                Marshal.WriteInt32(value, 8, 42);
                return SOk;
            });

            string name = ReadFriendlyName(CreateDevice(store));

            Assert.IsNull(name);
            Assert.AreEqual(1, store.RefCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDeviceFriendlyName_GetValueFailure_ReturnsNullAndReleasesStore()
        {
            var store = CreatePropertyStore((key, value) => EFail);

            string name = ReadFriendlyName(CreateDevice(store));

            Assert.IsNull(name);
            Assert.AreEqual(1, store.RefCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDeviceFriendlyName_OpenPropertyStoreFailure_ReturnsNull()
        {
            var device = CreateDevice(store: null, openResult: EFail);

            string name = ReadFriendlyName(device);

            Assert.IsNull(name);
            Assert.AreEqual(1, device.RefCount);
        }
    }
}
