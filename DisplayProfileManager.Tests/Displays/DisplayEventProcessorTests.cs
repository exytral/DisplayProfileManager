using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Events;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayEventProcessorTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_EventStorm_PreservesRawOrderAndCoalescesImmediateSamples()
        {
            var logs = new ConcurrentQueue<string>();
            var rawRecordsObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var immediateSampleObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int rawRecords = 0;
            int samples = 0;
            long start = Stopwatch.GetTimestamp();
            using var processor = new DisplayEventProcessor(
                () =>
                {
                    if (Interlocked.Increment(ref samples) == 1)
                        immediateSampleObserved.TrySetResult(true);

                    return new DisplayEventSnapshot();
                },
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                line =>
                {
                    logs.Enqueue(line);
                    if (line.Contains("\"recordKind\":\"raw\"") &&
                        Interlocked.Increment(ref rawRecords) == 3)
                    {
                        rawRecordsObserved.TrySetResult(true);
                    }
                },
                timestampProvider: () => start);
            processor.Start();

            processor.TryWrite(Raw(1, start));
            processor.TryWrite(Raw(2, start + Stopwatch.Frequency / 20));
            processor.TryWrite(Raw(3, start + Stopwatch.Frequency / 10));

            await rawRecordsObserved.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
            await immediateSampleObserved.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);

            string[] raw = [.. logs.Where(line => line.Contains("\"recordKind\":\"raw\""))];
            Assert.AreEqual(3, raw.Length);
            Assert.IsTrue(raw[0].Contains("\"sequence\":1"));
            Assert.IsTrue(raw[1].Contains("\"sequence\":2"));
            Assert.IsTrue(raw[2].Contains("\"sequence\":3"));
            Assert.AreEqual(1, Volatile.Read(ref samples));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_NewRawEvent_RebasesTailAndSamplesFinalSequence()
        {
            var logs = new ConcurrentQueue<string>();
            var firstDelayObserved = new TaskCompletionSource<TaskCompletionSource<bool>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondDelayObserved = new TaskCompletionSource<TaskCompletionSource<bool>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var finalSampleObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int delayInvocations = 0;
            long start = Stopwatch.GetTimestamp();
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) =>
                {
                    var completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    token.Register(() => completion.TrySetCanceled(token));

                    switch (Interlocked.Increment(ref delayInvocations))
                    {
                        case 1:
                            firstDelayObserved.TrySetResult(completion);
                            break;
                        case 2:
                            secondDelayObserved.TrySetResult(completion);
                            break;
                    }

                    return completion.Task;
                },
                line =>
                {
                    logs.Enqueue(line);
                    if (IsCcdSample(line, "tail+100", 11))
                        finalSampleObserved.TrySetResult(true);
                },
                timestampProvider: () => start);
            processor.Start();

            processor.TryWrite(Raw(10, Stopwatch.GetTimestamp()));
            TaskCompletionSource<bool> firstTailDelay = await firstDelayObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.CancellationToken);

            processor.TryWrite(Raw(11, Stopwatch.GetTimestamp()));
            TaskCompletionSource<bool> finalTailDelay = await secondDelayObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.CancellationToken);
            Assert.IsTrue(firstTailDelay.Task.IsCanceled);

            finalTailDelay.SetResult(true);

            await finalSampleObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.CancellationToken);
            Assert.IsTrue(logs.Any(line => IsCcdSample(line, "tail+100", 11)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_BaselineInFlight_DisposeWaitsAndStopsLast()
        {
            var logs = new ConcurrentQueue<string>();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var processor = new DisplayEventProcessor(
                () =>
                {
                    entered.Set();
                    release.Wait(TestContext.CancellationToken);
                    return new DisplayEventSnapshot();
                },
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                logs.Enqueue);
            processor.Start();
            processor.RequestBaselineSample();

            Assert.IsTrue(entered.Wait(2000, TestContext.CancellationToken));
#pragma warning disable MSTEST0049 // Cancellation must not prevent scheduling the cleanup operation under test
            Task disposeTask = Task.Run(processor.Dispose);
#pragma warning restore MSTEST0049
            Assert.IsFalse(disposeTask.Wait(100, TestContext.CancellationToken));
            Assert.IsFalse(logs.Any(line => line.Contains("processorStopped")));

            release.Set();

            Assert.IsTrue(disposeTask.Wait(2000, TestContext.CancellationToken));
            Assert.IsTrue(logs.Last().Contains("processorStopped"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_EventSampleInFlight_DisposeWaitsAndEmitsNoPostStopRecord()
        {
            var logs = new ConcurrentQueue<string>();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var processor = new DisplayEventProcessor(
                () =>
                {
                    entered.Set();
                    release.Wait(TestContext.CancellationToken);
                    return new DisplayEventSnapshot();
                },
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                logs.Enqueue);
            processor.Start();
            processor.TryWrite(Raw(1, Stopwatch.GetTimestamp()));

            Assert.IsTrue(entered.Wait(2000, TestContext.CancellationToken));
#pragma warning disable MSTEST0049 // Cancellation must not prevent scheduling the cleanup operation under test
            Task disposeTask = Task.Run(processor.Dispose);
#pragma warning restore MSTEST0049
            Assert.IsFalse(disposeTask.Wait(100, TestContext.CancellationToken));
            Assert.IsFalse(logs.Any(line => line.Contains("processorStopped")));

            release.Set();

            Assert.IsTrue(disposeTask.Wait(2000, TestContext.CancellationToken));
            int countAtStop = logs.Count;
            Assert.IsTrue(logs.Last().Contains("processorStopped"));
            Thread.Sleep(50);
            Assert.AreEqual(countAtStop, logs.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_SnapshotFactoryThrows_LogsCcdFailure()
        {
            var logs = new ConcurrentQueue<string>();
            using var processor = new DisplayEventProcessor(
                () => throw new InvalidOperationException("synthetic"),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                logs.Enqueue);
            processor.Start();

            processor.TryWrite(Raw(1, Stopwatch.GetTimestamp()));

            Assert.IsTrue(SpinWait.SpinUntil(() => logs.Any(line => line.Contains("ccdSampleFailed") && line.Contains("synthetic")), 2000));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_RawLoggingThrows_LogsProcessorFault()
        {
            var logs = new ConcurrentQueue<string>();
            bool failRaw = true;
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                line =>
                {
                    if (failRaw && line.Contains("\"recordKind\":\"raw\""))
                    {
                        failRaw = false;
                        throw new InvalidOperationException("synthetic processor fault");
                    }

                    logs.Enqueue(line);
                });
            processor.Start();

            processor.TryWrite(Raw(1, Stopwatch.GetTimestamp()));

            Assert.IsTrue(SpinWait.SpinUntil(
                () => logs.Any(line =>
                    line.Contains("processorFault") &&
                    line.Contains("synthetic processor fault")),
                2000));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetTailDelay_PriorCaptureDuration_DoesNotShiftAbsoluteDeadline()
        {
            long epoch = 1000;
            long frequency = 1000;

            TimeSpan first = DisplayEventProcessor.GetTailDelay(epoch, 100, 1000, frequency);
            TimeSpan secondAfterEightyMillisecondCapture =
                DisplayEventProcessor.GetTailDelay(epoch, 250, 1180, frequency);
            TimeSpan overdue =
                DisplayEventProcessor.GetTailDelay(epoch, 250, 1300, frequency);

            Assert.AreEqual(TimeSpan.FromMilliseconds(100), first);
            Assert.AreEqual(TimeSpan.FromMilliseconds(70), secondAfterEightyMillisecondCapture);
            Assert.AreEqual(TimeSpan.Zero, overdue);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ShouldTakeImmediateSample_WithinQuarterSecond_ReturnsFalse()
        {
            long frequency = 1000;

            Assert.IsFalse(DisplayEventProcessor.ShouldTakeImmediateSample(1000, 1249, frequency));
            Assert.IsTrue(DisplayEventProcessor.ShouldTakeImmediateSample(1000, 1250, frequency));
        }


        private static DisplayEventEngineeringOptions ResolveOptions(
            IReadOnlyDictionary<string, string> values,
            List<string> warnings = null) =>
            DisplayEventEngineeringOptions.Resolve(
                name => values.TryGetValue(name, out string value) ? value : null,
                warning => warnings?.Add(warning));

        [TestMethod]
        [TestCategory("Unit")]
        public void EngineeringOptions_EvidenceValues_ResolveQualifiedModes()
        {
            Assert.AreEqual(DisplayEventEvidenceMode.Disabled, ResolveOptions(new Dictionary<string, string>()).EvidenceMode);
            Assert.AreEqual(DisplayEventEvidenceMode.Disabled, ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName] = ""
            }).EvidenceMode);
            Assert.AreEqual(DisplayEventEvidenceMode.Disabled, ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName] = " off "
            }).EvidenceMode);
            Assert.AreEqual(DisplayEventEvidenceMode.ApplyScoped, ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName] = "apply-scoped"
            }).EvidenceMode);
            Assert.AreEqual(DisplayEventEvidenceMode.AppLifetime, ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName] = " app-lifetime "
            }).EvidenceMode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EngineeringOptions_InvalidEvidence_DisablesAndWarnsOnce()
        {
            var warnings = new List<string>();

            var options = ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName] = "APP-LIFETIME"
            }, warnings);

            Assert.AreEqual(DisplayEventEvidenceMode.Disabled, options.EvidenceMode);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "event=DisplayEventControlInvalid");
            StringAssert.Contains(warnings[0], DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EngineeringOptions_TraceValues_ResolveQualifiedState()
        {
            Assert.IsFalse(ResolveOptions(new Dictionary<string, string>()).TraceEnabled);
            Assert.IsFalse(ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.TraceEnvironmentVariableName] = ""
            }).TraceEnabled);
            Assert.IsFalse(ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.TraceEnvironmentVariableName] = " 0 "
            }).TraceEnabled);
            Assert.IsTrue(ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.TraceEnvironmentVariableName] = " 1 "
            }).TraceEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EngineeringOptions_InvalidTrace_DisablesAndWarnsOnce()
        {
            var warnings = new List<string>();

            var options = ResolveOptions(new Dictionary<string, string>
            {
                [DisplayEventEngineeringOptions.TraceEnvironmentVariableName] = "true"
            }, warnings);

            Assert.IsFalse(options.TraceEnabled);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "event=DisplayEventControlInvalid");
            StringAssert.Contains(warnings[0], DisplayEventEngineeringOptions.TraceEnvironmentVariableName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EngineeringOptions_ReadsEachKnownVariableExactlyOnce()
        {
            var reads = new Dictionary<string, int>();
            DisplayEventEngineeringOptions.Resolve(name =>
            {
                reads[name] = reads.TryGetValue(name, out int count) ? count + 1 : 1;
                return null;
            });

            Assert.AreEqual(1, reads[DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName]);
            Assert.AreEqual(1, reads[DisplayEventEngineeringOptions.TraceEnvironmentVariableName]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayEventEnvironmentVariables_AreOwnedOnlyByEngineeringOptions()
        {
            string root = FindRepositoryRoot();
            string productionRoot = Path.Combine(root, "DisplayProfileManager");
            string authority = Path.Combine(productionRoot,  "Displays", "Events", "DisplayEventEvidenceMode.cs");

            foreach (string variable in new[]
            {
                DisplayEventEngineeringOptions.EvidenceEnvironmentVariableName,
                DisplayEventEngineeringOptions.TraceEnvironmentVariableName
            })
            {
                string[] owners = [.. Directory.EnumerateFiles(productionRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(path => File.ReadAllText(path).Contains(variable, StringComparison.Ordinal))
                    .Select(Path.GetFullPath)];

                CollectionAssert.AreEquivalent(new[] { Path.GetFullPath(authority) }, owners, variable);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppComposition_DisabledMonitorAndIndependentTraceOwnership_ArePinned()
        {
            string root = FindRepositoryRoot();
            string app = File.ReadAllText(Path.Combine(root, "DisplayProfileManager",  "App.xaml.cs"));

            StringAssert.Contains(app, "DisplayEventEngineeringOptions.Resolve(");
            StringAssert.Contains(app, "displayEventOptions.EvidenceMode != DisplayEventEvidenceMode.Disabled");
            StringAssert.Contains(app, "if (_displayEventMonitor != null)");
            StringAssert.Contains(app, "_displayEventMonitor,");
            StringAssert.Contains(app, "displayEventOptions.TraceEnabled");
            StringAssert.Contains(app, "_displayEventMonitor?.Dispose()");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_ApplyScopedIdle_DoesNotCaptureBaselineOrRawEvidence()
        {
            var logs = new ConcurrentQueue<string>();
            int samples = 0;
            int delays = 0;
            long frequency = 1000;
            using var processor = new DisplayEventProcessor(
                () => { Interlocked.Increment(ref samples); return new DisplayEventSnapshot(); },
                (delay, token) => { Interlocked.Increment(ref delays); return Task.CompletedTask; },
                logs.Enqueue,
                timestampProvider: () => 11000,
                timestampFrequency: frequency,
                mode: DisplayEventEvidenceMode.ApplyScoped);
            processor.Start();

            processor.RequestBaselineSample();
            processor.TryWrite(Raw(1, 0));
            processor.TryWrite(Raw(2, 11000));

            Assert.IsTrue(SpinWait.SpinUntil(
                () => logs.Any(line => line.Contains("\"action\":\"processorStarted\"")),
                2000));
            Thread.Sleep(50);

            Assert.AreEqual(0, samples);
            Assert.AreEqual(0, delays, "Idle ApplyScoped events must not schedule even the +10 s tail.");
            Assert.IsFalse(logs.Any(line => line.Contains("\"recordKind\":\"raw\"")));
            Assert.IsFalse(logs.Any(line => line.Contains("\"recordKind\":\"ccd\"")));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_ApplyScoped_PreScopeCarryIn_IsRejectedAtEnqueue()
        {
            var logs = new ConcurrentQueue<string>();
            var readerEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReader = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int readGateCalls = 0;
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                logs.Enqueue,
                mode: DisplayEventEvidenceMode.ApplyScoped,
                beforeReadAsync: token =>
                {
                    if (Interlocked.Increment(ref readGateCalls) != 1)
                    {
                        return Task.CompletedTask;
                    }

                    readerEntered.TrySetResult(true);
                    token.Register(() => releaseReader.TrySetCanceled(token));
                    return releaseReader.Task;
                });
            processor.Start();

            Assert.IsTrue(readerEntered.Task.Wait(2000, TestContext.CancellationToken), "Reader gate was not reached.");
            Assert.IsFalse(processor.TryWrite(Raw(40, Stopwatch.GetTimestamp())));

            IAsyncDisposable scope = await processor.BeginApplyEvidenceScopeAsync();
            Assert.IsTrue(processor.TryWrite(Raw(41, Stopwatch.GetTimestamp())));
            releaseReader.TrySetResult(true);

            Assert.IsTrue(SpinWait.SpinUntil(
                () => logs.Any(line =>
                    line.Contains("\"recordKind\":\"raw\"") &&
                    line.Contains("\"sequence\":41")),
                2000));

            Assert.IsFalse(logs.Any(line =>
                line.Contains("\"recordKind\":\"raw\"") &&
                line.Contains("\"sequence\":40")));
            Assert.IsFalse(logs.Any(line => IsCcdSample(line, "event", 40)));
            Assert.IsFalse(logs.Any(line => line.Contains("\"triggerLastSequence\":40")));

            await scope.DisposeAsync();
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_ApplyScoped_InScopeTerminalEvent_DrainsBeforeCompletion()
        {
            var logs = new ConcurrentQueue<string>();
            var readerEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReader = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var tailCanceled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int readGateCalls = 0;
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) =>
                {
                    token.Register(() => tailCanceled.TrySetResult(true));
                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                logs.Enqueue,
                mode: DisplayEventEvidenceMode.ApplyScoped,
                beforeReadAsync: token =>
                {
                    if (Interlocked.Increment(ref readGateCalls) != 1)
                    {
                        return Task.CompletedTask;
                    }

                    readerEntered.TrySetResult(true);
                    token.Register(() => releaseReader.TrySetCanceled(token));
                    return releaseReader.Task;
                });
            processor.Start();

            Assert.IsTrue(readerEntered.Task.Wait(2000, TestContext.CancellationToken), "Reader gate was not reached.");
            IAsyncDisposable scope = await processor.BeginApplyEvidenceScopeAsync();
            Assert.IsTrue(processor.TryWrite(Raw(50, Stopwatch.GetTimestamp())));

            Task closeTask = scope.DisposeAsync().AsTask();
            await Task.Yield();
            Assert.IsFalse(closeTask.IsCompleted, "Scope close must wait for the accepted raw-event barrier.");

            releaseReader.TrySetResult(true);
            await closeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
            await closeTask;

            string[] ordered = [.. logs];
            int rawIndex = Array.FindIndex(ordered, line =>
                line.Contains("\"recordKind\":\"raw\"") &&
                line.Contains("\"sequence\":50"));
            int completionIndex = Array.FindIndex(ordered, line => IsCcdSample(line, "applyCompletion", 0));
            Assert.IsTrue(rawIndex >= 0, "Accepted terminal event must be processed.");
            Assert.IsTrue(completionIndex > rawIndex, "applyCompletion must follow terminal raw processing.");
            Assert.IsTrue(tailCanceled.Task.IsCompleted, "Remaining tail work must be canceled after the barrier.");

            Assert.IsFalse(processor.TryWrite(Raw(51, Stopwatch.GetTimestamp())));
            Assert.IsFalse(logs.Any(line =>
                line.Contains("\"recordKind\":\"raw\"") &&
                line.Contains("\"sequence\":51")));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_ApplyScopedWindow_CapturesBoundariesAndCancelsRemainingTail()
        {
            var logs = new ConcurrentQueue<string>();
            int samples = 0;
            using var processor = new DisplayEventProcessor(
                () => { Interlocked.Increment(ref samples); return new DisplayEventSnapshot(); },
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                logs.Enqueue,
                mode: DisplayEventEvidenceMode.ApplyScoped);
            processor.Start();

            IAsyncDisposable scope = await processor.BeginApplyEvidenceScopeAsync();
            Assert.IsTrue(SpinWait.SpinUntil(() => logs.Any(line => IsCcdSample(line, "applyStart", 0)), 2000));

            processor.TryWrite(Raw(10, Stopwatch.GetTimestamp()));
            Assert.IsTrue(SpinWait.SpinUntil(() => logs.Any(line => line.Contains("\"recordKind\":\"raw\"")), 2000));
            Assert.IsTrue(SpinWait.SpinUntil(() => logs.Any(line => IsCcdSample(line, "event", 10)), 2000));

            await scope.DisposeAsync();

            Assert.IsTrue(logs.Any(line => IsCcdSample(line, "applyCompletion", 0)));
            int ccdCountAtClose = logs.Count(line => line.Contains("\"recordKind\":\"ccd\""));
            Thread.Sleep(100);
            Assert.AreEqual(ccdCountAtClose, logs.Count(line => line.Contains("\"recordKind\":\"ccd\"")));
            Assert.IsTrue(samples >= 3);

            processor.TryWrite(Raw(11, Stopwatch.GetTimestamp()));
            Thread.Sleep(100);
            Assert.IsFalse(logs.Any(line => line.Contains("\"sequence\":11") && line.Contains("\"recordKind\":\"raw\"")));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_AppLifetimeApplyScope_DoesNotTruncateExistingTail()
        {
            var logs = new ConcurrentQueue<string>();
            var delays = new ConcurrentQueue<TaskCompletionSource<bool>>();
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) =>
                {
                    var completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    token.Register(() => completion.TrySetCanceled(token));
                    delays.Enqueue(completion);
                    return completion.Task;
                },
                logs.Enqueue,
                mode: DisplayEventEvidenceMode.AppLifetime);
            processor.Start();

            IAsyncDisposable scope = await processor.BeginApplyEvidenceScopeAsync();
            processor.TryWrite(Raw(20, Stopwatch.GetTimestamp()));
            Assert.IsTrue(SpinWait.SpinUntil(() => delays.Count >= 1, 2000));

            await scope.DisposeAsync();

            Assert.IsTrue(delays.TryDequeue(out var firstTailDelay));
            firstTailDelay.SetResult(true);
            Assert.IsTrue(SpinWait.SpinUntil(
                () => logs.Any(line => IsCcdSample(line, "tail+100", 20)),
                2000));
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_AppLifetime_RetainsStartupBaselineAndIdleEventEvidence()
        {
            var logs = new ConcurrentQueue<string>();
            int samples = 0;
            using var processor = new DisplayEventProcessor(
                () => { Interlocked.Increment(ref samples); return new DisplayEventSnapshot(); },
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                logs.Enqueue,
                mode: DisplayEventEvidenceMode.AppLifetime);
            processor.Start();

            processor.RequestBaselineSample();
            Assert.IsTrue(SpinWait.SpinUntil(() => logs.Any(line => IsCcdSample(line, "baseline", 0)), 2000));

            processor.TryWrite(Raw(30, Stopwatch.GetTimestamp()));

            Assert.IsTrue(SpinWait.SpinUntil(
                () => logs.Any(line => line.Contains("\"recordKind\":\"raw\"") && line.Contains("\"sequence\":30")),
                2000));
            Assert.IsTrue(SpinWait.SpinUntil(() => logs.Any(line => IsCcdSample(line, "event", 30)), 2000));
            Assert.IsTrue(samples >= 2);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_ApplyScoped_TraceOff_RetainsEvidenceWithoutOrdinaryMirror()
        {
            var evidence = new ConcurrentQueue<string>();
            var ordinary = new ConcurrentQueue<string>();
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                evidence.Enqueue,
                mode: DisplayEventEvidenceMode.ApplyScoped,
                ordinaryLogSink: ordinary.Enqueue);
            processor.Start();

            IAsyncDisposable scope = await processor.BeginApplyEvidenceScopeAsync();
            Assert.IsTrue(processor.TryWrite(Raw(59, Stopwatch.GetTimestamp())));
            Assert.IsTrue(SpinWait.SpinUntil(
                () => evidence.Any(line =>
                    line.Contains("\"recordKind\":\"raw\"") &&
                    line.Contains("\"sequence\":59")),
                2000));

            Assert.AreEqual(0, ordinary.Count);
            await scope.DisposeAsync();
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Processor_ApplyScoped_TraceMirrorsOnlyAcceptedGeneration()
        {
            var evidence = new ConcurrentQueue<string>();
            var ordinary = new ConcurrentQueue<string>();
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                evidence.Enqueue,
                mode: DisplayEventEvidenceMode.ApplyScoped,
                ordinaryLogSink: ordinary.Enqueue,
                traceEnabled: true);
            processor.Start();

            Assert.IsFalse(processor.TryWrite(Raw(60, Stopwatch.GetTimestamp())));
            IAsyncDisposable scope = await processor.BeginApplyEvidenceScopeAsync();
            Assert.IsTrue(processor.TryWrite(Raw(61, Stopwatch.GetTimestamp())));

            Assert.IsTrue(SpinWait.SpinUntil(() => ordinary.Any(line => line.Contains("sequence=61")), 2000));
            Assert.IsFalse(ordinary.Any(line => line.Contains("sequence=60")));
            string inside = ordinary.Single(line => line.Contains("sequence=61"));
            StringAssert.Contains(inside, "generation=1");
            StringAssert.Contains(inside, "family=WM_DISPLAYCHANGE");
            Assert.IsFalse(evidence.Any(line =>
                line.Contains("\"recordKind\":\"raw\"") &&
                line.Contains("\"sequence\":60")));
            Assert.IsTrue(SpinWait.SpinUntil(
                () => evidence.Any(line =>
                    line.Contains("\"recordKind\":\"raw\"") &&
                    line.Contains("\"sequence\":61")),
                2000));

            await scope.DisposeAsync();
            Assert.IsFalse(processor.TryWrite(Raw(62, Stopwatch.GetTimestamp())));
            Thread.Sleep(50);
            Assert.IsFalse(ordinary.Any(line => line.Contains("sequence=62")));
            Assert.IsFalse(evidence.Any(line =>
                line.Contains("\"recordKind\":\"raw\"") &&
                line.Contains("\"sequence\":62")));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_AppLifetime_OrdinaryMirrorIncludesAcceptedIdleEvent()
        {
            var evidence = new ConcurrentQueue<string>();
            var ordinary = new ConcurrentQueue<string>();
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                evidence.Enqueue,
                mode: DisplayEventEvidenceMode.AppLifetime,
                ordinaryLogSink: ordinary.Enqueue,
                traceEnabled: true);
            processor.Start();

            Assert.IsTrue(processor.TryWrite(Raw(70, Stopwatch.GetTimestamp())));
            Assert.IsTrue(SpinWait.SpinUntil(() => ordinary.Any(), 2000));

            string mirrored = ordinary.Single();
            StringAssert.Contains(mirrored, "sequence=70");
            Assert.IsFalse(mirrored.Contains("generation="));
            Assert.IsTrue(SpinWait.SpinUntil(
                () => evidence.Any(line =>
                    line.Contains("\"recordKind\":\"raw\"") &&
                    line.Contains("\"sequence\":70")),
                2000));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Processor_AppLifetime_OrdinaryMirrorSupportsAllEventFamiliesWithoutDuplicates()
        {
            var ordinary = new ConcurrentQueue<string>();
            using var processor = new DisplayEventProcessor(
                () => new DisplayEventSnapshot(),
                (delay, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
                mode: DisplayEventEvidenceMode.AppLifetime,
                ordinaryLogSink: ordinary.Enqueue,
                traceEnabled: true);
            processor.Start();

            Assert.IsTrue(processor.TryWrite(new DisplayRawEvent
            {
                Sequence = 71,
                Message = "WM_DISPLAYCHANGE",
                WParam = 32,
                BitsPerPixel = 32,
                Width = 1920,
                Height = 1080
            }));
            Assert.IsTrue(processor.TryWrite(new DisplayRawEvent
            {
                Sequence = 72,
                Message = "WM_DEVICECHANGE",
                SubtypeName = "DbtDeviceArrival",
                WParam = 0x8000,
                DeviceType = 5,
                DeviceInterfacePath = @"\\?\DISPLAY#TEST"
            }));
            Assert.IsTrue(processor.TryWrite(new DisplayRawEvent
            {
                Sequence = 73,
                Message = "WM_POWERBROADCAST",
                SubtypeName = "PbtPowerSettingChange",
                WParam = 0x8013,
                PowerSettingGuid = Guid.Empty,
                PowerStateName = "On",
                PowerStateValue = 1
            }));

            Assert.IsTrue(SpinWait.SpinUntil(() => ordinary.Count == 3, 2000));
            Assert.AreEqual(3, ordinary.Count);
            Assert.AreEqual(1, ordinary.Count(line => line.Contains("sequence=71") && line.Contains("family=WM_DISPLAYCHANGE")));
            Assert.AreEqual(1, ordinary.Count(line => line.Contains("sequence=72") && line.Contains("family=WM_DEVICECHANGE")));
            Assert.AreEqual(1, ordinary.Count(line => line.Contains("sequence=73") && line.Contains("family=WM_POWERBROADCAST")));
        }


        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "DisplayProfileManager.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            Assert.Fail("Repository root not found.");
            return string.Empty;
        }

        private static bool IsCcdSample(string line, string reason, long lastSequence)
        {
            const string prefix = "DPM_DISPLAY_EVENT ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(line[prefix.Length..]);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("recordKind", out JsonElement kind) &&
                kind.GetString() == "ccd" &&
                root.TryGetProperty("sampleReason", out JsonElement sampleReason) &&
                sampleReason.GetString() == reason &&
                root.TryGetProperty("triggerLastSequence", out JsonElement sequence) &&
                sequence.GetInt64() == lastSequence;
        }

        private static DisplayRawEvent Raw(long sequence, long ticks) => new()
        {
            Sequence = sequence,
            WallUtc = DateTime.UnixEpoch,
            MonotonicTicks = ticks,
            MessageId = 0x7E,
            Message = "WM_DISPLAYCHANGE"
        };
    }
}
