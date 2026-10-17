using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.UI.Themes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class ThemeServiceDispatcherRegressionTests
    {
        private sealed class MemoryStore(string root) : ISettingsStore
        {
            public string AppDataFolder => root;
            public string SettingsFilePath => Path.Combine(root, "unused.json");
            public Task<AppSettings> LoadAsync() => Task.FromResult(new AppSettings { Theme = "Theme" });
            public Task<bool> SaveAsync(AppSettings settings) => Task.FromResult(true);
        }

        private static string Xml(string marker) => "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"
            + "<Color x:Key=\"BackgroundColor\">#112233</Color>"
            + "<SolidColorBrush x:Key=\"WindowBackgroundBrush\" Color=\"" + marker + "\"/>"
            + "<SolidColorBrush x:Key=\"PrimaryTextBrush\" Color=\"White\"/>"
            + "<SolidColorBrush x:Key=\"ContentBackgroundBrush\" Color=\"Black\"/>"
            + "<SolidColorBrush x:Key=\"BorderBrush\" Color=\"#334455\"/>"
            + "<SolidColorBrush x:Key=\"ButtonBackgroundBrush\" Color=\"{DynamicResource {x:Static SystemColors.AccentColorKey}}\"/>"
            + "<SolidColorBrush x:Key=\"ButtonForegroundBrush\" Color=\"White\"/>"
            + "<DropShadowEffect x:Key=\"CardShadow\" Color=\"#222222\" BlurRadius=\"8\" Opacity=\"0.15\"/>"
            + "</ResourceDictionary>";

        private static Dictionary<string, ResourceDictionary> Themes(ThemeService s) =>
            (Dictionary<string, ResourceDictionary>)typeof(ThemeService)
                .GetField("_themes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;

        private static SystemThemeProvider Provider(ThemeService s) =>
            (SystemThemeProvider)typeof(ThemeService)
                .GetField("_systemThemeProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;

        private static void Raise(ThemeService s) =>
            typeof(SystemThemeProvider).GetMethod("OnUserPreferenceChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Provider(s), new object[] { null!, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color) });

        private static void RaiseMta(ThemeService s)
        {
            Exception error = null;
            var worker = new Thread(() => { try { Raise(s); } catch (Exception ex) { error = ex; } });
            worker.SetApartmentState(ApartmentState.MTA);
            worker.IsBackground = true;
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(8)), "MTA event was blocked/deadlocked");
            if (error != null) Assert.Fail("MTA event raised " + error);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RealAccentChangedPipeline_CoalescingInvalidAndDeletedTheme_KeepUiOwnershipAndLastGood()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dpm-theme-accent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "Theme.xaml");
            File.WriteAllText(file, Xml("#010203"));
            ThemeService service = null;
            SettingsService settings = null;
            Dispatcher dispatcher = null;
            Exception uiFailure = null;
            Exception unhandled = null;
            int changes = 0;
            var ready = new ManualResetEventSlim();
            var ui = new Thread(() =>
            {
                try
                {
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += (_, e) => { unhandled = e.Exception; e.Handled = true; };
                    settings = new SettingsService(new MemoryStore(dir));
                    settings.LoadSettingsAsync().GetAwaiter().GetResult();
                    service = new ThemeService(settings);
                    typeof(ThemeService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(service, new ThemeStore(dir));
                    service.ThemeChanged += (_, __) => changes++;
                    service.InitializeTheme();
                    dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception ex) { uiFailure = ex; }
                finally { ready.Set(); }
                if (uiFailure == null) Dispatcher.Run();
            });
            ui.IsBackground = true;
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            try
            {
                Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(12)), "STA UI fixture never initialized");
                if (uiFailure != null) Assert.Fail("STA fixture failed: " + uiFailure);
                ResourceDictionary initial = null;
                dispatcher.Invoke(() =>
                {
                    initial = Themes(service)["Theme"];
                    Assert.AreSame(initial, System.Windows.Application.Current.Resources.MergedDictionaries[^1]);
                    Assert.IsTrue(((SolidColorBrush)initial["ButtonBackgroundBrush"]).CheckAccess());
                    Assert.IsTrue(((DropShadowEffect)initial["CardShadow"]).CheckAccess());
                    Raise(service);
                    Assert.IsTrue(((SolidColorBrush)Themes(service)["Theme"]["ButtonBackgroundBrush"]).CheckAccess());
                    Assert.IsTrue(((DropShadowEffect)Themes(service)["Theme"]["CardShadow"]).CheckAccess());
                });

                int afterUiControl = dispatcher.Invoke(() => changes);
                var gateEntered = new ManualResetEventSlim();
                var gateRelease = new ManualResetEventSlim();
                dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    gateEntered.Set();
                    gateRelease.Wait(TimeSpan.FromSeconds(8));
                }));
                Assert.IsTrue(gateEntered.Wait(TimeSpan.FromSeconds(8)), "UI queue gate not entered");
                try
                {
                    RaiseMta(service);
                    File.WriteAllText(file, Xml("#AABBCC"));
                    RaiseMta(service);
                    Assert.AreEqual(1, (int)typeof(ThemeService)
                        .GetField("_accentRefreshQueued", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!,
                        "Both MTA events should share one pending UI refresh");
                }
                finally { gateRelease.Set(); }

                dispatcher.Invoke(() =>
                {
                    Assert.AreEqual(0, (int)typeof(ThemeService).GetField("_accentRefreshQueued", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!);
                    var selected = Themes(service)["Theme"];
                    Assert.AreNotSame(initial, selected, "Custom theme dictionary should rebuild");
                    Assert.AreSame(selected, System.Windows.Application.Current.Resources.MergedDictionaries[^1], "No Light fallback");
                    Assert.AreEqual("Theme", settings.Settings.Theme);
                    Assert.AreEqual((Color)ColorConverter.ConvertFromString("#AABBCC"),
                        ((SolidColorBrush)selected["WindowBackgroundBrush"]).Color, "Most recent synthetic file content must win");
                    Assert.AreEqual(SystemColors.AccentColor, ((SolidColorBrush)selected["ButtonBackgroundBrush"]).Color);
                    foreach (var value in selected.Values)
                    {
                        if (value is SolidColorBrush brush) Assert.IsTrue(brush.CheckAccess());
                        if (value is DropShadowEffect effect) Assert.IsTrue(effect.CheckAccess());
                    }
                    Assert.AreEqual(afterUiControl + 1, changes, "Two pending MTA events should coalesce to one apply");
                }, DispatcherPriority.Background);
                if (unhandled != null) Assert.Fail("DispatcherUnhandledException: " + unhandled);

                File.WriteAllText(file, "<not-a-resource-dictionary/>");
                RaiseMta(service);
                dispatcher.Invoke(() =>
                {
                    var selected = Themes(service)["Theme"];
                    Assert.AreEqual((Color)ColorConverter.ConvertFromString("#AABBCC"),
                        ((SolidColorBrush)selected["WindowBackgroundBrush"]).Color);
                    Assert.AreSame(selected, System.Windows.Application.Current.Resources.MergedDictionaries[^1]);
                    Assert.AreEqual("Theme", settings.Settings.Theme);
                }, DispatcherPriority.Background);

                File.Delete(file);
                RaiseMta(service);
                dispatcher.Invoke(() =>
                {
                    Assert.AreSame(Themes(service)["Theme"],
                        System.Windows.Application.Current.Resources.MergedDictionaries[^1], "Last known valid user theme must remain active after deletion");
                    Assert.AreEqual("Theme", settings.Settings.Theme);
                }, DispatcherPriority.Background);
                if (unhandled != null) Assert.Fail("Unhandled dispatcher refresh: " + unhandled);

                dispatcher.Invoke(() => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background));
                Assert.IsTrue(ui.Join(TimeSpan.FromSeconds(8)), "UI dispatcher did not shut down");
                RaiseMta(service);
                Assert.AreEqual(0, (int)typeof(ThemeService).GetField("_accentRefreshQueued", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!,
                    "Accent event after shutdown must not queue refresh");
            }
            finally
            {
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.Invoke(() =>
                    {
                        service?.Cleanup();
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    });
                }
                ui.Join(TimeSpan.FromSeconds(5));
                Directory.Delete(dir, true);
            }
        }
    }
}
