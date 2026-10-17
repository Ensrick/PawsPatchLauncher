using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class SmoothExperienceChecks
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static void Run(string language)
    {
        if(!ActivityStore.IsSmokeTest)throw new InvalidOperationException("Motion fixtures require smoke mode.");
        int checks=0;bool moving=SystemParameters.ClientAreaAnimation;
        void Check(bool ok,string reason){checks++;if(!ok)throw new Exception("Smooth experience: "+reason);}
        var content=new Border {Height=1600,Width=1100,Background=Brushes.Navy};
        var scroll=new ScrollViewer {Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var fixture=new Window {Content=scroll,Width=400,Height=320,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        var w=new MainWindow {Width=1050,Height=680,Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        object? Invoke(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Flags)!.Invoke(w,args);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,Flags)!.GetValue(w)!;
        T Control<T>(string name)=>(T)w.FindName(name);
        void Wheel(UIElement source,int delta)=>source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent});
        async Task Scenario()
        {
            fixture.Show();fixture.UpdateLayout();await Task.Delay(40);
            Check(SmoothScroll.GetEnabled(scroll),"global scrolling behavior absent");
            double step=SystemParameters.WheelScrollLines<0?scroll.ViewportHeight:SystemParameters.WheelScrollLines*16;
            Wheel(content,-120);
            if(moving&&step>0)
            {
                Check(SmoothScroll.IsAnimating(scroll)&&scroll.VerticalOffset==0,"wheel jumped immediately");
                await Task.Delay(65);
                Check(scroll.VerticalOffset>0&&scroll.VerticalOffset<step,"wheel has no intermediate offset");
            }
            await Task.Delay(220);Check(Math.Abs(scroll.VerticalOffset-step)<1.5&&!SmoothScroll.IsAnimating(scroll),"wheel endpoint");
            for(int i=0;i<5;i++)Wheel(content,-120);
            await Task.Delay(240);Check(Math.Abs(scroll.VerticalOffset-6*step)<1.5,"rapid wheel ticks lost");
            Wheel(content,-120);await Task.Delay(50);var reverse=scroll.VerticalOffset;Wheel(content,120);
            await Task.Delay(230);Check(scroll.VerticalOffset<reverse||step==0,"reverse continued in old direction");
            Wheel(content,-120);scroll.ScrollToBottom();fixture.UpdateLayout();await Task.Delay(240);
            Check(Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<1.5&&!SmoothScroll.IsAnimating(scroll),"animation overwrote application ScrollToBottom");
            scroll.ScrollToTop();fixture.UpdateLayout();
            var bar=(ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar",scroll);
            ScrollBar.PageDownCommand.Execute(null,bar);
            if(moving)Check(SmoothScroll.IsAnimating(scroll),"scrollbar paging skipped animation");
            await Task.Delay(240);Check(scroll.VerticalOffset>0,"page scroll failed");
            var horizontal=(ScrollBar)scroll.Template.FindName("PART_HorizontalScrollBar",scroll);
            ScrollBar.PageRightCommand.Execute(null,horizontal);await Task.Delay(240);Check(scroll.HorizontalOffset>0,"horizontal paging failed");
            Wheel(content,-120);
            bar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            Check(!SmoothScroll.IsAnimating(scroll),"mouse/drag did not stop inertial scroll");
            var track=(Track)bar.Template.FindName("PART_Track",bar);var dragStart=scroll.VerticalOffset;
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0,20){RoutedEvent=Thumb.DragDeltaEvent});fixture.UpdateLayout();
            Check(scroll.VerticalOffset>dragStart&&!SmoothScroll.IsAnimating(scroll),"native thumb drag delayed");
            Wheel(content,-120);scroll.Visibility=Visibility.Collapsed;
            Check(!SmoothScroll.IsAnimating(scroll),"hidden view retained render callback");scroll.Visibility=Visibility.Visible;fixture.UpdateLayout();
            Wheel(content,-120);content.Height=1800;fixture.UpdateLayout();
            Check(!SmoothScroll.IsAnimating(scroll),"content resize retained stale animation target");

            var innerContent=new Border {Height=700,Background=Brushes.DarkBlue};
            var inner=new ScrollViewer {Height=120,Content=innerContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var nested=new StackPanel();nested.Children.Add(inner);nested.Children.Add(new Border{Height=1000});
            scroll.Content=nested;fixture.UpdateLayout();scroll.ScrollToTop();fixture.UpdateLayout();
            Wheel(innerContent,-120);await Task.Delay(240);
            Check(inner.VerticalOffset>0&&scroll.VerticalOffset==0,"outer view stole nested wheel");
            inner.ScrollToBottom();fixture.UpdateLayout();Wheel(innerContent,-120);await Task.Delay(240);
            Check(scroll.VerticalOffset>0&&Math.Abs(inner.VerticalOffset-inner.ScrollableHeight)<1.5,"nested edge did not hand off to parent");
            var editor=new TextBox {Height=120,Text=string.Join("\n",Enumerable.Repeat("Caret stays native",50)),AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            scroll.Content=editor;fixture.UpdateLayout();
            var editorScroll=Descendants<ScrollViewer>(editor).First();
            Check(SmoothScroll.GetEnabled(editorScroll),"text editor scrolling missed global behavior");
            editor.CaretIndex=5;
            var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(fixture),0,Key.Down){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            editor.RaiseEvent(key);Check(!key.Handled,"smoothing intercepted editor caret key");
            fixture.Close();

            Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language);Invoke("ApplyLanguage");
            w.Show();w.UpdateLayout();await Task.Delay(220);
            foreach(var size in new[]{new Size(1050,680),new Size(1440,900)})
            {
                w.Width=size.Width;w.Height=size.Height;w.UpdateLayout();
                var header=Control<Grid>("WorkspaceHeader");var body=Control<Grid>("WorkspaceBody");
                Check(header.ActualHeight>0&&body.Margin.Top==12&&body.RowDefinitions[1].ActualHeight==10,"header lost its compact layout");
            }
            foreach(var name in new[]{"MainOptionsScroll","NewsScrollViewer"})Check(SmoothScroll.GetEnabled(Control<ScrollViewer>(name)),"missing smooth view "+name);
            var confirmation=(Task<bool>)Invoke("ConfirmActionAsync","Fixture","Body","Detail","Value","Action")!;await Task.Delay(200);
            var cancel=(Task)Invoke("CompleteConfirmationAsync",false)!;
            if(moving)
            {
                Check(!confirmation.IsCompleted&&Control<Grid>("MainBody").IsEnabled&&Control<Border>("ConfirmationOverlay").Visibility==Visibility.Visible,"confirmation removed its blocking overlay before fade");
                await Task.Delay(65);Check(Control<Border>("ConfirmationOverlay").Opacity is >0 and <1,"confirmation has no intermediate fade");
            }
            await cancel;Check(!await confirmation&&Control<Border>("ConfirmationOverlay").Visibility==Visibility.Collapsed,"confirmation close lost underlying profile");
            Console.WriteLine($"SMOOTH EXPERIENCE PASS {checks} {language}: real off-screen WPF frames, wheel/reverse/nested/page/drag/editor/resize, close/reopen/modal blocking and compact header; Windows animations={moving}");
        }
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(w.Dispatcher));
            var work=Scenario();var frame=new DispatcherFrame();var watchdog=new DispatcherTimer {Interval=TimeSpan.FromSeconds(25)};
            watchdog.Tick+=(_,_)=>frame.Continue=false;watchdog.Start();_=work.ContinueWith(_=>w.Dispatcher.BeginInvoke(()=>frame.Continue=false));
            if(!work.IsCompleted)Dispatcher.PushFrame(frame);watchdog.Stop();
            if(!work.IsCompleted)throw new TimeoutException("Smooth experience fixture");work.GetAwaiter().GetResult();
        }
        finally{fixture.Close();w.Close();SynchronizationContext.SetSynchronizationContext(null);}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
        {
            var child=VisualTreeHelper.GetChild(node,i);if(child is T found)yield return found;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
}
