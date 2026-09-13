using System.Diagnostics;
using System.Runtime.InteropServices;
using Briosa.Installer.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string? config = null, storeRoot = null;
            var checkOnly = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var key = args[index];
                if (!seen.Add(key)) throw new ManagementException(ManagementError.InvalidInput);
                if (key == "--check") { checkOnly = true; continue; }
                if (key is not ("--config" or "--store") || ++index == args.Length) throw new ManagementException(ManagementError.InvalidInput);
                if (key == "--config") config = Path.GetFullPath(args[index]); else storeRoot = Path.GetFullPath(args[index]);
            }
            var store = new PackageStore(storeRoot);
            var target = await store.ResolveActiveInstallerAsync() ?? Path.Combine(AppContext.BaseDirectory, "Briosa.Installer.exe");
            SafeFiles.NoLinks(target);
            if (!File.Exists(target)) throw new ManagementException(ManagementError.PackageNotFound);
            if (checkOnly) return 0;
            var process = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
            if (config is not null) { process.ArgumentList.Add("--config"); process.ArgumentList.Add(config); }
            process.ArgumentList.Add("--store"); process.ArgumentList.Add(store.Root);
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!, "receipt.json")))
            {
                process.ArgumentList.Add("--bootstrap"); process.ArgumentList.Add(Environment.ProcessPath!);
            }
            Process.Start(process)?.Dispose();
            return 0;
        }
        catch (Exception e) when (e is ManagementException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            if (!args.Contains("--check")) MessageBox(IntPtr.Zero,
                e is ManagementException managed ? managed.Message : "The selected installer could not be started. Check package integrity and file access.", "Briosa Installer", 0x10);
            return 6;
        }
    }
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
