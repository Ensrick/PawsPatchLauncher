using PawsPatchLauncher;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PreviewRenderer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { TestProcessErrorMode.Enable(); Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Run(string[] args)
    {
        if (args.Contains("--icon-checks")) { IconChecks.Run(args[0]); return; }
        var language = args.FirstOrDefault(arg => arg.StartsWith("--language="))?.Split('=')[1] ?? "ru";
        var communityChecks = args.Contains("--community-checks");
        var pureOptionsConfig = args.FirstOrDefault(a=>a.StartsWith("--pure-options-config="))?["--pure-options-config=".Length..];
        var compactConfig = args.FirstOrDefault(arg => arg.StartsWith("--compact-config="))?["--compact-config=".Length..];
        var compactGame = args.FirstOrDefault(arg => arg.StartsWith("--compact-game="))?["--compact-game=".Length..];
        var polishConfig = args.FirstOrDefault(arg => arg.StartsWith("--polish-config="))?["--polish-config=".Length..];
        var messageCopyChecks = args.Contains("--message-copy-checks");
        var messageSelectionChecks = args.Contains("--message-selection-checks");
        var popupScrollChecks = args.Contains("--popup-scroll-checks");
        var mediaProbe = args.FirstOrDefault(a=>a.StartsWith("--media-probe=",StringComparison.Ordinal))?["--media-probe=".Length..];
        var messageMediaChecks = args.Contains("--message-media-checks");
        var mediaLayoutChecks = args.Contains("--media-layout-checks");
        var startupChecks = args.Contains("--startup-checks");
        var startupPreview = args.Contains("--startup-preview");
        var updateExperienceChecks = args.Contains("--update-experience-checks");
        var modModeChecks = args.Contains("--mod-mode-checks");
        var refinementChecks = args.Contains("--refinement-checks");
        var teamChecks = args.Contains("--team-checks");
        var patchVersionChecks = args.Contains("--patch-version-checks");
        var modLibraryChecks = args.Contains("--mod-library-checks");
        var headlessChecks = args.Contains("--headless-checks");
        var legacyModMigrationChecks = args.Contains("--legacy-mod-migration-checks");
        var retainedSelectionChecks = args.Contains("--retained-selection-checks");
        var gameExitChecks = args.Contains("--game-exit-checks");
        var uiRoundTwoChecks = args.Contains("--ui-round-two-checks");
        var modNoticeChecks = args.Contains("--mod-notice-checks");
        var modNoticePreview = args.Contains("--mod-notice-preview");
        var firstRunPreview = args.Contains("--first-run-preview");
        var modPreview = args.FirstOrDefault(a=>a.StartsWith("--mod="))?.Split('=')[1];
        var width = int.Parse(args.FirstOrDefault(arg => arg.StartsWith("--width="))?.Split('=')[1] ?? "1440");
        var height = int.Parse(args.FirstOrDefault(arg => arg.StartsWith("--height="))?.Split('=')[1] ?? "900");
        var statusPreview = args.FirstOrDefault(arg => arg.StartsWith("--status="))?.Split('=')[1];
        var enhancements = args.Contains("--enhancements");
        var motionChecks = args.Contains("--motion");
        var feedbackChecks = args.Contains("--feedback");
        var friendsPolishChecks = args.Contains("--friends-polish-checks");
        var layoutRefreshChecks = args.Contains("--layout-refresh-checks");
        var chatActivityChecks = args.Contains("--chat-activity-checks");
        var updateRefreshChecks = args.Contains("--update-refresh-checks");
        var arrivalPolishChecks = args.Contains("--arrival-polish-checks");
        var chatArrivalChecks = args.Contains("--chat-arrival-checks");
        var appearanceChecks = args.Contains("--appearance");
        var changelogChecks = args.Contains("--changelog");
        var aboutChecks = args.Contains("--about-checks");
        var powersChecks = args.Contains("--powers-checks");
        var combinationAudit = args.Contains("--combination-audit");
        var launcherUpdateChecks = args.Contains("--launcher-update-checks");
        var componentSettingsChecks = args.Contains("--component-settings-checks");
        var gameSettingsChecks = args.Contains("--game-settings-checks");
        var customModsChecks = args.Contains("--custom-mods-checks");
        var helpCreditChecks = args.Contains("--help-credit-checks");
        var accountChecks = args.Contains("--account-checks");
        var socialChecks = args.Contains("--social-checks");
        var reliabilityChecks = args.Contains("--reliability-checks");
        var compactTransferChecks = args.Contains("--compact-transfer-checks");
        var languageReliabilityConfig = args.FirstOrDefault(a => a.StartsWith("--language-reliability-config="))?["--language-reliability-config=".Length..];
        var socialHubChecks = args.Contains("--social-hub-checks");
        var socialDemo = args.FirstOrDefault(a=>a.StartsWith("--social-demo="))?.Split('=')[1];
        var adminDemo=args.FirstOrDefault(a=>a.StartsWith("--admin-demo="))?.Split('=')[1];
        var adminChecks=args.Contains("--admin-checks");
        var progressMotionChecks=args.Contains("--progress-motion-checks");
        var historyResourceChecks=args.Contains("--history-resource-checks");
        var compatibilityChecks=args.Contains("--compatibility-checks");
        var compatibilityDemo=args.Contains("--compatibility-demo");
        var accountForm = args.FirstOrDefault(arg => arg.StartsWith("--account-form="))?.Split('=')[1];
        var accountDemo = args.FirstOrDefault(a => a.StartsWith("--account-demo=", StringComparison.Ordinal))?.Split('=', 2)[1];
        var powersPreview = args.Contains("--powers-preview");
        var aboutCategory = args.FirstOrDefault(arg => arg.StartsWith("--about-category="))?.Split('=')[1];
        var guideSubject = args.FirstOrDefault(arg => arg.StartsWith("--guide="))?.Split('=')[1];
        var guideVariant = args.FirstOrDefault(arg => arg.StartsWith("--guide-variant="))?.Split('=')[1];
        var guideFeed = args.FirstOrDefault(arg => arg.StartsWith("--guide-feed="))?["--guide-feed=".Length..];
        var guideChannel = args.FirstOrDefault(arg => arg.StartsWith("--guide-channel="))?["--guide-channel=".Length..] ?? "beta";
        var diagnosticsChecks = args.Contains("--diagnostics");
        var archivePreview = args.Contains("--archive-preview");
        var windowChecks = args.Contains("--window-checks");
        var storageConfirmationChecks = args.Contains("--storage-confirmation-checks");
        var patchChannelChecks = args.Contains("--patch-channel-checks");
        var placementChecks = args.Contains("--placement-checks");
        var typographyChecks = args.Contains("--typography-checks");
        var typographyPreview = args.Contains("--typography-preview");
        var launcherUpdatePreview = args.Contains("--launcher-update-preview");
        var confirmationPreview = args.FirstOrDefault(arg => arg.StartsWith("--confirmation="))?.Split('=')[1];
        var toastPreview = args.FirstOrDefault(arg => arg.StartsWith("--toast="))?.Split('=')[1];
        var helpPreview = args.Contains("--help-preview");
        var helpKey = args.FirstOrDefault(arg => arg.StartsWith("--help-key="))?.Split('=')[1] ?? "configuration";
        var focus = args.FirstOrDefault(arg => arg.StartsWith("--focus="))?.Split('=')[1];
        var evolutionChecks = args.Contains("--evolution-checks");
        var chatInteractionChecks = args.Contains("--chat-interaction-checks");
        var chatGlyphChecks = args.Contains("--chat-glyph-checks");
        var friendCopyChecks = args.Contains("--friend-copy-checks");
        var friendConfigurationCardChecks = args.Contains("--friend-configuration-card-checks");
        var peerOfferChecks = args.Contains("--peer-offer-checks");
        var friendsStartupChecks = args.Contains("--friends-startup-checks");
        var socialActionChecks = args.Contains("--social-action-checks");
        var chatRegressionChecks = args.Contains("--chat-regression-checks");
        var chatCacheChecks = args.Contains("--chat-cache-checks");
        var chatRowsChecks = args.Contains("--chat-rows-checks");
        var connectionChecks = args.Contains("--connection-checks");
        var connectionPreview = args.Contains("--connection-preview");
        var launcher074Checks = args.Contains("--launcher-074-checks");
        var gameActivityChecks = args.Contains("--game-activity-checks");
        var launcher080Checks = args.Contains("--launcher-080-checks");
        var gameParticipantChecks = args.Contains("--game-participant-checks");
        var avatarPreviewChecks = args.Contains("--avatar-preview-checks");
        var avatarPreviewDemo = args.Contains("--avatar-preview-demo");
        var modePerf = args.FirstOrDefault(a=>a.StartsWith("--mode-perf="))?["--mode-perf=".Length..];
        var modeGame = args.FirstOrDefault(a=>a.StartsWith("--mode-game="))?["--mode-game=".Length..];
        args = args.Where(arg => !arg.StartsWith("--")).ToArray();
        if (args.Length is < 1 or > 3) throw new ArgumentException("Pass the output PNG path, an optional page name, and an optional vertical offset.");
        var app = new App();
        app.InitializeComponent();
        // Avoid a second real MainWindow during deferred Application startup. Use an inert invisible fixture.
        app.StartupUri = new Uri("pack://application:,,,/PreviewRenderer;component/PreviewBootstrap.xaml");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (communityChecks) { CommunityChecks.Run(language,Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (pureOptionsConfig is not null) { PureOptionsUiChecks.Run(pureOptionsConfig); app.Shutdown(); return; }
        if (friendConfigurationCardChecks) { FriendConfigurationCardChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (popupScrollChecks) { PopupScrollChecks.Run(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (mediaProbe is not null) { MediaLayoutChecks.Probe(new Uri(mediaProbe)); app.Shutdown(); return; }
        if (messageCopyChecks) { MessageCopyChecks.Run(language,Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (messageSelectionChecks) { MessageSelectionChecks.Run(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (messageMediaChecks) { SocialFinishChecks.Run(language); MediaLayoutChecks.Run(language); app.Shutdown(); return; }
        if (mediaLayoutChecks) { MediaLayoutChecks.Run(language); app.Shutdown(); return; }
        if (launcher080Checks) { Launcher080Checks.Run(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (launcher074Checks) { Launcher074Checks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (gameActivityChecks) { GameActivityChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (patchVersionChecks) { PatchVersionChecks.Run(language); app.Shutdown(); return; }
        if (gameParticipantChecks) { GameParticipantChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (avatarPreviewChecks || avatarPreviewDemo) { AvatarPreviewChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!, avatarPreviewDemo); app.Shutdown(); return; }
        if (updateExperienceChecks) { UpdateExperienceChecks.Run(language); app.Shutdown(); return; }
        if (retainedSelectionChecks) { RetainedSelectionChecks.Run(language, guideChannel, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (legacyModMigrationChecks) { LegacyModMigrationChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (connectionPreview) { ConnectionUiChecks.Preview(language); app.Run(); return; }
        if (connectionChecks) { ConnectionUiChecks.Run(language); ChatInteractionChecks.Run(language); ChatPresentationChecks.Run(language); app.Shutdown(); return; }
        if (chatCacheChecks) { ChatCacheChecks.Run(language); SocialRefreshChecks.Run(language); app.Shutdown(); return; }
        if (chatRowsChecks) { ChatRowsChecks.Run(language); app.Shutdown(); return; }
        if (chatGlyphChecks) { ChatInteractionChecks.Run(language); app.Shutdown(); return; }
        if (chatInteractionChecks) { ChatInteractionChecks.Run(language); FriendSettingsChecks.Run(language); app.Shutdown(); return; }
        if (friendCopyChecks) { FriendSettingsChecks.Run(language); app.Shutdown(); return; }
        if (peerOfferChecks) { OfferUiChecks.Run(language); app.Shutdown(); return; }
        if (friendsStartupChecks) { FriendsStartupChecks.Run(language); SocialRefreshChecks.Run(language); app.Shutdown(); return; }
        if (socialActionChecks) { SocialActionAvailabilityChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (chatRegressionChecks) { ChatPresentationChecks.Run(language); SocialChecks.Run(language); FriendsPolishChecks.Run(language); UpdateRefreshChecks.Run(language); app.Shutdown(); return; }
        if (modePerf is not null) { ModePerformanceChecks.Run(modePerf,modeGame!);app.Shutdown();return; }
        if (evolutionChecks) { LauncherEvolutionChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (startupPreview) { StartupChecks.RenderChecking(language, args[0]); app.Shutdown(); return; }
        if (compactConfig is not null) { CompactComponentsChecks.Run(compactConfig, compactGame!, language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (polishConfig is not null) { LauncherPolishChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!, polishConfig); app.Shutdown(); return; }
        if (startupChecks) { StartupChecks.Run(language, args[0]); app.Shutdown(); return; }
        if (adminChecks) { AdminChecks.Run(language); app.Shutdown(); return; }
        if (compactTransferChecks) { CompactTransferChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); StartupChecks.RenderChecking(language,args[0]); app.Shutdown(); return; }
        if (modNoticeChecks) ModNoticeChecks.Run(language);
        // Other fixtures exercise established Arcane Wars profiles, with onboarding already read.
        if (ActivityStore.IsSmokeTest && languageReliabilityConfig is null) new SettingsStore().Save(new UserSettings { ModNoticeSeen = true });
        if (uiRoundTwoChecks)
        {
            UiRoundTwoChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
            app.Shutdown(); return;
        }
        if (headlessChecks) { HeadlessChecks.Run(language); app.Shutdown(); return; }
        if (gameExitChecks || modLibraryChecks)
        {
            if (gameExitChecks) GameExitRecoveryChecks.Run(language);
            if (modLibraryChecks) ModLibraryChecks.Run(language);
            app.Shutdown(); return;
        }
        if (modModeChecks) ModModeChecks.Run(language);
        if (refinementChecks) RefinementChecks.Run(language);
        if (teamChecks) TeamAccessChecks.Run(language);
        if (motionChecks)
        {
            var motionFixture = new MainWindow();
            try { MotionChecks.Run(motionFixture); }
            finally { motionFixture.Close(); }
        }
        if (feedbackChecks) FeedbackChecks.Run(language);
        if (friendsPolishChecks) FriendsPolishChecks.Run(language);
        if (layoutRefreshChecks) LayoutRefreshChecks.Run(language);
        if (chatActivityChecks) ChatActivityChecks.Run(language,args[0]);
        if(progressMotionChecks)ProgressMotionChecks.Run(language);
        if(historyResourceChecks)HistoryResourceChecks.Run(language);
        if(compatibilityChecks)CompatibilityChecks.Run(language);
        if (updateRefreshChecks) UpdateRefreshChecks.Run(language);
        if (arrivalPolishChecks) ArrivalPolishChecks.Run(language);
        if (chatArrivalChecks) { ArrivalPolishChecks.Run(language, true); app.Shutdown(); return; }
        if (motionChecks) SmoothExperienceChecks.Run(language);
        if (appearanceChecks) AppearanceChecks.Run(language, args[0]);
        if (changelogChecks) ChangelogChecks.Run(language);
        if (aboutChecks) AboutChecks.Run(language);
        if (powersChecks) PowersUiChecks.Run(language);
        if (combinationAudit) CombinationUiAudit.Run();
        if (launcherUpdateChecks) LauncherUpdateChecks.Run();
        if (componentSettingsChecks) ComponentSettingsChecks.Run(language);
        if (gameSettingsChecks) { GameSettingsChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (customModsChecks) { CustomModsChecks.Run(language, Path.GetDirectoryName(Path.GetFullPath(args[0]))!); app.Shutdown(); return; }
        if (helpCreditChecks) HelpCreditChecks.Run(language);
        if (accountChecks) AccountChecks.Run(language);
        if (languageReliabilityConfig is not null) { LanguageReliabilityChecks.Run(languageReliabilityConfig, language); app.Shutdown(); return; }
        if (reliabilityChecks) { ChatDividerChecks.Run(language, args[0]); ChatPresentationChecks.Run(language); app.Shutdown(); return; }
        if (socialChecks) { SocialChecks.Run(language); SocialRefreshChecks.Run(language); SessionMenuChecks.Run(language,args[0]); ChatPresentationChecks.Run(language); FriendSettingsChecks.Run(language); OfferUiChecks.Run(language); IdentityMediaUiChecks.Run(language); SocialRefinementChecks.Run(language); SocialFinishChecks.Run(language); MediaLayoutChecks.Run(language); }
        if (socialHubChecks) SocialHubChecks.Run(language);
        if (diagnosticsChecks) DiagnosticsUiChecks.Run(language);
        if (windowChecks) WindowExperienceChecks.Run(language);
        if (storageConfirmationChecks) StorageConfirmationChecks.Run(language);
        if (patchChannelChecks) PerModChannelChecks.Run(language);
        if (placementChecks) PlacementChecks.Run();
        if (typographyChecks) TypographyChecks.Run(language);
        // Create the rendered window after other fixtures: unshown WPF radio groups share a root.
        var window = new MainWindow();
        if (modNoticePreview || firstRunPreview)
        {
            var firstRun = (UserSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            firstRun.Mod = GameMod.Vanilla;
            firstRun.ModNoticeSeen = false;
        }
        if(modPreview is not null)
        {
            var selection=(UserSettings)typeof(MainWindow).GetField("_settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            selection.Mod=modPreview=="vanilla"?GameMod.Vanilla:modPreview=="immortals"?GameMod.Immortals:GameMod.ArcaneWars;
            selection.PawPatchEnabled=modPreview!="pure-aw";
        }
        if (guideFeed is not null)
        {
            var config = SettingsStore.LoadConfiguration();
            if (guideChannel == "stable") config.FeedUrls = [Path.GetFullPath(guideFeed)];
            else if (guideChannel == "beta") config.BetaFeedUrls = [Path.GetFullPath(guideFeed)];
            else throw new ArgumentException("Guide preview channel must be stable or beta.");
            config.CacheRoot = Path.Combine(ActivityStore.Root, "signed-guide-preview");
            var selected = Task.Run(() => new FeedClient(config).GetChannelAsync(guideChannel)).GetAwaiter().GetResult();
            typeof(MainWindow).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, selected);
            ((UserSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Channel = guideChannel;
        }
        if (archivePreview)
        {
            var archive = new DiagnosticArchiveHistory(ActivityStore.Root).RecordCompleted(DiagnosticsUiChecks.CreateFixture());
            typeof(MainWindow).GetField("_lastDiagnosticArchive", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, archive);
            typeof(MainWindow).GetField("_diagnosticArchiveExists", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        }
        if (window.Width > SystemParameters.WorkArea.Width || window.Height > SystemParameters.WorkArea.Height)
            throw new InvalidOperationException("Initial window exceeds the screen work area.");
        window.Width = width; window.Height = height;
        var localization = (PawsPatchLauncher.Localization)typeof(MainWindow).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        localization.SetLanguage(language);
        Invoke(window, "ApplyLanguage");
        CheckChannelPresentation(window, language);
        CheckTransferLifecycle(window);
        if (launcherUpdatePreview)
        {
            var updateButton = (Button)window.FindName("LauncherUpdateButton");
            updateButton.Visibility = Visibility.Visible;
            updateButton.Content = string.Format(localization["button.launcherupdate"], "99.99.99");
        }
        if (enhancements) PopulateEnhancementsPreview(window);
        if (powersPreview) PowersUiChecks.Populate(window);
        if (args.Length >= 2)
            typeof(MainWindow).GetMethod("SetActivePage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [args[1]]);
        if (guideSubject is not null || aboutCategory is not null)
        {
            typeof(MainWindow).GetField("_guideSubject", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, guideSubject ?? GameMod.ArcaneWars);
            typeof(MainWindow).GetField("_guideVariant", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, guideVariant ?? (aboutCategory is null ? "mod" : "patch"));
            Invoke(window, "RefreshAboutPage");
        }
        if (aboutCategory is not null) ((Task)Invoke(window, "SwitchAboutCategoryAsync", aboutCategory)!).GetAwaiter().GetResult();
        if (accountForm is not null) Invoke(window, "ShowAccountForm", accountForm == "register");
        if (accountDemo is not null) AccountChecks.Populate(window, accountDemo);
        if (socialDemo=="search")
        {
            SocialHubChecks.Populate(window);Invoke(window,"ResetBroadcast");

            ((TextBox)window.FindName("FriendsSearchInput")).Text="friend";
            Invoke(window,"FriendsShowAdd_Click",window.FindName("FriendsShowAddButton"),new RoutedEventArgs());
        }
        else if (socialDemo is "broadcast" or "broadcast-selected")
        {
            SocialHubChecks.Populate(window);
            if(socialDemo=="broadcast-selected")
                foreach(var recipient in ((StackPanel)window.FindName("BroadcastRows")).Children.OfType<CheckBox>().Where(c=>c.IsEnabled).Take(2))
                    recipient.IsChecked=true;
        }
        else if (socialDemo == "history") HistoryResourceChecks.PopulateHistory(window);
        else if (socialDemo == "media") MediaLayoutChecks.Populate(window);
        else if (socialDemo == "offers") OfferUiChecks.Populate(window);
        else if (socialDemo == "copy") OfferUiChecks.PopulateConfirmation(window);
        else if (socialDemo is "offer-confirm" or "offer-match") SocialRefinementChecks.PopulateConfirmation(window,socialDemo=="offer-match");
        else if (socialDemo == "add") { SocialChecks.Populate(window, "chat"); Invoke(window, "OpenFriendsDialog", "add"); }
        else if (socialDemo is not null) SocialChecks.Populate(window, socialDemo);
        if(adminDemo is "monitor-resources" or "monitor-status")MonitorChecks.Populate(window,adminDemo=="monitor-resources"?"resources":"status");
        else if(adminDemo=="resources")HistoryResourceChecks.PopulateResources(window);
        else if(adminDemo is not null)AdminChecks.Populate(window,adminDemo);
        if(compatibilityDemo)CompatibilityChecks.Populate(window);
        if (accountForm == "confirmation") { Invoke(window,"ShowAccountForm",true); ((TextBox)window.FindName("AccountEmailInput")).Text="player@example.test"; Invoke(window,"BeginEmailConfirmation"); Invoke(window,"RenderAccount"); }
        if (accountDemo == "avatar")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
            File.WriteAllBytes(Path.ChangeExtension(args[0], ".jpg"), AccountChecks.AvatarFixture());
        }
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        CheckBrandAndFonts(window, content);
        CheckCaptionLayout(window, content);
        if (args.Length >= 2)
        {
            typeof(MainWindow).GetMethod("SetActivePage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [args[1]]);
            content.UpdateLayout();
        }
        if (typographyPreview)
        {
            TypographyChecks.PopulatePreview(window, language);
            content.UpdateLayout();
        }
        if (statusPreview is not null)
        {
            PopulateStatusPreview(window, statusPreview, language);
            content.UpdateLayout();
            CheckStatusLayout(window, content);
        }
        if (args.Length == 3 && double.TryParse(args[2], out var offset))
        {
            var scroll = (ScrollViewer)window.FindName(((Grid)window.FindName("AdminPanel")).Visibility == Visibility.Visible ? "AdminContentScroll" : "MainOptionsScroll");
            scroll.ScrollToVerticalOffset(offset);
            content.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            content.UpdateLayout();
        }
        if (focus is not null)
        {
            var target = (FrameworkElement)window.FindName(focus);
            var scroll = (ScrollViewer)window.FindName("MainOptionsScroll");
            scroll.ScrollToVerticalOffset(target.TranslatePoint(new Point(0, 0), (FrameworkElement)scroll.Content).Y);
            content.UpdateLayout(); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); content.UpdateLayout();
            if (target.Visibility != Visibility.Visible || target.ActualHeight < 30) throw new InvalidOperationException("Focused feature is not visible.");
            Console.WriteLine("FOCUS PASS " + focus);
        }
        CheckOptionsLayout(window, content, args.Length >= 2 ? args[1] : "home");
        if(socialDemo=="history")
        {
            var chat=(ScrollViewer)window.FindName("FriendsChatScroll");chat.ScrollToVerticalOffset(600);
            content.UpdateLayout();window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);content.UpdateLayout();
            Invoke(window,"RefreshHistoryJump");
        }
        TypographyChecks.Layout(window, content, args.Length >= 2 ? args[1] : "home");
        AboutChecks.Layout(window, content);
        if (args.Length >= 2 && args[1] == "settings") CheckStorageLabelGap(window);
        if (args.Length >= 2 && args[1] == "modules") CheckMultiplayerNoteAlignment(window);
        CheckScrollbars(window, content);
        foreach (var name in new[] { "HomeNav", "ModulesNav", "FriendsNav", "SettingsNav", "AboutNav", "AboutModsNav" })
        {
            var button = (Button)window.FindName(name);
            if (button.Visibility != Visibility.Visible) continue;
            var label = new TextBlock { Text = button.Content.ToString(), FontFamily = button.FontFamily, FontSize = button.FontSize, FontWeight = button.FontWeight };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var available = button.ActualWidth - button.Padding.Left - button.Padding.Right - button.BorderThickness.Left - button.BorderThickness.Right;
            var iconWidth = LauncherIcon.GetKind(button) == IconKind.None ? 0 : 27;
            var measuredContentWidth = button.Content is FrameworkElement visual ? visual.DesiredSize.Width : label.DesiredSize.Width;
            if (measuredContentWidth + iconWidth > available) throw new InvalidOperationException(name + " icon/label is clipped.");
        }
        Console.WriteLine($"LAYOUT PASS {language} {width}x{height}: all navigation labels fit");
        if (args.Length >= 2 && args[1] == "settings")
        {
            foreach (var name in new[] { "RemoveLauncherButton" })
            {
                var button = (Button)window.FindName(name);
                if (button.Visibility != Visibility.Visible || button.ActualHeight < 30 || button.Content.ToString()!.Length < 5)
                    throw new InvalidOperationException(name + " is missing from settings.");
            }
            if (((Button)window.FindName("RemovePatchButton")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Standalone patch removal must not remain in Settings.");
            Console.WriteLine("LAYOUT PASS settings: launcher removal present, standalone patch removal hidden");
        }
        if (toastPreview is not null)
        {
            if(toastPreview=="stack")
            {
                Invoke(window,"ShowToast",(Func<string>)(()=>language=="ru"?"Заявка отправлена.":"Request sent."),false);
                Invoke(window,"ShowToast",(Func<string>)(()=>language=="ru"?"Конфигурация отправлена.":"Configuration sent."),false);
            }
            Invoke(window, "ShowToast", (Func<string>)(() => toastPreview == "error"
                ? language == "ru" ? "Буфер обмена занят. Подождите немного и повторите действие." : "The clipboard is busy. Wait a moment and try again."
                : toastPreview == "avatar" ? language == "ru" ? "Аватарка успешно изменена." : "Avatar updated successfully."
                : language == "ru" ? "Код конфигурации скопирован" : "Configuration code copied"), toastPreview == "error");
            var toast = (Border)window.FindName("ToastPanel"); toast.BeginAnimation(UIElement.OpacityProperty, null); toast.Opacity = 1;
            // Static fixture: show the resting position and mid-life fill, not the first entry frame.
            var toastSlide=(TranslateTransform)window.FindName("ToastSlide");toastSlide.BeginAnimation(TranslateTransform.YProperty,null);toastSlide.Y=0;
            var toastProgress=(ScaleTransform)window.FindName("ToastProgressScale");toastProgress.BeginAnimation(ScaleTransform.ScaleXProperty,null);toastProgress.ScaleX=.45;
            foreach(var panel in ((StackPanel)window.FindName("ToastStack")).Children.OfType<Border>())
            { panel.BeginAnimation(UIElement.OpacityProperty,null);panel.Opacity=1;
              if(panel.RenderTransform is TranslateTransform slide) {slide.BeginAnimation(TranslateTransform.YProperty,null);slide.Y=0;} }
        }
        if (helpPreview)
        {
            Invoke(window, "HelpButton_Click", new Button { Tag = helpKey }, new RoutedEventArgs());
            var help = (Border)window.FindName("HelpOverlay"); help.BeginAnimation(UIElement.OpacityProperty, null); help.Opacity = 1;
        }
        if (confirmationPreview is not null)
        {
            var confirmationPath = confirmationPreview == "launcher"
                ? @"C:\Users\Игрок\Documents\Codex\2026-08-11\kohan-ii-d-steamlibrary-steamapps-common\work\PawsPatchLauncher\release_workspace_056\window-fix\win-x64\PawsPatchLauncher.exe"
                : @"D:\SteamLibrary\steamapps\common\Kohan II";
            if (confirmationPreview.StartsWith("storage"))
                Invoke(window, "ConfirmStorageCleanupAsync", StorageConfirmationChecks.PreviewPlan,
                    confirmationPreview != "storage-backups", confirmationPreview != "storage-cache");
            else if (confirmationPreview == "launcher") Invoke(window, "ConfirmLauncherRemovalAsync", confirmationPath);
            else Invoke(window, "ConfirmRemovalAsync", false, confirmationPath);
            var overlay = (Border)window.FindName("ConfirmationOverlay"); overlay.BeginAnimation(UIElement.OpacityProperty, null); overlay.Opacity = 1;
            content.UpdateLayout();
            var card = (Border)window.FindName("ConfirmationCard");
            var position = card.TranslatePoint(new Point(), content);
            if (position.X < 20 || position.Y < 20 || position.Y + card.ActualHeight > height - 20) throw new InvalidOperationException("Confirmation card exceeds the window.");
            Console.WriteLine("CONFIRMATION LAYOUT PASS " + confirmationPreview);
        }
        content.UpdateLayout();
        // Render settled visuals, not the first frame of checked/hover brush transitions.
        var animationFrame = new System.Windows.Threading.DispatcherFrame();
        var animationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        animationTimer.Tick += (_, _) => animationFrame.Continue = false;
        animationTimer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(animationFrame); }
        finally { animationTimer.Stop(); }
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        if (args.Length >= 2 && args[1] == "about" && ((WrapPanel)window.FindName("AboutTabsPanel")).Visibility == Visibility.Visible)
        {
            var selected = ((WrapPanel)window.FindName("AboutTabsPanel")).Children.OfType<Button>().Single(t => ((SolidColorBrush)t.Background).Color == (Color)ColorConverter.ConvertFromString("#5B451D"));
            if ((string)selected.Tag != (aboutCategory ?? "always")) throw new InvalidOperationException("Guide preview has a stale category selection.");
        }
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = File.Create(output);
        encoder.Save(stream);
        if (enhancements)
        {
            foreach (var kind in new[] { "Comparison", "FriendlyError" })
            {
                if (kind == "FriendlyError") Invoke(window, "SetFriendlyError", new System.Net.Http.HttpRequestException("Response status code does not indicate success: 404", null, System.Net.HttpStatusCode.NotFound), false);
                var dialog = (Window)Invoke(window, "Create" + kind + "Dialog")!;
                var view = (FrameworkElement)dialog.Content;
                var size = new Size(dialog.Width, double.IsNaN(dialog.Height) ? 490 : dialog.Height);
                view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                if (kind == "Comparison")
                {
                    var table = ((Grid)view).Children.OfType<DataGrid>().Single();
                    if (table.Columns.Any(x => x.ActualWidth < 80)) throw new InvalidOperationException("Comparison column collapsed.");
                    Console.WriteLine("COMPARISON COLUMNS " + string.Join(", ", table.Columns.Select(x => x.ActualWidth.ToString("F0"))));
                }
                var rendered = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(view);
                var saved = new PngBitmapEncoder(); saved.Frames.Add(BitmapFrame.Create(rendered));
                using var file = File.Create(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-" + kind + ".png"));
                saved.Save(file);
                Console.WriteLine("ENHANCEMENT WINDOW PASS " + kind + ": " + size);
            }
        }
        if (statusPreview is not null)
        {
            var panel = (Border)window.FindName("OperationStatusPanel");
            var card = (FrameworkElement)((FrameworkElement)panel.Parent).Parent;
            var origin = card.TranslatePoint(new Point(0, 0), content);
            var cardBitmap = new CroppedBitmap(bitmap, new Int32Rect((int)origin.X, (int)origin.Y, (int)card.ActualWidth, (int)card.ActualHeight));
            var cardEncoder = new PngBitmapEncoder();
            cardEncoder.Frames.Add(BitmapFrame.Create(cardBitmap));
            using var cardStream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-card.png"));
            cardEncoder.Save(cardStream);
        }
    }

    private static void CheckChannelPresentation(MainWindow window, string language)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var settings = (UserSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
        var expected = ChannelPresentation.Name(settings.Channel, language);
        var footer = (TextBlock)window.FindName("LauncherVersionText");
        if (!Version.TryParse(footer.Text, out _) || (string)Invoke(window, "CurrentChannelName")! != expected)
            throw new InvalidOperationException("Footer/current channel did not use the display name.");
        if (((TextBlock)window.FindName("PatchChannelDescriptionText")).Text.Contains("Stable"))
            throw new InvalidOperationException("Settings retained the old display label.");
        var comparison = typeof(MainWindow).GetField("_detailedComparison", flags)!;
        var previous = comparison.GetValue(window);
        try
        {
            comparison.SetValue(window, new MultiplayerComparison(false, [new("setting", "Channel", "STABLE", "BETA")]));
            var dialog = (Window)Invoke(window, "CreateComparisonDialog")!;
            var row = ((Grid)dialog.Content).Children.OfType<DataGrid>().Single().Items[0];
            if ((string)row.GetType().GetProperty("Local")!.GetValue(row)! != ChannelPresentation.Name("stable", language)
                || (string)row.GetType().GetProperty("Peer")!.GetValue(row)! != ChannelPresentation.Name("beta", language))
                throw new InvalidOperationException("Multiplayer comparison retained raw channel IDs.");
            dialog.Close();
        }
        finally { comparison.SetValue(window, previous); }
        Console.WriteLine("CHANNEL UI PASS: footer, current channel, settings, multiplayer comparison; internal IDs unchanged");
    }

    private static void CheckCaptionLayout(MainWindow window, FrameworkElement content)
    {
        var titleBar = (Grid)window.FindName("TitleBar");
        var actions = (StackPanel)window.FindName("TitleActions");
        var brand = titleBar.Children.OfType<StackPanel>().First(x => x != actions);
        var brandEnd = brand.TranslatePoint(new Point(0,0),content).X + brand.Children.OfType<FrameworkElement>().Sum(x=>x.DesiredSize.Width);
        var actionsStart = actions.TranslatePoint(new Point(0,0),content).X;
        if (actionsStart < brandEnd + 12 || actionsStart + actions.ActualWidth > content.ActualWidth + .1)
            throw new InvalidOperationException("Patch channel/update controls overlap the title brand or window edge.");
        foreach (var name in new[] { "ModReleaseRadio", "ModBetaRadio" })
        {
            var choice = (RadioButton)window.FindName(name);
            if (choice.Visibility != Visibility.Visible || choice.ActualHeight == 0) continue;
            if (choice.ActualHeight < 24 || choice.ActualWidth < 60) throw new InvalidOperationException("Patch channel choice is clipped.");
        }
        Console.WriteLine("CAPTION LAYOUT PASS: window actions and per-mod component channel choices fit");
    }

    private static void CheckOptionsLayout(MainWindow window, FrameworkElement content, string page)
    {
        var scroll = (ScrollViewer)window.FindName(((Grid)window.FindName("AdminPanel")).Visibility == Visibility.Visible ? "AdminContentScroll" : "MainOptionsScroll");
        var stack = (StackPanel)scroll.Content;
        var bar = (FrameworkElement)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        var rightEdge = bar.Visibility == Visibility.Visible
            ? bar.TranslatePoint(new Point(0, 0), content).X
            : scroll.TranslatePoint(new Point(scroll.ActualWidth, 0), content).X;
        int cards = 0;
        double minimumGap = double.PositiveInfinity;
        void CheckCards(StackPanel parent)
        {
            foreach (var item in parent.Children.OfType<FrameworkElement>())
            {
                if (item.Visibility != Visibility.Visible) continue;
                if (item is StackPanel nested) CheckCards(nested);
                if (item is not Border card || card.ActualWidth <= 0) continue;
                var gap = rightEdge - card.TranslatePoint(new Point(card.ActualWidth, 0), content).X;
                if (gap < 11.5) throw new InvalidOperationException($"{card.Name}: card crowds the options scrollbar ({gap:F1}px).");
                minimumGap = Math.Min(minimumGap, gap);
                cards++;
            }
        }
        CheckCards(stack);
        if (cards == 0) throw new InvalidOperationException("No visible options cards were checked.");
        var code = (Border)window.FindName("ConfigurationCodeCard");
        var import = (StackPanel)window.FindName("ConfigurationImportHost");
        var diagnostics = (Border)window.FindName("DiagnosticsCard");
        var game = (Border)window.FindName("GameInfoCard");
        var recovery = (StackPanel)window.FindName("RecoveryHost");
        var removal = (Border)window.FindName("RemovalCard");
        var visible = stack.Children.OfType<FrameworkElement>()
            .Where(x => x.Visibility == Visibility.Visible && x.ActualHeight > 0).ToArray();
        if (code.Visibility != Visibility.Collapsed || import.Visibility != Visibility.Collapsed
                 || import.Children[0].Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Manual configuration code and friend import must stay hidden.");
        if (page == "settings")
        {
            if (visible[0] != window.FindName("SettingsPanel"))
                throw new InvalidOperationException("Launcher settings must precede recovery, versions and storage.");
            if (visible[1] != window.FindName("PatchUpdatesCard"))
                throw new InvalidOperationException("Patch updates must follow launcher settings.");
            if (visible[^1] != removal || visible[^2] != recovery || recovery.Children.Count != 1
                || recovery.Children[0] != window.FindName("RecoveryCard") || recovery.Children[0].Visibility != Visibility.Visible)
                throw new InvalidOperationException("Settings must end with expanded Recovery followed by Uninstall.");
            if (diagnostics.Visibility != Visibility.Visible || diagnostics.Parent != game.Parent
                || Array.IndexOf(visible, diagnostics) + 1 != Array.IndexOf(visible, game))
                throw new InvalidOperationException("Diagnostics must be directly above the game folder in Settings.");
        }
        else if (diagnostics.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Diagnostics card must only appear in Settings.");
        if (page != "settings" && (recovery.Visibility != Visibility.Collapsed || removal.Visibility != Visibility.Collapsed))
            throw new InvalidOperationException("Recovery and uninstall must remain exclusive to Settings.");
        if (game.Visibility == Visibility.Visible)
        {
            var heading = (TextBlock)window.FindName("GamePathLabel");
            var text = (PawsPatchLauncher.Localization)typeof(MainWindow).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            if (heading.FontWeight != FontWeights.Bold || heading.FontSize != 18 || heading.Text != text["game.path"]
                || heading.Foreground is not SolidColorBrush color || color.Color != ((SolidColorBrush)window.FindResource("TextMainBrush")).Color)
                throw new InvalidOperationException("Game folder title must match the white bold card headings in sentence case.");
            Console.WriteLine("GAME FOLDER HEADING PASS: main text color, bold 18px, localized sentence case");
        }
        Console.WriteLine($"OPTIONS ORDER PASS {page}: manual codes removed; diagnostics before game folder");
        Console.WriteLine($"OPTIONS GAP PASS {page}: {cards} cards, minimum {minimumGap:F1}px, scrollbar={bar.Visibility}");
    }

    private static void CheckScrollbars(MainWindow window, FrameworkElement content)
    {
        void Pump()
        {
            content.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            content.UpdateLayout();
        }
        foreach (var name in new[] { "MainOptionsScroll", "AdminContentScroll", "NewsScrollViewer", "FriendsChatScroll" })
        {
            var scroll = (ScrollViewer)window.FindName(name);
            if (scroll.ActualWidth <= 0 || scroll.ActualHeight <= 0) continue;
            var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
            if (bar.Visibility != Visibility.Visible) continue;
            if (bar.Template.FindName("ScrollRail", bar) is not Border { CornerRadius.TopLeft: >= 4, ActualWidth: <= 14 }
                || bar.Template.FindName("PART_Track", bar) is not Track track
                || track.Thumb.ActualHeight < 23 || !track.IsDirectionReversed)
                throw new InvalidOperationException(name + " is missing the dark, rounded scrollbar.");
            var savedOffset = scroll.VerticalOffset;
            scroll.ScrollToTop(); Pump();
            if (!ScrollBar.PageDownCommand.CanExecute(null, bar)) throw new InvalidOperationException("Scrollbar paging is unavailable.");
            ScrollBar.PageDownCommand.Execute(null, bar); Pump();
            if (scroll.VerticalOffset <= 0) throw new InvalidOperationException("Scrollbar page click did not scroll down.");
            scroll.ScrollToTop(); Pump();
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 20) { RoutedEvent = Thumb.DragDeltaEvent }); Pump();
            if (scroll.VerticalOffset <= 0) throw new InvalidOperationException("Scrollbar thumb drag did not scroll down.");
            scroll.ScrollToBottom(); Pump();
            if (Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) > 1) throw new InvalidOperationException("Scroll end is unreachable.");
            scroll.ScrollToVerticalOffset(savedOffset); Pump();
            Console.WriteLine($"SCROLLBAR PASS {name}: dark rounded theme, page click, thumb drag, full range");
        }
        // The same application style also serves horizontal scrollbars in report tables.
        var horizontal = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Width = 800, Height = 30 }
        };
        void LayoutHorizontal()
        {
            horizontal.Measure(new Size(200, 80)); horizontal.Arrange(new Rect(0, 0, 200, 80)); horizontal.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            horizontal.UpdateLayout();
        }
        LayoutHorizontal();
        var horizontalBar = (ScrollBar)horizontal.Template.FindName("PART_HorizontalScrollBar", horizontal);
        if (horizontalBar.Visibility != Visibility.Visible
            || horizontalBar.Template.FindName("ScrollRail", horizontalBar) is not Border { ActualHeight: <= 14 }
            || horizontalBar.Template.FindName("PART_Track", horizontalBar) is not Track horizontalTrack
            || horizontalTrack.IsDirectionReversed || horizontalTrack.Thumb.ActualWidth < 23)
            throw new InvalidOperationException("Horizontal scrollbar theme/orientation is invalid.");
        ScrollBar.PageRightCommand.Execute(null, horizontalBar); LayoutHorizontal();
        if (horizontal.HorizontalOffset <= 0) throw new InvalidOperationException("Horizontal paging failed.");
        horizontal.ScrollToLeftEnd(); LayoutHorizontal();
        horizontalTrack.Thumb.RaiseEvent(new DragDeltaEventArgs(20, 0) { RoutedEvent = Thumb.DragDeltaEvent }); LayoutHorizontal();
        if (horizontal.HorizontalOffset <= 0) throw new InvalidOperationException("Horizontal dragging failed.");
        Console.WriteLine("SCROLLBAR PASS horizontal: theme, orientation, page click, thumb drag");
    }

    private static void PopulateStatusPreview(MainWindow window, string state, string language)
    {
        var ru = language == "ru";
        var news = (StackPanel)window.FindName("NewsEntriesPanel");
        news.Children.Clear();
        for (int i = 0; i < 12; i++)
        {
            news.Children.Add(new TextBlock { Text = ru ? "Цвета: исправление рассинхрона в лобби" : "Colors: lobby desync fix", Style = (Style)window.FindResource("CardSubtitle"), Margin = new Thickness(0, 0, 0, 10) });
            news.Children.Add(new TextBlock { Text = ru ? "Исправлена запись команды выбора цвета в контрольную сумму хоста. Удалён тёмно-розовый, осталось 49 цветов. Все участники должны обновить Beta. Живая проверка мультиплеера продолжается." : "The host now records color-command boundaries in its checksum. Dark pink was removed, leaving 49 colors. All peers must update Beta. Live multiplayer validation is ongoing.", Style = (Style)window.FindResource("CardDescription"), Margin = new Thickness(0, 0, 0, 22) });
        }
        var operation = (TextBlock)window.FindName("OperationText");
        operation.Text = state switch
        {
            "ready" => ru ? "Готово к игре · Бета" : "Ready to play · Beta",
            "download" => ru ? "Скачиваю: цвета игроков" : "Downloading: player colors",
            "error" => ru ? "Файл обновления не найден" : "Update file not found",
            _ => ru ? "Применяю выбранные настройки перед запуском…" : "Applying selected settings before launch…"
        };
        if (state == "error")
        {
            operation.Foreground = (Brush)window.FindResource("DangerBrush");
            Invoke(window, "SetFriendlyError", new System.Net.Http.HttpRequestException("Response status code does not indicate success: 404", null, System.Net.HttpStatusCode.NotFound), false);
        }
        else Invoke(window, "ClearFriendlyError");
        var progress = (ProgressBar)window.FindName("OperationProgress");
        progress.Visibility = state is "ready" or "error" ? Visibility.Collapsed : Visibility.Visible;
        progress.IsIndeterminate = false; progress.Value = 65;
        var details = (TextBlock)window.FindName("TransferText");
        details.Visibility = state == "download" ? Visibility.Visible : Visibility.Collapsed;
        details.Text = ru ? "42,2 МБ / 65,0 МБ\n8,1 МБ/с · осталось 00:03" : "42.2 MB / 65.0 MB\n8.1 MB/s · 00:03 remaining";
        var cancel = (Button)window.FindName("CancelDownloadButton");
        cancel.Visibility = state == "download" ? Visibility.Visible : Visibility.Collapsed;
        cancel.Content = ru ? "Приостановить" : "Pause";
    }

    private static void CheckStatusLayout(MainWindow window, FrameworkElement content)
    {
        var panel = (Border)window.FindName("OperationStatusPanel");
        var scroll = (ScrollViewer)window.FindName("NewsScrollViewer");
        var text = (TextBlock)window.FindName("OperationText");
        var panelTop = panel.TranslatePoint(new Point(0, 0), content).Y;
        var newsBottom = scroll.TranslatePoint(new Point(0, scroll.ActualHeight), content).Y;
        if (panelTop - newsBottom < 13.5 || !scroll.ClipToBounds || scroll.ActualHeight < 40)
            throw new InvalidOperationException("Status panel overlaps or crowds the news viewport.");
        foreach (var name in new[] { "OperationText", "OperationProgress", "TransferText", "CancelDownloadButton", "ErrorActionsPanel" })
        {
            var item = (FrameworkElement)window.FindName(name);
            if (item.Visibility != Visibility.Visible) continue;
            var bounds = item.TransformToAncestor(panel).TransformBounds(new Rect(item.RenderSize));
            if (bounds.Top < panel.Padding.Top || bounds.Bottom > panel.ActualHeight - panel.Padding.Bottom
                || bounds.Left < panel.Padding.Left || bounds.Right > panel.ActualWidth - panel.Padding.Right)
                throw new InvalidOperationException(name + " extends outside its status panel.");
        }
        if (text.FontSize < 13 || text.FontWeight != FontWeights.SemiBold || panel.Background is not SolidColorBrush { Opacity: 1 })
            throw new InvalidOperationException("Status readability styling is missing.");
        Console.WriteLine($"STATUS LAYOUT PASS: {panelTop - newsBottom:F1}px separation; history={scroll.ActualHeight:F0}px; panel={panel.ActualHeight:F0}px");
    }

    private static void PopulateEnhancementsPreview(MainWindow window)
    {
        void Field(string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
        Field("_storagePlan", new StoragePlan([
            new("downloads", "preview-cache", 1_250_000_000, false, "fixture"),
            new("packages", "preview-old-cache", 430_000_000, true, "fixture"),
            new("originals", "preview-originals", 180_000_000, false, "fixture"),
            new("backups", "preview-latest-backup", 220_000_000, false, "fixture"),
            new("backups", "preview-old-backup", 64_000_000, true, "fixture")]));
        Field("_detailedComparison", new MultiplayerComparison(false, [
            new("setting", "Roaming spawn", "SP4", "SP1"),
            new("module", "player-colors", "0.1.0-beta.6", "0.1.0-beta.5"),
            new("file", "paws_player_colors.ini", new string('A', 64), new string('B', 64)),
            new("file", "Data\\old_units.tgi", "NOT_LISTED", new string('C', 64)),
            new("file", "Data\\missing_units.tgi", new string('D', 64), "MISSING") ]));
        Invoke(window, "SetFriendlyError", new System.Net.Http.HttpRequestException("Response status code does not indicate success: 404", null, System.Net.HttpStatusCode.NotFound), false);
        Invoke(window, "RefreshEnhancements");
        foreach (var name in new[] { "ExportMultiplayerReportButton", "ImportMultiplayerReportButton", "ScanStorageButton", "CleanStorageButton" })
            if (window.FindName(name) is not Button { Content: not null }) throw new InvalidOperationException("Enhancement control missing: " + name);
    }

    private static void CheckStorageLabelGap(MainWindow window)
    {
        foreach (var name in new[] { "_cleanCache", "_cleanBackups" })
        {
            var check = (CheckBox)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var label = (TextBlock)check.Content;
            var box = (Border)check.Template.FindName("CheckBoxBorder", check);
            var boxRight = box.TranslatePoint(new Point(box.ActualWidth, 0), check).X;
            var labelLeft = label.TranslatePoint(new Point(), check).X;
            if (Math.Abs(labelLeft - boxRight - 8) > .75 || labelLeft + label.ActualWidth > check.ActualWidth + .75)
                throw new InvalidOperationException("Storage checkbox label gap/clipping regression.");
        }
        Console.WriteLine("STORAGE LABEL PASS: 8px gaps, bounded labels with wrapping");
    }

    private static void CheckMultiplayerNoteAlignment(MainWindow window)
    {
        var card = (Border)window.FindName("MultiplayerNoteCard");
        var group = (Grid)window.FindName("MultiplayerNoteContent");
        var text = (TextBlock)window.FindName("MultiplayerNoteText");
        var bounds = group.TransformToAncestor(card).TransformBounds(new Rect(group.RenderSize));
        var textBounds = text.TransformToAncestor(group).TransformBounds(new Rect(text.RenderSize));
        if (Math.Abs(bounds.Left + bounds.Width / 2 - card.ActualWidth / 2) > .75
            || Math.Abs(bounds.Top + bounds.Height / 2 - card.ActualHeight / 2) > .75
            || Math.Abs(textBounds.Top + textBounds.Height / 2 - group.ActualHeight / 2) > .75
            || text.TextAlignment != TextAlignment.Center || text.TextWrapping != TextWrapping.Wrap)
            throw new InvalidOperationException("Multiplayer note is not centered or wrapping is lost.");
        Console.WriteLine("NOTE ALIGNMENT PASS: icon/text group centered horizontally and vertically, wrapping retained");
    }

    private static object? Invoke(MainWindow window, string name, params object?[] values)
        => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);

    private static void CheckBrandAndFonts(MainWindow window, DependencyObject root)
    {
        if (window.Icon is null || window.FontFamily.Source != "Arial" || ((Image)window.FindName("BrandMark")).Source is not DrawingImage)
            throw new InvalidOperationException("Window icon, shared brand or Arial default is missing.");
        int checkedElements = 0;
        void Visit(DependencyObject item)
        {
            FontFamily? family = item is TextBlock text ? text.FontFamily
                : item is Button or TextBox or ComboBox or CheckBox or RadioButton ? ((Control)item).FontFamily : null;
            if (family is not null)
            {
                if (family.Source != "Arial") throw new InvalidOperationException("Non-Arial UI font: " + item.GetType().Name + " " + family.Source);
                checkedElements++;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) Visit(VisualTreeHelper.GetChild(item, i));
        }
        Visit(root);
        if (checkedElements < 15) throw new InvalidOperationException("Insufficient rendered font coverage.");
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/PawsPatchLauncher;component/Assets/PawsPatch.ico"))!;
        using var iconStream = info.Stream;
        var decoder = new IconBitmapDecoder(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (!decoder.Frames.Select(f => f.PixelWidth).SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }))
            throw new InvalidOperationException("Embedded ICO resolutions changed.");
        Console.WriteLine($"BRAND/FONT PASS: icon resource, vector logo, 9 icon resolutions, {checkedElements} Arial elements");
    }

    private static void CheckTransferLifecycle(MainWindow window)
    {
        var previous = SynchronizationContext.Current;
        var queue = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(queue);
        var details = (TextBlock)window.FindName("TransferText");
        var operation = (TextBlock)window.FindName("OperationText");
        try
        {
            IProgress<(long Received, long? Total)> Begin(string name) => (IProgress<(long Received, long? Total)>)Invoke(window, "TransferProgress", name)!;
            void CheckHidden()
            {
                if (details.Text != "" || details.Visibility != Visibility.Collapsed || operation.Text != "READY")
                    throw new InvalidOperationException("Completed/cancelled download left stale progress.");
            }
            var active = Begin("active");
            active.Report((10, 100)); queue.Drain();
            if (details.Visibility != Visibility.Visible || details.Text.Length == 0)
                throw new InvalidOperationException("Live transfer details are hidden.");
            active.Report((100, 100));
            Invoke(window, "FinishTransfer"); operation.Text = "READY"; queue.Drain(); CheckHidden();
            var cancelled = Begin("cancelled"); cancelled.Report((30, null));
            Invoke(window, "SetBusy", false, null); operation.Text = "READY"; queue.Drain(); CheckHidden();
            var old = Begin("old"); old.Report((100, 100));
            var current = Begin("current"); current.Report((10, 100)); queue.Drain();
            if (!operation.Text.EndsWith(": current")) throw new InvalidOperationException("Old transfer overwrote the current transfer.");
            // Also handle a cached download with no progress reports.
            Begin("cached"); Invoke(window, "FinishTransfer"); operation.Text = "READY"; queue.Drain(); CheckHidden();
            operation.Text = "";
            Invoke(window,"SetOperationProgress",0d,false);
            Console.WriteLine("UI TRANSFER PASS: live, completed, cancelled, replaced, cached; queued callbacks ignored");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue(() => callback(state));
        public void Drain() { while (_queue.TryDequeue(out var action)) action(); }
    }
}
