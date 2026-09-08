using System.Security.Cryptography;
using System.Text;

namespace Briosa.Installer.Core;

public sealed record ConfigurationPaths(string UserFile, string? MachineFile = null, string? ExplicitFile = null)
{
    public static ConfigurationPaths ForCurrentUser(string? explicitFile = null) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Briosa", "Installer", "settings.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Briosa", "Installer", "settings.json"),
        explicitFile is null ? null : Path.GetFullPath(explicitFile));
}

public enum SettingsOrigin { SetupRequired, ExplicitFile, UserFile, MachineDefaults }

public sealed record SettingsSnapshot(InstallerSettings? Settings, SettingsOrigin Origin, string SavePath, string? Revision);

public sealed class SettingsStore
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly UTF8Encoding Encoding = new(false, true);

    public Outcome<SettingsSnapshot> Load(ConfigurationPaths paths)
    {
        if (paths.ExplicitFile is not null)
            return LoadFile(paths.ExplicitFile, SettingsOrigin.ExplicitFile, paths.ExplicitFile, required: true);
        var user = LoadFile(paths.UserFile, SettingsOrigin.UserFile, paths.UserFile, required: false);
        if (user is not Outcome<SettingsSnapshot>.Success { Value.Origin: SettingsOrigin.SetupRequired }) return user;
        if (paths.MachineFile is null) return user;
        return LoadFile(paths.MachineFile, SettingsOrigin.MachineDefaults, paths.UserFile, required: false);
    }

    public Outcome<SettingsSnapshot> Save(SettingsSnapshot snapshot, InstallerSettings settings)
    {
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure invalid)
            return new Outcome<SettingsSnapshot>.Failure(invalid.Error);
        string? temporaryPath = null;
        try
        {
            var destination = Path.GetFullPath(snapshot.SavePath);
            var parent = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(parent);
            // GUI and CLI saves serialize through the same sidecar lock. External
            // editors are detected by content revision, not by this cooperative lock.
            using var saveLock = new FileStream(destination + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var current = ReadBytes(destination);
            if (current is Outcome<byte[]?>.Failure readFailure)
                return new Outcome<SettingsSnapshot>.Failure(readFailure.Error);
            var bytes = ((Outcome<byte[]?>.Success)current).Value;
            if (Revision(bytes) != snapshot.Revision)
                return Outcome<SettingsSnapshot>.Fail(ConfigurationError.SaveConflict);
            var output = Encoding.GetBytes(SettingsCodec.Serialize(settings));
            if (output.Length > MaximumBytes) return Outcome<SettingsSnapshot>.Fail(ConfigurationError.FileTooLarge);
            temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(output);
                stream.Flush(flushToDisk: true);
            }
            // Recheck immediately before replacement; never claim an OS-wide CAS
            // guarantee against unrelated programs that atomically replace files.
            current = ReadBytes(destination);
            if (current is Outcome<byte[]?>.Failure failure)
                return new Outcome<SettingsSnapshot>.Failure(failure.Error);
            if (Revision(((Outcome<byte[]?>.Success)current).Value) != snapshot.Revision)
                return Outcome<SettingsSnapshot>.Fail(ConfigurationError.SaveConflict);
            if (snapshot.Revision is null) File.Move(temporaryPath, destination, overwrite: false);
            else File.Replace(temporaryPath, destination, destinationBackupFileName: null);
            temporaryPath = null;
            return new Outcome<SettingsSnapshot>.Success(new(settings,
                snapshot.Origin == SettingsOrigin.ExplicitFile ? SettingsOrigin.ExplicitFile : SettingsOrigin.UserFile,
                destination, Revision(output)));
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            return Outcome<SettingsSnapshot>.Fail(ConfigurationError.WriteFailed);
        }
        finally
        {
            if (temporaryPath is not null)
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (IsFileError(exception)) { /* Preserve the primary failure. */ }
        }
    }

    private static Outcome<SettingsSnapshot> LoadFile(string path, SettingsOrigin origin, string savePath, bool required)
    {
        var read = ReadBytes(path);
        if (read is Outcome<byte[]?>.Failure failure) return new Outcome<SettingsSnapshot>.Failure(failure.Error);
        var bytes = ((Outcome<byte[]?>.Success)read).Value;
        if (bytes is null)
            return required ? Outcome<SettingsSnapshot>.Fail(ConfigurationError.MissingFile) :
                new Outcome<SettingsSnapshot>.Success(new(null, SettingsOrigin.SetupRequired, savePath, null));
        try
        {
            var text = Encoding.GetString(bytes).TrimStart('\uFEFF');
            return SettingsCodec.Parse(text) switch
            {
                Outcome<InstallerSettings>.Success success => new Outcome<SettingsSnapshot>.Success(
                    new(success.Value, origin, savePath, origin == SettingsOrigin.MachineDefaults ? null : Revision(bytes))),
                Outcome<InstallerSettings>.Failure invalid => new Outcome<SettingsSnapshot>.Failure(invalid.Error),
                _ => throw new InvalidOperationException(),
            };
        }
        catch (DecoderFallbackException)
        {
            return Outcome<SettingsSnapshot>.Fail(ConfigurationError.ReadFailed);
        }
    }

    private static Outcome<byte[]?> ReadBytes(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumBytes) return Outcome<byte[]?>.Fail(ConfigurationError.FileTooLarge);
            using var buffer = new MemoryStream();
            var block = new byte[4096];
            int count;
            while ((count = stream.Read(block)) > 0)
            {
                if (buffer.Length + count > MaximumBytes) return Outcome<byte[]?>.Fail(ConfigurationError.FileTooLarge);
                buffer.Write(block, 0, count);
            }
            return new Outcome<byte[]?>.Success(buffer.ToArray());
        }
        catch (FileNotFoundException) { return new Outcome<byte[]?>.Success(null); }
        catch (DirectoryNotFoundException) { return new Outcome<byte[]?>.Success(null); }
        catch (Exception exception) when (IsFileError(exception)) { return Outcome<byte[]?>.Fail(ConfigurationError.ReadFailed); }
    }

    private static string? Revision(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsFileError(Exception exception) => exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
