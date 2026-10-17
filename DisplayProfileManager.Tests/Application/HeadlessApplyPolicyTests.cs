using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Application;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class HeadlessApplyPolicyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Evaluate_ApplyFailure_IsFailure()
        {
            var result = new ProfileApplyResult { Success = false, CurrentProfilePersisted = false };

            Assert.AreEqual(HeadlessApplyOutcome.ApplyFailed, HeadlessApplyPolicy.Evaluate(result));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Evaluate_ApplySuccessWithMarkerFailure_IsPersistenceFailure()
        {
            var result = new ProfileApplyResult { Success = true, CurrentProfilePersisted = false };

            Assert.AreEqual(HeadlessApplyOutcome.CurrentProfilePersistenceFailed, HeadlessApplyPolicy.Evaluate(result));
            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.CurrentProfilePersisted);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Evaluate_ApplyAndMarkerSuccess_IsComplete()
        {
            var result = new ProfileApplyResult { Success = true, CurrentProfilePersisted = true };

            Assert.AreEqual(HeadlessApplyOutcome.Complete, HeadlessApplyPolicy.Evaluate(result));
        }
    }
}
