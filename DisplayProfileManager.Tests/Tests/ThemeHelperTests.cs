using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ThemeHelperTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyButtonForegroundOwnership_PackagedCustomPackaged_TransfersPrimaryOwnership()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var resources = new ResourceDictionary();
                    var customForeground = new SolidColorBrush(Color.FromRgb(31, 79, 127));
                    var custom = new ResourceDictionary
                    {
                        ["ButtonBackgroundBrush"] = new SolidColorBrush(Colors.White),
                        ["ButtonForegroundBrush"] = customForeground
                    };
                    resources.MergedDictionaries.Add(custom);
                    var element = new Border { Resources = resources };

                    ThemeHelper.ApplyButtonForegroundOwnership(resources, useAutomaticForeground: true, Colors.White);
                    Assert.IsTrue(resources.Keys.Cast<object>().Contains("ButtonForegroundBrush"));
                    Assert.AreEqual(Colors.Black, ((SolidColorBrush)element.TryFindResource("ButtonForegroundBrush")).Color);

                    ThemeHelper.ApplyButtonForegroundOwnership(resources, useAutomaticForeground: false, Colors.White);
                    Assert.IsFalse(resources.Keys.Cast<object>().Contains("ButtonForegroundBrush"));
                    Assert.AreSame(customForeground, element.TryFindResource("ButtonForegroundBrush"));

                    ThemeHelper.ApplyButtonForegroundOwnership(resources, useAutomaticForeground: true, Colors.Black);
                    Assert.IsTrue(resources.Keys.Cast<object>().Contains("ButtonForegroundBrush"));
                    Assert.AreEqual(Colors.White, ((SolidColorBrush)element.TryFindResource("ButtonForegroundBrush")).Color);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null) throw failure;
        }
    }
}
