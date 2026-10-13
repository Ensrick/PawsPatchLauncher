using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

class PresentationTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at,int size,uint type,uint protection);
    [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr at,int size,uint type);
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static IntPtr Add(IntPtr p,int n){return new IntPtr(p.ToInt64()+n);}
    static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.Combine(root,"data/UI/Menus"));
        Directory.CreateDirectory(Path.Combine(root,"data/UI/Game"));
        File.WriteAllText(Path.Combine(root,"data/UI/Menus/main.tgi"),"fixture");
        foreach(string name in new[]{"paws_patch_versions.ini","paws_launch_versions.ini"})
            File.WriteAllText(Path.Combine(root,name),"[Versions]\nMod=arcane-wars\nModVersion=0.82.1.8\nPawPatch=0.2.0\nPatchChannel=beta");
        var install=typeof(PawGamePresentation).GetMethod("InstallPresentation",BindingFlags.NonPublic|BindingFlags.Static);
        int[] sites={0xBF99F,0xFA979,0xC772E,0xC88BD,0x2BFA43};
        byte[][] originals={new byte[]{0xe8,0xfb,0xd3,0x1f,0},new byte[5],new byte[]{0xff,0x92,0xd0,0,0,0},new byte[]{0xe8,0xc3,0xf8,0x1e,0},new byte[]{0xe8,0x80,0x74,0xff,0xff}};
        foreach(bool pureBeta in new[]{false,true})
        foreach(bool menuOnly in new[]{false,true})
        foreach(bool icon in new[]{false,true})
        foreach(bool layout in new[]{false,true})
        {
            string png=Path.Combine(root,"data/UI/Game/PawGoldSound.png");
            if(icon)File.WriteAllBytes(png,new byte[]{1});else if(File.Exists(png))File.Delete(png);
            File.WriteAllText(Path.Combine(root,"data/UI/Game/game_interface.tgi"),layout?"[PawGoldSoundButton Template=PushButtonWidget]":"old interface");
            IntPtr image=VirtualAlloc(IntPtr.Zero,0x610000,0x3000,0x40),cave=IntPtr.Zero;
            Check(image!=IntPtr.Zero,"disposable image allocation");
            try
            {
                originals[1][0]=0xa1;Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image.ToInt32()+0x5f3fc0)),0,originals[1],1,4);
                for(int i=0;i<sites.Length;i++)Marshal.Copy(originals[i],0,Add(image,sites[i]),originals[i].Length);
                byte[] uniformGuard={0x8b,0x80,0xe8,0,0,0,0x80,0x38,0,0x74,0x39};
                Marshal.Copy(new byte[]{0x8b,0x80,0xe8,0,0,0,0x8a,0},0,Add(image,0x13c856),8);
                Marshal.Copy(new byte[]{0xf3,0x0f,0x11,0x4d,0xfc},0,Add(image,0x141688),5);
                Marshal.Copy(uniformGuard,0,Add(image,0x141649),uniformGuard.Length);
                using(var proc=Process.GetCurrentProcess())
                    install.Invoke(null,new object[]{proc.Handle,image,Path.Combine(root,"test.log"),root,menuOnly,pureBeta});
                cave=Add(image,unchecked(0xFA979+5+Marshal.ReadInt32(Add(image,0xFA97A))-0x400));
                bool button=(!menuOnly || pureBeta) && icon && layout;
#if PAW_UNIFORM_UI
                bool uniform=!menuOnly || pureBeta;
#else
                bool uniform=false;
#endif
                Check(Marshal.ReadByte(Add(image,0x13c85c))==(uniform?0xb0:0x8a),"stock layout detector observes effective setting");
                Check(Marshal.ReadByte(Add(image,0x141688))==(uniform?0xe9:0xf3),"narrow displays retain the complete stock panel");
                if(uniform)
                {
                    Check(Marshal.ReadInt32(Add(image,0x141689))==unchecked(cave.ToInt32()+0xc00-image.ToInt32()-0x14168d),"clamp branch target");
                    Check(Marshal.ReadInt32(Add(cave,0xc04))==unchecked(image.ToInt32()+0x4b2e38),"relocated stock minimum width");
                    Check(Marshal.ReadInt32(Add(cave,0xc0e))==unchecked(image.ToInt32()+0x14168d-cave.ToInt32()-0xc12),"clamp resumes native setter");
                }
                Check(Marshal.ReadByte(Add(image,0x141652))==(uniform?0x90:0x74),"uniform UI scoped to full patch");
                Check(Marshal.ReadByte(Add(image,0x141653))==(uniform?0x90:0x39),"uniform UI branch padding");
                for(int j=0;j<9;j++)Check(Marshal.ReadByte(Add(image,0x141649+j))==uniformGuard[j],"flag read and flag storage untouched");
                for(int i=0;i<sites.Length;i++)
                {
                    bool changed=i==1 || (i==0 && !menuOnly) || (i>=2 && button);
                    if(changed)
                    {
                        Check(Marshal.ReadByte(Add(image,sites[i]))==(i==1?0xe9:0xe8),"proper call/jump kind");
                        int dest=unchecked(image.ToInt32()+sites[i]+5+Marshal.ReadInt32(Add(image,sites[i]+1)));
                        Check(dest==unchecked(cave.ToInt32()+(i==0?0:i==1?0x400:i==2?0x600:i==3?0xa00:0xb00)),"branch destination");
                        if(i==2)Check(Marshal.ReadByte(Add(image,sites[i]+5))==0x90,"six-byte virtual call padded with NOP");
                    }
                    else for(int j=0;j<originals[i].Length;j++)Check(Marshal.ReadByte(Add(image,sites[i]+j))==originals[i][j],"unselected hook untouched");
                }
                Check(Marshal.ReadInt32(Add(cave,0x26c))==unchecked(cave.ToInt32()+0x780),"safe private Activate");
                Check(Marshal.ReadInt32(Add(cave,0x2a8))==unchecked(cave.ToInt32()+0x790),"ordinary sort key method");
                Check(Marshal.PtrToStringUni(Add(cave,0x700))=="PawGoldSoundButton","widget name retained");
                Check(Marshal.PtrToStringUni(Add(cave,0x800)).Contains("Arcane Wars"),"menu labels retained");
            }
            finally{if(cave!=IntPtr.Zero)VirtualFree(cave,0,0x8000);VirtualFree(image,0,0x8000);}
        }
        Console.WriteLine("PRESENTATION_INSTALL_PASS "+checks+"; disposable test process; game untouched");return 0;
    }
}
