using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class SelectionScrollTests {
 sealed class Memory:IMemory {
  readonly Dictionary<uint,byte> bytes=new Dictionary<uint,byte>();
  readonly byte[] allocated=new byte[SelectionScrollPayload.Allocation];
  internal int Operations,FailAt=-1;internal bool Allocated,PersistentFailure;
  internal void Seed(uint a,byte[] b){for(int i=0;i<b.Length;i++){uint p=a+(uint)i;if(p>=0x60000000&&p<0x60000000+allocated.Length)allocated[p-0x60000000]=b[i];else bytes[p]=b[i];}}
  void Op(){if(++Operations==FailAt)throw new IOException("injected failure");}
  internal Memory(uint image){Seed(image+0xed127,TerrainPatch.Hex("83f8067d3b"));for(int i=0;i<SelectionScrollPayload.Sites.Length;i++)Seed(image+SelectionScrollPayload.Sites[i],BitConverter.GetBytes(image+SelectionScrollPayload.Targets[i]));}
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
 internal static int Main(){foreach(uint image in new uint[]{0x460000,0xf20000,0x18000000}){
  var success=new Memory(image);uint cave=SelectionScrollPatch.Install(success,image,delegate{});int operations=success.Operations;
  Check(success.Allocated);Check(success.Read(image+0xed12a,2).SequenceEqual(TerrainPatch.Hex("9090")));Check(success.Read(cave+(uint)SelectionScrollPayload.DataOffset,60).All(b=>b==0));
  for(int i=0;i<SelectionScrollPayload.Sites.Length;i++)Check(BitConverter.ToUInt32(success.Read(image+SelectionScrollPayload.Sites[i],4),0)==cave+SelectionScrollPayload.Offsets[i]);
  for(int fault=1;fault<=operations;fault++){
   var m=new Memory(image){FailAt=fault};bool threw=false;try{SelectionScrollPatch.Install(m,image,delegate{});}catch(IOException){threw=true;}
   Check(threw&&!m.Allocated);Check(m.Read(image+0xed12a,2).SequenceEqual(TerrainPatch.Hex("7d3b")));for(int i=0;i<SelectionScrollPayload.Sites.Length;i++)Check(BitConverter.ToUInt32(m.Read(image+SelectionScrollPayload.Sites[i],4),0)==image+SelectionScrollPayload.Targets[i]);
  }
  for(int i=0;i<SelectionScrollPayload.Sites.Length;i++){var m=new Memory(image);m.Seed(image+SelectionScrollPayload.Sites[i],new byte[]{0xcc});bool rejected=false;try{SelectionScrollPatch.Install(m,image,delegate{});}catch(InvalidOperationException){rejected=true;}Check(rejected&&!m.Allocated);}
  var persistent=new Memory(image){PersistentFailure=true};try{SelectionScrollPatch.Install(persistent,image,delegate{});}catch(IOException){}Check(persistent.Allocated);
 }
 Console.WriteLine("SELECTION_SCROLL_TRANSACTION_PASS "+checks+" checks: guards, relocation, partial writes, rollback, uncertain rollback retains payload");return 0;}
}
