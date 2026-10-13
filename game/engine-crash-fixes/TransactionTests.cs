using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
internal static class EngineCrashTests {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
 sealed class Memory:IMemory {
  readonly Dictionary<uint,byte> bytes=new Dictionary<uint,byte>();
  readonly byte[] allocated=new byte[EngineCrashPayload.Allocation];
  internal int Operations,FailAt=-1;internal bool Allocated,PersistentFailure;
  internal void Seed(uint a,byte[] b){for(int i=0;i<b.Length;i++){uint p=a+(uint)i;if(p>=0x60000000&&p<0x60000000+allocated.Length)allocated[p-0x60000000]=b[i];else bytes[p]=b[i];}}
  void Op(){if(++Operations==FailAt)throw new IOException("injected failure");}
  internal Memory(uint image){
   for(int i=0;i<EngineCrashPayload.Sites.Length;i++)Seed(image+EngineCrashPayload.Sites[i],EngineCrashPayload.Original(i,image));
   Seed(image+0x31EAAC,TerrainPatch.Hex("85C974368B018B4004FFD0"));Seed(image+0x31EB1A,TerrainPatch.Hex("8B7508468975083B7738"));
   Seed(image+0x14C03F,TerrainPatch.Hex("8B068D4A018B7604"));Seed(image+0x14C04E,TerrainPatch.Hex("0F44CA8BD185F675E8"));
  }
  public byte[] Read(uint a,int n){Op();var result=new byte[n];for(int i=0;i<n;i++){uint p=a+(uint)i;if(p>=0x60000000&&p<0x60000000+allocated.Length)result[i]=allocated[p-0x60000000];else bytes.TryGetValue(p,out result[i]);}return result;}
  public void Write(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();Seed(a,b);}
  public void WriteCode(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();if(PersistentFailure)throw new IOException("persistent partial write");Seed(a,b);}
  public uint Allocate(int n){Op();Allocated=true;return 0x60000000;}
  public void MakeExecutable(uint a,int n){Op();if(n!=4096)throw new Exception("data page must remain writable");}
  public void Flush(uint a,int n){Op();}
  public void Free(uint a){Allocated=false;}
 }
 static int checks; static void Check(bool ok){checks++;if(!ok)throw new Exception("Check "+checks);}
 internal static int Main(){
 using(var self=Process.GetCurrentProcess())Check(EngineCrashFixesPatch.ResolveHandlerRegistration(self)==unchecked((uint)GetProcAddress(GetModuleHandle("kernel32.dll"),"AddVectoredExceptionHandler").ToInt32()));
 foreach(uint image in new uint[]{0x460000,0xf20000,0x18000000}){
  var success=new Memory(image);uint cave=EngineCrashFixesPatch.Install(success,image,0x76543210,delegate{});int operations=success.Operations;
  Check(success.Allocated);Check(success.Read(cave+4096,44).All(b=>b==0));Check(BitConverter.ToUInt32(success.Read(cave+4140,4),0)==0x76543210);
  for(int i=0;i<EngineCrashPayload.Sites.Length;i++){
   uint site=image+EngineCrashPayload.Sites[i];byte[] bytes=success.Read(site,EngineCrashPayload.Originals[i].Length/2);
   Check(bytes[0]==0xe9);Check(unchecked(site+5+BitConverter.ToUInt32(bytes,1))==cave+EngineCrashPayload.Offsets[i]);
  }
  for(int fault=1;fault<=operations;fault++){
   var m=new Memory(image){FailAt=fault};bool threw=false;try{EngineCrashFixesPatch.Install(m,image,0x76543210,delegate{});}catch(IOException){threw=true;}
   Check(threw&&!m.Allocated);for(int i=0;i<EngineCrashPayload.Sites.Length;i++)Check(m.Read(image+EngineCrashPayload.Sites[i],EngineCrashPayload.Originals[i].Length/2).SequenceEqual(EngineCrashPayload.Original(i,image)));
  }
  foreach(uint site in EngineCrashPayload.Sites.Concat(new uint[]{0x31eaac,0x31eb1a,0x14c03f,0x14c04e})){
   var m=new Memory(image);m.Seed(image+site,new byte[]{0xcc});bool rejected=false;try{EngineCrashFixesPatch.Install(m,image,0x76543210,delegate{});}catch(InvalidOperationException){rejected=true;}Check(rejected&&!m.Allocated);
  }
  var noapi=new Memory(image);try{EngineCrashFixesPatch.Install(noapi,image,0,delegate{});}catch(ArgumentException){}Check(!noapi.Allocated&&noapi.Operations==0);
  var persistent=new Memory(image){PersistentFailure=true};try{EngineCrashFixesPatch.Install(persistent,image,0x76543210,delegate{});}catch(IOException){}Check(persistent.Allocated);
 }
 Console.WriteLine("ENGINE_CRASH_TRANSACTION_PASS "+checks+" checks: guards, ASLR, partial writes, rollback, uncertain rollback retains code");return 0;}
}
