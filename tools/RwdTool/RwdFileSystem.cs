using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PawsPatchLauncher.Rwd;

// A caller-selected Windows root may resolve through junctions. Pin its actual
// directory and ancestors, then use only that physical path. Below this boundary
// links remain forbidden. Unix callers must exclusively own the tree during use.
internal sealed class RwdFileSystem : IDisposable
{
    readonly List<SafeFileHandle> directories = new();
    public string DirectoryPath { get; private set; } = "";

    internal static RwdFileSystem LockRoot(string path, bool create = false)
    {
        if (!OperatingSystem.IsWindows()) return LockDirectory(path, create);
        var candidate = Path.GetFullPath(path);
        var missing = new Stack<string>();
        SafeFileHandle selected;
        while (true)
        {
            // Follow the selected path once. No archive-controlled entry is used
            // here. Keep this handle while resolving and locking the physical path.
            selected = CreateFile(candidate, 0x81, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (!selected.IsInvalid) break;
            var error = Marshal.GetLastWin32Error(); selected.Dispose();
            if (!create || error is not (2 or 3)) throw new IOException("Cannot open " + candidate, new Win32Exception(error));
            var parent = Path.GetDirectoryName(candidate);
            if (parent is null) throw new DirectoryNotFoundException(candidate);
            missing.Push(Path.GetFileName(candidate)); candidate = parent;
        }
        using (selected)
        {
            ValidateHandle(selected, candidate, true);
            var physical = FinalPath(selected);
            var guard = LockDirectory(physical);
            try
            {
                // Fail if an ancestor moved between resolving its path and locking
                // it. Merely resolving a string is not protection against swaps.
                if (Identity(selected) != Identity(guard.directories[^1]))
                    throw new IOException("Selected directory changed while opening: " + path);
                while (missing.TryPop(out var part))
                {
                    physical = Path.Combine(physical, part);
                    CheckLink(physical);
                    Directory.CreateDirectory(physical);
                    guard.LockOne(physical);
                }
                guard.DirectoryPath = physical;
                return guard;
            }
            catch { guard.Dispose(); throw; }
        }
    }

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
            guard.DirectoryPath = full;
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
        // FILE_LIST_DIRECTORY is necessary: metadata-only access does not
        // participate in Windows delete-sharing checks and cannot pin a path.
        // Write sharing is needed by native atomic child-file replacement.
        var handle = CreateFile(path, 0x81, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        try
        {
            ValidateHandle(handle, path, true);
            directories.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    public static FileStream OpenRead(string path, bool allowLinkedParents = false)
    {
        var full = Path.GetFullPath(path);
        // Explicit archive reads need only one stable file handle. Tree packing
        // uses the strict mode so a nested directory cannot become a link later.
        using var guard = allowLinkedParents ? null : LockDirectory(Path.GetDirectoryName(full)!);
        CheckLink(full);
        if (!OperatingSystem.IsWindows()) return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        // OPEN_REPARSE_POINT means validation inspects the link itself, never its target.
        var handle = CreateFile(full, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        try { ValidateHandle(handle, full, false); return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    internal static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(512);
        var size = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (size >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)size + 1));
            size = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (size == 0 || size >= buffer.Capacity) throw new IOException("Cannot resolve open file path.", new Win32Exception(Marshal.GetLastWin32Error()));
        return buffer.ToString();
    }

    static (uint Volume, uint High, uint Low) Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot identify selected directory.", new Win32Exception(Marshal.GetLastWin32Error()));
        return (info.Volume, info.IndexHigh, info.IndexLow);
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
        var tagRead = GetFileInformationByHandleEx(handle, 9, out var info, 8);
        var tagError = tagRead ? 0 : Marshal.GetLastWin32Error();
        var attributes = ResolveAttributes(tagRead, info.Attributes, tagError,
            () => GetFileInformationByHandle(handle, out var basic) ? basic.Attributes : null, path);
        CheckAttributes(attributes, directory, path);
    }

    // FAT and exFAT volumes reject the attribute-tag class with ERROR_INVALID_PARAMETER (87); a game library on such a
    // drive must still validate. The basic handle information carries the same attribute bits, including the reparse
    // point flag, so links stay rejected on every volume.
    internal static uint ResolveAttributes(bool tagRead, uint tagAttributes, int tagError, Func<uint?> basicAttributes, string path)
    {
        if (tagRead) return tagAttributes;
        if (tagError == 87 && basicAttributes() is uint attributes) return attributes;
        throw new IOException("Cannot validate " + path, new Win32Exception(tagError));
    }

    internal static void CheckAttributes(uint attributes, bool directory, string path)
    {
        if ((attributes & 0x400) != 0 || ((attributes & 0x10) != 0) != directory)
            throw new InvalidDataException("Unexpected file type or link: " + path);
    }

    public void Dispose() { for (var i = directories.Count - 1; i >= 0; i--) directories[i].Dispose(); directories.Clear(); }
    [StructLayout(LayoutKind.Sequential)] struct AttributeTag { public uint Attributes, Tag; }
    [StructLayout(LayoutKind.Sequential)] struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTag info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
