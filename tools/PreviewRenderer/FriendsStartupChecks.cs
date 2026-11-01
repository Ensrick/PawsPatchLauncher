using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class FriendsStartupChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Isolated fixture required.");
        new SettingsStore().Save(new UserSettings { ModNoticeSeen = true, Language = language });
        var window = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null);
        var fixtures = new List<Fixture>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T C<T>(string name) => (T)window.FindName(name);
        Task Restore() => (Task)Call("RestoreStartupAccountAsync")!;
        Task Refresh() => (Task)Call("RefreshSocialAsync")!;
        Task Probe(Fixture fixture) => (Task)typeof(AccountService).GetMethod("ProbeConnectionAsync", flags)!.Invoke(fixture.Service, [CancellationToken.None])!;
        void Visit() => Call("FriendsNav_Click", C<Button>("FriendsNav"), new RoutedEventArgs());
        void Retry() => Call("FriendsListRetry_Click", C<Button>("FriendsListRetryButton"), new RoutedEventArgs());
        string Text(string ru, string en) => language == "ru" ? ru : en;
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Friends startup: " + message); }
        bool Visible(string name) => C<UIElement>(name).Visibility == Visibility.Visible;
        string StateText() => C<TextBlock>("FriendsListEmptyText").Text;
        async Task Until(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (!done())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Deferred Friends request did not settle.");
                await Task.Delay(5);
            }
        }
        Fixture Attach(bool saved = true)
        {
            var fixture = new Fixture(saved); fixtures.Add(fixture);
            // Keep replaced services alive until their delayed response is released, to test owner isolation.
            Set("_account", fixture.Service);
            Set("_accountAvatarLoaded", true); Set("_accountAvatarOwner", fixture.Id.ToString()); Set("_accountAvatarRevision", null);
            Call("RenderAccount");
            return fixture;
        }

        async Task Scenarios()
        {
            Field<AccountService>("_account").Dispose();
            // A disconnected adapter/fast health failure can precede the account Loaded handler.
            // Startup still has to read the saved session so reconnect can resume it.
            var disconnectedStartup = Attach(); disconnectedStartup.Offline = true;
            await Probe(disconnectedStartup);
            await Restore();
            Check(disconnectedStartup.UserCalls == 1 && disconnectedStartup.Service.State == AccountState.Offline,
                "an early offline observation skipped the saved session and left a remembered account as Guest");
            Check(!Field<bool>("_accountStartupPending") && !C<FrameworkElement>("FriendsPanel").IsEnabled,
                "offline startup did not finish with social actions blocked");
            disconnectedStartup.Offline = false;
            await Probe(disconnectedStartup);
            await (Task)Call("RestoreAccountAsync", true)!;
            Check(disconnectedStartup.Service.State == AccountState.SignedIn && C<FrameworkElement>("FriendsPanel").IsEnabled,
                "a remembered offline account did not resume after the connection returned");
            var f = Attach();
            f.AuthDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            f.ListDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var startup = Restore();
            for (var i = 0; i < 12; i++) Visit();
            Check(!startup.IsCompleted && f.ListCalls == 0, "early Friends clicks bypass account restoration");
            Check(Visible("FriendsSignedInPanel") && !Visible("FriendsGuestPanel"), "sign-in prompt flashes during restoration");
            Check(StateText() == Text("Подключаю аккаунт…", "Connecting your account…") && Visible("FriendsListLoadingIndicator"), "restoring account looks like an empty list");
            Check(!Visible("FriendsSearchPanel") && !C<Button>("FriendsRequestsTab").IsEnabled, "unloaded search/requests are active");

            f.AuthDelay.SetResult(); await startup;
            await Until(() => f.ListCalls == 1);
            Check(!Field<bool>("_accountStartupPending") && !Field<bool>("_accountBusy"), "startup state never finishes");
            Check(StateText() == Text("Загружаю друзей…", "Loading friends…") && Visible("FriendsListLoadingIndicator"), "first response pending looks empty");
            for (var i = 0; i < 12; i++) { Visit(); Call("LoadInitialFriendsAfterAccount"); }
            Check(f.ListCalls == 1, "rapid navigation duplicates the initial request");
            f.ListDelay.SetResult();
            await Until(() => Field<DateTimeOffset>("_socialListReceived") != default && !Field<bool>("_socialListLoading"));
            Check(f.ListCalls == 1 && C<StackPanel>("FriendsRowsPanel").Children.Count == 1, "deferred Friends click was not completed immediately after login");
            Check(!Visible("FriendsListEmptyText") && !Visible("FriendsListLoadingIndicator") && !Visible("FriendsListRetryButton"), "loading/empty/error stays over received friends");
            Check(Visible("FriendsSearchPanel") && C<Button>("FriendsRequestsTab").IsEnabled && C<Button>("FriendsShowAddButton").IsEnabled, "received list remains locked");

            Set("_socialPresencePublishOverride", (Func<Guid, Task>)(_ => throw new AccountException("invalid_presence")));
            await Refresh();
            Check(f.ListCalls == 2 && C<StackPanel>("FriendsRowsPanel").Children.Count == 1
                && !Field<bool>("_socialListFailed"), "rejected presence hid the friends list");
            Check(Field<DateTimeOffset>("_socialPresenceNext") > DateTimeOffset.UtcNow, "rejected presence was not throttled");
            Set("_socialPresencePublishOverride", null);
            f.ListCalls = 1;
            var row = C<StackPanel>("FriendsRowsPanel").Children[0];
            var transitions = 0;
            C<Button>("FriendsShowAddButton").IsEnabledChanged += (_, _) => transitions++;
            f.ListDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = Refresh(); await Until(() => f.ListCalls == 2);
            Check(ReferenceEquals(row, C<StackPanel>("FriendsRowsPanel").Children[0]) && !Visible("FriendsListLoadingIndicator"), "background refresh clears the list or shows first-load progress");
            Check(transitions == 0 && C<Button>("FriendsShowAddButton").IsEnabled, "pending background refresh flickers controls");
            f.FailList = true; f.ListDelay.SetResult(); await refresh;
            Check(ReferenceEquals(row, C<StackPanel>("FriendsRowsPanel").Children[0]) && !Visible("FriendsListEmptyText"), "failed refresh replaces a known friend list with empty/error");
            Check(transitions == 1 && !C<Button>("FriendsShowAddButton").IsEnabled && Visible("ConnectionNotice"), "lost connection did not block controls exactly once");
            f.FailList = false; f.HasFriend = false; f.ListDelay = null;
            await Probe(f); Call("RenderAccount");
            Check(transitions == 2 && C<Button>("FriendsShowAddButton").IsEnabled && !Visible("ConnectionNotice"), "reconnect did not restore controls exactly once");
            await Refresh();
            Check(Visible("FriendsListEmptyText") && StateText() == Text("Друзей пока нет", "No friends yet"), "confirmed empty list lacks its normal empty state");
            f.HasFriend = true; await Refresh();
            C<TextBox>("FriendsSearchInput").Text = "missing-name";
            Check(Visible("FriendsListEmptyText") && StateText() == Text("Никого не найдено", "No matches"), "empty search is confused with loading/empty friends");
            C<TextBox>("FriendsSearchInput").Clear();
            Check(!Visible("FriendsListEmptyText"), "clearing search leaves empty state");

            var failure = Attach(); failure.FailList = true;
            await Restore(); await Until(() => failure.ListCalls == 1 && !Field<bool>("_socialListLoading"));
            Check(Field<DateTimeOffset>("_socialListReceived") == default && StateText() == Text("Не удалось загрузить список друзей.", "Could not load your friends."), "failed first read reports no friends");
            Check(Visible("FriendsListRetryButton") && !C<Button>("FriendsListRetryButton").IsEnabled && !Visible("FriendsListLoadingIndicator"), "initial connection failure leaves actions enabled");
            failure.FailList = false; failure.ListDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Probe(failure); Call("RenderAccount");
            Check(C<Button>("FriendsListRetryButton").IsEnabled, "retry stayed disabled after reconnect");
            Retry(); await Until(() => failure.ListCalls == 2);
            Retry();
            Check(Visible("FriendsListLoadingIndicator") && !Visible("FriendsListRetryButton") && failure.ListCalls == 2, "retry state stuck or double request");
            failure.ListDelay.SetResult(); await Until(() => !Field<bool>("_socialListLoading"));
            Check(C<StackPanel>("FriendsRowsPanel").Children.Count == 1 && !Visible("FriendsListEmptyText"), "retry did not restore the friend list");

            var offline = Attach(); offline.Offline = true;
            await Restore();
            Check(offline.Service.State == AccountState.Offline && offline.ListCalls == 0, "offline startup fetched friends before authentication");
            Check(Visible("FriendsListRetryButton") && !Visible("FriendsGuestPanel") && !Visible("FriendsListLoadingIndicator"), "offline account was reported as guest/loading");
            Check(StateText() == Text("Не удалось загрузить список друзей.", "Could not load your friends."), "offline account claims no friends");
            offline.Offline = false; offline.AuthDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Probe(offline); Call("RenderAccount");
            Retry();
            Check(StateText() == Text("Подключаю аккаунт…", "Connecting your account…") && Visible("FriendsListLoadingIndicator"), "reconnect lacks account progress");
            offline.AuthDelay.SetResult();
            await Until(() => offline.ListCalls == 1 && !Field<bool>("_accountBusy") && !Field<bool>("_socialListLoading"));
            Check(offline.ListCalls == 1 && C<StackPanel>("FriendsRowsPanel").Children.Count == 1, "reconnect loses/duplicates the first friend read");

            var guest = Attach(saved: false); await Restore(); Visit();
            Check(guest.UserCalls == 0 && guest.ListCalls == 0 && Visible("FriendsGuestPanel"), "real guest does not reach sign-in state");
            Check(!Visible("FriendsSignedInPanel") && !Visible("FriendsListLoadingIndicator"), "guest is stuck in loading");

            var previous = Attach(); previous.ListDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Restore(); await Until(() => previous.ListCalls == 1);
            Attach(saved: false);
            previous.ListDelay.SetResult(); await Until(() => Field<SemaphoreSlim>("_socialGate").CurrentCount == 1);
            Check(Visible("FriendsGuestPanel") && !Visible("FriendsSignedInPanel"), "old request restored a signed-out panel");
            Check(Field<IReadOnlyList<SocialPlayer>>("_socialPlayers").Count == 0 && Field<DateTimeOffset>("_socialListReceived") == default, "old account response leaked friends into new account");
            Check(!Field<bool>("_socialListLoading") && !Field<bool>("_socialListFailed"), "old request polluted new load state");
            Console.WriteLine($"FRIENDS STARTUP UI PASS {checks} {language}: delayed auth/list, early and repeated navigation, empty/search states, failed read/retry, offline reconnect, guest, owner isolation; fake HTTP only; no visible window or input injection");
        }

        try
        {
            var task = window.Dispatcher.InvokeAsync(Scenarios).Task.Unwrap();
            var frame = new DispatcherFrame();
            var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            watchdog.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            watchdog.Start(); Dispatcher.PushFrame(frame); watchdog.Stop();
            if (!task.IsCompleted) throw new TimeoutException("Friends startup checks timed out.");
            task.GetAwaiter().GetResult();
        }
        finally { window.Close(); foreach (var fixture in fixtures) fixture.Service.Dispose(); }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        internal readonly Guid Id = Guid.NewGuid();
        internal readonly AccountService Service;
        internal TaskCompletionSource? AuthDelay, ListDelay;
        internal bool Offline, FailList, HasFriend = true;
        internal int UserCalls, ListCalls;
        private readonly Guid _friend = Guid.NewGuid();

        internal Fixture(bool saved)
        {
            var store = new AccountSessionStore(Path.Combine(ActivityStore.Root, "friends-startup-" + Guid.NewGuid().ToString("N")));
            if (saved) store.Save(new AccountSession { UserId = Id.ToString(), Email = "fixture@example.invalid", Nickname = "fixture",
                AccessToken = "fixture-access", RefreshToken = "fixture-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                LoginId = Guid.NewGuid(), LauncherId = Guid.NewGuid() });
            Service = new AccountService(store, this);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (path.EndsWith("/health"))
            {
                if (Offline) throw new HttpRequestException("Fixture offline");
                body = new { status = "ok" };
            }
            else if (path.EndsWith("/user"))
            {
                Interlocked.Increment(ref UserCalls);
                if (AuthDelay is not null) await AuthDelay.Task.WaitAsync(ct);
                if (Offline) throw new HttpRequestException("Fixture offline");
                body = new { id = Id, email = "fixture@example.invalid" };
            }
            else if (path.EndsWith("/paw_launcher_session")) body = new { status = "ok" };
            else if (path.EndsWith("/paw_profiles")) body = new[] { new { id = Id, nickname = "fixture", created_at = "2026-09-07T12:00:00Z" } };
            else if (path.EndsWith("/paw_social_list"))
            {
                Interlocked.Increment(ref ListCalls);
                if (ListDelay is not null) await ListDelay.Task.WaitAsync(ct);
                if (FailList) throw new HttpRequestException("Fixture list unavailable");
                body = new { status = "ok", players = HasFriend ? new[] { new { id = _friend, nickname = "friend", relation = "friend" } } : [] };
            }
            else throw new InvalidOperationException("Unexpected fixture route: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
