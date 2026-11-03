using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class GroupMovementSpeedTests
{
    sealed class Memory : IMemory
    {
        readonly Dictionary<uint,byte> bytes=new Dictionary<uint,byte>();
        internal int Operations,FailAt=-1;internal bool Allocated,PersistentFailure;
        internal void Seed(uint a,byte[] b){for(int i=0;i<b.Length;i++)bytes[a+(uint)i]=b[i];}
        void Op(){if(++Operations==FailAt)throw new IOException("injected failure");}
        internal Memory(uint image){for(int i=0;i<GroupMovementSpeedPatch.Sites.Length;i++)Seed(image+GroupMovementSpeedPatch.Sites[i],TerrainPatch.Hex(GroupMovementSpeedPatch.Guards[i]));}
        public byte[] Read(uint a,int n){Op();var b=new byte[n];for(int i=0;i<n;i++)bytes.TryGetValue(a+(uint)i,out b[i]);return b;}
        public void Write(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();Seed(a,b);}
        public void WriteCode(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();if(PersistentFailure)throw new IOException("persistent partial write");Seed(a,b);}
        public uint Allocate(int n){Op();Allocated=true;return 0x60000000;}
        public void MakeExecutable(uint a,int n){Op();}public void Flush(uint a,int n){Op();}public void Free(uint a){Allocated=false;}
    }
    static int checks;static void Check(bool v){checks++;if(!v)throw new Exception("Check "+checks);}
    static int Main(string[] args)
    {
        if(args.Length==4){File.WriteAllBytes(args[0],GroupMovementSpeedPayload.Build(int.Parse(args[1]),Convert.ToUInt32(args[2],16),Convert.ToUInt32(args[3],16)));return 0;}
        foreach(uint image in new uint[]{0x460000,0x390000,0x18000000})
        {
            var m=new Memory(image);uint cave=GroupMovementSpeedPatch.Install(m,image,delegate{});int operations=m.Operations;Check(m.Allocated);
            for(int i=0;i<GroupMovementSpeedPatch.Sites.Length;i++){Check(m.Read(image+GroupMovementSpeedPatch.Sites[i],TerrainPatch.Hex(GroupMovementSpeedPatch.Originals[i]).Length).SequenceEqual(GroupMovementSpeedPatch.Jump(i,image,cave)));var b=GroupMovementSpeedPayload.Build(i,image,cave+(uint)i*256);Check(m.Read(cave+(uint)i*256,b.Length).SequenceEqual(b));}
            for(int fault=1;fault<=operations;fault++)
            {
                m=new Memory(image){FailAt=fault};bool threw=false;try{GroupMovementSpeedPatch.Install(m,image,delegate{});}catch(IOException){threw=true;}
                Check(threw&&!m.Allocated);for(int i=0;i<GroupMovementSpeedPatch.Sites.Length;i++)Check(m.Read(image+GroupMovementSpeedPatch.Sites[i],TerrainPatch.Hex(GroupMovementSpeedPatch.Originals[i]).Length).SequenceEqual(TerrainPatch.Hex(GroupMovementSpeedPatch.Originals[i])));
            }
            for(int i=0;i<GroupMovementSpeedPatch.Sites.Length;i++)for(int o=0;o<TerrainPatch.Hex(GroupMovementSpeedPatch.Guards[i]).Length;o++)
            {
                m=new Memory(image);m.Seed(image+GroupMovementSpeedPatch.Sites[i]+(uint)o,new byte[]{0xcc});bool threw=false;try{GroupMovementSpeedPatch.Install(m,image,delegate{});}catch(InvalidOperationException){threw=true;}Check(threw&&!m.Allocated);
            }
            m=new Memory(image){PersistentFailure=true};bool failed=false;try{GroupMovementSpeedPatch.Install(m,image,delegate{});}catch(IOException){failed=true;}Check(failed&&m.Allocated);
        }
        Console.WriteLine("GROUP_MOVEMENT_SPEED_TRANSACTION_PASS "+checks+" checks");return 0;
    }
}
