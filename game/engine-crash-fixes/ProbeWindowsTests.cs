// Runs generated x86 probe code in a disposable process, never in the game.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
internal static class TerrainPatch {
 internal static byte[] Hex(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(i*2,2),16);return b;}
}
internal static class ProbeWindowsTests {
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr VirtualAlloc(IntPtr p,UIntPtr n,uint t,uint protect);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualProtect(IntPtr p,UIntPtr n,uint protect,out uint old);
 [DllImport("kernel32.dll")]static extern bool VirtualFree(IntPtr p,UIntPtr n,uint t);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr m,string n);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string n);
 [DllImport("kernel32.dll")]static extern uint RemoveVectoredExceptionHandler(IntPtr cookie);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate uint Probe(IntPtr target);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Handler(IntPtr pointers);
 static IntPtr P(uint x){return new IntPtr(unchecked((int)x));}
 static uint U(IntPtr p){return unchecked((uint)p.ToInt32());}
 static IntPtr Alloc(int n){var p=VirtualAlloc(IntPtr.Zero,new UIntPtr((uint)n),0x3000,4);if(p==IntPtr.Zero)throw new Exception("VirtualAlloc");return p;}
 static void W(IntPtr p,int offset,uint value){Marshal.WriteInt32(p,offset,unchecked((int)value));}
 static int checks;
 static void Check(bool b,string message){checks++;if(!b)throw new Exception(message);}
 static int Main(){
  var image=Alloc(0x610000);var cave=Alloc(EngineCrashPayload.Allocation);var target=Alloc(8192);uint old;
  try{
   var code=EngineCrashPayload.Build(U(image),U(cave));
   // Isolated Windows API failure fixture: return NULL, stdcall with two args.
   Array.Copy(new byte[]{0x33,0xc0,0xc2,8,0},0,code,0x800,5);Marshal.Copy(code,0,cave,code.Length);
   Check(VirtualProtect(cave,new UIntPtr(4096),0x20,out old),"code RX");
   W(cave,4096+44,U(GetProcAddress(GetModuleHandle("kernel32.dll"),"AddVectoredExceptionHandler")));
   var probe=(Probe)Marshal.GetDelegateForFunctionPointer(P(U(cave)+(uint)EngineCrashPayload.ProbeOffset),typeof(Probe));
   var stopProbe=(Probe)Marshal.GetDelegateForFunctionPointer(P(U(cave)+(uint)EngineCrashPayload.StopProbeOffset),typeof(Probe));
   var handler=(Handler)Marshal.GetDelegateForFunctionPointer(P(U(cave)+0x400),typeof(Handler));
   uint vt=U(image)+0x500000;W(P(vt),4,U(image)+0x2000);W(target,0,vt);W(target,4,1);
   Check(probe(target)==0,"live target");Check(probe(IntPtr.Zero)==0,"native null semantics");
   W(target,4,0);Check(probe(target)==1,"destroyed target");W(target,4,0xffffffff);Check(probe(target)==1,"invalid refcount");W(target,4,1);
   W(target,0,0);Check(probe(target)==2,"null vtable");W(target,0,U(image)+0x600000);Check(probe(target)==2,"outside rdata");W(target,0,vt);
   W(P(vt),4,U(image)+0x57ca1c);Check(probe(target)==3,"historical data-as-code crash");W(P(vt),4,U(image)+0x2000);
   Check(VirtualProtect(target,new UIntPtr(4096),1,out old),"target noaccess");
   for(int i=0;i<20;i++)Check(probe(target)==4,"unreadable target recover locally");
   Check(VirtualProtect(target,new UIntPtr(4096),4,out old),"target restore");Check(probe(target)==0,"valid after faults");
   Check(VirtualProtect(P(U(target)+4096),new UIntPtr(4096),1,out old),"second page noaccess");
   Check(probe(P(U(target)+4092))==4,"field crosses page");
   Check(VirtualProtect(P(vt),new UIntPtr(4096),1,out old),"vtable noaccess");Check(probe(target)==4,"unreadable vtable");
   Check(VirtualProtect(P(vt),new UIntPtr(4096),4,out old),"vtable restore");Check(probe(target)==0,"valid after table fault");
   // The vectored handler is registered through Windows, with no dynamic SEH
   // chain node or mitigation changes. Reject all unrelated exceptions.
   IntPtr ex=P(U(target)+256),ctx=P(U(target)+512),pointers=P(U(target)+1024);
   W(pointers,0,U(ex));W(pointers,4,U(ctx));
   W(ex,0,0xc0000005);W(ex,4,0);W(ex,16,2);W(ex,20,0);W(ctx,0xb8,U(cave)+0x240);
   Check(handler(pointers)==-1,"local read disposition");
   Check(unchecked((uint)Marshal.ReadInt32(ctx,0xb8))==U(cave)+0x380,"local recovery address");
   foreach(uint ip in new uint[]{U(image)+0x57ca1c,U(image)+0x14c04a,U(cave)+0x300,U(cave)+0x1ff}){
    W(ctx,0xb8,ip);Check(handler(pointers)==0,"unrelated IP propagated");
   }
   W(ctx,0xb8,U(cave)+0x240);W(ex,20,1);Check(handler(pointers)==0,"writes propagated");
   W(ex,20,8);Check(handler(pointers)==0,"execution fault propagated");
   W(ex,20,0);W(ex,4,2);Check(handler(pointers)==0,"unwind propagated");
   W(ex,4,0);W(ex,0,0xc0000094);Check(handler(pointers)==0,"other exception propagated");
   // Real Windows page faults in the new controller probe, not a mocked read.
   uint controllerTable=unchecked((uint)Marshal.ReadInt32(cave,0xc00));
   W(target,0,controllerTable);W(P(controllerTable),0x40,U(image)+0x2000);
   Check(stopProbe(target)==0,"native controller admitted");
   Check(stopProbe(IntPtr.Zero)==0,"stop null semantics");
   W(target,4,0);Check(stopProbe(target)==1,"dead controller");W(target,4,1);
   W(target,0,U(image)+0x53c3c0);W(P(U(image)+0x53c3c0),0x40,0x62);
   Check(stopProbe(target)==2,"log-70 recycled material rejected");
   W(P(U(image)+0x53c3c0),0x40,U(image)+0x2000);
   Check(stopProbe(target)==2,"wrong type rejected even with executable slot");
   W(target,0,controllerTable);W(P(controllerTable),0x40,0x62);
   Check(stopProbe(target)==3,"bad stop slot");W(P(controllerTable),0x40,U(image)+0x2000);
   Check(VirtualProtect(target,new UIntPtr(4096),1,out old),"controller noaccess");
   for(int i=0;i<20;i++)Check(stopProbe(target)==4,"unreadable controller recovers only probe");
   Check(VirtualProtect(target,new UIntPtr(4096),4,out old),"controller restore");
   Check(stopProbe(P(U(target)+4092))==4,"controller refcount crosses inaccessible page");
   Check(VirtualProtect(P(controllerTable&0xfffff000),new UIntPtr(4096),1,out old),"controller table noaccess");
   Check(stopProbe(target)==4,"unreadable controller stop slot");
   Check(VirtualProtect(P(controllerTable&0xfffff000),new UIntPtr(4096),4,out old),"controller table restore");
   Check(stopProbe(target)==0,"valid stop after faults");
   W(ex,0,0xc0000005);W(ctx,0xb8,U(cave)+0xa60);
   Check(handler(pointers)==-1,"stop local read disposition");
   Check(unchecked((uint)Marshal.ReadInt32(ctx,0xb8))==U(cave)+0x380,"stop local recovery address");
   foreach(uint ip in new uint[]{U(cave)+0x900,U(cave)+0x9ff,U(cave)+0xb00,U(image)+0x31ceae,0x62}){
    W(ctx,0xb8,ip);Check(handler(pointers)==0,"stop unrelated exception propagated");
   }
   W(ctx,0xb8,U(cave)+0xa60);W(ex,20,8);Check(handler(pointers)==0,"stop execute fault propagated");
   W(ex,20,1);Check(handler(pointers)==0,"stop write fault propagated");W(ex,20,0);
   W(target,0,vt);
   var timer=Stopwatch.StartNew();uint sum=0;for(int i=0;i<1000000;i++)sum+=probe(target);timer.Stop();
   Check(sum==0,"benchmark valid targets preserved");
   Check(RemoveVectoredExceptionHandler(P(unchecked((uint)Marshal.ReadInt32(cave,4096+40))))!=0,"unregister fixture");
   W(cave,4096+40,0);W(cave,4096+44,U(cave)+0x800);
   W(target,4,0);Check(probe(target)==0,"registration failure preserves stock behavior");
   Check(Marshal.ReadInt32(cave,4096+48)==1,"registration failure counted");
   Check(probe(target)==0&&Marshal.ReadInt32(cave,4096+48)==1,"failed registration not retried per controller");
   Check(stopProbe(target)==0,"stop registration failure preserves native behavior");
   Console.WriteLine("ENGINE_PROBE_WINDOWS_PASS "+checks+" checks; one million valid probes incl managed calls="+timer.ElapsedMilliseconds+"ms");
   return 0;
  }finally{var cookie=P(unchecked((uint)Marshal.ReadInt32(cave,4096+40)));if(cookie!=IntPtr.Zero)RemoveVectoredExceptionHandler(cookie);VirtualFree(cave,UIntPtr.Zero,0x8000);VirtualFree(image,UIntPtr.Zero,0x8000);VirtualFree(target,UIntPtr.Zero,0x8000);}
 }
}
