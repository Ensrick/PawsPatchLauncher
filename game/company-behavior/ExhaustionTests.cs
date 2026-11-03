using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
internal static class ExhaustionTests
{
    sealed class Memory:IMemory
    {
        internal Dictionary<uint,byte> B=new Dictionary<uint,byte>();
        internal int Ops,Fail=-1;
        internal void Seed(uint a,byte[] b){for(int i=0;i<b.Length;i++)B[a+(uint)i]=b[i];}
        void Op(){if(++Ops==Fail)throw new IOException("injected");}
        internal Memory(uint image){Seed(image+CompanyExhaustionPatch.EntryRva,CompanyExhaustionPatch.EntryOriginal(image));Seed(image+CompanyExhaustionPatch.EntryRva+5,TerrainPatch.Hex("E80C9D160051568BF18B4E04"));Seed(image+ExhaustionRecoveryPatch.HookRva-3,ExhaustionRecoveryPatch.Guard);Seed(image+0x2147ec,ExhaustionRecoveryPatch.DecisionGuard);}
        public byte[] Read(uint a,int n){Op();return Enumerable.Range(0,n).Select(i=>B.ContainsKey(a+(uint)i)?B[a+(uint)i]:(byte)0).ToArray();}
        public void Write(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();Seed(a,b);}
        public void WriteCode(uint a,byte[] b){Write(a,b);}
        public uint Allocate(int n){throw new Exception("No allocation allowed");}
        public void MakeExecutable(uint a,int n){throw new Exception("No allocation allowed");}
        public void Flush(uint a,int n){Op();}
        public void Free(uint a){throw new Exception("No allocation allowed");}
    }
    static int checks;
    static void Check(bool b){checks++;if(!b)throw new Exception("Check "+checks);}
    public static int Main(string[] args)
    {
        if(args.Length==1){File.WriteAllBytes(args[0]+".entry",CompanyExhaustionPatch.EntryDisabled);File.WriteAllBytes(args[0]+".update",CompanyExhaustionPatch.UpdateDisabled);return 0;}
        foreach(uint image in new uint[]{0x460000,0xf20000,0x18000000})
        {
            var m=new Memory(image);CompanyExhaustionPatch.Install(m,image,delegate{});int ops=m.Ops;
            Check(m.Read(image+CompanyExhaustionPatch.EntryRva,5).SequenceEqual(CompanyExhaustionPatch.EntryDisabled));
            Check(m.Read(image+CompanyExhaustionPatch.UpdateRva,5).SequenceEqual(CompanyExhaustionPatch.UpdateDisabled));
            for(int f=1;f<=ops;f++)
            {
                m=new Memory(image){Fail=f};bool failed=false;
                try{CompanyExhaustionPatch.Install(m,image,delegate{});}catch(IOException){failed=true;}
                Check(failed);Check(m.Read(image+CompanyExhaustionPatch.EntryRva,5).SequenceEqual(CompanyExhaustionPatch.EntryOriginal(image)));
                Check(m.Read(image+CompanyExhaustionPatch.UpdateRva,5).SequenceEqual(ExhaustionRecoveryPatch.Original));
            }
            m=new Memory(image);m.Seed(image+CompanyExhaustionPatch.EntryRva+1,new byte[]{0xcc});
            bool reject=false;try{CompanyExhaustionPatch.Install(m,image,delegate{});}catch(InvalidOperationException){reject=true;}Check(reject);
        }
        foreach(bool enabled in new[]{false,true})foreach(string mod in new[]{"arcane-wars","vanilla","immortals"})foreach(bool core in new[]{false,true})foreach(bool data in new[]{false,true})foreach(string channel in new[]{"stable","beta","bad"})
        {
            var s=new Dictionary<string,object>{{"ignoreZeroMorale",enabled},{"mod",mod},{"pawPatchEnabled",core},{"dataOnly",data},{"channel",channel}};
            bool valid=!enabled || mod=="arcane-wars"&&core&&!data&&channel!="bad";
#if PAW_PURE_EXTENDED
            valid=!enabled;
#endif
            bool failed=false,result=false;try{result=PawCompanyOptions.Parse(s);}catch(InvalidDataException){failed=true;}
            Check(failed!=valid&&(!valid||result==enabled));
        }
        Check(!PawCompanyOptions.Parse(new Dictionary<string,object>()));
        bool invalid=false;try{PawCompanyOptions.Parse(new Dictionary<string,object>{{"ignoreZeroMorale","true"}});}catch(InvalidDataException){invalid=true;}Check(invalid);
        Console.WriteLine("EXHAUSTION_OPTION_TRANSACTION_PASS "+checks);return 0;
    }
}
