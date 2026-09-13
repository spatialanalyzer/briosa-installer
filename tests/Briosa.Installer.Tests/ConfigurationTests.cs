using Briosa.Installer.Cli;
using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Briosa.Installer.Tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore store = new();
    private static readonly InstallerSettings ServerOnly = new("https://mirror.example.test/server/catalog.json");
    private string UserFile => Path.Combine(directory, "user.json");
    private string MachineFile => Path.Combine(directory, "machine.json");
    private ConfigurationPaths Paths => new(UserFile, MachineFile);

    public ConfigurationTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("http://public.example.test/catalog.json")]
    [InlineData("https://user:secret@example.test/catalog.json")]
    [InlineData("https://example.test/catalog.json?token=secret")]
    [InlineData("https://example.test/catalog.json#secret")]
    [InlineData("catalog.json")]
    [InlineData("C:catalog.json")]
    [InlineData(@"\\?\C:\catalog.json")]
    [InlineData(@"\\.\pipe\catalog.json")]
    [InlineData(@"C:\feeds\..\catalog.json")]
    [InlineData(@"\\server\share")]
    public void RejectsUnsafeOrAmbiguousCatalogs(string catalog) =>
        Assert.Equal(ConfigurationError.InvalidCatalog, Assert.IsType<Outcome<InstallerSettings>.Failure>(
            SettingsCodec.Validate(new(catalog))).Error.Code);

    [Theory]
    [InlineData(@"C:\feeds\catalog.json")]
    [InlineData(@"\\files.example.test\briosa\catalog.json")]
    [InlineData("https://mirror.example.test/artifactory/briosa/catalog.json")]
    public void AcceptsCatalogLocationsWithoutCheckingTheirAvailability(string catalog) =>
        Assert.IsType<Outcome<InstallerSettings>.Success>(SettingsCodec.Validate(new(catalog)));

    [Theory]
    [InlineData("{", ConfigurationError.InvalidJson)]
    [InlineData("{\"schemaVersion\":2,\"source\":{\"catalog\":\"https://example.test/a\"}}", ConfigurationError.UnsupportedSchema)]
    [InlineData("{\"schemaVersion\":1,\"source\":{\"catalog\":\"https://example.test/a\"},\"installerUpdates\":null}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"schemaVersion\":1,\"source\":{\"catalog\":\"https://example.test/a\",\"password\":\"secret\"}}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"source\":{\"catalog\":\"https://example.test/a\"}}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"schemaVersion\":1,\"source\":{\"catalog\":\"https://example.test/a\",\"catalog\":\"https://example.test/b\"}}", ConfigurationError.InvalidProperties)]
    public void RejectsMalformedSettingsWithoutLeakingValues(string json, ConfigurationError expected)
    {
        var failure = Assert.IsType<Outcome<InstallerSettings>.Failure>(SettingsCodec.Parse(json));
        Assert.Equal(expected, failure.Error.Code);
        Assert.DoesNotContain("secret", failure.Error.Message);
    }

    [Fact]
    public void PreservesIndependentUpdateCatalogAcrossSerialization()
    {
        var settings = ServerOnly with { InstallerCatalog = @"D:\approved-installer\catalog.json" };
        var decoded = Assert.IsType<Outcome<InstallerSettings>.Success>(SettingsCodec.Parse(SettingsCodec.Serialize(settings))).Value;
        Assert.Equal(settings, decoded);
        Assert.NotEqual(decoded.ServerCatalog, decoded.EffectiveInstallerCatalog);
        Assert.Equal(ServerOnly.ServerCatalog, ServerOnly.EffectiveInstallerCatalog);
        Assert.DoesNotContain("installerUpdates", SettingsCodec.Serialize(ServerOnly));
    }

    [Fact]
    public void InvalidOverrideNeverResolvesToValidServerSource()
    {
        var json = SettingsCodec.Serialize(ServerOnly with { InstallerCatalog = "" });
        Assert.IsType<Outcome<InstallerSettings>.Failure>(SettingsCodec.Parse(json));
    }

    [Fact]
    public void MissingSettingsRequireSetupAndDoNotInventAPublicEndpoint()
    {
        var snapshot = Load();
        Assert.Equal(SettingsOrigin.SetupRequired, snapshot.Origin);
        Assert.Null(snapshot.Settings);
        Assert.False(File.Exists(UserFile));
    }

    [Fact]
    public void MalformedUserSettingsDoNotFallBackToMachineDefaults()
    {
        File.WriteAllText(UserFile, "invalid");
        File.WriteAllText(MachineFile, SettingsCodec.Serialize(ServerOnly));
        Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Load(Paths));
    }

    [Fact]
    public void MissingExplicitFileDoesNotFallBack()
    {
        File.WriteAllText(UserFile, SettingsCodec.Serialize(ServerOnly));
        var result = store.Load(Paths with { ExplicitFile = Path.Combine(directory, "missing.json") });
        Assert.Equal(ConfigurationError.MissingFile, Assert.IsType<Outcome<SettingsSnapshot>.Failure>(result).Error.Code);
    }

    [Fact]
    public void WholeUserDocumentReplacesMachineDefaultsWithoutInheritingUpdaterOverride()
    {
        File.WriteAllText(MachineFile, SettingsCodec.Serialize(ServerOnly with { InstallerCatalog = "https://other.example.test/installer.json" }));
        File.WriteAllText(UserFile, SettingsCodec.Serialize(ServerOnly));
        Assert.Null(Load().Settings!.InstallerCatalog);
    }

    [Fact]
    public void SavingMachineDefaultsCreatesUserSettingsWithoutChangingMachineFile()
    {
        var original = SettingsCodec.Serialize(ServerOnly);
        File.WriteAllText(MachineFile, original);
        var loaded = Load();
        Assert.Equal(SettingsOrigin.MachineDefaults, loaded.Origin);
        var changed = ServerOnly with { InstallerCatalog = @"D:\installer\catalog.json" };
        Assert.IsType<Outcome<SettingsSnapshot>.Success>(store.Save(loaded, changed));
        Assert.Equal(original, File.ReadAllText(MachineFile));
        Assert.Equal(changed, Load().Settings);
    }

    [Fact]
    public void StaleEditorCannotOverwriteAnotherEditorsSavedSources()
    {
        Save(Load(), ServerOnly);
        var firstEditor = Load();
        var secondEditor = Load();
        var changed = ServerOnly with { InstallerCatalog = @"D:\installer\catalog.json" };
        Save(secondEditor, changed);
        Assert.Equal(ConfigurationError.SaveConflict,
            Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Save(firstEditor, ServerOnly)).Error.Code);
        Assert.Equal(changed, Load().Settings);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void DetectsExternalFileEditsEvenWhenTimestampIsPreserved()
    {
        Save(Load(), ServerOnly);
        var loaded = Load();
        var timestamp = File.GetLastWriteTimeUtc(UserFile);
        File.WriteAllText(UserFile, SettingsCodec.Serialize(ServerOnly with { InstallerCatalog = @"D:\installer\catalog.json" }));
        File.SetLastWriteTimeUtc(UserFile, timestamp);
        Assert.Equal(ConfigurationError.SaveConflict,
            Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Save(loaded, ServerOnly)).Error.Code);
    }

    [Fact]
    public void DeletedSettingsAndConcurrentCreationAreConflicts()
    {
        var empty = Load();
        Save(empty, ServerOnly);
        Assert.Equal(ConfigurationError.SaveConflict,
            Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Save(empty, ServerOnly)).Error.Code);
        var existing = Load();
        File.Delete(UserFile);
        Assert.Equal(ConfigurationError.SaveConflict,
            Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Save(existing, ServerOnly)).Error.Code);
    }

    [Fact]
    public void RejectsOversizedSettingsAndInvalidUtf8()
    {
        File.WriteAllBytes(UserFile, new byte[1024 * 1024 + 1]);
        Assert.Equal(ConfigurationError.FileTooLarge, Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Load(Paths)).Error.Code);
        File.WriteAllBytes(UserFile, [0xff, 0xff]);
        Assert.Equal(ConfigurationError.ReadFailed, Assert.IsType<Outcome<SettingsSnapshot>.Failure>(store.Load(Paths)).Error.Code);
    }

    [Fact]
    public void CliAndGuiEngineShareSettingsAndOverrideSemantics()
    {
        Save(Load(), ServerOnly with { InstallerCatalog = @"D:\installer\catalog.json" });
        Assert.Equal(0, Cli("settings", "set", "--server-catalog", "https://new.example.test/catalog.json"));
        Assert.Equal(@"D:\installer\catalog.json", Load().Settings!.InstallerCatalog);
        Assert.Equal(0, Cli("settings", "set", "--same-source"));
        Assert.Null(Load().Settings!.InstallerCatalog);
        Assert.Equal(0, Cli("settings", "validate"));
    }

    [Fact]
    public void ExplicitInitializationDoesNotClobberExistingSettings()
    {
        Assert.Equal(0, Cli("settings", "init", "--config", UserFile, "--server-catalog", ServerOnly.ServerCatalog));
        Assert.Equal(4, Cli("settings", "init", "--config", UserFile, "--server-catalog", "https://other.example.test/catalog.json"));
        Assert.Equal(ServerOnly, Load().Settings);
    }

    [Fact]
    public void InvalidCliInputDoesNotSaveOrEchoSecrets()
    {
        using var error = new StringWriter();
        var exit = CliApplication.Run(["settings", "set", "--server-catalog", "https://u:secret@example.test/a"], TextWriter.Null, error, Paths);
        Assert.Equal(2, exit);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.False(File.Exists(UserFile));
        Assert.Equal(2, Cli("settings", "set", "--same-source", "--installer-catalog", ServerOnly.ServerCatalog));
        Assert.Equal(3, Cli("settings", "show"));
    }

    private int Cli(params string[] args) => CliApplication.Run(args, TextWriter.Null, TextWriter.Null, Paths);

    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void AppearanceRoundTripsThroughSettingsAndCliWithoutChangingSources(string theme)
    {
        var original = ServerOnly with { InstallerCatalog = @"D:\installer\catalog.json" };
        Save(Load(), original);
        Assert.Equal(0, Cli("settings", "set", "--theme", theme));
        Assert.Equal(original with { Theme = theme }, Load().Settings);
        var encoded = SettingsCodec.Serialize(Load().Settings!);
        Assert.Equal(Load().Settings, Assert.IsType<Outcome<InstallerSettings>.Success>(SettingsCodec.Parse(encoded)).Value);
        Assert.Equal(0, Cli("settings", "set", "--server-catalog", "https://new.example.test/catalog.json"));
        Assert.Equal(theme, Load().Settings!.Theme);
    }

    [Theory]
    [InlineData("null", ConfigurationError.InvalidProperties)]
    [InlineData("{}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"theme\":null}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"theme\":1}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"theme\":\"dark\",\"theme\":\"light\"}", ConfigurationError.InvalidProperties)]
    [InlineData("{\"theme\":\"Dark\"}", ConfigurationError.InvalidTheme)]
    [InlineData("{\"theme\":\"unknown\"}", ConfigurationError.InvalidTheme)]
    public void RejectsInvalidAppearanceWithoutReplacingSavedConfiguration(string appearance, ConfigurationError expected)
    {
        Save(Load(), ServerOnly);
        var json = "{\"schemaVersion\":1,\"source\":{\"catalog\":\"https://example.test/catalog.json\"},\"appearance\":" + appearance + "}";
        Assert.Equal(expected, Assert.IsType<Outcome<InstallerSettings>.Failure>(SettingsCodec.Parse(json)).Error.Code);
        Assert.Equal(2, Cli("settings", "set", "--theme", "unknown"));
        Assert.Equal(ServerOnly, Load().Settings);
    }

    [Fact]
    public void ExistingConfigurationDefaultsToSystemAppearance()
    {
        var original = SettingsCodec.Serialize(ServerOnly);
        Assert.DoesNotContain("appearance", original);
        Assert.Equal("system", Assert.IsType<Outcome<InstallerSettings>.Success>(SettingsCodec.Parse(original)).Value.Theme);
    }

    [Fact]
    public async Task AppearanceCanPersistBeforeSetupWithoutEnablingSourceAccess()
    {
        var settings = new InstallerSettings(Theme: "dark");
        Save(Load(), settings);
        Assert.Equal(settings, Load().Settings);
        Assert.DoesNotContain("source", File.ReadAllText(UserFile));
        Assert.IsType<Outcome<InstallerSettings>.Failure>(SettingsCodec.Validate(settings));
        using var catalogs = new ReleaseCatalogClient();
        Assert.Equal(CatalogError.InvalidSource, Assert.IsType<CatalogResult<CatalogSnapshot>.Failure>(
            await catalogs.ReadAsync(settings, CatalogComponent.Server)).Error.Code);
        Assert.Equal(0, Cli("settings", "set", "--server-catalog", ServerOnly.ServerCatalog));
        Assert.Equal("dark", Load().Settings!.Theme);
    }

    private SettingsSnapshot Load() => Assert.IsType<Outcome<SettingsSnapshot>.Success>(store.Load(Paths)).Value;
    private SettingsSnapshot Save(SettingsSnapshot snapshot, InstallerSettings settings) =>
        Assert.IsType<Outcome<SettingsSnapshot>.Success>(store.Save(snapshot, settings)).Value;

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
