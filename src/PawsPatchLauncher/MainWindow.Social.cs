using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private SocialOutbox _socialOutbox=null!;
    private readonly DispatcherTimer _socialTimer=new();
    private readonly DispatcherTimer _socialScrollReadTimer=new() {Interval=TimeSpan.FromMilliseconds(200)};
    private string _socialIdentity="";
    private bool _socialBusy;
    private readonly SemaphoreSlim _socialGate = new(1, 1);
    private Guid? _socialPeer;
    private IReadOnlyList<SocialPlayer> _socialPlayers=[];
    private IReadOnlyList<SocialMessage> _socialMessages=[];
    private IReadOnlyList<PendingSocialMessage> _socialPending=[];
    private string? _socialRenderedContext;
    private IReadOnlyList<SocialMessage> _socialRenderedMessages=[];
    private IReadOnlyList<PendingSocialMessage> _socialRenderedPending=[];
    private DateTimeOffset _socialRetryAfter;
    private int _socialFailures;

    private void InitializeSocialUi()
    {
        PreviewKeyDown += SocialDetails_PreviewKeyDown;
        _socialOutbox=new SocialOutbox(ActivityStore.Root);
        _socialTimer.Interval=TimeSpan.FromSeconds(1);
        _socialTimer.Tick+=async (_,_)=> await SocialTickAsync();
        Loaded+=(_,_)=>{if(!ActivityStore.IsSmokeTest)_socialTimer.Start();};
        Closed+=(_,_)=>{_socialTimer.Stop();_socialScrollReadTimer.Stop();ResetChatLoad();_chatMemory.Clear();CloseAvatarPreview();CloseGameActivity();};
        Activated+=async (_,_)=>{if(!ActivityStore.IsSmokeTest && _activePage=="friends")await RefreshSocialAsync();};
        _socialScrollReadTimer.Tick+=async (_,_)=>{
            _socialScrollReadTimer.Stop();
            if(!ActivityStore.IsSmokeTest && IsActive && _activePage=="friends" && !_socialBusy && DateTimeOffset.UtcNow>=_socialRetryAfter)
            {
                if(!SmoothScroll.IsAnimating(FriendsChatScroll)&&!_historyNavigating&&!_historyPreserveScroll)
                {
                    var direction=_historyScrollDirection;_historyScrollDirection=0;
                    if(direction<0&&FriendsChatScroll.VerticalOffset<80)await NavigateHistoryAsync(true);
                    else if(direction>0&&FriendsChatScroll.ScrollableHeight-FriendsChatScroll.VerticalOffset<80)await NavigateHistoryAsync(false);
                }
                await SocialOperationAsync(AcknowledgeSocialAsync, background:true);
            }
        };
        FriendsChatScroll.ScrollChanged+=(_,e)=>{
            RefreshHistoryJump();
            if(ActivityStore.IsSmokeTest)return;
            _socialScrollReadTimer.Stop();_socialScrollReadTimer.Start();
            if (!_historyNavigating && !_historyPreserveScroll && e.ExtentHeightChange == 0 && e.VerticalChange!=0)
                _historyScrollDirection=Math.Sign(e.VerticalChange);
        };
    }
    private void ApplySocialLanguage()
    {
        ApplyNotificationSoundLanguage();
        ApplyGameActivityLanguage();
        if (SocialDetailsPlayer() is SocialPlayer profile)
            RenderSocialDetails(profile);
        FriendsNicknameLabel.Text=T("Username друга", "Friend's username");
        foreach(var (control,label) in new[] {(FriendsAddButton,T("Отправить заявку · Enter", "Send request · Enter")),
            (FriendsDialogClose,T("Закрыть", "Close")),(FriendsShowAddButton,T("Добавить друга", "Add friend")),
            (FriendsRequestsTab,T("Заявки", "Requests"))})
        { control.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(control,label); }
        FriendsSearchInput.ToolTip=T("Имя или @username", "Display name or @username");
        System.Windows.Automation.AutomationProperties.SetName(FriendsSearchInput,(string)FriendsSearchInput.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(FriendsNicknameInput,FriendsNicknameLabel.Text);
        FriendsSearchPlaceholder.Text=T("Поиск", "Search");
        RefreshFriendsSearchPlaceholder();
        FriendsBroadcastButton.ToolTip=T("Отправить друзьям", "Send to friends");
        System.Windows.Automation.AutomationProperties.SetName(FriendsBroadcastButton,(string)FriendsBroadcastButton.ToolTip);
        FriendsSendButton.ToolTip=T("Отправить · Enter", "Send · Enter");
        System.Windows.Automation.AutomationProperties.SetName(FriendsSendButton,T("Отправить", "Send"));
        FriendsMessageInput.ToolTip=null;
        FriendsMessageInput.UiLanguage = _text.Language;
        FriendsGlyphButton.ToolTip = T("Значки Kohan II", "Kohan II glyphs");
        System.Windows.Automation.AutomationProperties.SetName(FriendsGlyphButton, (string)FriendsGlyphButton.ToolTip);
        FriendsChatEmptyTitle.Text=T("Чат", "Chat");
        FriendsChatEmptyText.Text=T("Выберите друга", "Choose a friend");
        RenderSocialRows(); RenderSocialMessages(); RenderSocialNotifications();
    }
    private void RenderSocialIdentity()
    {
        RenderConnectionUi();
        RefreshCommunityIdentity();
        if(_socialIdentity!=_account.UserId) {
            ResetBroadcast();
            ClearToastStack();
            FriendsSearchInput.Clear(); ResetFriendsDialog();
            _socialIdentity=_account.UserId; _socialPeer=null; _socialPlayers=[]; _socialMessages=[]; _socialPending=[];
            _chatFriendships=new Dictionary<Guid,DateTimeOffset?>();
            ResetSocialHistory();
            _chatMemory.Clear();
            ClearSocialProfiles();
            _socialLoadedChat=null;
            _socialOffers=[];_sendingOffers.Clear();_soundOwner=null;_soundPlayers=[];ResetChatMedia(dispose:true);
            _socialRetryAfter=default; _socialFailures=0; _socialSection="chats"; _socialRequestSection="incoming";
            FriendsAddPanel.Visibility=Visibility.Collapsed;
            FriendsNicknameInput.Clear();FriendsMessageInput.Clear();FriendsStatusText.Text="";
            RenderSocialRows();RenderSocialMessages();
        }
        if (_account.Restricted && (_chatMemory.Count > 0 || _socialMessages.Count > 0 || _chatViewLifetime is not null))
        {
            ResetSocialHistory(); _chatMemory.Clear(); _socialMessages = []; _socialOffers = []; _socialLoadedChat = null;
            RenderSocialMessages();
        }
        var ready=!_socialBusy && !_accountBusy && _account.State!=AccountState.Guest && !_account.Restricted && _socialListReceived!=default;
        FriendsToolbar.Visibility=_account.State==AccountState.Guest?Visibility.Collapsed:Visibility.Visible;
        FriendsAddButton.IsEnabled=FriendsShowAddButton.IsEnabled=FriendsRowsPanel.IsEnabled=FriendsDialogRows.IsEnabled=ready;
        FriendsRequestsTab.IsEnabled = FriendsBlockedTab.IsEnabled = ready;
        RenderSocialNotifications();
        var contact=SocialContactAvailable(_socialPlayers.FirstOrDefault(p=>p.Id==_socialPeer));
        FriendsSendButton.IsEnabled=ready && contact;
        FriendsMessageInput.IsEnabled=ready&&contact;
        FriendsComposerMoreButton.IsEnabled=ready && contact && !_offerSending && !_busy;
        SendSaveBroadcastButton.IsEnabled=FriendsBroadcastButton.IsEnabled=ready && !_offerSending && !_busy && _socialPlayers.Any(p=>p.Relation=="friend"&&p.Available);
        RenderSocialListState();
    }
    private async Task SocialOperationAsync(Func<Guid,Task> action, bool background = false)
    {
        if(_socialBusy || _accountBusy || ConfirmationActive || _accountLifetime.IsCancellationRequested || _account.State==AccountState.Guest || _account.Restricted || !background && AccountConnectionBlocked)return;
        if(!Guid.TryParse(_account.UserId,out var owner))return;
        var entered=false;
        if(!background){_socialBusy=true;RenderSocialIdentity();}
        try {
            if(background){entered=await _socialGate.WaitAsync(0,_accountLifetime.Token);if(!entered)return;}
            else {await _socialGate.WaitAsync(_accountLifetime.Token);entered=true;}
            if(_account.UserId!=owner.ToString() || _account.State==AccountState.Guest || ConfirmationActive)return;
            await action(owner);
            if(_account.UserId==owner.ToString() && _chatLoadError is null){_socialFailures=0;_socialRetryAfter=default;}
        }
        catch(OperationCanceledException)when(_accountLifetime.IsCancellationRequested){}
        catch(AccountException error) {
            HandleEndedAccount(error.Code);
            if(_account.UserId==owner.ToString()) {
                SetSocialStatus(SocialError(error.Code));
                if(!background)ShowToast(()=>SocialError(error.Code),true);
                if(error.Code is "network" or "rate_limit" or "outcome_unknown") {
                    _socialFailures=Math.Min(5,_socialFailures+1);
                    _socialRetryAfter=DateTimeOffset.UtcNow.AddSeconds(error.Code=="rate_limit"?60:Math.Min(120,5*Math.Pow(2,_socialFailures)));
                }
            }
        }
        catch {if(_account.UserId==owner.ToString()) {
            SetSocialStatus(T("Не удалось выполнить действие. Попробуйте ещё раз.", "Could not complete the action. Try again."));
            if(!background)ShowToast(()=>T("Не удалось выполнить действие. Попробуйте ещё раз.", "Could not complete the action. Try again."),true);
        }}
        finally {
            if(entered)_socialGate.Release();
            if(!background)_socialBusy=false;
            if(!_accountLifetime.IsCancellationRequested){RenderSocialIdentity();RefreshSocialCopyAvailability();}
        }
    }
    private string SocialError(string code)=>code switch {
        "player_unavailable"=>T("Игрок недоступен. Проверьте username и список блокировок.", "Player unavailable. Check the username and your blocked list."),
        "friend_required"=>T("Общение недоступно: заявка не принята, дружба удалена или включена блокировка.", "Contact unavailable: the request is not accepted, friendship ended or a block is active."),
        "request_missing"=>T("Эта заявка уже изменилась. Обновите список.", "This request has changed. Refresh the list."),
        "friend_limit"=>T("Достигнут предел заявок, друзей или блокировок.", "Friend, request or block limit reached."),
        "message_conflict"=>T("Не удалось подтвердить сообщение. Повтор с изменённым содержимым запрещён.", "Could not confirm the message. A retry with different content is not allowed."),
        "invalid_message"=>T("Сообщение должно содержать от 1 до 2000 символов, не только пробелы.", "Use 1 to 2000 characters, not only whitespace."),
        "message_limit"=>T("Достигнут предел сохранённых сообщений этого аккаунта.", "This account's stored message limit was reached."),
        "outbox_full"=>T("Уже 100 неотправленных сообщений. Дождитесь отправки или удалите ненужные.", "100 unsent messages. Wait for delivery or delete unwanted messages."),
        "offer_expired"=>T("Предложение истекло. Отправьте новое.", "The offer expired. Send a new one."),
        "offer_unavailable"=>T("Предложение больше недоступно или уже обработано.", "The offer is no longer available or was already handled."),
        "invalid_offer" or "invalid_save"=>T("Не удалось проверить данные предложения.", "Could not validate the offer data."),
        "storage_limit"=>T("Временное хранилище сейвов заполнено. Дождитесь ответа на предыдущие отправки.", "Temporary save storage is full. Wait for earlier transfers to finish."),
        "configuration_matches"=>T("Конфигурация друга уже совпадает с вашей. Предложение не отправлено.", "Your friend's configuration already matches yours. The offer was not sent."),
        "request_unconfirmed"=>T("Не удалось подтвердить состояние заявки. Обновите список друзей.", "Could not confirm the request status. Refresh your friends list."),
        "delivery_timeout"=>T("Не удалось отправить за минуту", "Could not send within a minute"),
        "network" or "outcome_unknown"=>T("Нет связи с сервером. Пробуем подключиться…", "Cannot reach the server. Reconnecting…"),
        _=>AccountMessage(code)
    };
    private Func<Guid, Task>? _socialPresencePublishOverride;
    private async Task RefreshSocialAsync()=>await SocialOperationAsync(async owner=> {
        _socialListLoading = true; _socialListFailed = false;
        RenderSocialListState();
        try {
        // Publishing optional configuration metadata must not prevent reading friends
        // when a client and server deployment briefly have different capabilities.
        try { await (_socialPresencePublishOverride?.Invoke(owner) ?? PublishSocialPresenceAsync(owner)); }
        catch (AccountException ex) when (ex.Code == "invalid_presence")
        { _socialPresenceNext = DateTimeOffset.UtcNow.AddSeconds(30); }
        var players=await _account.GetFriendsAsync(_accountLifetime.Token);
        if(_account.UserId!=owner.ToString())return;
        NotifySocialArrival(owner, players);
        SetSocialPlayers(owner,players);
        PruneChatMemory();
        _socialListReceived=DateTimeOffset.UtcNow;
        if(_socialPeer is not null && !players.Any(p=>p.Id==_socialPeer && p.Relation=="friend")){_socialPeer=null;_socialMessages=[];_socialOffers=[];ResetSocialHistory();FriendsMessageInput.Clear();}
        RenderSocialRows();RenderSocialMessages();RenderSocialIdentity();
        await RefreshSocialProfilesAsync(owner);
        if(_account.UserId!=owner.ToString())return;
        if(_activePage=="friends" && _socialSection=="chats" && WindowState!=WindowState.Minimized)await LoadSocialChatAsync(owner);
        await AcknowledgeSocialAsync(owner);
        RenderSocialRows();RenderSocialNotifications();
        SetSocialStatus("");
        }
        catch
        {
            if (_account.UserId == owner.ToString() && _socialListReceived == default) _socialListFailed = true;
            throw;
        }
        finally
        {
            if (_account.UserId == owner.ToString()) { _socialListLoading = false; RenderSocialListState(); }
        }
    }, background:true);
    private async void FriendsAdd_Click(object sender,RoutedEventArgs e)
    {
        var raw=FriendsNicknameInput.Text;var nickname=raw.Trim().TrimStart('@');
        await SocialOperationAsync(async owner=> {
            var result=await _account.RequestFriendAsync(nickname,ct:_accountLifetime.Token);
            if(_account.UserId!=owner.ToString())return;
            if(FriendsNicknameInput.Text==raw){FriendsNicknameInput.Clear();await CloseFriendsDialogAsync();}
            SetSocialStatus("");
            ShowFriendRequestResult(result);
        });
    }
    private async Task SocialFriendActionAsync(SocialPlayer player,string action)
    {
        if(_busy||FeedBlocksActions||_socialBusy||_accountBusy||ConfirmationActive||_account.State!=AccountState.SignedIn)return;
        var accountOwner=_account.UserId;
        if(!_socialPlayers.Any(p=>p.Id==player.Id&&p.Relation==player.Relation)&&SocialDetailsPlayer()?.Id!=player.Id)return;
        if(action is "block" or "remove") {
            var approved=await ConfirmActionAsync(T(action=="block"?"Заблокировать игрока?":"Удалить из друзей?",action=="block"?"Block player?":"Remove friend?"),
                T("Переписка и передача файлов между вами станут недоступны.", "Chat and file transfers between you will become unavailable."),T("Игрок", "Player"),player.Nickname,
                T(action=="block"?"Заблокировать":"Удалить",action=="block"?"Block":"Remove"));
            if(!approved)return;
        }
        if(_account.UserId!=accountOwner||_account.State!=AccountState.SignedIn
            ||!_socialPlayers.Any(p=>p.Id==player.Id&&p.Relation==player.Relation)&&SocialDetailsPlayer()?.Id!=player.Id)return;
        await SocialOperationAsync(async owner=> {
            await _account.FriendActionAsync(action,player.Id,ct:_accountLifetime.Token);
            if(_account.UserId!=accountOwner)return;
            if(_socialDetailsPeer==player.Id&&action is "remove" or "block")CloseSocialDetails();
            var players=await _account.GetFriendsAsync(_accountLifetime.Token);
            if(_account.UserId!=owner.ToString())return;
            SetSocialPlayers(owner,players);
            PruneSocialProfiles();
            PruneChatMemory();
            if (action is "remove" or "block" or "hide_chat") _chatMemory.Remove(player.Id);
            if(_socialPeer==player.Id && action is "remove" or "block" or "hide_chat"){_socialPeer=null;_socialMessages=[];_socialOffers=[];ResetSocialHistory();FriendsMessageInput.Clear();}
            RenderSocialRows();RenderSocialMessages();RenderSocialNotifications();SetSocialStatus("");
            ShowToast(()=>SocialActionResult(action));
        });
    }
    private Button SocialButton(string label,Func<Task> action,bool gold=false)
    {
        var button=new Button {Content=label,Style=(Style)FindResource(gold?"GoldButton":"GhostButton"),Margin=new Thickness(0,4,6,0),Padding=new Thickness(10,5,10,5)};
        button.Click+=async (_,_)=>await action();return button;
    }

}
