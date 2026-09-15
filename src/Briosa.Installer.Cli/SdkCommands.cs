using System.Text.Json;
using Briosa.Installer.Core;

namespace Briosa.Installer.Cli;

public static class SdkCommands
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, ISdkRegistrationService? service = null)
    {
        try
        {
            if (args.Length < 3 || args[0] is not ("plan" or "use")) throw new ArgumentException();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (key is not ("--installation" or "--review-sha256" or "--yes") || options.ContainsKey(key)) throw new ArgumentException();
                if (key == "--yes") options.Add(key, "true");
                else { if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException(); options.Add(key, args[i]); }
            }
            if (!options.TryGetValue("--installation", out var directory) ||
                (args[0] == "use" ? !options.ContainsKey("--yes") || !options.ContainsKey("--review-sha256") : options.Count != 1))
                throw new ArgumentException();
            service ??= new SdkRegistrationService();
            var plan = await service.PrepareAsync(directory);
            var fingerprint = SdkRegistrationService.ReviewFingerprint(plan);
            if (args[0] == "plan") { output.WriteLine(JsonSerializer.Serialize(new { plan, reviewSha256 = fingerprint }, InstallerJson.Options)); return 0; }
            if (!string.Equals(fingerprint, options["--review-sha256"], StringComparison.OrdinalIgnoreCase))
                throw new SdkRegistrationException(SdkRegistrationError.ConcurrentChange);
            var result = await service.ApplyAsync(plan);
            output.WriteLine(JsonSerializer.Serialize(result, InstallerJson.Options));
            return result.Code == SdkRegistrationResultCode.Succeeded ? 0 : result.Code == SdkRegistrationResultCode.Cancelled ? 130 : 7;
        }
        catch (SdkRegistrationException e) { error.WriteLine(e.Message); return 6; }
        catch (ArgumentException) { error.WriteLine("Use sdk plan --installation <SA-directory>, then sdk use --installation <SA-directory> --review-sha256 <reviewed-plan-hash> --yes."); return 2; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ManagementException)
        { error.WriteLine("SDK maintenance could not access the required local files."); return 6; }
    }
}
