using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class TeamAccessChecks
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static void Run(string language)
    {
        if(!ActivityStore.IsSmokeTest)throw new InvalidOperationException("Disposable profile required");
        var w=new MainWindow {Left=-32000,Top=-32000,Width=1050,Height=680,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,F)!.GetValue(w)!;
        void Set(string name,object value)=>typeof(MainWindow).GetField(name,F)!.SetValue(w,value);
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,F)!.Invoke(w,args);
        T C<T>(string name)=>(T)w.FindName(name);
        var checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("Team UI: "+why);checks++;}
        var service=Field<AccountService>("_account");
        var settings=Field<UserSettings>("_settings");
        void Role(int level,bool team,AccountState state=AccountState.SignedIn)
        {
            typeof(AccountService).GetField("_session",F)!.SetValue(service,new AccountSession{UserId="10000000-0000-4000-8000-000000000001",Nickname="pawtest",DisplayName="Paw",Email="test@example.invalid",AdminLevel=level,PawsTeam=team});
            typeof(AccountService).GetProperty("State")!.SetValue(service,state);
            Call("RenderAccount");Call("RefreshStatus");w.UpdateLayout();
        }
        Button ArcaneRow()=>Field<List<(string Mod,Button Button,bool Allowed)>>("_baseGameButtons").Single(r=>r.Mod==GameMod.ArcaneWars).Button;
        async Task Scenario()
        {
            Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language);Call("ApplyLanguage");
            settings.Mod=GameMod.Vanilla;settings.ModNoticeSeen=true;Call("SetActivePage","modules");
            foreach(var state in new[]{AccountState.Guest,AccountState.SignedIn,AccountState.Offline})
            {
                Role(0,false,state);
                Check(!C<RadioButton>("ArcaneWarsModRadio").IsEnabled,"ordinary account can select Arcane Wars");
                Check(C<RadioButton>("VanillaModRadio").IsEnabled&&C<RadioButton>("ImmortalsModRadio").IsEnabled,"public modes blocked");
                Call("BuildBaseGames");
                Check(ArcaneRow().ToolTip is string reason&&reason==(string)typeof(MainWindow).GetProperty("ArcaneAccessReason",F)!.GetValue(w)!&&!ArcaneRow().IsEnabled,
                    "Mod manager does not explain the Arcane Wars restriction");
                C<RadioButton>("ArcaneWarsModRadio").IsChecked=true;await Task.Delay(10);
                Check(settings.Mod==GameMod.Vanilla,"checked event bypassed role guard");
            }
            Check(C<RadioButton>("ArcaneWarsModRadio").ToolTip is null,"Access explanation belongs to the Mod manager row");
            foreach(var (level,team) in new[]{(0,true),(1,false),(2,false)})
            {
                Role(level,team);
                Call("BuildBaseGames");
                Check(C<RadioButton>("ArcaneWarsModRadio").IsEnabled&&ArcaneRow().ToolTip is null,"trusted member denied");
                Check((Call("PlayerRoleBadge",level,team,true) as Border)?.Child is TextBlock,"trusted badge missing");
            }
            Role(0,true);Check(service.AdminLevel==0&&C<Button>("AdminNav").Visibility==Visibility.Collapsed,"team gained admin panel");
            C<RadioButton>("ArcaneWarsModRadio").IsChecked=true;await Task.Delay(30);Check(settings.Mod==GameMod.ArcaneWars,"team cannot choose Arcane Wars");
            Role(0,false);Check(settings.Mod==GameMod.ArcaneWars,"role loss overwrote remembered game mode");
            Check(!C<Button>("LaunchButton").IsEnabled&&!C<Button>("ApplySettingsButton").IsEnabled&&!C<Button>("UpdateButton").IsEnabled,"old Arcane profile bypassed restriction");
            try{Call("EnsureModAccess",GameMod.ArcaneWars);throw new Exception("Access guard did not reject");}
            catch(TargetInvocationException error){Check(error.InnerException is InvalidOperationException&&error.InnerException.Message.Contains("Darquan"),"wrong access rejection");}
            Call("EnsureModAccess",GameMod.Vanilla);Call("EnsureModAccess",GameMod.Immortals);checks+=2;
            await (Task)Call("LaunchGameAsync")!;await (Task)Call("ApplySettingsAsync")!;Check(!Field<bool>("_busy"),"denied operation started work");
            var player=new SocialPlayer(Guid.NewGuid(),"pawtest","friend",PawsTeam:true);
            var name=(StackPanel)Call("SocialNameLabel",player)!;
            var line=(SocialIdentityLine)name.Children[1];line.Measure(new Size(280,100));line.Arrange(new Rect(0,0,280,line.DesiredSize.Height));
            var username=line.Children[0];var badge=(Border)line.Children[1];
            Check(((TextBlock)badge.Child).Text=="Paw's Team","wrong team badge");
            Check(badge.TranslatePoint(new Point(),line).X>username.TranslatePoint(new Point(),line).X,"badge not after username");
            Check(Math.Abs((badge.TranslatePoint(new Point(),line).Y+badge.ActualHeight/2)-(username.TranslatePoint(new Point(),line).Y+username.RenderSize.Height/2))<1,"badge height differs from username");
            string? link=null;Set("_openHelpLink",new Func<string,Task>(url=>{link=url;return Task.CompletedTask;}));
            C<Button>("ModsDiscordButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(20);
            Check(link is null && C<Border>("ConfirmationOverlay").Visibility == Visibility.Visible,"Discord bypasses browser confirmation");
            C<Button>("ConfirmationDeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(240);
            Check(link=="https://discord.gg/krCK7DDwyz","Discord opens wrong invitation");
            AdminChecks.Populate(w,"users");var peer=Field<AdminPage>("_adminData").Users[0];int? assigned=null;
            Set("_adminActionOverride",new Func<string,Guid,string,DateTimeOffset?,int?,Task>((action,target,reason,until,role)=>{assigned=role;return Task.CompletedTask;}));
            var pending=(Task)Call("ModerateAsync",peer,"role")!;await Task.Delay(30);
            var buttons=Descendants<Button>(C<StackPanel>("ConfirmationChangesPanel")).Where(b=>b.Tag is int).ToArray();
            Check(buttons.Length==4&&buttons.Any(b=>Equals(b.Tag,-1)),"team absent from role choices");
            buttons.Single(b=>Equals(b.Tag,-1)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(C<Button>("ConfirmationDeleteButton").IsEnabled,"team grant cannot be saved");
            C<Button>("ConfirmationDeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await pending;
            Check(assigned==-1,"role dialog sent admin level for team");
            Console.WriteLine($"TEAM UI PASS {checks} {language}: role gates, remembered AW, public modes, guarded operations, badge placement, Discord and role assignment");
        }
        try
        {
            w.Show();var task=w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
            timer.Tick+=(_,_)=>frame.Continue=false;task.ContinueWith(_=>w.Dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);timer.Start();try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}
            if(!task.IsCompleted)throw new TimeoutException("Team UI checks");task.GetAwaiter().GetResult();
        }
        finally{w.Close();}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node)where T:DependencyObject
    {if(node is T item)yield return item;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
}
