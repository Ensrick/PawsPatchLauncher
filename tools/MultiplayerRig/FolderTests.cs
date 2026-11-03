using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
internal static class FolderTests {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr dll,string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)] delegate int FolderW(IntPtr w,int id,IntPtr token,uint flags,StringBuilder path);
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Ansi)] delegate int FolderA(IntPtr w,int id,IntPtr token,uint flags,StringBuilder path);
    static int checks;
    static void Check(bool good) { if(!good) throw new Exception("Private-folder check "+checks+" failed"); checks++; }
    static int Main(string[] args) {
        try {
            Environment.SetEnvironmentVariable("K2_RIG_JOIN",null);
            Environment.SetEnvironmentVariable("K2_RIG_DOCUMENTS",null);
            var dll=LoadLibrary(Path.GetFullPath(args[0])); Check(dll!=IntPtr.Zero);
            var w=(FolderW)Marshal.GetDelegateForFunctionPointer(GetProcAddress(dll,"SHGetFolderPathW"),typeof(FolderW));
            var a=(FolderA)Marshal.GetDelegateForFunctionPointer(GetProcAddress(dll,"SHGetFolderPathA"),typeof(FolderA));
            var path=new StringBuilder(260);
            Check(w(IntPtr.Zero,5,IntPtr.Zero,0,path)<0);
            Check(a(IntPtr.Zero,5,IntPtr.Zero,0,path)<0);
            var folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable("K2_RIG_DOCUMENTS",folder);
            path.Length=0;Check(w(IntPtr.Zero,5,IntPtr.Zero,0,path)==0 && path.ToString()==folder);
            path.Length=0;Check(a(IntPtr.Zero,5,IntPtr.Zero,0,path)==0 && path.ToString()==folder);
            var unicode=folder+"-\u0416\u010d";Directory.CreateDirectory(unicode);
            Environment.SetEnvironmentVariable("K2_RIG_DOCUMENTS",unicode);
            path.Length=0;Check(w(IntPtr.Zero,5,IntPtr.Zero,0,path)==0 && path.ToString()==unicode);
            Environment.SetEnvironmentVariable("K2_RIG_DOCUMENTS",new string('x',300));
            Check(w(IntPtr.Zero,5,IntPtr.Zero,0,path)<0);
            Check(a(IntPtr.Zero,5,IntPtr.Zero,0,path)<0);
            Console.WriteLine("RIG_FOLDER_PASS "+checks);return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
