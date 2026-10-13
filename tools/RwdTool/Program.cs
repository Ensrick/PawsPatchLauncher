using PawsPatchLauncher.Rwd;

if (args.Length == 0)
{
    Usage();
    return 2;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "list" when args.Length == 2:
            List(args[1]);
            return 0;
        case "verify" when args.Length == 2:
            return Verify(args[1]) ? 0 : 1;
        case "extract" when args.Length is 3 or 4 && (args.Length == 3 || args[3] == "--overwrite"):
            Extract(args[1], args[2], args.Length == 4);
            return 0;
        case "pack" when args.Length == 3:
            Pack(args[1], args[2], null);
            return 0;
        case "pack" when args.Length == 5 && args[3] == "--stamp":
            Pack(args[1], args[2], Convert.ToUInt32(args[4], 16));
            return 0;
        default:
            Usage();
            return 2;
    }
}
catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void Usage()
{
    Console.Error.WriteLine("""
        RwdTool: Kohan II .rwd archives, without the game.
          RwdTool list <archive.rwd>                     stamp, then index, offset, size and name of every file
          RwdTool verify <archive.rwd>                   checks the structure and reads every file
          RwdTool extract <archive.rwd> <folder> [--overwrite]
                                                         writes every file under <folder>; existing files are
                                                         kept unless --overwrite
          RwdTool pack <folder> <archive.rwd> [--stamp <hex>]
                                                         every file under <folder>, in a fixed order; the stamp
                                                         is derived from the content unless given
        """);
}

static void List(string path)
{
    using var archive = RwdArchive.Open(path);
    Console.WriteLine($"stamp {archive.Stamp:x8}  version {archive.Version}  game {archive.GameId}  files {archive.Entries.Count}");
    foreach (var e in archive.Entries.OrderBy(e => e.LoadIndex).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"{e.LoadIndex,7} {e.Offset,12} {e.Size,10}  {e.Name}");
}

static bool Verify(string path)
{
    using var archive = RwdArchive.Open(path);
    long bytes = 0;
    foreach (var e in archive.Entries) { archive.CopyTo(e, Stream.Null); bytes += e.Size; }
    var indices = archive.Entries.Select(e => e.LoadIndex).ToHashSet();
    var sequential = indices.Count == archive.Entries.Count && indices.All(i => i >= 0 && i < archive.Entries.Count);
    Console.WriteLine($"ok: {archive.Entries.Count} files, {bytes} bytes, stamp {archive.Stamp:x8}" +
                      (sequential ? "" : "; load indices are not 0..n-1 (reported, not an error)"));
    return true;
}

static void Extract(string path, string folder, bool overwrite)
{
    var (written, kept) = RwdOperations.Extract(path, folder, overwrite);
    Console.WriteLine($"{written} files written to {Path.GetFullPath(folder)}" + (kept > 0 ? $"; {kept} existing files kept (--overwrite replaces them)" : ""));
}

static void Pack(string folder, string path, uint? stamp)
{
    var (count, value) = RwdOperations.Pack(folder, path, stamp);
    Console.WriteLine($"{count} files packed into {path}, stamp {value:x8}");
}
