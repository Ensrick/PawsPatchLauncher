using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class TaskStatusTests
{
    sealed class Memory:IMemory
    {
        internal readonly Dictionary<uint,byte> Values=new Dictionary<uint,byte>();
        readonly byte[] raw;readonly uint image;
        internal int Operations,FailAt=-1;internal bool Allocated,PersistentCodeFailure;
        internal Memory(byte[] mapped,uint at,bool existing){raw=mapped;image=at;Seed(image+0x5f3fec,BitConverter.GetBytes(existing?0x50000000u:0));Seed(image+0x5f3fe4,BitConverter.GetBytes(0x50001000u));Seed(0x50000000,BitConverter.GetBytes(image+0x4b69e4));Seed(0x500000a0,BitConverter.GetBytes(0x73756a2du));}
        internal void Seed(uint at,byte[] b){for(int i=0;i<b.Length;i++)Values[at+(uint)i]=b[i];}
        void Op(){if(++Operations==FailAt)throw new IOException("test failure");}
        public byte[] Read(uint at,int count){Op();return Enumerable.Range(0,count).Select(i=>{uint p=at+(uint)i;byte b;return Values.TryGetValue(p,out b)?b:(p>=image&&p-image<raw.Length?raw[p-image]:(byte)0);}).ToArray();}
        public void Write(uint at,byte[] b){Seed(at,b.Take(2).ToArray());Op();Seed(at,b);}
        public void WriteCode(uint at,byte[] b){if(PersistentCodeFailure){Seed(at,b.Take(2).ToArray());throw new IOException("persistent partial write");}Write(at,b);}
        public uint Allocate(int count){Op();Allocated=true;return 0x60000000;}
        public void Free(uint at){Allocated=false;}
        public void MakeExecutable(uint at,int count){Op();}
        public void Flush(uint at,int count){Op();}
    }
    static int count;
    static void Check(bool condition){count++;if(!condition)throw new Exception("Check "+count);}
    internal static int Main(string[] args)
    {
        byte[] raw=File.ReadAllBytes(args[0]);
        foreach(uint image in new uint[]{0x460000,0xf20000,0x18000000})foreach(bool existing in new[]{false,true})
        {
            var success=new Memory(raw,image,existing);TaskStatusPatch.Install(success,image,delegate{});int ops=success.Operations;
            Check(success.Allocated);Check(!existing||BitConverter.ToUInt32(success.Read(0x500000a0,4),0)==0);
            var redirect=success.Read(image+TaskStatusPatch.Site,5);Check(redirect[0]==0xe9);
            Check(unchecked(image+TaskStatusPatch.Site+5+BitConverter.ToUInt32(redirect,1))==0x60000000);
            var body=success.Read(0x60000000,20);Check(body[15]==0xe9);
            Check(unchecked(0x60000000+20+BitConverter.ToUInt32(body,16))==image+TaskStatusPatch.Site+5);
            for(int fault=1;fault<=ops;fault++)
            {
                var m=new Memory(raw,image,existing){FailAt=fault};bool failed=false;
                try{TaskStatusPatch.Install(m,image,delegate{});}catch(IOException){failed=true;}
                Check(failed&&!m.Allocated);Check(m.Read(image+TaskStatusPatch.Site,5).SequenceEqual(TaskStatusPatch.Original));
                Check(BitConverter.ToUInt32(m.Read(0x500000a0,4),0)==0x73756a2d);
            }
            foreach(uint field in new uint[]{0x500010f0,0x500002e4,0x500002cc})if(existing)
            {var m=new Memory(raw,image,true);m.Seed(field,BitConverter.GetBytes(1));bool failed=false;try{TaskStatusPatch.Install(m,image,delegate{});}catch(InvalidOperationException){failed=true;}Check(failed&&!m.Allocated);}
            var wrong=new Memory(raw,image,existing);wrong.Seed(image+TaskStatusPatch.Site,new byte[]{0xcc});bool rejected=false;
            try{TaskStatusPatch.Install(wrong,image,delegate{});}catch(InvalidOperationException){rejected=true;}Check(rejected&&!wrong.Allocated);
            var uncertain=new Memory(raw,image,existing){PersistentCodeFailure=true};
            try{TaskStatusPatch.Install(uncertain,image,delegate{});}catch(IOException){}
            Check(uncertain.Allocated); // Never free code while its redirect may still be reachable.
        }
        File.WriteAllBytes(args[1],TaskStatusPatch.Build(0x460000,0x10000000));
        Console.WriteLine("TASK_STATUS_TRANSACTIONS_PASS "+count+" checks");return 0;
    }
}
