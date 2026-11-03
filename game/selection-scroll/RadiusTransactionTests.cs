using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class GroupFormationRadiusTests
{
    sealed class Memory : IMemory
    {
        readonly Dictionary<uint,byte> bytes = new Dictionary<uint,byte>();
        internal int Operations, FailAt = -1;
        internal bool Allocated, PersistentFailure;
        internal void Seed(uint a, byte[] b) { for(int i=0;i<b.Length;i++) bytes[a+(uint)i]=b[i]; }
        void Op() { if(++Operations==FailAt) throw new IOException("injected failure"); }
        internal Memory(uint image) { Seed(image+GroupFormationRadiusPatch.Sites[0],GroupFormationRadiusPatch.Original(image)); }
        public byte[] Read(uint a,int n) { Op(); var b=new byte[n]; for(int i=0;i<n;i++) bytes.TryGetValue(a+(uint)i,out b[i]); return b; }
        public void Write(uint a,byte[] b) { Seed(a,b.Take(2).ToArray()); Op(); Seed(a,b); }
        public void WriteCode(uint a,byte[] b) { Seed(a,b.Take(2).ToArray()); Op(); if(PersistentFailure) throw new IOException("persistent partial write"); Seed(a,b); }
        public uint Allocate(int n) { Op(); Allocated=true; return 0x60000000; }
        public void MakeExecutable(uint a,int n) { Op(); }
        public void Flush(uint a,int n) { Op(); }
        public void Free(uint a) { Allocated=false; }
    }
    static int checks;
    static void Check(bool ok) { checks++; if(!ok) throw new Exception("Check "+checks); }
    internal static int Main(string[] args)
    {
        if(args.Length==3) { File.WriteAllBytes(args[0],GroupFormationRadiusPayload.Build(Convert.ToUInt32(args[1],16),Convert.ToUInt32(args[2],16))); return 0; }
        foreach(uint image in new uint[]{0x460000,0xf20000,0x18000000})
        {
            var success=new Memory(image); uint cave=GroupFormationRadiusPatch.Install(success,image,delegate{}); int operations=success.Operations;
            Check(success.Allocated);
            Check(success.Read(image+GroupFormationRadiusPatch.Sites[0],5).SequenceEqual(GroupFormationRadiusPatch.Jump(image+GroupFormationRadiusPatch.Sites[0],cave+GroupFormationRadiusPayload.Offset)));
            Check(success.Read(cave,GroupFormationRadiusPayload.Allocation).SequenceEqual(GroupFormationRadiusPayload.Build(image,cave)));
            for(int fault=1;fault<=operations;fault++)
            {
                var m=new Memory(image){FailAt=fault}; bool threw=false;
                try { GroupFormationRadiusPatch.Install(m,image,delegate{}); } catch(IOException) { threw=true; }
                Check(threw&&!m.Allocated);
                Check(m.Read(image+GroupFormationRadiusPatch.Sites[0],5).SequenceEqual(GroupFormationRadiusPatch.Original(image)));
            }
            for(int offset=0;offset<5;offset++)
            {
                var m=new Memory(image); m.Seed(image+GroupFormationRadiusPatch.Sites[0]+(uint)offset,new byte[]{0xcc}); bool rejected=false;
                try { GroupFormationRadiusPatch.Install(m,image,delegate{}); } catch(InvalidOperationException) { rejected=true; }
                Check(rejected&&!m.Allocated);
            }
            var persistent=new Memory(image){PersistentFailure=true}; bool failed=false;
            try { GroupFormationRadiusPatch.Install(persistent,image,delegate{}); } catch(IOException) { failed=true; }
            Check(failed&&persistent.Allocated);
        }
        Console.WriteLine("GROUP_FORMATION_RADIUS_TRANSACTION_PASS "+checks+" checks"); return 0;
    }
}
