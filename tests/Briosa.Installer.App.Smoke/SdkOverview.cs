using Briosa.Installer.App;
using Briosa.Installer.Core;

internal static partial class Program
{
    private static void ExerciseSdkInstallationOverview()
    {
        const string first = @"C:\SA\one";
        const string second = @"C:\SA\two";
        static SdkObservation Product(string version, string directory) =>
            new("Installed SA product", "Fixture", version, directory, "Installer registration");
        static SdkObservation Registered(string version, string directory, SdkEvidenceState state = SdkEvidenceState.Observed) =>
            new(SdkReport.ConfiguredRegistration, "Fixture / 32-bit", version, directory + @"\SpatialAnalyzerSDK.exe", "Fixture registration", state);
        static SdkSetupPresentation Present(params SdkObservation[] observations) =>
            SdkSetupPresentation.FromReport(new(DateTimeOffset.UtcNow, observations, "Fixture; no runtime validation"));

        var sameVersion = Present(Product("2099.1.0101.1", first), Product("2099.1.0101.1", second),
            Registered("2099.1.0101.1", second.ToUpperInvariant()),
            new("Installed SDK file", "Fixture", "2099.1.0101.1", second + @"\SpatialAnalyzerSDK.exe", "File version"));
        Require(sameVersion.Installations.Count == 2 && !sameVersion.Installations[0].IsRegistered &&
            sameVersion.Installations[1].IsRegistered && sameVersion.Installations[1].Location == second,
            "Registration must match the complete SDK path, not a version or a separate SDK row.");
        Require(sameVersion.Title == "Configured SDK: 2099.1.0101.1", "Configured version was lost from the title.");
        Require(sameVersion.Installations[1].AccessibleName.Contains("SDK registered", StringComparison.Ordinal),
            "The registration marker has no accessible text.");

        var outside = Present(Product("2099.1.0101.1", first), Registered("2099.1.0101.1", first + "-copy"));
        Require(!outside.Installations[0].IsRegistered && outside.Findings.Contains("does not match", StringComparison.Ordinal),
            "An outside registration matched a directory prefix.");
        var relative = Present(Product("2099.1.0101.1", "Not recorded"), Registered("2099.1.0101.1", first));
        Require(!relative.Installations[0].IsRegistered, "An unknown installation location was marked by version.");
        var trailing = Present(Product("2099.1.0101.1", first + @"\"), Registered("2099.1.0101.1", first));
        Require(trailing.Installations[0].IsRegistered, "Trailing directory separator prevented an exact path match.");

        var none = Present(Product("2099.1.0101.1", first));
        Require(none.Title == "Configured SDK: Not found" && !none.Installations[0].IsRegistered,
            "An installed release was promoted to registered without registration evidence.");
        var incomplete = Present(new SdkObservation("Discovery incomplete", "Fixture", "Unknown", "Not disclosed", "Unreadable registry"));
        Require(incomplete.Title == "Configured SDK: Unable to determine" &&
            incomplete.Findings.Contains("could not be inspected", StringComparison.Ordinal), "Incomplete inspection was presented as an absent SDK.");

        var missing = Present(Product("2099.1.0101.1", first), Registered("Unknown", first, SdkEvidenceState.MissingFile));
        Require(missing.Installations[0].IsRegistered && missing.Title == "Configured SDK: Needs review" &&
            missing.Findings.Contains("executable is missing", StringComparison.Ordinal),
            "A registry reference to a missing file must stay visible without implying a healthy SDK.");

        var multiple = Present(Product("2099.1.0101.1", first), Product("2099.2.0202.2", second),
            Registered("2099.1.0101.1", first),
            Registered("2099.2.0202.2", second) with { Context = "Fixture / 64-bit" });
        Require(multiple.Title == "Configured SDK: Multiple versions" &&
            multiple.Installations.All(i => i.IsRegistered) && multiple.Findings.Contains("different SDK versions", StringComparison.Ordinal),
            "Conflicting registry views were reduced to a single configured version.");

        var shadowed = Present(Product("2099.1.0101.1", first), Product("2099.2.0202.2", second),
            Registered("2099.1.0101.1", first),
            Registered("2099.2.0202.2", second) with { Kind = "Other SDK registration", State = SdkEvidenceState.OtherRegistration });
        Require(shadowed.Installations[0].IsRegistered && !shadowed.Installations[1].IsRegistered,
            "An underlying registration was promoted to configured.");
        var service = Present(Product("2099.1.0101.1", first), Registered("Unknown", first, SdkEvidenceState.ServiceRegistration));
        Require(!service.Installations[0].IsRegistered && service.Title == "Configured SDK: Needs review",
            "A service registration was matched to a non-authoritative local executable.");
        var unquoted = Present(Product("2099.1.0101.1", first), Registered("2099.1.0101.1", first, SdkEvidenceState.UnquotedPath));
        Require(unquoted.Installations[0].IsRegistered && unquoted.Findings.Contains("unquoted", StringComparison.Ordinal),
            "Unquoted path caveat was lost during presentation.");

        static SdkObservation SdkFile(string version, string directory) =>
            new("Installed SDK file", "Fixture", version, directory + @"\SpatialAnalyzerSDK.exe", "File version");
        foreach (var (current, newest, expected) in new[]
        {
            ("2099.9.1001.1", "2099.10.1001.1", true), // Numeric, not lexical order.
            ("2099.10.1001.9", "2099.10.1001.10", true), // Include the fourth component.
            ("2099.10.0101.10", "2099.10.101.10", false), // Padding does not change identity.
            ("2099.10.1001.10", "2099.10.1001.10", false),
            ("2100.1.1001.1", "2099.10.1001.10", false), // Never recommend a downgrade.
            ("Unknown", "2099.10.1001.10", false),
        })
        {
            var recommendation = Present(Product(newest, second), SdkFile(newest, second), Registered(current, first));
            Require((recommendation.Recommendation.Length > 0) == expected,
                $"SDK recommendation incorrectly compared {current} with {newest}.");
            if (expected) Require(recommendation.Recommendation.Contains(newest, StringComparison.Ordinal) &&
                recommendation.Recommendation.Contains("strongly recommend", StringComparison.Ordinal) &&
                recommendation.Recommendation.Contains("Keeping an older SDK is valid", StringComparison.Ordinal),
                "The advisory must identify the local version and preserve intentional older-SDK use.");
        }
        SdkObservation[] available = [Product("2099.1.0101.1", first), Product("2099.2.0202.2", second),
            SdkFile("2099.2.0202.2", second), Registered("2099.1.0101.1", first, SdkEvidenceState.UnquotedPath)];
        Require(Present(available).Recommendation.Length > 0, "An unquoted older SDK lost its advisory.");
        foreach (var extra in new[]
        {
            new SdkObservation("Discovery incomplete", "Fixture", "Unknown", "Not disclosed", "Unreadable registry"),
            Product("Unknown", @"C:\SA\unknown"),
            Registered("2099.2.0202.2", second) with { Context = "Fixture / 64-bit" },
        }) Require(Present([.. available, extra]).Recommendation.Length == 0,
            "Ambiguous evidence was promoted to a latest-SDK recommendation.");
        Require(Present(available.Where(o => o.Kind != "Installed SDK file").ToArray()).Recommendation.Length == 0,
            "The advisory recommended an SDK without bundled file evidence.");
        Require(Present(available.Select(o => o.Kind == SdkReport.ConfiguredRegistration ?
            o with { State = SdkEvidenceState.MissingFile } : o).ToArray()).Recommendation.Length == 0,
            "Missing registration was presented as an ordinary older-SDK choice.");
    }
}
