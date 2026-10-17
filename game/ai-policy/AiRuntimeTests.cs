using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class AiRuntimeTests {
 sealed class Memory:IMemory {
  readonly Dictionary<uint,byte> bytes=new Dictionary<uint,byte>();
  readonly byte[] allocated=new byte[AiPolicyPayload.Allocation];
  internal int Operations,FailAt=-1;internal bool Allocated,PersistentFailure;
  internal void Seed(uint a,byte[] b){for(int i=0;i<b.Length;i++){uint p=a+(uint)i;if(p>=0x60000000&&p<0x60000000+allocated.Length)allocated[p-0x60000000]=b[i];else bytes[p]=b[i];}}
  void Op(){if(++Operations==FailAt)throw new IOException("injected failure");}
  internal Memory(uint image){for(int i=0;i<AiPolicyPayload.Sites.Length;i++){
   var guard=TerrainPatch.Hex(AiPolicyPayload.Guards[i]);
   // Independent native-instruction fixture: mov eax,[image+0x5f3fb8]
   // follows the grid-cost call. Do not seed this from production Guard().
   if(AiPolicyPayload.Sites[i]==0x262c71)Buffer.BlockCopy(BitConverter.GetBytes(image+0x5f3fb8),0,guard,9,4);
   if(AiPolicyPayload.Sites[i]==0x1e4807)Buffer.BlockCopy(BitConverter.GetBytes(image+0x5f3fc8),0,guard,10,4);
   if(AiPolicyPayload.Sites[i]==0x1e15c1)Buffer.BlockCopy(BitConverter.GetBytes(image+0x5f8720),0,guard,1,4);
   if(AiPolicyPayload.Sites[i]==0x1e0c03)Buffer.BlockCopy(BitConverter.GetBytes(image+0x458c38),0,guard,12,4);
   Seed(image+AiPolicyPayload.GuardStarts[i],guard);
  }}
  public byte[] Read(uint a,int n){Op();var result=new byte[n];for(int i=0;i<n;i++){uint p=a+(uint)i;if(p>=0x60000000&&p<0x60000000+allocated.Length)result[i]=allocated[p-0x60000000];else bytes.TryGetValue(p,out result[i]);}return result;}
  public void Write(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();Seed(a,b);}
  public void WriteCode(uint a,byte[] b){Seed(a,b.Take(2).ToArray());Op();if(PersistentFailure)throw new IOException("persistent partial write");Seed(a,b);}
  public uint Allocate(int n){Op();Allocated=true;return 0x60000000;}
  public void MakeExecutable(uint a,int n){Op();}
  public void Flush(uint a,int n){Op();}
  public void Free(uint a){Allocated=false;}
 }
 static int checks;
 static void Check(bool ok){checks++;if(!ok)throw new Exception("Check "+checks);}
 internal static int Main(){foreach(uint image in new uint[]{0x460000,0x3a0000,0x860000,0x18000000}){
  var quiet=new Memory(image);var absent=Path.Combine(Path.GetTempPath(),"PawsAiNoLogs-"+Guid.NewGuid().ToString("N"));
  AiPolicyRuntime.Install(quiet,image,-1,absent,delegate{});int quietOperations=quiet.Operations;
  AiPolicyRuntime.Tick();AiPolicyRuntime.Tick();
  Check(!Directory.Exists(absent));Check(quiet.Operations==quietOperations);Check(quiet.Allocated);
  var success=new Memory(image);uint cave=AiPolicyRuntime.InstallNative(success,image,delegate{});int operations=success.Operations;
  Check(success.Allocated);Check(BitConverter.ToUInt32(success.Read(cave+(uint)AiPolicyPayload.DataOffset+4,4),0)==15);
  Check(BitConverter.ToUInt32(success.Read(cave+(uint)AiPolicyPayload.DataOffset+(uint)AiPolicyPayload.QueryPointerOffset,4),0)==cave+(uint)AiPolicyPayload.QueryOffset);
  for(int i=0;i<AiPolicyPayload.Sites.Length;i++)Check(success.Read(image+AiPolicyPayload.Sites[i],5).SequenceEqual(TerrainPatch.Call(image+AiPolicyPayload.Sites[i],cave+AiPolicyPayload.Offsets[i])));
  for(int fault=1;fault<=operations;fault++){
   var m=new Memory(image){FailAt=fault};bool threw=false;try{AiPolicyRuntime.InstallNative(m,image,delegate{});}catch(IOException){threw=true;}
   Check(threw&&!m.Allocated);for(int i=0;i<AiPolicyPayload.Sites.Length;i++)Check(m.Read(image+AiPolicyPayload.Sites[i],5).SequenceEqual(TerrainPatch.Hex(AiPolicyPayload.Originals[i])));
  }
  for(int i=0;i<AiPolicyPayload.Sites.Length;i++){var m=new Memory(image);m.Seed(image+AiPolicyPayload.Sites[i]-1,new byte[]{0xcc});bool rejected=false;try{AiPolicyRuntime.InstallNative(m,image,delegate{});}catch(InvalidOperationException){rejected=true;}Check(rejected&&!m.Allocated);}
  var persistent=new Memory(image){PersistentFailure=true};try{AiPolicyRuntime.InstallNative(persistent,image,delegate{});}catch(IOException){}Check(persistent.Allocated);
 }
 Console.WriteLine("AI_TRANSACTION_PASS "+checks+" checks: guards, relocation, partial writes, rollback, uncertain rollback retains payload");return 0;}
}
