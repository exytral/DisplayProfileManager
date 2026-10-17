using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class MainWindowApplyBusyStateTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task BusyStateWrapper_RestoresInteractionAfterNormalCompletion()
        {
            var states = new List<bool>();
            await MainWindow.RunWithApplyBusyStateAsync(states.Add, () => Task.CompletedTask);
            CollectionAssert.AreEqual(new[] { true, false }, states);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task BusyStateWrapper_RestoresInteractionAfterException()
        {
            var states = new List<bool>();
            bool threw = false;
            try
            {
                await MainWindow.RunWithApplyBusyStateAsync(states.Add, () => throw new InvalidOperationException("boom"));
            }
            catch (InvalidOperationException ex) when (ex.Message == "boom")
            {
                threw = true;
            }

            Assert.IsTrue(threw);
            CollectionAssert.AreEqual(new[] { true, false }, states);
        }
    }
}
