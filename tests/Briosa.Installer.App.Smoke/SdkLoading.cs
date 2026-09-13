using System.IO;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.App;
using Briosa.Installer.Core;
using Briosa.Installer.Tests;

internal static partial class Program
{
    private static void ExerciseAutomaticSdkLoading()
    {
        using var feed = new SignedFeed();
        var config = Path.Combine(feed.Root, "settings.json");
        File.WriteAllText(config, """{"schemaVersion":1,"appearance":{"theme":"dark"}}""");
        var discovery = new HeldSdkDiscovery();
        var report = new FakeSdkDiscovery().Inspect();
        var window = new MainWindow(new(config), packageStore: new PackageStore(feed.StorePath), sdkDiscovery: discovery);
        try
        {
            // A page opened before startup finishes must still load, without a configured source.
            Page(window, "SdkNavigation"); Load(window);
            PumpUntil(() => discovery.Calls == 1);
            Require(!window.IsWorking && !Find<Button>(window, "RefreshSdkButton").IsEnabled,
                "SDK discovery blocked the app or allowed an overlapping refresh.");
            Page(window, "SettingsNavigation");
            Require(Find<ComboBox>(window, "ThemeSelector").IsEnabled, "SDK discovery disabled unrelated Settings.");
            Page(window, "SdkNavigation"); Click(window, "RefreshSdkButton");
            Require(discovery.Calls == 1, "Rapid navigation or refresh duplicated a pending SDK scan.");
            discovery.Pending!.SetResult(report);
            PumpUntil(() => Find<Button>(window, "RefreshSdkButton").IsEnabled);
            Require(Find<DataGrid>(window, "SdkObservations").Items.Count == 3, "SDK rows did not load automatically.");
            Require(Find<Border>(window, "SdkRecommendation").Visibility == Visibility.Visible &&
                Find<TextBlock>(window, "SdkRecommendationText").Text.Contains("strongly recommend", StringComparison.Ordinal) &&
                Find<Button>(window, "ChangeSdkButton").IsEnabled,
                "An older SDK did not produce a non-blocking recommendation on automatic load.");

            // Returning to the page rereads local state; an empty result must remove stale rows.
            Page(window, "ActivityNavigation"); Page(window, "SdkNavigation");
            PumpUntil(() => discovery.Calls == 2);
            discovery.Pending!.SetResult(new(DateTimeOffset.UtcNow, [], "Fixture"));
            PumpUntil(() => Find<Button>(window, "RefreshSdkButton").IsEnabled);
            Require(Find<DataGrid>(window, "SdkObservations").Items.Count == 0 &&
                Find<TextBlock>(window, "SdkSummaryTitle").Text == "Configured SDK: Not found",
                "Returning to SDK Setup retained outdated installation or registration evidence.");
            Require(Find<Border>(window, "SdkRecommendation").Visibility == Visibility.Collapsed &&
                Find<TextBlock>(window, "SdkRecommendationText").Text.Length == 0,
                "A stale SDK recommendation survived refreshed evidence.");

            Click(window, "RefreshSdkButton");
            PumpUntil(() => discovery.Calls == 3);
            discovery.Pending!.SetException(new UnauthorizedAccessException("Fixture failure"));
            PumpUntil(() => Find<Button>(window, "RefreshSdkButton").IsEnabled);
            Require(Find<TextBlock>(window, "SdkSummaryText").Text.Contains("select Refresh", StringComparison.Ordinal),
                "A failed SDK scan did not offer a usable retry.");
            Click(window, "RefreshSdkButton");
            PumpUntil(() => discovery.Calls == 4);
            discovery.Pending!.SetResult(report);
            PumpUntil(() => Find<Button>(window, "RefreshSdkButton").IsEnabled);
            Require(Find<DataGrid>(window, "SdkObservations").Items.Count == 3, "SDK Refresh did not recover after failure.");

            Click(window, "RefreshSdkButton");
            PumpUntil(() => discovery.Calls == 5);
            var savedActivity = new ActivityStore(feed.Root).Read().Count;
            window.Close();
            discovery.Pending!.SetResult(report);
            PumpUntil(() => discovery.Finished == 5); PumpDispatcher();
            Require(new ActivityStore(feed.Root).Read().Count == savedActivity,
                "A late SDK result changed Activity after the window closed.");
        }
        finally { discovery.Pending?.TrySetResult(report); window.Close(); }
    }

    private sealed class HeldSdkDiscovery : ISdkDiscovery
    {
        private int calls, finished;
        public int Calls => Volatile.Read(ref calls);
        public int Finished => Volatile.Read(ref finished);
        public TaskCompletionSource<SdkReport>? Pending { get; private set; }
        public SdkReport Inspect()
        {
            var pending = new TaskCompletionSource<SdkReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending = pending; Interlocked.Increment(ref calls);
            try { return pending.Task.GetAwaiter().GetResult(); }
            finally { Interlocked.Increment(ref finished); }
        }
    }
}
