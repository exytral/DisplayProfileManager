using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI.Displays;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class SavedLayoutProjectorTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SingleDisplayAtOrigin_ProjectsCenteredWithUniformScale()
        {
            var geometry = SavedLayoutProjector.Build([Setting("A", 0, 0, 1920, 1080)]);
            var projection = SavedLayoutProjector.Fit(geometry, 300, 168, 12);

            Assert.AreEqual(1, geometry.Footprints.Count);
            Assert.AreEqual(0L, geometry.LogicalBounds.X);
            Assert.AreEqual(1920L, geometry.LogicalBounds.Width);
            Assert.AreEqual(144.0 / 1080.0, projection.Scale, 0.000001);
            Assert.AreEqual(22, projection.Footprints[0].X, 0.000001);
            Assert.AreEqual(12, projection.Footprints[0].Y, 0.000001);
            Assert.AreEqual(256, projection.Footprints[0].Width, 0.000001);
            Assert.AreEqual(144, projection.Footprints[0].Height, 0.000001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DualDisplayWithNegativeCoordinate_PreservesRelativeOffsetAndBounds()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("Left", -1280, 100, 1280, 1024),
                Setting("Right", 0, 0, 1920, 1080)
            ]);

            Assert.AreEqual(-1280L, geometry.LogicalBounds.X);
            Assert.AreEqual(0L, geometry.LogicalBounds.Y);
            Assert.AreEqual(3200L, geometry.LogicalBounds.Width);
            Assert.AreEqual(1124L, geometry.LogicalBounds.Height);
            Assert.AreEqual(-1280L, geometry.Footprints[0].X);
            Assert.AreEqual(100L, geometry.Footprints[0].Y);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void VerticalStagger_PreservesSavedYDifference()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("A", 0, -300, 1600, 900),
                Setting("B", 1600, 200, 1920, 1080)
            ]);

            var projection = SavedLayoutProjector.Fit(geometry, 500, 168, 12);
            double projectedDelta = projection.Footprints[1].Y - projection.Footprints[0].Y;
            Assert.AreEqual(500 * projection.Scale, projectedDelta, 0.000001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ThreeDisplayMixedGeometry_ComputesFullUnion()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("Left", -1200, 100, 1200, 1920),
                Setting("Center", 0, 0, 2560, 1440),
                Setting("Right", 2560, 400, 3440, 1440)
            ]);

            Assert.AreEqual(-1200L, geometry.LogicalBounds.X);
            Assert.AreEqual(0L, geometry.LogicalBounds.Y);
            Assert.AreEqual(7200L, geometry.LogicalBounds.Width);
            Assert.AreEqual(2020L, geometry.LogicalBounds.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NonZeroDesktopOrigin_NormalizesByTranslationOnly()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("A", 500, 250, 1000, 500),
                Setting("B", 1500, 250, 1000, 500)
            ]);
            var projection = SavedLayoutProjector.Fit(geometry, 224, 74, 12);

            Assert.AreEqual(500L, geometry.LogicalBounds.X);
            Assert.AreEqual(250L, geometry.LogicalBounds.Y);
            Assert.AreEqual(12, projection.Footprints[0].X, 0.000001);
            Assert.AreEqual(112, projection.Footprints[1].X, 0.000001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SavedPrimary_MarksItsFootprintWithoutInferringFromOrigin()
        {
            var origin = Setting("Origin", 0, 0, 1920, 1080);
            var primary = Setting("Primary", 1920, 0, 1920, 1080, isPrimary: true);
            var geometry = SavedLayoutProjector.Build([origin, primary]);

            Assert.IsFalse(geometry.Footprints[0].IsPrimary);
            Assert.IsTrue(geometry.Footprints[1].IsPrimary);
            Assert.AreEqual(1, geometry.PrimaryDisplayCount);
            Assert.IsFalse(geometry.HasPrimaryStateWarning);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CloneGroup_ProducesOneFootprintAndRetainsMemberMetadata()
        {
            var attached = Setting("Attached", 0, 0, 1920, 1080, cloneGroup: "g");
            var source = Setting("Source", 0, 0, 1920, 1080, cloneGroup: "g", cloneSource: true, isPrimary: true);
            var geometry = SavedLayoutProjector.Build([attached, source]);

            Assert.AreEqual(1, geometry.Footprints.Count);
            Assert.AreEqual("Source", geometry.Footprints[0].Name);
            Assert.IsTrue(geometry.Footprints[0].IsCloneGroup);
            Assert.AreEqual(2, geometry.Footprints[0].CloneMemberCount);
            Assert.AreEqual(2, geometry.Footprints[0].Members.Count);
            Assert.AreEqual("Source", geometry.Footprints[0].Members[0].Name);
            Assert.AreEqual("Attached", geometry.Footprints[0].Members[1].Name);
            Assert.IsTrue(geometry.Footprints[0].IsPrimary);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CloneGroupMissingSource_UsesBuilderFirstMemberFallback()
        {
            var first = Setting("First", 100, 50, 1600, 900, cloneGroup: "g");
            var second = Setting("Second", 100, 50, 1600, 900, cloneGroup: "g");
            var geometry = SavedLayoutProjector.Build([first, second]);

            Assert.AreEqual(1, geometry.Footprints.Count);
            Assert.AreEqual("First", geometry.Footprints[0].Name);
            Assert.AreEqual(100L, geometry.Footprints[0].X);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CloneGroupDivergentGeometry_UsesRepresentativeAndFlagsInconsistency()
        {
            var source = Setting("Source", 0, 0, 1920, 1080, cloneGroup: "g", cloneSource: true);
            var attached = Setting("Attached", 25, 0, 1920, 1080, cloneGroup: "g");
            var geometry = SavedLayoutProjector.Build([source, attached]);

            Assert.AreEqual(0L, geometry.Footprints[0].X);
            Assert.IsTrue(geometry.Footprints[0].HasInconsistentCloneGeometry);
            Assert.AreEqual(1, geometry.InconsistentCloneCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisabledDisplay_DoesNotContributeToBoundsAndIsCounted()
        {
            var enabled = Setting("Enabled", 0, 0, 1920, 1080);
            var disabled = Setting("Disabled", -5000, -5000, 4000, 3000, isEnabled: false);
            var geometry = SavedLayoutProjector.Build([enabled, disabled]);

            Assert.AreEqual(1, geometry.Footprints.Count);
            Assert.AreEqual(1920L, geometry.LogicalBounds.Width);
            Assert.AreEqual(1, geometry.DisabledDisplayCount);
            Assert.AreEqual(1, geometry.EnabledLayoutCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MixedEnabledDisabledClone_UsesEnabledDrawableFallbackWithoutMutation()
        {
            var source = Setting("Source", 50, 60, 0, 0, cloneGroup: "g", cloneSource: true, isEnabled: false);
            var attached = Setting("Attached", 200, 300, 2560, 1440, cloneGroup: "g", isPrimary: true);
            var geometry = SavedLayoutProjector.Build([source, attached]);

            Assert.AreEqual("Source", geometry.Footprints[0].Name);
            Assert.AreEqual(200L, geometry.Footprints[0].X);
            Assert.AreEqual(300L, geometry.Footprints[0].Y);
            Assert.IsTrue(geometry.Footprints[0].IsPrimary);
            Assert.AreEqual(1, geometry.DisabledDisplayCount);
            Assert.IsFalse(source.IsEnabled);
            Assert.AreEqual(0, source.Width);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Rotate90_DoesNotSwapSourceWidthHeight()
        {
            var geometry = SavedLayoutProjector.Build([Setting("Portrait", 0, 0, 1080, 1920, rotation: 2)]);

            Assert.AreEqual(1080L, geometry.Footprints[0].Width);
            Assert.AreEqual(1920L, geometry.Footprints[0].Height);
            Assert.AreEqual(2, geometry.Footprints[0].Rotation);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Rotate270_DoesNotSwapSourceWidthHeight()
        {
            var geometry = SavedLayoutProjector.Build([Setting("Portrait", 0, 0, 1200, 1920, rotation: 4)]);

            Assert.AreEqual(1200L, geometry.Footprints[0].Width);
            Assert.AreEqual(1920L, geometry.Footprints[0].Height);
            Assert.AreEqual(4, geometry.Footprints[0].Rotation);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ZeroOrNegativeDimensions_AreExcludedWithoutFabrication()
        {
            var zero = Setting("Zero", 0, 0, 0, 1080);
            var negative = Setting("Negative", 100, 100, 1920, -1);
            var geometry = SavedLayoutProjector.Build([zero, negative]);

            Assert.AreEqual(0, geometry.Footprints.Count);
            Assert.AreEqual(2, geometry.LayoutUnavailableCount);
            Assert.IsFalse(geometry.HasLogicalBounds);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OverlappingIndependentDisplays_PreserveExactCoordinatesAndFlagOverlap()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("A", 10, 20, 1920, 1080),
                Setting("B", 10, 20, 1920, 1080)
            ]);

            Assert.AreEqual(10L, geometry.Footprints[0].X);
            Assert.AreEqual(10L, geometry.Footprints[1].X);
            Assert.AreEqual(1, geometry.OverlapCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExtremeCoordinates_UseOverflowSafeBounds()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("Left", int.MinValue, 0, 1, 1),
                Setting("Right", int.MaxValue - 10, 0, int.MaxValue, 1)
            ]);

            long expectedRight = (long)(int.MaxValue - 10) + int.MaxValue;
            Assert.AreEqual(int.MinValue, geometry.LogicalBounds.X);
            Assert.AreEqual(expectedRight - int.MinValue, geometry.LogicalBounds.Width);
            Assert.IsTrue(geometry.LogicalBounds.Width > int.MaxValue);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FitToBounds_UsesSingleMinimumScaleAndTwelveDipInset()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("A", 0, 0, 1000, 1000),
                Setting("B", 1000, 0, 1000, 500)
            ]);
            var projection = SavedLayoutProjector.Fit(geometry, 224, 124, 12);

            Assert.AreEqual(0.1, projection.Scale, 0.000001);
            Assert.AreEqual(12, projection.OriginX, 0.000001);
            Assert.AreEqual(12, projection.OriginY, 0.000001);
            Assert.AreEqual(100, projection.Footprints[0].Width, 0.000001);
            Assert.AreEqual(100, projection.Footprints[0].Height, 0.000001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resize_ReprojectsProportionallyFromSameLogicalGeometry()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("A", 0, 0, 1000, 500),
                Setting("B", 1000, 0, 1000, 500)
            ]);
            var small = SavedLayoutProjector.Fit(geometry, 224, 74, 12);
            var large = SavedLayoutProjector.Fit(geometry, 424, 124, 12);

            Assert.AreSame(geometry, small.Geometry);
            Assert.AreSame(geometry, large.Geometry);
            Assert.AreEqual(2, large.Scale / small.Scale, 0.000001);
            Assert.AreEqual(2, large.Footprints[0].Width / small.Footprints[0].Width, 0.000001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DegenerateViewport_ReturnsFiniteEmptyProjection()
        {
            var geometry = SavedLayoutProjector.Build([Setting("A", 0, 0, 1920, 1080)]);

            foreach (var projection in new[]
            {
                SavedLayoutProjector.Fit(geometry, 0, 168, 12),
                SavedLayoutProjector.Fit(geometry, double.NaN, 168, 12),
                SavedLayoutProjector.Fit(geometry, double.PositiveInfinity, 168, 12),
                SavedLayoutProjector.Fit(geometry, 20, 20, 12)
            })
            {
                Assert.AreEqual(0, projection.Footprints.Count);
                Assert.IsTrue(double.IsFinite(projection.Scale));
                Assert.IsTrue(double.IsFinite(projection.OriginX));
                Assert.IsTrue(double.IsFinite(projection.OriginY));
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildAndFit_DoNotMutateSourceSettingsOrOrdering()
        {
            var first = Setting("First", -100, 25, 1600, 900, rotation: 2);
            var second = Setting("Second", 1500, 50, 1920, 1080, isPrimary: true);
            var source = new List<DisplaySetting> { first, second };
            var snapshot = source.Select(setting => new
            {
                setting.DisplayPositionX,
                setting.DisplayPositionY,
                setting.Width,
                setting.Height,
                setting.Rotation,
                setting.IsPrimary
            }).ToList();

            var geometry = SavedLayoutProjector.Build(source);
            _ = SavedLayoutProjector.Fit(geometry, 300, 168, 12);

            Assert.AreSame(first, source[0]);
            Assert.AreSame(second, source[1]);
            Assert.AreEqual(snapshot[0].DisplayPositionX, first.DisplayPositionX);
            Assert.AreEqual(snapshot[0].DisplayPositionY, first.DisplayPositionY);
            Assert.AreEqual(snapshot[0].Width, first.Width);
            Assert.AreEqual(snapshot[0].Height, first.Height);
            Assert.AreEqual(snapshot[0].Rotation, first.Rotation);
            Assert.AreEqual(snapshot[1].DisplayPositionX, second.DisplayPositionX);
            Assert.AreEqual(snapshot[1].IsPrimary, second.IsPrimary);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void VisibleNames_CloneGroup_UsesRepresentativeFirstWithoutRoleOrCountText()
        {
            var attached = Setting("Attached", 0, 0, 1920, 1080, cloneGroup: "g");
            var source = Setting("Source", 0, 0, 1920, 1080, cloneGroup: "g", cloneSource: true);
            var geometry = SavedLayoutProjector.Build([attached, source]);

            var names = SavedLayoutPresentationPolicy.GetVisibleNames(geometry.Footprints[0]);

            CollectionAssert.AreEqual(new[] { "Source", "Attached" }, names.ToArray());
            Assert.IsFalse(names.Any(name => name.Contains("Source)", StringComparison.Ordinal)));
            Assert.IsFalse(names.Any(name => name.Contains("Clone", StringComparison.Ordinal)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExceptionalSummary_NormalSavedLayout_IsEmpty()
        {
            var geometry = SavedLayoutProjector.Build(
            [
                Setting("Primary", 0, 0, 1920, 1080, isPrimary: true),
                Setting("Secondary", 1920, 0, 1920, 1080)
            ]);

            Assert.AreEqual(string.Empty, SavedLayoutPresentationPolicy.BuildExceptionalSummary(geometry));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExceptionalSummary_ExceptionalSavedState_ContainsOnlyWarnings()
        {
            var source = Setting("Source", 0, 0, 1920, 1080, cloneGroup: "g", cloneSource: true);
            var attached = Setting("Attached", 10, 0, 1920, 1080, cloneGroup: "g");
            var overlap = Setting("Overlap", 0, 0, 1920, 1080);
            var disabled = Setting("Disabled", 5000, 0, 1920, 1080, isEnabled: false);
            var unavailable = Setting("Unavailable", 7000, 0, 0, 1080);
            var geometry = SavedLayoutProjector.Build([source, attached, overlap, disabled, unavailable]);

            string summary = SavedLayoutPresentationPolicy.BuildExceptionalSummary(geometry);

            StringAssert.Contains(summary, "1 disabled display not shown");
            StringAssert.Contains(summary, "1 layout unavailable");
            StringAssert.Contains(summary, "saved overlap");
            StringAssert.Contains(summary, "clone geometry differs");
            StringAssert.Contains(summary, "primary state warning (0 saved)");
            Assert.IsFalse(summary.Contains("enabled layout", StringComparison.Ordinal));
            Assert.IsFalse(summary.Contains("clone group", StringComparison.Ordinal));
            Assert.IsFalse(summary.Contains("Primary:", StringComparison.Ordinal));
        }

        private static DisplaySetting Setting(
            string name,
            int x,
            int y,
            int width,
            int height,
            bool isEnabled = true,
            bool isPrimary = false,
            int rotation = 1,
            string cloneGroup = "",
            bool cloneSource = false)
        {
            return new DisplaySettingBuilder()
                .WithDeviceName($"\\\\.\\DISPLAY-{name}")
                .WithName(name)
                .WithPosition(x, y)
                .WithResolution(width, height)
                .Enabled(isEnabled)
                .Primary(isPrimary)
                .WithRotation(rotation)
                .WithCloneGroup(cloneGroup)
                .AsCloneSource(cloneSource)
                .Build();
        }
    }
}
