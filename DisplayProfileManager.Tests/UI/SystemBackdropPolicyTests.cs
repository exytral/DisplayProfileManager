using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.UI.Interop;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class SystemBackdropPolicyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void NativeReadback_FailureIsDistinctFromSuccessfulZero()
        {
            Assert.AreEqual("unavailable", SystemBackdrop.FormatNativeReadback(false, 0));
            Assert.AreEqual("0", SystemBackdrop.FormatNativeReadback(true, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NativeReadback_HexadecimalSuccessPreservesActualZero()
        {
            Assert.AreEqual("unavailable", SystemBackdrop.FormatNativeReadback(false, 0, hexadecimal: true));
            Assert.AreEqual("0x0", SystemBackdrop.FormatNativeReadback(true, 0, hexadecimal: true));
            Assert.AreEqual("0x800000", SystemBackdrop.FormatNativeReadback(true, SystemBackdrop.WsBorder, hexadecimal: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundedCorners_Windows11Build_IsSupported()
        {
            Assert.IsTrue(SystemBackdrop.CanUseRoundedCorners(22000));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundedCorners_PreWindows11Build_IsUnsupported()
        {
            Assert.IsFalse(SystemBackdrop.CanUseRoundedCorners(19045));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WindowStyleRead_ZeroWithError_IsFailure()
        {
            var result = SystemBackdrop.EvaluateWindowStyleRead(0, 5);

            Assert.IsFalse(result.Succeeded);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WindowStyleRead_ZeroWithoutError_IsValid()
        {
            var result = SystemBackdrop.EvaluateWindowStyleRead(0, 0);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(0L, result.Style);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NativeFrame_StyleReadFailure_SkipsMutationAndFrameChange()
        {
            int writeCount = 0;
            int frameChangedCount = 0;

            var result = SystemBackdrop.TryPrepareQuickSwitchNativeFrameCore(
                () => new SystemBackdropNativeStyleReadResult(false, 0),
                _ => { writeCount++; return true; },
                _ => { frameChangedCount++; return true; },
                () => new SystemBackdropNativeStyleReadResult(true, SystemBackdrop.WsBorder));

            Assert.IsFalse(result.StyleReadSucceeded);
            Assert.IsFalse(result.NativeFrameReady);
            Assert.AreEqual(0, writeCount);
            Assert.AreEqual(0, frameChangedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NativeFrame_StyleWriteFailure_SkipsFrameChangeAndIsNotReady()
        {
            int frameChangedCount = 0;

            var result = SystemBackdrop.TryPrepareQuickSwitchNativeFrameCore(
                () => new SystemBackdropNativeStyleReadResult(true, 0),
                _ => false,
                _ => { frameChangedCount++; return true; },
                () => new SystemBackdropNativeStyleReadResult(true, SystemBackdrop.WsBorder));

            Assert.IsTrue(result.StyleReadSucceeded);
            Assert.IsFalse(result.StyleWriteSucceeded);
            Assert.IsFalse(result.NativeFrameReady);
            Assert.AreEqual(0, frameChangedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NativeFrame_FrameChangeFailure_ReportsPartialStateWithoutReadiness()
        {
            int frameChangedCount = 0;

            var result = SystemBackdrop.TryPrepareQuickSwitchNativeFrameCore(
                () => new SystemBackdropNativeStyleReadResult(true, 0),
                _ => true,
                _ => { frameChangedCount++; return false; },
                () => new SystemBackdropNativeStyleReadResult(true, SystemBackdrop.WsBorder));

            Assert.IsTrue(result.StyleWriteSucceeded);
            Assert.IsFalse(result.FrameChangedSucceeded);
            Assert.IsFalse(result.NativeFrameReady);
            Assert.IsTrue(result.HasConfirmedBorder);
            Assert.AreEqual(1, frameChangedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NativeFrame_Success_UsesOneFrameChangeAndConfirmsBorderOnly()
        {
            int frameChangedCount = 0;
            long desiredStyle = 0;

            var result = SystemBackdrop.TryPrepareQuickSwitchNativeFrameCore(
                () => new SystemBackdropNativeStyleReadResult(true, 0),
                style => { desiredStyle = style; return true; },
                _ => { frameChangedCount++; return true; },
                () => new SystemBackdropNativeStyleReadResult(true, desiredStyle));

            Assert.IsTrue(result.NativeFrameReady);
            Assert.IsTrue(result.HasConfirmedBorder);
            Assert.IsFalse(result.HasConfirmedCaption);
            Assert.IsFalse(result.HasConfirmedThickFrame);
            Assert.AreEqual(1, frameChangedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NativeFrame_NotReady_DisqualifiesDwmEdgeAndRetainsWpfFallback()
        {
            uint visibleFrame = SystemBackdrop.GetQualifiedVisibleFrameThickness(false, 2);

            Assert.AreEqual(0u, visibleFrame);
            Assert.AreEqual(0.5, SystemBackdrop.GetWpfOuterBorderThicknessDip(visibleFrame, 192, false), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OuterBorder_DwmFramePresent_UsesDwmAsSingleOwner()
        {
            Assert.AreEqual(0.0, SystemBackdrop.GetWpfOuterBorderThicknessDip(1, 144, false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OuterBorder_NoDwmFrame_UsesOnePhysicalPixelFallback()
        {
            Assert.AreEqual(1.0, SystemBackdrop.GetWpfOuterBorderThicknessDip(0, 96, false), 0.0001);
            Assert.AreEqual(0.5, SystemBackdrop.GetWpfOuterBorderThicknessDip(0, 192, false), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OuterBorder_HighContrast_DwmFrameRemainsSingleOwner()
        {
            Assert.AreEqual(0.0, SystemBackdrop.GetWpfOuterBorderThicknessDip(2, 192, true), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OuterBorder_HighContrastWithoutDwmFrame_UsesDpiAwareFallback()
        {
            Assert.AreEqual(0.5, SystemBackdrop.GetWpfOuterBorderThicknessDip(0, 192, true), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundedCornerScalarPayload_UsesFourByteInt32Seam()
        {
            uint cornerSize = 0;
            int cornerValue = 0;
            SystemBackdrop.DwmSetInt32Attribute setCorner = (IntPtr _, int _, ref int value, uint size) =>
            {
                cornerSize = size;
                cornerValue = value;
                return 0;
            };

            Assert.AreEqual(0, SystemBackdrop.SetWindowCornerPreferenceCore(IntPtr.Zero, 2, setCorner));
            Assert.AreEqual(4u, cornerSize);
            Assert.AreEqual(2, cornerValue);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DwmAttributePaths_DoNotUseEnumMarshalSize()
        {
            string source = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Interop", "SystemBackdrop.cs")));

            Assert.IsFalse(source.Contains("Marshal.SizeOf<DwmWindowCornerPreference>()", StringComparison.Ordinal));
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException($"Repository file not found: {relativePath}");
        }

    }
}
