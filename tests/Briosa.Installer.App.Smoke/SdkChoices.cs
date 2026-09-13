using System.Windows.Controls;
using Briosa.Installer.App;
using Briosa.Installer.Core;

internal static partial class Program
{
    private static void ExerciseSdkRegistrationChoices()
    {
        static SdkObservation[] Installation(string version, string path) =>
        [
            new("Installed SA product", "Fixture", version, path, "Installer registration"),
            new("Installed SDK file", "Fixture", version, path + @"\SpatialAnalyzerSDK.exe", "File version"),
        ];
        static SdkReport Report(params SdkObservation[] observations) =>
            new(DateTimeOffset.UtcNow, observations, "Fixture; no runtime validation");
        // Out of order, with both a two-digit month/revision and a duplicate release.
        var report = Report([.. Installation("2099.10.0101.10", @"C:\SA\latest"),
            .. Installation("2099.9.0101.9", @"C:\SA\older"),
            .. Installation("2099.10.0101.9", @"C:\SA\previous"),
            .. Installation("2099.10.101.10", @"C:\SA\duplicate")]);
        var choices = SdkSetupPresentation.RegistrationChoices(report);
        Require(choices.Count == 4 && choices[0].IsRecommended && choices[3].IsRecommended &&
            !choices[1].IsRecommended && !choices[2].IsRecommended,
            "Recommended must follow the newest local numeric release, including equivalent copies.");
        Require(choices[0].DisplayName == "2099.10.0101.10 — Recommended" &&
            choices[1].DisplayName == "2099.9.0101.9", "SDK choice labels lost the version or recommendation.");

        var dialog = new SdkChoiceDialog(report, "Configured SDK: 2099.9.0101.9");
        try
        {
            var selector = Descendants(dialog).OfType<ComboBox>().Single();
            Require(selector.SelectedIndex == -1 && dialog.SelectedInstallation is null,
                "Recommended registration must remain an explicit user choice.");
            foreach (var choice in selector.Items.OfType<SdkInstallationChoice>())
            {
                selector.SelectedItem = choice;
                Require(dialog.SelectedInstallation == choice.Installation,
                    "A recommended label changed the installation passed to registration.");
            }
        }
        finally { dialog.Close(); }

        foreach (var extra in new[]
        {
            new SdkObservation("Discovery incomplete", "Fixture", "Unknown", "Not disclosed", "Unreadable registry"),
            new SdkObservation("Installed SA product", "Fixture", "Unknown", @"C:\SA\unknown", "Unknown version"),
        }) Require(!SdkSetupPresentation.RegistrationChoices(Report([.. report.Observations, extra])).Any(c => c.IsRecommended),
            "Incomplete local installation evidence produced a Recommended label.");
        var unavailable = Report([.. Installation("2099.1.0101.1", @"C:\SA\old"),
            new("Installed SA product", "Fixture", "2099.2.0101.1", @"C:\SA\missing-sdk", "Installer registration")]);
        Require(!SdkSetupPresentation.RegistrationChoices(unavailable).Any(c => c.IsRecommended),
            "A missing latest SDK must not promote an older installed release to Recommended.");
        Require(SdkSetupPresentation.RegistrationChoices(Report()).Count == 0, "Empty setup fabricated a choice.");
    }
}
