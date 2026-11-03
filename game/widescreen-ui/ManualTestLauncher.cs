using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class ManualTestLauncher
{
    [STAThread] static void Main()
    {
        try
        {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string game=Path.Combine(root,"game");
            string helper=File.ReadAllText(Path.Combine(root,"helper.txt")).Trim();
            if(Path.GetFileName(helper)!=helper || !helper.StartsWith("k2_paws_",StringComparison.Ordinal))
                throw new InvalidDataException("Invalid test helper.");
            if(Process.GetProcessesByName("k2").Length!=0)
                throw new InvalidOperationException("Сначала закрой запущенную Kohan II.");
            if(!File.Exists(Path.Combine(game,"reconnect-test.marker")))
                throw new InvalidDataException("Missing test marker.");
            var start=new ProcessStartInfo(Path.Combine(game,helper));
            start.WorkingDirectory=game;
            start.UseShellExecute=false;
            start.CreateNoWindow=true;
            start.WindowStyle=ProcessWindowStyle.Hidden;
            start.EnvironmentVariables["PAWS_TEST_DOCUMENTS"]=Path.Combine(root,"Documents");
            start.EnvironmentVariables["SteamAppId"]="97130";
            start.EnvironmentVariables["SteamGameId"]="97130";
            start.EnvironmentVariables.Remove("PAWS_TEST_JOIN");
            Process.Start(start);
        }
        catch(Exception e){MessageBox.Show(e.Message,"Проверка интерфейса Paw’s Patch");}
    }
}
