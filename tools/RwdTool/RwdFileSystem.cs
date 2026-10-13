using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PawsPatchLauncher.Rwd;

// Reject links at every path component. On Windows, keep ancestor directories
// open without delete sharing until the operation completes, so they cannot be
// renamed/replaced between validation and use. Unix callers must own the tree
// exclusively; pre-existing symlinks are still rejected there.
internal sealed class RwdFileSystem : IDisposable
{
    readonly List<SafeFileHandle> directories = new();

    public static void CheckLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links/reparse points are not allowed: " + path);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    internal static RwdFileSystem LockDirectory(string path, bool create = false)
    {
        var guard = new RwdFileSystem();
        try
        {
            var full = Path.GetFullPath(path);
            var current = Path.GetPathRoot(full)!;
            guard.LockOne(current);
            foreach (var part in full[current.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                CheckLink(current);
                if (!Directory.Exists(current) && create) Directory.CreateDirectory(current);
                guard.LockOne(current);
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }

    void LockOne(string path)
    {
        CheckLink(path);
        if (!OperatingSystem.IsWindows())
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            return;
        }
        var handle = CreateFile(path, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        try
        {
            ValidateHandle(handle, path, true);
            directories.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    public static FileStream OpenRead(string path)
    {
        var full = Path.GetFullPath(path);
        using var guard = LockDirectory(Path.GetDirectoryName(full)!);
        CheckLink(full);
        if (!OperatingSystem.IsWindows()) return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        // OPEN_REPARSE_POINT means validation inspects the link itself, never its target.
        var handle = CreateFile(full, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        try { ValidateHandle(handle, full, false); return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    public static bool AtomicWrite(string path, bool overwrite, Action<FileStream> write)
    {
        var full = Path.GetFullPath(path);
        using var guard = LockDirectory(Path.GetDirectoryName(full)!, create: true);
        CheckLink(full);
        if (File.Exists(full) && !overwrite) return false;
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool owned = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true; write(output); output.Flush(true);
            }
            CheckLink(full);
            // Replacing the directory entry does not write through an existing hard link.
            File.Move(temporary, full, overwrite);
            owned = false;
            return true;
        }
        finally { if (owned) File.Delete(temporary); }
    }

    static void ValidateHandle(SafeFileHandle handle, string path, bool directory)
    {
        if (handle.IsInvalid) throw new IOException("Cannot open " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandleEx(handle, 9, out var info, 8))
            throw new IOException("Cannot validate " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory)
            throw new InvalidDataException("Unexpected file type or link: " + path);
    }

    public void Dispose() { for (var i = directories.Count - 1; i >= 0; i--) directories[i].Dispose(); directories.Clear(); }
    [StructLayout(LayoutKind.Sequential)] struct AttributeTag { public uint Attributes, Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTag info, uint size);
}
