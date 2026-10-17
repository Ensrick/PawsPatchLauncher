using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class WindowExperienceChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Window checks require --smoke-test.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var w = new MainWindow { Width = 1050, Height = 680, Left = -30000, Top = -30000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        object? Invoke(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, args);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(w)!;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(w, value);
        T Element<T>(string name) => (T)w.FindName(name);
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        Task<bool> Confirm(bool launcher) => (Task<bool>)Invoke("ConfirmRemovalAsync", launcher, @"C:\Игры, тест\Kohan II\PawsPatchLauncher.exe")!;
        void Click(string name) => Element<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language); Invoke("ApplyLanguage"); Invoke("SetActivePage", "settings");
        w.Show(); w.UpdateLayout();
        async Task Scenario()
        {
            var chrome = WindowChrome.GetWindowChrome(w);
            Check(chrome is { CaptionHeight: 46, GlassFrameThickness: { Top: 0 } }, "Native titlebar is not configured.");
            Check(chrome.ResizeBorderThickness.Top == 5 && !chrome.UseAeroCaptionButtons, "Custom frame resize/caption changed.");
            var handle = new WindowInteropHelper(w).Handle;
            int Hit(FrameworkElement element, double x, double y)
            {
                var p = element.PointToScreen(new Point(x, y));
                var packed = (int)(ushort)(short)p.X | ((int)(ushort)(short)p.Y << 16);
                return (int)SendMessage(handle, 0x84, IntPtr.Zero, (IntPtr)packed); // WM_NCHITTEST, own hidden-window HWND only.
            }
            Check(Hit(Element<Grid>("TitleBar"), 330, 24) == 2, "Titlebar does not return HTCAPTION for native drag/restore.");
            foreach (var name in new[] { "MinimizeWindowButton", "CloseWindowButton" })
            {
                var control = Element<FrameworkElement>(name);
                Check(Hit(control, control.ActualWidth / 2, control.ActualHeight / 2) == 1, name + " no longer clickable in native caption.");
            }
            var before = Element<TextBlock>("OperationText").Text;
            var pending = Confirm(false); w.UpdateLayout();
            Check(!pending.IsCompleted && !Field<bool>("_busy"), "Confirmation started a destructive/busy operation early.");
            Check(Element<Grid>("MainBody").IsEnabled && Element<Grid>("TitleBar").IsEnabled
                && !Element<Grid>("MainBody").IsHitTestVisible && !Element<Grid>("TitleBar").IsHitTestVisible,
                "Confirmation lost its input shield or toggled background enabled appearance.");
            Check(Element<Button>("ConfirmationCancelButton").IsDefault && !Element<Button>("ConfirmationDeleteButton").IsDefault, "Destructive action is the default.");
            Check(Element<TextBlock>("ConfirmationBodyText").Text == Element<TextBlock>("RemovePatchDescriptionText").Text, "Patch consequences differ from settings.");
            Check(Hit(Element<Grid>("TitleBar"), 330, 24) == 1, "Dimmed titlebar bypasses modal input block.");
            Check(!await (Task<bool>)Invoke("CheckFeedAsync", true)!, "Background feed check ran through confirmation.");
            Check(!await Confirm(true) && !pending.IsCompleted, "Second prompt replaced first confirmation.");
            Click("ConfirmationCancelButton"); Check(!await pending, "Cancel accepted deletion.");
            Check(Element<Grid>("MainBody").IsEnabled && Element<Border>("ConfirmationOverlay").Visibility == Visibility.Collapsed, "Cancel left modal/background locked.");
            Check(Element<TextBlock>("OperationText").Text == before, "Cancelled confirmation changed status.");
            pending = Confirm(true);
            Check(Element<TextBlock>("ConfirmationBodyText").Text == Element<TextBlock>("RemoveLauncherDescriptionText").Text, "Launcher consequences differ from settings.");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            w.RaiseEvent(escape);
            Check(!await pending && escape.Handled, "Escape failed to cancel.");
            pending = Confirm(true); w.Close(); Check(!await pending && w.IsLoaded, "Alt+F4/close did not cancel modal safely.");
            pending = Confirm(false); Click("ConfirmationCloseButton"); Check(!await pending, "Dialog X failed to cancel.");
            pending = Confirm(false); Click("ConfirmationDeleteButton"); Check(await pending, "Explicit confirmation was lost.");
            Check(!Field<bool>("_busy"), "Presentation-only confirmation executed removal.");
            Invoke("RemoveLauncher_Click", Element<Button>("RemoveLauncherButton"), new RoutedEventArgs());
            var handlerConfirmation = Field<TaskCompletionSource<bool>>("_confirmation").Task;
            Click("ConfirmationCancelButton"); await handlerConfirmation; await Task.Yield();
            Check(!Field<bool>("_busy") && Element<TextBlock>("OperationText").Text == before && w.IsLoaded, "Real uninstall-handler cancellation changed status or closed launcher.");
            Check(!Element<Button>("OpenGameFolderButton").IsEnabled, "Folder action enabled with no game.");
            var folder = Path.Combine(ActivityStore.Root, "Игры, тест", "Kohan II"); Directory.CreateDirectory(folder);
            Set("_game", new GameInstallation(folder, Path.Combine(folder, "k2.exe"), null, null)); Invoke("RefreshGameFolderButton");
            Check(Element<Button>("OpenGameFolderButton").IsEnabled, "Valid game folder action disabled.");
            string? opened = null;
            Set("_openGameFolder", (Func<string, Task>)(path => { opened = path; return Task.CompletedTask; }));
            await (Task)Invoke("OpenGameFolderAsync")!;
            Check(opened == folder && Element<TextBlock>("OperationText").Text == before, "Folder command changed path/status.");
            Set("_openGameFolder", (Func<string, Task>)(_ => throw new DirectoryNotFoundException("Synthetic missing folder")));
            await (Task)Invoke("OpenGameFolderAsync")!;
            Check(Element<Button>("OpenGameFolderButton").IsEnabled && Field<OperationFeedback>("_toast").Failed, "Folder failure blocked retries or did not report an error.");
            var gate = new TaskCompletionSource(); int opens = 0;
            Set("_openGameFolder", (Func<string, Task>)(_ => { opens++; return gate.Task; }));
            var opening = (Task)Invoke("OpenGameFolderAsync")!; await (Task)Invoke("OpenGameFolderAsync")!;
            Check(opens == 1 && !Element<Button>("OpenGameFolderButton").IsEnabled, "Repeated folder clicks were not guarded.");
            gate.SetResult(); await opening;
            Console.WriteLine($"WINDOW EXPERIENCE PASS {checks} {language}: native caption/button hit-tests, modal cancel/escape/close/accept, real uninstall-handler cancellation, safe default, input/feed/status isolation, folder action/error/repeat; no deletion or Explorer launched");
        }
        try
        {
            var task = w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => w.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            if (!task.IsCompleted) throw new TimeoutException("Window checks did not complete.");
            task.GetAwaiter().GetResult();
        }
        finally { w.Close(); }
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
