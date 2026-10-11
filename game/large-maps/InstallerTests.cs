using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class ReleaseStartup { internal const string Build="installer-test"; }
// Exercise the production installer and memory interface, including partial
// writes. This executable never opens a process or changes game files.
internal sealed class RegistryTestMemory : IMemory {
    internal const uint Image=0x620000, Cave=0x16000000;
    internal byte[] ImageBytes=new byte[0x700000], CaveBytes;
    internal int Operations,FailAt=-1,PartialHook=-1,HookWrites,Frees;
    internal bool PermanentHookFailure;
    internal RegistryTestMemory(){var b=ExtendedRegistryPayload.Originals(Image);for(int i=0;i<b.Length;i++)Buffer.BlockCopy(b[i],0,ImageBytes,(int)ExtendedRegistryPayload.Sites[i],b[i].Length);}
    void Op(){if(++Operations==FailAt)throw new IOException("injected failure");}
    byte[] Region(uint a,out int o){if(a>=Cave){o=(int)(a-Cave);return CaveBytes;}o=(int)(a-Image);return ImageBytes;}
    internal void RawWrite(uint a,byte[] b){int o;var dst=Region(a,out o);Buffer.BlockCopy(b,0,dst,o,b.Length);}
    public byte[] Read(uint a,int n){Op();int o;var src=Region(a,out o);var b=new byte[n];Buffer.BlockCopy(src,o,b,0,n);return b;}
    public void Write(uint a,byte[] b){Op();RawWrite(a,b);}
    public void WriteCode(uint a,byte[] b){Op();if(HookWrites++==PartialHook||PermanentHookFailure){RawWrite(a,b.Take(2).ToArray());throw new IOException("partial hook");}RawWrite(a,b);}
    public uint Allocate(int n){Op();CaveBytes=new byte[n];return Cave;}
    public void MakeExecutable(uint a,int n){Op();}
    public void Flush(uint a,int n){Op();}
    public void Free(uint a){Frees++;CaveBytes=null;}
    internal bool Original(){var b=ExtendedRegistryPayload.Originals(Image);for(int i=0;i<b.Length;i++)if(!ImageBytes.Skip((int)ExtendedRegistryPayload.Sites[i]).Take(b[i].Length).SequenceEqual(b[i]))return false;return true;}
}
internal static class InstallerTests {
    static int checks;
    static void Check(bool ok){if(!ok)throw new Exception("check "+checks);checks++;}
    static void Install(RegistryTestMemory m){LargeMapsPatch.Install(m,RegistryTestMemory.Image,delegate(string s){});}
    static void Rejected(RegistryTestMemory m){bool failed=false;try{Install(m);}catch(IOException){failed=true;}catch(InvalidOperationException){failed=true;}Check(failed);}
    public static int Main(){
        foreach(string frequency in new[]{"standard","x2","x4","unknown",null})
            foreach(uint language in new uint[]{0,1,2,3,4,5,99}){
                LargeMapsPatch.ConfigureWarning(frequency,language);
                var configured=new RegistryTestMemory();Install(configured);
                uint expected=(frequency=="x2"?2u:frequency=="x4"?4u:1u)|((language<6?language:0)<<8);
                Check(BitConverter.ToUInt32(configured.CaveBytes,ExtendedRegistryPayload.DataOffset+28)==expected);
            }
        LargeMapsPatch.ConfigureWarning("standard",0);
        var good=new RegistryTestMemory();Install(good);Check(!good.Original()&&good.CaveBytes!=null);int total=good.Operations;
        for(int n=1;n<=total;n++){var m=new RegistryTestMemory();m.FailAt=n;Rejected(m);Check(m.Original());Check(m.CaveBytes==null);}
        for(int n=0;n<ExtendedRegistryPayload.Sites.Length;n++){var m=new RegistryTestMemory();m.PartialHook=n;Rejected(m);Check(m.Original()&&m.Frees==1);}
        var bad=new RegistryTestMemory();bad.ImageBytes[ExtendedRegistryPayload.Sites[0]]^=1;Rejected(bad);Check(bad.CaveBytes==null&&bad.HookWrites==0);
        var active=new RegistryTestMemory();active.RawWrite(RegistryTestMemory.Image+0x5ef72c,BitConverter.GetBytes(0x1234));Rejected(active);Check(active.CaveBytes==null&&active.HookWrites==0);
        var uncertain=new RegistryTestMemory();uncertain.PermanentHookFailure=true;Rejected(uncertain);Check(uncertain.CaveBytes!=null&&uncertain.Frees==0);
        Console.WriteLine("EXTENDED_REGISTRY_INSTALLER_PASS "+checks+" checks; all operation failures, partial writes, guards, unsafe rollback retention");return 0;
    }
}
