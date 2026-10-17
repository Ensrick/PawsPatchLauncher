using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private sealed class CommunityView : CommunityHistory
    {
        public string Draft = "";
        public double Offset;
        public bool Bottom = true;
        public Guid RetryId;
        public string RetryBody = "";
        private IReadOnlyList<CommunityMessage>? _unreadMessages;
        private string _unreadOwner = "";
        private long _unreadPosition;
        private int _unread;
        public int Unread(string owner, long position)
        {
            if (!ReferenceEquals(_unreadMessages, Messages) || _unreadOwner != owner || _unreadPosition != position)
            {
                Guid.TryParse(owner, out var id);
                _unread = Messages.Count(m => !m.Removed && m.SenderId != id && m.Ordinal > position);
                _unreadMessages = Messages; _unreadOwner = owner; _unreadPosition = position;
            }
            return _unread;
        }
    }
    private readonly Dictionary<string, CommunityView> _communityViews = new() { ["ru"] = new(), ["en"] = new() };
    private readonly DispatcherTimer _communityTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _communitySaveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _communityReady, _communityPolling, _communitySending, _communityRendering, _communityDragging;
    private double _communityDragWidth;
    private string _communityChannel = "ru", _communityOwner = "", _communityError = "", _communitySendError = "";
    private DateTimeOffset _communityNextPoll;
    private int _communityFailures;
    private int _communityMutationVersion;
    private Func<string, CancellationToken, Task<IReadOnlyList<CommunityMessage>>>? _communityReadOverride = null;
    private Func<string, long?, CancellationToken, Task<CommunityPage>>? _communityPageReadOverride = null;
    private Window? _historyWindow;
    private bool _communityFriendsList;
    private string _communityRowsContext = "";
    private readonly Dictionary<Guid, (CommunityMessage Message, Border Element)> _communityRowCache = new();
    private IReadOnlyList<CommunityMessage> _communityRenderedMessages = [];
    private readonly HashSet<Guid> _communityArrivals = [];
    private int _communityArrivalVersion;
    private readonly CommunityNotificationTracker _communityNotifications = new();

    private void InitializeCommunity()
    {
        RefreshOperationPlacement();
        CommunityInput.CopyTextRequested = value => CopyTextAsync(value, () => T("Скопировано.", "Copied."), (message, failed) => { if (failed) ShowToast(message, true); });
        InitializeCommunityHistory();
        ((Panel)ChangelogCard.Parent).Children.Remove(ChangelogCard);
        _communityChannel = _settings.CommunityChannel is "ru" or "en" ? _settings.CommunityChannel : "en";
        _communityTimer.Tick += async (_, _) => await PollCommunityAsync();
        _communitySaveTimer.Tick += (_, _) => { _communitySaveTimer.Stop(); _settingsStore.Save(_settings); };
        Loaded += async (_, _) => { UpdateCommunityWidth(); if (!ActivityStore.IsSmokeTest && !HeadlessMode.Active) { _communityTimer.Start(); await PollCommunityAsync(); } };
        Activated += (_, _) => { if (_communityReady) { RenderCommunityMessages(); MarkCommunityRead(); _communityNextPoll = default; } };
        Closed += (_, _) => { _communityTimer.Stop(); _communitySaveTimer.Stop(); if (_communityReady) _settingsStore.Save(_settings); _historyWindow?.Close(); };
        _communityReady = true;
    }

    private void ApplyCommunityLanguage()
    {
        if (!_communityReady) return;
        CommunityTitle.Text = T("Общий чат", "Community chat");
        CompactFriendsBack.Content = T("← Друзья и переписки", "← Friends and conversations");
        RefreshCommunityOnline();
        CommunityGuestText.Text = T("Войдите, чтобы написать.", "Sign in to write.");
        CommunityLogin.Content = T("Войти в аккаунт", "Sign in");
        CommunityInput.UiLanguage = _text.Language;
        CommunityInput.ToolTip = T("Сообщение увидят все · Enter — отправить, Shift+Enter — новая строка", "Visible to everyone · Enter to send, Shift+Enter for a new line");
        CommunitySend.ToolTip = T("Отправить в общий чат", "Send to community chat");
        CommunityGlyphButton.ToolTip = T("Значки Kohan II", "Kohan II glyphs");
        CommunitySplitter.ToolTip = T("Потяните для изменения ширины · двойной щелчок — сброс", "Drag to resize · double-click to reset");
        HistoryButton.ToolTip = T("История изменений", "Release history");
        foreach (var c in new FrameworkElement[] { CommunitySend, CommunityGlyphButton, CommunitySplitter, HistoryButton, CommunityInput })
            System.Windows.Automation.AutomationProperties.SetName(c, c.ToolTip?.ToString() ?? "");
        CommunityRu.ToolTip = "Русский чат"; CommunityEn.ToolTip = "English chat";
        RefreshCommunityNotificationMode();
        RefreshCommunityIdentity(); RenderCommunityMessages(); RefreshCommunityStatus();
    }

    private void CommunityWorkspace_SizeChanged(object sender, SizeChangedEventArgs e) { if (_communityReady && !_communityDragging) UpdateCommunityWidth(); }
    private void UpdateCommunityWidth()
    {
        var available = Math.Max(0, CommunityWorkspace.ActualWidth - 14);
        if (available == 0) return;
        CommunityColumn.MinWidth = Math.Min(290, CommunityChat.Width(available, 1));
        CommunityColumn.MaxWidth = CommunityChat.Width(available, double.MaxValue);
        CommunityColumn.Width = new GridLength(CommunityChat.Width(available, _settings.CommunityWidth));
        CommunityWorkspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
    }
    private void CommunitySplitter_Started(object sender, DragStartedEventArgs e) { _communityDragging = true; _communityDragWidth = _settings.CommunityWidth; }
    private void CommunitySplitter_Delta(object sender, DragDeltaEventArgs e) { }
    private void CommunitySplitter_Completed(object sender, DragCompletedEventArgs e)
    {
        _communityDragging = false;
        _settings.CommunityWidth = e.Canceled ? _communityDragWidth : CommunityColumn.ActualWidth;
        UpdateCommunityWidth(); SaveCommunitySoon();
    }
    private void CommunitySplitter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Home)) return;
        e.Handled = true;
        _settings.CommunityWidth = e.Key == Key.Home ? 0 : CommunityChat.Width(CommunityWorkspace.ActualWidth - 14, CommunityColumn.ActualWidth + (e.Key == Key.Left ? 20 : -20));
        UpdateCommunityWidth(); SaveCommunitySoon();
    }
    private void CommunitySplitter_Reset(object sender, MouseButtonEventArgs e) { _settings.CommunityWidth = 0; UpdateCommunityWidth(); SaveCommunitySoon(); e.Handled = true; }
    private void SaveCommunitySoon() { _communitySaveTimer.Stop(); _communitySaveTimer.Start(); }

    private void PagesWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    { if (_communityReady) { RefreshCommunityPageLayout(); RefreshActionLayout(); } }
    private void CompactFriendsBack_Click(object sender, RoutedEventArgs e)
    { _communityFriendsList = true; RefreshCommunityPageLayout(); }
    private void RefreshCommunityPageLayout()
    {
        if (!_communityReady) return;
        CompactFriendsBack.Visibility = Visibility.Collapsed;
        Grid.SetColumn(FriendsConversationScroll, 2); Grid.SetColumnSpan(FriendsConversationScroll, 1);
        if (_activePage != "friends") return;
        var compact = PagesWorkspace.ActualWidth > 0 && PagesWorkspace.ActualWidth < 710;
        var conversation = compact && !_communityFriendsList && _socialPeer is not null;
        MainOptionsHost.Visibility = conversation ? Visibility.Collapsed : Visibility.Visible;
        FriendsConversationScroll.Visibility = !compact || conversation ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(MainOptionsHost, compact ? 3 : 1);
        OptionsColumn.Width = new GridLength(compact ? 1 : .8, GridUnitType.Star);
        NewsColumn.Width = compact ? new GridLength(0) : new GridLength(1.5, GridUnitType.Star);
        OptionsGapColumn.Width = new GridLength(compact ? 0 : 20);
        if (conversation)
        { Grid.SetColumn(FriendsConversationScroll, 0); Grid.SetColumnSpan(FriendsConversationScroll, 3); CompactFriendsBack.Visibility = Visibility.Visible; }
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmationActive) return;
        var (window, body) = CreateCommunityDialog(T("История изменений", "Release history"), IconKind.ReleaseNotes, 840, 840);
        _historyWindow = window;
        ChangelogCard.Margin = new Thickness(0); ChangelogCard.Padding = new Thickness(16);
        NewsTitleText.Visibility = Visibility.Collapsed; body.Content = ChangelogCard;
        window.ContentRendered += (_, _) => { RefreshNews(); MarkVisibleChangelogRead(); };
        window.Activated += (_, _) => MarkVisibleChangelogRead();
        try { window.ShowDialog(); }
        finally { body.Content = null; ChangelogCard.ClearValue(Border.PaddingProperty); NewsTitleText.Visibility = Visibility.Visible;
            _historyWindow = null; CancelChangelogTransition(); HistoryButton.Focus(); }
    }

    private void RefreshCommunityIdentity()
    {
        if (!_communityReady) return;
        if (_communityOwner != _account.UserId)
        {
            _communityOwner = _account.UserId;
            if (ReferenceEquals(_glyphComposer, CommunityInput)) RemoveChatPopup();
            foreach (var view in _communityViews.Values) { view.Draft = ""; view.RetryId = Guid.Empty; view.RetryBody = ""; }
            CommunityInput.Text = ""; _communitySendError = "";
            RenderCommunityMessages();
        }
        var guest = _account.State == AccountState.Guest;
        CommunityGuest.Visibility = guest ? Visibility.Visible : Visibility.Collapsed;
        CommunityMember.Visibility = guest ? Visibility.Collapsed : Visibility.Visible;
        CommunityInput.IsEnabled = !guest && !_account.Restricted && !_communitySending;
        RenderCommunityMessages();
        RefreshCommunityStatus();
        RefreshCommunityPageLayout();
    }

    private async Task PollCommunityAsync()
    {
        if (_communityPolling || _communitySending || _communityNavigating || _accountLifetime.IsCancellationRequested || DateTimeOffset.UtcNow < _communityNextPoll
            || ActivityStore.IsSmokeTest && _communityReadOverride is null && _communityPageReadOverride is null) return;
        _communityPolling = true;
        _ = RefreshCommunityExtrasAsync();
        var notify = false;
        var notificationOwner = _account.UserId;
        var mutationVersion = _communityMutationVersion;
        try
        {
            // Two bounded public reads. Inactive language still receives its unread badge.
            foreach (var channel in new[] { _communityChannel, _communityChannel == "ru" ? "en" : "ru" })
            {
                var page = await ReadCommunityPageAsync(channel);
                // Let input/render work run before applying a network snapshot.
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (_accountLifetime.IsCancellationRequested) return;
                if (mutationVersion != _communityMutationVersion) { _communityNextPoll = default; return; }
                var view = _communityViews[channel];
                var previous = view.Messages; var loaded = view.Loaded; var last = previous.LastOrDefault()?.Ordinal ?? 0;
                if (!view.Merge(page, false, view.AtNewest && view.Bottom)) continue;
                notify |= _communityNotifications.Observe(channel, page.Messages, _account.UserId, _account.Nickname, _settings.CommunityNotifications);
                if (channel == _communityChannel && !ReferenceEquals(previous, view.Messages) && WindowState != WindowState.Minimized)
                {
                    _communityArrivals.Clear();
                    if (loaded) foreach (var m in page.Messages.Where(m => m.Ordinal > last).TakeLast(4)) _communityArrivals.Add(m.Id);
                    RenderCommunityMessages();
                }
            }
            _communityFailures = 0; _communityError = "";
            _communityNextPoll = DateTimeOffset.UtcNow.AddSeconds(WindowState == WindowState.Minimized ? 60 : IsActive ? 5 : 15);
            MarkCommunityRead();
        }
        catch (OperationCanceledException) when (_accountLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _communityError = ex is AccountException a ? a.Code : "network";
            _communityNextPoll = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(4, ++_communityFailures))));
        }
        finally
        {
            _communityPolling = false;
            if (!_accountLifetime.IsCancellationRequested)
            {
                if (notify && notificationOwner == _account.UserId && _account.State == AccountState.SignedIn && _settings.CommunityNotifications != "mute") PlayNotificationSound();
                RefreshCommunityStatus();
            }
        }
    }

    private void CommunityChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string channel } || channel == _communityChannel || _communitySending) return;
        RemoveChatPopup(); _communityArrivals.Clear(); _communityHistoryScrollTimer.Stop();
        var old = _communityViews[_communityChannel]; old.Draft = CommunityInput.Text;
        old.Offset = CommunityScroll.VerticalOffset; old.Bottom = CommunityAtBottom();
        _communityChannel = channel; _settings.CommunityChannel = channel;
        CommunityInput.Text = _communityViews[channel].Draft; _communitySendError = "";
        RenderCommunityMessages(); RefreshCommunityStatus(); SaveCommunitySoon();
        _communityNextPoll = default;
    }

    private bool CommunityAtBottom() => CommunityScroll.ScrollableHeight - CommunityScroll.VerticalOffset < 28;
    private void CommunityScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (!_communityReady || _communityRendering) return;
        var view = _communityViews[_communityChannel];
        if (e.ExtentHeightChange == 0) { view.Bottom = CommunityAtBottom(); view.Offset = CommunityScroll.VerticalOffset; }
        ScheduleCommunityHistory(e);
        if (view.Bottom) MarkCommunityRead();
        RefreshCommunityStatus();
    }
    private void MarkCommunityRead()
    {
        if (!_communityReady || !IsActive || WindowState == WindowState.Minimized || ConfirmationActive || !CommunityAtBottom()
            || new FrameworkElement[] { ModNoticeOverlay, HelpOverlay, GameSettingsOverlay, SocialDetailsOverlay, GameActivityOverlay, AvatarPreviewOverlay, FriendsDialogOverlay, BroadcastOverlay }.Any(p => p.IsVisible)) return;
        var view = _communityViews[_communityChannel];
        if (!view.AtNewest) return;
        var last = view.Messages.LastOrDefault()?.Ordinal ?? 0;
        if (last > _settings.CommunityRead.GetValueOrDefault(_communityChannel)) { _settings.CommunityRead[_communityChannel] = last; SaveCommunitySoon(); }
        RefreshCommunityStatus();
    }
    private void CommunityJump_Click(object sender, RoutedEventArgs e)
    {
        var view = _communityViews[_communityChannel]; view.ViewStart = Math.Max(0, view.Messages.Count - CommunityHistory.VisibleLimit);
        view.Bottom = true; RenderCommunityMessages(); SmoothScroll.ToBottom(CommunityScroll); MarkCommunityRead();
    }

    private void RenderCommunityMessages()
    {
        if (!_communityReady) return;
        var view = _communityViews[_communityChannel];
        var messages = view.Visible.ToArray();
        var context = _communityChannel + ":" + _text.Language + ":" + _account.UserId + ":" + _account.AdminLevel + ":" + _account.State + ":" + _account.Nickname + ":" + _socialAvatarGeneration + ":" + _account.AvatarChangedAt;
        if (context == _communityRowsContext && _communityRenderedMessages.SequenceEqual(messages)) return;
        _communityRendering = true;
        var entrances = new List<Border>(); var channel = _communityChannel; var arrivalVersion = ++_communityArrivalVersion;
        try
        {
            // Preserve an anchor rather than a raw offset when the oldest row drops out.
            var anchor = CommunityRows.Children.OfType<Border>().FirstOrDefault(b => b.TransformToAncestor(CommunityScroll).Transform(new Point()).Y + b.ActualHeight > 0);
            var anchorId = anchor?.Tag;
            var anchorY = anchor?.TransformToAncestor(CommunityScroll).Transform(new Point()).Y ?? 0;
            if (context != _communityRowsContext) { _communityRowsContext = context; _communityRowCache.Clear(); }
            var rows = new List<Border>();
            foreach (var m in messages)
            {
                if (_communityRowCache.TryGetValue(m.Id, out var cached) && cached.Message == m) { rows.Add(cached.Element); continue; }
                var content = new StackPanel(); var header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var avatar = new Button { Content = CommunityAvatar(m, 28), Style = (Style)FindResource("ChatIdentityButton"),
                    Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0,0,8,0),
                    ToolTip = T("Профиль: ", "Profile: ") + m.DisplayName };
                System.Windows.Automation.AutomationProperties.SetName(avatar, avatar.ToolTip.ToString());
                avatar.Click += async (_, _) => await OpenCommunityProfileAsync(m);
                header.Children.Add(avatar);
                var name = new Button { Content = new TextBlock { Text = m.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = SocialBrush(m.SenderId.ToString() == _account.UserId ? "#E8C36E" : "#91BDE5") }, ToolTip = "@" + m.Nickname, Style = (Style)FindResource("ChatIdentityButton"),
                    Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                    Foreground = SocialBrush(m.SenderId.ToString() == _account.UserId ? "#E8C36E" : "#91BDE5"),
                    HorizontalContentAlignment = HorizontalAlignment.Left, FontWeight = FontWeights.SemiBold };
                name.Click += (_, _) => CommunityNameClicked(m); Grid.SetColumn(name, 1);
                header.Children.Add(name);
                var time = new TextBlock { Text = ChatTime(m.CreatedAt), ToolTip = ChatDate(m.CreatedAt),
                    FontSize = 11, Foreground = SocialBrush("#889EB8"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(time, 3); header.Children.Add(time); content.Children.Add(header);
                var body = new ChatMessageText { UiLanguage = _text.Language, Text = m.Removed ? T("Сообщение удалено", "Message removed") : m.Body,
                    FontSize = 13, Foreground = SocialBrush(m.Removed ? "#889EB8" : "#DBE4F1"),
                    CopyTextRequested = value => CopyTextAsync(value, () => T("Скопировано.", "Copied."), (message, failed) => { if (failed) ShowToast(message, true); }) };
                content.Children.Add(body);
                if (!m.Removed && _account.State != AccountState.Guest && (m.SenderId.ToString() == _account.UserId || _account.AdminLevel > 0))
                {
                    body.PopulateContextMenu = menu =>
                    {
                        var delete = new MenuItem { Header = T("Удалить сообщение", "Remove message"), Style = (Style)FindResource("SocialMenuItem") };
                        delete.Click += async (_, _) => await RemoveCommunityAsync(m); menu.Items.Add(delete);
                    };
                }
                var mentioned = !m.Removed && _account.State != AccountState.Guest && CommunityChat.Mentions(m.Body, _account.Nickname);
                var row = new Border { Tag = m.Id, Child = content, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8),
                    CornerRadius = new CornerRadius(6), Background = SocialBrush(mentioned ? "#393322" : "#152C45"), BorderBrush = SocialBrush(mentioned ? "#D3AF59" : "#2E4863"), BorderThickness = new Thickness(mentioned ? 2 : 1) };
                if (!m.Removed)
                {
                    var copy = CreateMessageCopyButton(m.Body, row, "CopyCommunityMessage");
                    Grid.SetColumn(copy, 2); header.Children.Add(copy);
                }
                rows.Add(row); _communityRowCache[m.Id] = (m, row);
                if (_communityArrivals.Contains(m.Id)) entrances.Add(row);
            }
            var keep = rows.ToHashSet();
            for (var i = CommunityRows.Children.Count - 1; i >= 0; i--)
                if (!keep.Contains((Border)CommunityRows.Children[i])) CommunityRows.Children.RemoveAt(i);
            for (var i = 0; i < rows.Count; i++)
            {
                if (i < CommunityRows.Children.Count && CommunityRows.Children[i] == rows[i]) continue;
                CommunityRows.Children.Remove(rows[i]); CommunityRows.Children.Insert(i, rows[i]);
            }
            foreach (var id in _communityRowCache.Where(pair => !keep.Contains(pair.Value.Element)).Select(pair => pair.Key).ToArray()) _communityRowCache.Remove(id);
            _communityRenderedMessages = messages;
            RefreshCommunityHistoryControls();
            CommunityRows.UpdateLayout();
            if (view.Bottom && view.AtNewest) CommunityScroll.ScrollToBottom();
            else
            {
                CommunityScroll.ScrollToVerticalOffset(view.Offset); CommunityScroll.UpdateLayout();
                var retained = CommunityRows.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, anchorId));
                if (retained is not null) CommunityScroll.ScrollToVerticalOffset(CommunityScroll.VerticalOffset + retained.TransformToAncestor(CommunityScroll).Transform(new Point()).Y - anchorY);
            }
        }
        finally { _communityRendering = false; _communityArrivals.Clear(); }
        if (IsActive) foreach (var row in entrances.TakeLast(4)) ScheduleSocialArrival(row, CommunityScroll,
            () => channel == _communityChannel && arrivalVersion == _communityArrivalVersion);
        RefreshCommunityStatus();
    }

    private void CommunityNameClicked(CommunityMessage message)
    {
        if (_account.State == AccountState.Guest) { ShowAccountForm(false); return; }
        if (!CommunityInput.IsEnabled) return;
        var before = new System.Windows.Documents.TextRange(CommunityInput.Document.ContentStart, CommunityInput.Selection.Start).Text;
        CommunityInput.InsertText((before.Length > 0 && !char.IsWhiteSpace(before[^1]) ? " " : "") + "@" + message.Nickname + " ");
    }

    private void RefreshCommunityStatus()
    {
        if (!_communityReady) return;
        var view = _communityViews[_communityChannel];
        RefreshCommunityHistoryControls();
        foreach (var (button, channel) in new[] { (CommunityRu, "ru"), (CommunityEn, "en") })
        {
            var count = _communityViews[channel].Unread(_account.UserId, _settings.CommunityRead.GetValueOrDefault(channel));
            var badge = channel == "en" ? CommunityEnUnread : CommunityRuUnread;
            var label = count > 99 ? "99+" : count.ToString();
            if (badge.Text != label) badge.Text = label;
            badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetNavState(button, channel == _communityChannel); button.IsEnabled = !_communitySending;
        }
        CommunityEmpty.Visibility = view.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CommunityEmpty.Text = view.Loaded ? T("Здесь пока тихо. Начните разговор!", "It's quiet here. Start a conversation!")
            : _communityError.Length == 0 ? T("Загружаем чат…", "Loading chat…") : T("Сообщения пока недоступны", "Messages are unavailable");
        var error = _communitySendError.Length > 0 ? _communitySendError : _communityError;
        CommunityStatus.Text = _account.Restricted ? T("Для этого аккаунта отправка недоступна.", "Sending is unavailable for this account.") : error switch
        {
            "" => _communitySending ? T("Отправляем…", "Sending…") : "",
            "rate_limit" => T("Слишком часто. Подождите несколько секунд и повторите.", "Too fast. Wait a few seconds and try again."),
            "invalid_message" => T("От 1 до 1000 символов, не только пробелы.", "Use 1–1000 characters, not only whitespace."),
            "community_unavailable" => T("Общий чат ещё не включён на сервере.", "Community chat is not enabled on the server yet."),
            "service_error" or "invalid_response" => T("Не удалось обработать ответ сервера. Попробуйте снова.", "Could not process the server response. Try again."),
            "account_banned" or "account_deletion_pending" => T("Для этого аккаунта отправка недоступна.", "Sending is unavailable for this account."),
            "session_expired" or "session_replaced" => T("Войдите снова, чтобы отправлять сообщения.", "Sign in again to send messages."),
            _ => T("Нет связи. Переписка и ваш текст сохранены в окне.", "Connection lost. Messages and your draft remain in this window.")
        };
        CommunityStatus.Visibility = CommunityStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CommunityRetry.Content = T("Обновить чат", "Refresh chat"); CommunityRetry.Visibility = _communityError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshCommunityComposer();
        CommunityJump.Content = T("К последним сообщениям", "Latest messages") + " ↓";
        CommunityJump.Visibility = !view.AtNewest || CommunityScroll.ScrollableHeight - CommunityScroll.VerticalOffset > 200 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshCommunityComposer()
    {
        var draft = _communityViews[_communityChannel].Draft; var count = draft.EnumerateRunes().Count(); CommunityCount.Text = count + " / 1000";
        CommunitySend.IsEnabled = !_communitySending && _account.State == AccountState.SignedIn && !_account.Restricted && count is > 0 and <= 1000 && !string.IsNullOrWhiteSpace(draft);
    }
    private void CommunityInput_Changed(object sender, TextChangedEventArgs e) { if (_communityReady) { _communityViews[_communityChannel].Draft = CommunityInput.Text; RefreshCommunityComposer(); } }
    private async void CommunityInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await SendCommunityAsync(); } }
    private async void CommunitySend_Click(object sender, RoutedEventArgs e) => await SendCommunityAsync();
    private async void CommunityRetry_Click(object sender, RoutedEventArgs e) { _communityNextPoll = default; await PollCommunityAsync(); }
    private async Task SendCommunityAsync()
    {
        if (!CommunitySend.IsEnabled || ConfirmationActive) return;
        var channel = _communityChannel; var owner = _account.UserId; var view = _communityViews[channel]; var body = CommunityInput.Text;
        if (view.RetryBody != body || view.RetryId == Guid.Empty) { view.RetryId = Guid.NewGuid(); view.RetryBody = body; }
        _communitySending = true; _communityMutationVersion++; _communitySendError = ""; RefreshCommunityIdentity();
        try
        {
            var sent = await _account.SendCommunityAsync(channel, view.RetryId, body, _accountLifetime.Token);
            if (_account.UserId != owner) return;
            if (CommunityInput.Text == body) CommunityInput.Text = "";
            view.RetryId = Guid.Empty; view.RetryBody = "";
            view.Append(sent); _communityArrivals.Add(sent.Id);
            view.Loaded = true; view.Bottom = true; RenderCommunityMessages(); _communityNextPoll = default;
        }
        catch (OperationCanceledException) when (_accountLifetime.IsCancellationRequested) { }
        catch (Exception ex) { _communitySendError = ex is AccountException a ? a.Code : "network"; }
        finally { _communitySending = false; if (!_accountLifetime.IsCancellationRequested) { RefreshCommunityIdentity(); CommunityInput.Focus(); } }
    }
    private async Task RemoveCommunityAsync(CommunityMessage message)
    {
        if (!await ConfirmActionAsync(T("Удалить сообщение?", "Remove message?"), T("Оно исчезнет из общего чата у всех.", "It will disappear from community chat for everyone."), "", "", T("Удалить", "Remove"))) return;
        try { await _account.RemoveCommunityAsync(message.Id, _accountLifetime.Token); _communityMutationVersion++; _communityNextPoll = default; await PollCommunityAsync(); }
        catch (Exception ex) { if (!_accountLifetime.IsCancellationRequested) { _communitySendError = ex is AccountException a ? a.Code : "network"; RefreshCommunityStatus(); } }
    }
}
