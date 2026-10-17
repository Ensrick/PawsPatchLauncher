using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class PopupScrollChecks
{
    private sealed record Choice(string Label);
    internal static void Run(string output)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null) throw new InvalidOperationException("Disposable smoke profile required.");
        var main = new MainWindow(new LauncherConfiguration { FeedUrls=[], BetaFeedUrls=[] }, null);
        var style = (Style)main.FindResource("ReleaseCombo");
        var combo = new ComboBox { Style=style, Width=240, ItemsSource=Enumerable.Range(1,30).Select(i=>new Choice("Language "+i)).ToArray(), SelectedIndex=0 };
        var content = new StackPanel();
        content.Children.Add(combo);
        var paper = new Border { Height=2000, Width=850, Background=Brushes.Navy }; content.Children.Add(paper);
        var scroll = new ScrollViewer { Content=content, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto };
        Window Fixture(object view) => new() { Content=view, Width=420, Height=350, Left=-32000, Top=-32000,
            ShowActivated=false, ShowInTaskbar=false, WindowStartupLocation=WindowStartupLocation.Manual };
        var window=Fixture(scroll);
        var otherPaper=new Border { Height=1200 }; var otherScroll=new ScrollViewer { Content=otherPaper }; var other=Fixture(otherScroll);
        var checks=0;
        void Check(bool value,string why) { checks++; if(!value)throw new Exception("Popup wheel: "+why); }
        MouseWheelEventArgs Wheel(UIElement source,int delta)
        {
            var args=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,delta) { RoutedEvent=Mouse.PreviewMouseWheelEvent };
            source.RaiseEvent(args);
            if(!args.Handled) { args.RoutedEvent=Mouse.MouseWheelEvent;source.RaiseEvent(args); }
            return args;
        }
        async Task Settle() { await Task.Delay(240); window.UpdateLayout();other.UpdateLayout(); }
        Popup Open()
        {
            combo.ApplyTemplate();
            var popup=(Popup)combo.Template.FindName("PART_Popup",combo);
            popup.Child.Opacity=0; // Native popup routing, without displaying a test menu on the user's screen.
            combo.IsDropDownOpen=true;
            window.UpdateLayout();
            return popup;
        }
        async Task Scenario()
        {
            foreach(var name in new[]{"LauncherLanguageCombo","GameLanguageCombo","GameVoiceCombo","HistorySubjectCombo","HistorySourceCombo","HistoryBranchCombo"})
            {
                var control=(ComboBox)main.FindName(name);control.ApplyTemplate();
                var p=control.Template.FindName("PART_Popup",control) as Popup;
                Check(p is not null && PopupScrollIsolation.GetEnabled(p),name+" is missing the common popup boundary");
            }
            var release=(ComboBox)typeof(MainWindow).GetField("_releaseChoice",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
            release.ApplyTemplate();Check(PopupScrollIsolation.GetEnabled((Popup)release.Template.FindName("PART_Popup",release)),"release chooser boundary");
            window.Show();other.Show();window.UpdateLayout();other.UpdateLayout();
            Wheel(paper,-120);await Settle();Check(scroll.VerticalOffset>0,"ordinary page wheel stopped working");
            scroll.ScrollToTop();window.UpdateLayout();
            Wheel(paper,-120);await Task.Delay(40);
            var popup=Open();await Task.Delay(50);var frozen=scroll.VerticalOffset;
            Check(popup.IsOpen,"long dropdown did not open");
            Check(!SmoothScroll.IsAnimating(scroll),"opening popup retained background inertia");
            foreach(var source in new UIElement[]{paper,combo,scroll})
            {
                foreach(var delta in new[]{-120,120,-360})Check(Wheel(source,delta).Handled,"outside wheel not consumed");
                await Settle();Check(Math.Abs(scroll.VerticalOffset-frozen)<.1,"background moved behind dropdown");
            }
            var inner=Descendants<ScrollViewer>(popup.Child).First();
            var items=(UIElement)inner.Content;
            Check(inner.ScrollableHeight>0,"long list is not scrollable");
            Wheel(items,-120);await Settle();Check(inner.VerticalOffset>0,"dropdown list wheel was blocked");
            Check(Math.Abs(scroll.VerticalOffset-frozen)<.1,"list wheel moved background");
            inner.ScrollToBottom();popup.Child.UpdateLayout();Wheel(items,-120);await Settle();
            Check(Math.Abs(scroll.VerticalOffset-frozen)<.1,"bottom edge leaked into page");
            inner.ScrollToTop();popup.Child.UpdateLayout();Wheel(items,120);await Settle();
            Check(Math.Abs(scroll.VerticalOffset-frozen)<.1,"top edge leaked into page");
            Wheel(otherPaper,-120);await Settle();Check(otherScroll.VerticalOffset>0,"popup blocked an unrelated window");
            combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent });
            await Settle();Check(!combo.IsDropDownOpen,"Escape no longer closes dropdown");
            Wheel(paper,-120);await Settle();Check(scroll.VerticalOffset>frozen,"closing dropdown did not restore page wheel");
            combo.ItemsSource=new[]{new Choice("English"),new Choice("Русский")};combo.SelectedIndex=0;
            for(var i=0;i<3;i++)
            {
                scroll.ScrollToTop();window.UpdateLayout();popup=Open();await Task.Delay(40);frozen=scroll.VerticalOffset;
                var small=Descendants<ScrollViewer>(popup.Child).First();Check(small.ScrollableHeight==0,"short-list fixture overflows");
                Wheel((UIElement)small.Content,-120);Wheel(paper,-120);await Settle();
                Check(Math.Abs(scroll.VerticalOffset-frozen)<.1,"short dropdown/reopen leaked wheel");
                combo.SelectedIndex=1;combo.IsDropDownOpen=false;await Settle(); // Wait for the native Fade/Closed event.
                Check(combo.SelectedIndex==1,"dropdown selection lost");
                var before=scroll.VerticalOffset;Wheel(paper,-120);await Settle();
                Check(scroll.VerticalOffset>before,"reopened dropdown left scrolling locked");
            }
            foreach(var menuStyle in new Style?[]{null})
            {
                var menu=new ContextMenu { PlacementTarget=combo, Opacity=0 };
                if(menuStyle is not null)menu.Style=menuStyle;
                menu.Items.Add(new MenuItem { Header="Copy" });menu.IsOpen=true;await Task.Delay(50);
                Check(PopupScrollIsolation.GetEnabled(menu),"context menu has no isolation");frozen=scroll.VerticalOffset;
                Wheel(paper,-120);Wheel(combo,-120);Wheel((MenuItem)menu.Items[0],-120);await Settle();
                Check(Math.Abs(scroll.VerticalOffset-frozen)<.1,"context menu leaked background scroll");
                menu.IsOpen=false;await Settle();Wheel(paper,-120);await Settle();
                Check(scroll.VerticalOffset>frozen,"context menu close retained wheel lock");
            }
            popup=Open();await Task.Delay(40);content.Children.Remove(combo);await Task.Delay(40);
            frozen=scroll.VerticalOffset;Wheel(paper,-120);await Settle();
            Check(scroll.VerticalOffset>frozen,"unloaded dropdown retained wheel lock");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"popup-scroll-result.json"),System.Text.Json.JsonSerializer.Serialize(new { checks, actualWpfWindows=true, nativeInputInjected=false, gameStarted=false }));
            Console.WriteLine($"POPUP SCROLL PASS {checks}: 7 selectors, short/long lists, edges, capture target, inertia, Escape, reopen, context menus, unrelated window and unload.");
        }
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            var task=Scenario();var frame=new DispatcherFrame();var deadline=new DispatcherTimer { Interval=TimeSpan.FromSeconds(30) };
            deadline.Tick+=(_,_)=>frame.Continue=false;deadline.Start();_=task.ContinueWith(_=>window.Dispatcher.BeginInvoke(()=>frame.Continue=false));
            if(!task.IsCompleted)Dispatcher.PushFrame(frame);deadline.Stop();
            if(!task.IsCompleted)throw new TimeoutException("Popup wheel regression checks");task.GetAwaiter().GetResult();
        }
        finally { window.Close();other.Close();main.Close();SynchronizationContext.SetSynchronizationContext(null); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
        {
            var child=VisualTreeHelper.GetChild(node,i);if(child is T found)yield return found;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
}
