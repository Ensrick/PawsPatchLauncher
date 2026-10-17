using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
namespace PawsPatchLauncher;
public partial class MainWindow
{
    public sealed record TimelineChoice(string Id, string Label, bool Unread = false)
    { public Visibility UnreadVisibility => Unread ? Visibility.Visible : Visibility.Collapsed; }
    private int _changelogTransitionVersion;
    private bool _changelogTransitionPending, _syncingTimeline;
    private string? _renderedNewsIdentity;
    private string _historySubject = "", _historySource = "all", _historyBranch = "stable", _historyActiveSelection = "";
    private IReadOnlyList<TimelineEntry> _renderedHistory = [];
    private ChannelManifest?[] HistoryManifests() => [ChannelForMod("stable"), ChannelForMod("beta"), _latestChannel, _channel];
    private IReadOnlyList<TimelineEntry> HistoryRows(string subject, string source, string branch)
        => ChangelogTimeline.Entries(HistoryManifests(), subject, source, branch);
    private string HistoryBranchFor(string mod) => mod == _settings.Mod ? _settings.Channel : _settings.ModChannels?.GetValueOrDefault(mod)?.Channel ?? "stable";
    private void SyncHistorySelection()
    {
        var active = _settings.Mod + ":" + _settings.Channel;
        if (_historyActiveSelection == active) return;
        _historyActiveSelection = active; _historySubject = _settings.Mod; _historySource = "all"; _historyBranch = _settings.Channel;
    }
    private void SyncHistoryFilters()
    {
        SyncHistorySelection();
        _syncingTimeline = true;
        try
        {
            bool Unread(string subject, string source, string branch) => HistoryRows(subject, source, branch).Any(r => ChangelogTimeline.IsUnread(_settings, r));
            SetChoices(HistorySubjectCombo, new[] { "launcher", GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars }
                .Select(id => new TimelineChoice(id, id == "launcher" ? T("Лаунчер", "Launcher") : GameMod.Name(id, _text.Language == "ru"), Unread(id, "all", HistoryBranchFor(id)))).ToArray(), _historySubject);
            SetChoices(HistorySourceCombo, [new("all", T("Все изменения", "All changes")),
                new("mod", _historySubject == GameMod.Vanilla ? T("Игра", "Game") : T("Мод", "Mod"), Unread(_historySubject, "mod", "all")),
                new("patch", "Paw's Patch", Unread(_historySubject, "patch", _historyBranch))], _historySource);
            SetChoices(HistoryBranchCombo, [new("all", T("Все ветки", "All branches")),
                new("stable", T("Релиз", "Release"), Unread(_historySubject, "patch", "stable")),
                new("beta", T("Бета", "Beta"), Unread(_historySubject, "patch", "beta"))], _historyBranch);
            HistorySourceCombo.Visibility = _historySubject == "launcher" ? Visibility.Collapsed : Visibility.Visible;
            HistoryBranchCombo.Visibility = _historySubject == "launcher" || _historySource == "mod" ? Visibility.Collapsed : Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(HistorySubjectCombo, T("История: игра или лаунчер", "History: game or launcher"));
            System.Windows.Automation.AutomationProperties.SetName(HistorySourceCombo, T("История: мод или патч", "History: mod or patch"));
            System.Windows.Automation.AutomationProperties.SetName(HistoryBranchCombo, T("Канал истории патча", "Patch history channel"));
        }
        finally { _syncingTimeline = false; }
    }
    private static void SetChoices(ComboBox combo, TimelineChoice[] choices, string selected)
    {
        if (combo.ItemsSource is not TimelineChoice[] old || !old.SequenceEqual(choices)) combo.ItemsSource = choices;
        combo.SelectedItem = ((TimelineChoice[])combo.ItemsSource).FirstOrDefault(c => c.Id == selected);
    }
    private async void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _syncingTimeline || sender is not ComboBox { SelectedItem: TimelineChoice choice } combo) return;
        var subject = combo == HistorySubjectCombo ? choice.Id : _historySubject;
        var source = combo == HistorySourceCombo ? choice.Id : _historySource;
        var branch = combo == HistoryBranchCombo ? choice.Id : _historyBranch;
        if (combo == HistorySubjectCombo) { source = "all"; branch = HistoryBranchFor(subject); }
        await SwitchHistoryAsync(subject, source, branch);
    }
    private void CancelChangelogTransition()
    {
        _changelogTransitionVersion++; _changelogTransitionPending = false;
        NewsScrollViewer.BeginAnimation(UIElement.OpacityProperty, null); NewsScrollViewer.Opacity = 1;
    }
    private async Task SwitchHistoryAsync(string subject, string source, string branch)
    {
        if ((_historySubject, _historySource, _historyBranch) == (subject, source, branch)) return;
        (_historySubject, _historySource, _historyBranch) = (subject, source, branch);
        var version = ++_changelogTransitionVersion; _changelogTransitionPending = true; SyncHistoryFilters();
        var animate = SystemParameters.ClientAreaAnimation && NewsScrollViewer.IsVisible && NewsScrollViewer.ActualHeight > 0;
        if (animate)
        {
            NewsScrollViewer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(NewsScrollViewer.Opacity, 0, TimeSpan.FromMilliseconds(80))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd });
            await Task.Delay(90);
        }
        if (version != _changelogTransitionVersion) return;
        RefreshNews(); NewsScrollViewer.ScrollToTop();
        if (animate) { NewsScrollViewer.Opacity = 0; Motion.Reveal(NewsScrollViewer); }
    }
    private void RefreshNews()
    {
        if (NewsEntriesPanel is null) return;
        SyncHistorySelection(); NewsTitleText.Text = _text["news.title"]; SyncHistoryFilters();
        var entries = HistoryRows(_historySubject, _historySource, _historyBranch);
        var identity = _text.Language + ":" + _historySubject + ":" + _historySource + ":" + _historyBranch + string.Join('|', entries.Select(e => e.ReadKey + e.ContentId + e.Overview));
        if (identity == _renderedNewsIdentity) { MarkVisibleChangelogRead(); return; }
        _renderedNewsIdentity = identity; _renderedHistory = entries; CancelChangelogTransition(); NewsEntriesPanel.Children.Clear();
        if (entries.Count == 0) NewsEntriesPanel.Children.Add(new TextBlock { Text = _historySource == "mod"
            ? T("История выпусков оригинальной игры пока не добавлена.", "Release notes for the original game have not been added yet.") : _text["news.empty"], Style = (Style)FindResource("CardDescription") });
        foreach (var group in entries.GroupBy(e => e.Subject + ":" + e.Source + ":" + e.ContentId + ":" + e.Overview)) NewsEntriesPanel.Children.Add(HistoryCard(group.ToArray()));
        MarkVisibleChangelogRead();
    }
    private Border HistoryCard(TimelineEntry[] rows)
    {
        var row = rows[0]; var entry = row.Entry; var item = new StackPanel();
        var owner = row.Source == "patch" ? "Paw's Patch" : row.Source == "launcher" ? T("Лаунчер", "Launcher") : T("Авторский мод", "Original mod");
        var branch = row.Source == "patch" ? " · " + string.Join(" / ", rows.Select(r => ChannelPresentation.Name(r.Branch, _text.Language)).Distinct()) : "";
        item.Children.Add(new TextBlock { Text = owner + branch + (row.Overview ? T(" · обзор версии", " · version overview") : ""), Style = (Style)FindResource("MetadataText"), Foreground = ColorBrush(row.Source == "mod" ? "#92CBBE" : "#D9BD78") });
        item.Children.Add(new TextBlock { Text = row.Source == "patch" ? ChannelPresentation.ChangelogText(entry.Title.Get(_text.Language), _text.Language) : entry.Title.Get(_text.Language), Style = (Style)FindResource("CardSubtitle"), Margin = new Thickness(0, 7, 0, 0) });
        var metadata = row.Source == "patch" ? PawPatchVersions.Display(row.Subject, entry.Version)! : entry.Version;
        if (DateTimeOffset.TryParse(entry.PublishedAt, out var date)) metadata += " · " + date.ToString(UiLanguages.DatePattern(_text.Language) + "");
        else if (!row.Overview) metadata += T(" · дата не указана", " · date not specified");
        item.Children.Add(new TextBlock { Text = metadata, Style = (Style)FindResource("SmallMetadataText"), Margin = new Thickness(0, 4, 0, 0) });
        var body = row.Source == "patch" ? ChannelPresentation.ChangelogText(entry.Body.Get(_text.Language), _text.Language) : entry.Body.Get(_text.Language);
        var content = new StackPanel { Margin = new Thickness(0, 9, 0, 0), Tag = "changelog-body" }; item.Children.Add(content);
        void RenderBody(string text)
        {
            content.Children.Clear();
            foreach (var line in ChangelogTextLayout.Parse(text))
            {
                var block = new TextBlock { Text = line.Kind == ChangelogTextKind.Bullet ? "• " + line.Text : line.Text,
                    TextWrapping = TextWrapping.Wrap, Foreground = ColorBrush(line.Kind == ChangelogTextKind.Heading ? "#E7D099" : "#CDD5E1") };
                if (line.Kind == ChangelogTextKind.Heading)
                { block.FontSize = 15; block.FontWeight = FontWeights.SemiBold; block.Margin = new Thickness(0, content.Children.Count == 0 ? 0 : 10, 0, 3); }
                else { block.Style = (Style)FindResource("CardDescription"); block.Margin = new Thickness(line.Kind == ChangelogTextKind.Bullet ? 9 : 0, 0, 0, 5); }
                content.Children.Add(block);
            }
        }
        RenderBody(body);
        if (body.Length > 420 || body.Count(c => c == '\n') > 5)
        {
            var preview = ChangelogTextLayout.Preview(body);
            RenderBody(preview); var expanded = false;
            var button = new Button { Content = T("Показать полностью ▾", "Show all ▾"), Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
            button.Click += (_, _) => { expanded = !expanded; RenderBody(expanded ? body : preview); button.Content = expanded ? T("Свернуть ▴", "Collapse ▴") : T("Показать полностью ▾", "Show all ▾"); Motion.Reveal(content); }; item.Children.Add(button);
        }
        return new Border { Child = item, Background = ColorBrush("#132840"), BorderBrush = ColorBrush("#40536E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) };
    }
}
