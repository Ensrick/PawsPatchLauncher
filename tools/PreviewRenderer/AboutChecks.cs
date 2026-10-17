using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class AboutChecks
{
    internal static void Layout(MainWindow window, FrameworkElement content)
    {
        T Named<T>(string name) => (T)window.FindName(name);
        void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        var nav = Named<Button>("AboutNav");
        var settings = Named<Button>("SettingsNav");
        var footer = Named<TextBlock>("LauncherVersionLabel");
        Require(nav.Parent == settings.Parent
            && ((Panel)nav.Parent).Children.OfType<FrameworkElement>().Last(c => c.Visibility == Visibility.Visible) == nav,
            "The guide must be the last navigation entry, after Settings.");
        Require(nav.TranslatePoint(new Point(), content).Y >= settings.TranslatePoint(new Point(0, settings.ActualHeight), content).Y + 7
            && nav.TranslatePoint(new Point(0, nav.ActualHeight), content).Y < footer.TranslatePoint(new Point(), content).Y - 10,
            "About navigation overlaps Settings or the launcher footer.");
        if (Named<StackPanel>("AboutPatchPanel").Visibility != Visibility.Visible) return;
        var tabs = Named<WrapPanel>("AboutTabsPanel");
        if (tabs.Visibility == Visibility.Visible)
            Require(tabs.Children.Count == 3 && tabs.Children.OfType<Button>().Count() == 3, "Patch guide must use three history-style buttons, not setting radios.");
        foreach (var tab in tabs.Children.OfType<Button>().Where(_ => tabs.Visibility == Visibility.Visible))
        {
            var position = tab.TranslatePoint(new Point(), tabs);
            Require(position.X >= 0 && position.X + tab.ActualWidth <= tabs.ActualWidth + 1
                && position.Y + tab.ActualHeight <= tabs.ActualHeight + 1, "About category tabs overflow instead of wrapping.");
        }
        var intro = Named<TextBlock>("AboutTitleText");
        Require(intro.FontSize == 18 && intro.FontWeight == FontWeights.Bold, "About page title lost standard typography.");
        foreach (var card in Named<StackPanel>("AboutEntriesPanel").Children.OfType<Border>())
        {
            var text = ((StackPanel)card.Child).Children.OfType<TextBlock>().ToArray();
            Require(text.Length is 2 or 3 && (text.Length != 3 || text[0].FontSize == 11) && text[^2].FontSize == 18
                && text[^2].FontWeight == FontWeights.Bold && text[^1].FontSize == 13 && text[^1].LineHeight == 19,
                "About feature typography differs from the shared styles.");
            Require(text[^2].TextWrapping == TextWrapping.Wrap && text[^1].TextWrapping == TextWrapping.Wrap
                && text.All(t => t.ActualWidth <= card.ActualWidth), "About text is clipped horizontally.");
        }
        Console.WriteLine("ABOUT LAYOUT PASS: final navigation, footer clearance, wrapped tabs and consistent feature typography");
    }

    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("About checks require --smoke-test.");
        var window = new MainWindow { Left = -32000, Top = -32000, Width = 1050, Height = 680,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        T Named<T>(string name) => (T)window.FindName(name);
        Task Switch(string category) => (Task)Invoke("SwitchAboutCategoryAsync", category)!;
        var entries = Named<StackPanel>("AboutEntriesPanel");
        var scroll = Named<ScrollViewer>("MainOptionsScroll");
        string FirstId() => entries.Children[0] is Border card ? (string)card.Tag : "empty-beta";
        var checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks++; }
        async Task Scenario()
        {
            // Never borrow a persisted smoke fixture's older guide.
            var fixture = new ChannelManifest { Channel = "beta" };
            typeof(MainWindow).GetField("_channel", flags)!.SetValue(window, fixture);
            typeof(MainWindow).GetField("_latestChannel", flags)!.SetValue(window, fixture);
            Field<UserSettings>("_settings").Channel = "beta";
            Field<UserSettings>("_settings").PinnedRelease = null;
            var localization = Field<PawsPatchLauncher.Localization>("_text");
            localization.SetLanguage(language); Invoke("ApplyLanguage");
            var settingsBefore = JsonSerializer.Serialize(Field<UserSettings>("_settings"));
            Named<Button>("AboutNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(230); window.UpdateLayout();
            Check(Field<string>("_activePage") == "about" && Named<StackPanel>("AboutPatchPanel").Visibility == Visibility.Visible, "Guide navigation does not open the guide.");
            foreach (var name in new[] { "SettingsPanel", "GameInfoCard", "ConfigurationCodeCard", "DiagnosticsCard", "ColorsModuleCard", "RemovalCard" })
                Check(Named<FrameworkElement>(name).Visibility == Visibility.Collapsed, "Unrelated block remains visible in About: " + name);
            Layout(window, (FrameworkElement)window.Content);
            checks += CheckGuideIntroductions(window, localization.Language);
            Check(JsonSerializer.Serialize(Field<UserSettings>("_settings")) == settingsBefore, "Reading the guide changed settings.");
            Console.WriteLine($"ABOUT PASS {checks} {language}: guide navigation, merged bilingual introductions for Vanilla and Immortals, latest/pinned documentation, read-only navigation");
        }
        try
        {
            window.Show();
            var task = window.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            if (!task.IsCompleted) throw new TimeoutException("About checks did not complete.");
            task.GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    }

    private static int CheckGuideIntroductions(MainWindow window, string language)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Named<T>(string name) => (T)window.FindName(name);
        var settings = Field<UserSettings>("_settings");
        var savedChannel = Field<ChannelManifest?>("_channel");
        var savedLatest = Field<ChannelManifest?>("_latestChannel");
        var savedSubject = Field<string>("_guideSubject");
        var savedVariant = Field<string>("_guideVariant");
        var savedPinned = settings.PinnedRelease;
        var entries = Named<StackPanel>("AboutEntriesPanel");
        var introText = Named<TextBlock>("AboutIntroText");
        var checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks++; }
        try
        {
            Named<Button>("GuideGeneralTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(entries.Children.Count == 2 && ((Border)entries.Children[0]).Tag is "language"
                && introText.Text.Contains(language == "ru" ? "Применить настройки" : "Apply settings"),
                "The general introduction still has a separate repeated mode-selection card.");
            foreach (var (mod, tab) in new[] { (GameMod.Vanilla, "GuideVanillaTab"), (GameMod.Immortals, "GuideImmortalsTab") })
            {
                var document = GuideCatalog.Resolve(null, mod)!;
                Check(GuideCatalog.IsValid(document), "Invalid embedded bilingual guide: " + mod);
                var introduction = GuideCatalog.IntroductionSection(document)!;
                Named<Button>(tab).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Check(entries.Children.Count == document.Sections.Count - 1
                    && entries.Children.OfType<Border>().All(card => (string)card.Tag != introduction.Id),
                    "The mod introduction was duplicated or an unrelated feature disappeared: " + mod);
                Check(introText.Text.Contains(document.Description.Get(language)) && introText.Text.Contains(introduction.Body.Get(language))
                    && introText.Text.Contains(document.Version) && introText.Text.Contains(document.Author),
                    "Merged mod introduction lost text or attribution: " + mod);
                var remaining = entries.Children.OfType<Border>().Select(card => ((StackPanel)card.Child).Children.OfType<TextBlock>().Last().Text).ToArray();
                Check(remaining.SequenceEqual(document.Sections.Skip(1).Select(section => section.Body.Get(language))),
                    "Merging the introduction changed the ordered feature descriptions: " + mod);
                Layout(window, (FrameworkElement)window.Content);
            }
            var installed = savedChannel!;
            var latest = new ChannelManifest { Channel = settings.Channel,
                Packages = [new PackageRelease { Id = "offline-guide-fixture", Version = "2.0", Size = 10, Sha256 = new string('B', 64) }],
                ModGuides = [new ModGuideDocument
            {
                Id = GameMod.Immortals, Version = "same-version", Author = "MartialDoctor",
                Description = new LocalizedText { Ru = "Новая справка", En = "New guide" },
                Sections = [new ModGuideSection { Id = "recovery", Title = new LocalizedText { Ru = "Восстановление", En = "Recovery" },
                    Body = new LocalizedText { Ru = "Независимая механика: 2.1", En = "Unrelated mechanic: 2.1" } }]
            }] };
            Set("_latestChannel", latest);
            Invoke("RefreshAboutFeed");
            Check(ReferenceEquals(Invoke("GuideChannel"), latest) && introText.Text.Contains(latest.ModGuides[0].Description.Get(language)),
                "An installed offline mod hid newer unpinned documentation.");
            Check(entries.Children.Count == 1 && ((Border)entries.Children[0]).Tag is "recovery"
                && !introText.Text.Contains(latest.ModGuides[0].Sections[0].Body.Get(language)),
                "A remote guide's unrelated first mechanic was merged into its introduction.");
            latest.ModGuides[0].Description = new LocalizedText { Ru = "Исправленная справка", En = "Corrected guide" };
            Invoke("RefreshAboutFeed");
            Check(introText.Text.Contains(latest.ModGuides[0].Description.Get(language)),
                "Same-version documentation corrections were not refreshed.");
            settings.PinnedRelease = ChannelFingerprint.Create(installed);
            Check(settings.PinnedRelease != ChannelFingerprint.Create(latest), "The newer-release fixture must have different installable content.");
            Check(ReferenceEquals(Invoke("GuideChannel"), installed), "Latest documentation replaced an explicitly pinned release.");
            var key = settings.Channel + ":" + settings.PinnedRelease;
            var cached = Field<Dictionary<string, ChannelManifest?>>("_cachedGuideChannels");
            var hadCached = cached.TryGetValue(key, out var savedCached);
            cached[key] = installed;
            try
            {
                Set("_channel", latest);
                Invoke("RefreshAboutFeed");
                Check(ReferenceEquals(Invoke("GuideChannel"), installed) && ReferenceEquals(cached[key], installed),
                    "A mismatched installed release replaced the pinned guide archive.");
            }
            finally { if (hadCached) cached[key] = savedCached; else cached.Remove(key); }
            return checks;
        }
        finally
        {
            settings.PinnedRelease = savedPinned;
            Set("_channel", savedChannel); Set("_latestChannel", savedLatest);
            Set("_guideSubject", savedSubject); Set("_guideVariant", savedVariant);
            Invoke("RefreshAboutPage");
        }
    }
}
