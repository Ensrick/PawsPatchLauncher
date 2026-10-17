using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PawsPatchLauncher;

/// <summary>
/// <c>--headless &lt;command&gt;</c>: the launcher's own window and code paths, run off-screen and hidden,
/// with results written to the calling console or redirected output. See docs/headless.md.
/// </summary>
public static class HeadlessMode
{
    public const int Success = 0, Failure = 1, Usage = 2, Busy = 3;
    public const string Switch = "--headless";

    public static bool Active { get; } = Environment.GetCommandLineArgs().Contains(Switch, StringComparer.Ordinal);

    /// <summary>The command words after <c>--headless</c>, without the launcher's own switches.</summary>
    public static string[] Arguments(IEnumerable<string> args)
    {
        var list = args.ToList();
        var at = list.IndexOf(Switch);
        return at < 0 ? [] : list.Skip(at + 1).Where(a => !IsLauncherSwitch(a)).ToArray();
    }

    public static bool IsHelp(string word) => word is "help" or "--help" or "-h" or "-?" or "/?";

    static bool IsLauncherSwitch(string value) => value.StartsWith("--test-profile=", StringComparison.Ordinal)
        || value is "--smoke-test" or "--skip-startup-update";

    /// <summary>
    /// Redirected output (a pipe or file) is written as UTF-8. Otherwise the process attaches to the calling
    /// console and writes through the console's Unicode API, so Cyrillic text survives any console code page.
    /// </summary>
    public static TextWriter OpenOutput()
    {
        var handle = GetStdHandle(StdOutput);
        if (handle != IntPtr.Zero && handle != new IntPtr(-1) && GetFileType(handle) != FileTypeChar)
            return new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        if (!AttachConsole(AttachParentProcess)) return TextWriter.Null;
        var console = CreateFileW("CONOUT$", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (console.IsInvalid) return TextWriter.Null;
        var writer = new ConsoleWriter(console);
        // The calling shell has already printed its prompt; start on a fresh line.
        writer.WriteLine();
        return writer;
    }

    sealed class ConsoleWriter(SafeFileHandle handle) : TextWriter
    {
        public override Encoding Encoding => Encoding.Unicode;
        public override void Write(char value) => Write(value.ToString());
        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            WriteConsoleW(handle, value, (uint)value.Length, out _, IntPtr.Zero);
        }
        protected override void Dispose(bool disposing) { if (disposing) handle.Dispose(); base.Dispose(disposing); }
    }

    const int StdOutput = -11, AttachParentProcess = -1;
    const uint FileTypeChar = 2, GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WriteConsoleW(SafeFileHandle console, string text, uint length, out uint written, IntPtr reserved);
}
