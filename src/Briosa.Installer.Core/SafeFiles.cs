namespace Briosa.Installer.Core;

public static class SafeFiles
{
    public static string Child(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new ManagementException(ManagementError.UnsafeArchive);
        return full;
    }
    public static void NoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ManagementException(ManagementError.UnsafeArchive);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
    public static IReadOnlyList<string> Files(string root)
    {
        NoLinks(root);
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ManagementException(ManagementError.UnsafeArchive);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else files.Add(entry);
                if (pending.Count + files.Count > 25000) throw new ManagementException(ManagementError.LimitExceeded);
            }
        }
        return files;
    }
    public static void DeleteTree(string allowedRoot, string path)
    {
        var relative = Path.GetRelativePath(allowedRoot, path);
        var checkedPath = Child(allowedRoot, relative);
        if (!Directory.Exists(checkedPath)) return;
        _ = Files(checkedPath);
        Directory.Delete(checkedPath, recursive: true);
    }
    public static IDisposable LockFiles(string root)
    {
        var handles = new List<FileStream>();
        try
        {
            foreach (var file in Files(root)) handles.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Delete));
            return new HandleSet(handles);
        }
        catch (IOException)
        {
            foreach (var handle in handles) handle.Dispose();
            throw new ManagementException(ManagementError.InUse);
        }
        catch { foreach (var handle in handles) handle.Dispose(); throw; }
    }
    private sealed class HandleSet(List<FileStream> handles) : IDisposable
    { public void Dispose() { foreach (var handle in handles) handle.Dispose(); } }
}
