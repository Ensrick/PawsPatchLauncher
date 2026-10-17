using PawsPatchLauncher;
using System.Security.Cryptography;

internal static class CompatibilityModerationTests
{
    internal static async Task<int> RunAsync(string root)
    {
        int n=0;void Check(bool ok,string why){n++;if(!ok)throw new Exception("Compatibility: "+why);}
        var now=new DateTimeOffset(2026,9,8,12,30,1,TimeSpan.Zero);
        var path=Path.Combine(root,"compatibility-fixture.exe");
        var bytes=new byte[]{1,3,5,7};
        await File.WriteAllBytesAsync(path,bytes);
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        Check(await GameCompatibility.CheckAsync(path,new[]{hash})==GameCompatibilityState.Supported,"matching hash");
        Check(await GameCompatibility.CheckAsync(path,new[]{hash.ToLowerInvariant()})==GameCompatibilityState.Supported,"case insensitive hash");
        Check(await GameCompatibility.CheckAsync(path,new[]{new string('0',64),hash})==GameCompatibilityState.Supported,"multiple supported binaries");
        var before=GameFileStamp.Read(path);await File.WriteAllBytesAsync(path,new byte[]{1,2,3,4,5});
        Check(before!=GameFileStamp.Read(path),"replacement invalidates cache");
        Check(await GameCompatibility.CheckAsync(path,new[]{hash})==GameCompatibilityState.Unsupported,"updated binary rejected");
        Check(await GameCompatibility.CheckAsync(path+".missing",new[]{hash})==GameCompatibilityState.Unavailable,"missing not mislabeled unsupported");
        using(var locked=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Check(await GameCompatibility.CheckAsync(path,new[]{hash})==GameCompatibilityState.Unavailable,"locked not mislabeled unsupported");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool cancelled=false;
            try{await GameCompatibility.CheckAsync(path,new[]{hash},cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cancelled read");
        }
        await File.WriteAllBytesAsync(path,bytes);
        Check(await GameCompatibility.CheckAsync(path,new[]{hash})==GameCompatibilityState.Supported,"restored game accepted");
        Console.WriteLine($"COMPATIBILITY/MODERATION PASS {n}: synthetic files only");
        return n;
    }
}
