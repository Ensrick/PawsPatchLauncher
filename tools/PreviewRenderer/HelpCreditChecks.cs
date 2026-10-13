using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class HelpCreditChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Help checks require --smoke-test.");
        const string invite = "https://discord.gg/krCK7DDwyz";
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow();
        object? Invoke(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Control<T>(string name) => (T)window.FindName(name);
        var text = (PawsPatchLauncher.Localization)typeof(MainWindow).GetField("_text", flags)!.GetValue(window)!;
        text.SetLanguage(language); Invoke("ApplyLanguage");
        var link = Control<Hyperlink>("ArcaneWarsDiscordLink");
        var credit = Control<Border>("ArcaneWarsCreditPanel");
        var error = Control<TextBlock>("HelpLinkErrorText");
        var checks = 0;
        void Check(bool ok, string reason) { checks++; if (!ok) throw new InvalidOperationException(reason); }
        async Task WaitUntil(Func<bool> completed)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!completed() && DateTime.UtcNow < deadline) await Task.Delay(15);
        }
        Task Open(Uri uri) => (Task)Invoke("OpenArcaneWarsDiscordAsync", uri)!;
        void Help(string key) => Invoke("HelpButton_Click", new Button { Tag = key }, new RoutedEventArgs());
        async Task Scenario()
        {
            Check(credit.Visibility == Visibility.Collapsed, "Credits visible before opening help");
            Help("modules.core");
            Check(credit.Visibility == Visibility.Visible, "Core help missing credits");
            Check(Control<Run>("ArcaneWarsAuthorLabel").Text == (language == "ru" ? "Автор Arcane Wars: " : "Arcane Wars author: "), "Wrong author attribution/locale");
            var author = (TextBlock)Control<Run>("ArcaneWarsAuthorLabel").Parent;
            Check(author.Inlines.OfType<Run>().Any(r => r.Text == "Darquan Mortis" && r.FontWeight == FontWeights.Bold), "Author not bold/exact");
            Check(link.NavigateUri.AbsoluteUri == invite && link.ToolTip.ToString() == invite, "Invite changed");
            Check(new TextRange(link.ContentStart, link.ContentEnd).Text.Trim() == "discord.gg/krCK7DDwyz", "Visible invite changed");
            Check(Control<TextBlock>("ArcaneWarsDistributionText").Text == (language == "ru" ? "Автор распространяет мод на этом Discord-сервере." : "The author distributes the mod on this Discord server."), "Wrong distribution text");
            Help("configuration");
            Check(credit.Visibility == Visibility.Collapsed, "Credits leaked into unrelated help");
            Help("modules.core");
            Check(credit.Visibility == Visibility.Visible, "Reopening core lost credits");
            var opened = new List<string>();
            Set("_openHelpLink", (Func<string, Task>)(url => { opened.Add(url); return Task.CompletedTask; }));
            var navigation = new RequestNavigateEventArgs(new Uri(invite), "") { RoutedEvent = Hyperlink.RequestNavigateEvent };
            link.RaiseEvent(navigation);
            Check(navigation.Handled && opened.Count == 0 && Control<Border>("ConfirmationOverlay").Visibility == Visibility.Visible,
                "Invite link bypassed browser confirmation");
            Control<Button>("ConfirmationDeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => opened.Count == 1 && link.IsEnabled);
            Check(navigation.Handled && opened.SequenceEqual(new[] { invite }), "Click not routed to exact invite");
            Check(link.IsEnabled, "Successful navigation disabled link");
            foreach (var uri in new[] { new Uri("https://discord.gg/other"), new Uri("file:///C:/test.exe"), new Uri("relative", UriKind.Relative) }) await Open(uri);
            Check(opened.Count == 1, "Unsafe/unrelated URI opened");
            Set("_openHelpLink", (Func<string, Task>)(_ => Task.FromException(new InvalidOperationException("Injected browser failure"))));
            var failedOpen = Open(new Uri(invite));
            Control<Button>("ConfirmationDeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await failedOpen;
            Check(error.Visibility == Visibility.Visible && error.Text.Contains(language == "ru" ? "Не удалось открыть браузер" : "Could not open the browser"), "No visible localized browser error inside help");
            Check(Control<Border>("ToastPanel").Visibility == Visibility.Visible && Control<TextBlock>("ToastText").Text == error.Text,
                "Browser failure was left inside the closed help dialog");
            Check(link.IsEnabled, "Browser error disabled retry");
            var pending = new TaskCompletionSource();
            var attempts = 0;
            Set("_openHelpLink", (Func<string, Task>)(_ => { attempts++; return pending.Task; }));
            var first = Open(new Uri(invite));
            Control<Button>("ConfirmationDeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => attempts == 1);
            Check(!link.IsEnabled && error.Visibility == Visibility.Collapsed, "Retry not busy/error not cleared");
            await Open(new Uri(invite));
            Check(attempts == 1, "Repeated click opened duplicate browser request");
            pending.SetResult(); await first;
            Check(link.IsEnabled, "Pending navigation did not re-enable link");
            foreach (var size in new[] { new Size(1050, 680), new Size(1440, 900) })
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                var card = (FrameworkElement)credit.Parent;
                var bounds = card.TransformToAncestor(content).TransformBounds(new Rect(card.RenderSize));
                Check(bounds.Left >= 0 && bounds.Right <= size.Width && bounds.Top >= 0 && bounds.Bottom <= size.Height, "Help card clipped at " + size);
            }
            Console.WriteLine($"HELP CREDIT PASS {checks} {language}: exact attribution/link, isolated help, navigation/error/retry guards, compact layout; browser not opened");
        }
        try
        {
            var task = window.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            var deadline = DateTime.UtcNow.AddSeconds(10);
            timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
            if (!task.IsCompleted) throw new TimeoutException("Help credit checks timed out");
            task.GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    }
}
