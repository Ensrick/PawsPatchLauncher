using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PawPureFixes
{
    internal static class PureBetaNativeTests
    {
        sealed class Memory : IPatchMemory, global::IMemory
        {
            internal readonly Dictionary<uint,byte[]> Regions = new Dictionary<uint,byte[]>();
            internal readonly List<Tuple<uint,int>> Hooks = new List<Tuple<uint,int>>();
            uint next=0x20000000;
            public byte[] Read(uint a,int n) {var r=Regions.Single(p=>a>=p.Key&&(ulong)a+(uint)n<=(ulong)p.Key+(uint)p.Value.Length);return r.Value.Skip((int)(a-r.Key)).Take(n).ToArray();}
            public void Write(uint a,byte[] b) {var r=Regions.Single(p=>a>=p.Key&&(ulong)a+(uint)b.Length<=(ulong)p.Key+(uint)p.Value.Length);Buffer.BlockCopy(b,0,r.Value,(int)(a-r.Key),b.Length);}
            public void WriteCode(uint a,byte[] b)
            {
                if(Hooks.Any(h=>a<h.Item1+h.Item2&&h.Item1<a+b.Length))throw new Exception("Overlapping code owners at "+a.ToString("X"));
                Hooks.Add(Tuple.Create(a,b.Length));Write(a,b);
            }
            public uint Allocate(int n) {uint a=next;next+=(uint)((n+4095)&~4095);Regions.Add(a,new byte[n]);return a;}
            public void MakeExecutable(uint a,int n) { Read(a,n); }
            public void Flush(uint a,int n) { }
            public void Free(uint a) {if(!Regions.Remove(a))throw new Exception("Unowned allocation");}
        }
        static byte[] Relocate(byte[] raw,uint image)
        {
            var b=(byte[])raw.Clone();int pe=BitConverter.ToInt32(b,60);
            int at=(int)BitConverter.ToUInt32(b,pe+24+96+40),end=at+(int)BitConverter.ToUInt32(b,pe+24+96+44);
            while(at<end)
            {
                uint page=BitConverter.ToUInt32(b,at);int size=BitConverter.ToInt32(b,at+4);
                if(size<8||at+size>end)throw new Exception("Invalid relocation directory");
                for(int p=at+8;p<at+size;p+=2)
                {
                    ushort e=BitConverter.ToUInt16(b,p);if((e>>12)!=3)continue;
                    int target=(int)(page+(e&4095));
                    Buffer.BlockCopy(BitConverter.GetBytes(unchecked(BitConverter.ToUInt32(b,target)+image-0x460000)),0,b,target,4);
                }
                at+=size;
            }
            return b;
        }
        static void CheckLobbyIdentity()
        {
            var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
            foreach(string mode in new[]{"vanilla","immortals"})
            {
                string stable=null;
                foreach(string channel in new[]{"stable","beta","unknown"})
                foreach(bool patch in new[]{true,false}) foreach(bool dataOnly in new[]{false,true})
                {
                    var settings=new {mod=mode,channel=channel,pawPatchEnabled=patch,dataOnly=dataOnly};
                    var modules=new Dictionary<string,object>{
                        {"pure-fixes-data",new {enabled=true,version=PureChannel.PatchVersion,files=new object[0]}},
                        {"immortals",new {enabled=mode=="immortals",version="2.1",files=new object[0]}}
                    };
                    string json=serializer.Serialize(new {appliedSettings=settings,modules=modules});
                    bool allowed=channel!="unknown"&&patch&&!dataOnly, rejected=false, russian;
                    string token=null;
                    try { token=PawLobbyCompatibility.Identity(json,new string('A',64),"helper.exe",new string('B',64),out russian); }
                    catch(InvalidDataException) { rejected=true; }
                    if(rejected==allowed)throw new Exception("Pure lobby channel gate: "+mode+"/"+channel);
                    if(!allowed)continue;
                    if(channel=="stable")stable=token;
                    else if(token!=stable)throw new Exception("Equivalent Pure channels have different lobby identities");
                }
            }
            Console.WriteLine("PURE_LOBBY_CHANNEL_PASS 24 valid/invalid channel and patch modes");
        }
        static int Main(string[] args)
        {
            CheckLobbyIdentity();
            if (PawLobbyCompatibility.Version != PureChannel.PatchVersion) throw new Exception("Pure lobby version mismatch");
            if (AiPolicyPayload.Profile != "pure") throw new Exception("Pure runtime contains another AI profile");
            if(Program.Hash(args[0])!="B865D8206990C4F055C51DE857F0B09B88AB5EE3B6AE74EE5232134001AA591C")throw new Exception("Unverified game snapshot");
            byte[] raw=File.ReadAllBytes(args[0]);int checks=0;
            foreach(uint image in new uint[]{0x460000,0xF20000,0x18000000})
            foreach(bool ai in new[]{false,true}) foreach(bool lair in new[]{false,true})
            {
                var m=new Memory();m.Regions.Add(image,Relocate(raw,image));m.Regions.Add(0x51000000,new byte[0x2000]);
                m.Write(image+0x5F94D0,BitConverter.GetBytes(0x51000000u));
                for(int i=0;i<3;i++) {uint f=0x51001000u+(uint)i*4;m.Write(0x51000120u+(uint)i*4,BitConverter.GetBytes(f));m.Write(f,BitConverter.GetBytes(new[]{20,64,256}[i]));}
                PureBetaRuntime.ImprovedAi=ai;PureBetaRuntime.WoundedDefenders=lair;
                PureBetaRuntime.Validate(m,image);PurePatch.Validate(m,image);PawFastTransfer.Validate(m,image);
                PureChannel.Install(m,image);
                CameraZoomPatch.Install(m,image,delegate{});CompanyPositionPatch.Install(m,image,delegate{});
                ExhaustionRecoveryPatch.Install(m,image,delegate{});AllyEconomyPatch.Install(m,image,delegate{});
                BotLobbyPatch.Install(m,image,0,ai,delegate{});
                uint randomMap = RandomMapPatch.Install(m,image,delegate{});
                RandomMapPatch.Verify(m,image,randomMap);
                if(lair)LairRecoveryPatch.Install(m,image,delegate{});
                EngineCrashFixesPatch.Install(m,image,0x77000000,delegate{});
                if(ai)AiPolicyRuntime.InstallNative(m,image,delegate{});
                // Constructor owners and UI sites remain untouched until their
                // later installers. In particular, no second terrain/zero hook.
                foreach(uint rva in new uint[]{0x22C7CC,0x22D00F})
                    if(m.Hooks.Any(h=>image+rva>=h.Item1&&image+rva<h.Item1+h.Item2))throw new Exception("Family hook conflict");
                PawGamePresentation.VerifyOffline();
                checks+=m.Hooks.Count;
            }
            Console.WriteLine("PURE_BETA_COMPOSITION_PASS "+checks+" guarded hook installs; 12 compositions, 3 image bases; no native process access");
            Console.WriteLine("PURE_RANDOM_MAP_TRANSACTION_PASS "+RandomMapPatch.SelfTest());
            return 0;
        }
    }
}
