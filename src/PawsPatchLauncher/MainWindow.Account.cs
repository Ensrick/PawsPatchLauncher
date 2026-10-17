using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private AccountService _account = null!;
    private readonly CancellationTokenSource _accountLifetime = new();
    private readonly DispatcherTimer _accountTimer = new();
    private readonly DispatcherTimer _accountResendTimer = new();
    private bool _accountBusy;
    private readonly SemaphoreSlim _accountGate = new(1, 1);
    private bool _accountRefreshing;
    private bool _accountRegister;
    private bool _accountConfirmation;
    private string _accountMessage = "";
    private DateTimeOffset _accountResendAfter;

    private void InitializeAccountUi()
    {
        _account = new AccountService(new AccountSessionStore(ActivityStore.Root));
        InitializeConnectionUi();
        InitializeSocialUi();
        InitializePasswordReveal();
        _accountTimer.Interval = TimeSpan.FromSeconds(10);
        _accountResendTimer.Interval = TimeSpan.FromSeconds(1);
        _accountResendTimer.Tick += (_, _) => { if (DateTimeOffset.UtcNow >= _accountResendAfter) { _accountResendTimer.Stop(); RenderAccount(); } };
        _accountTimer.Tick += async (_, _) =>
        {
            if (_account.State != AccountState.Guest && !_accountBusy && !_accountRefreshing) await RestoreAccountAsync(background: true);
        };
        Loaded += async (_, _) =>
        {
            // Smoke/preview fixtures and headless commands never read a real session or send Auth requests.
            if (ActivityStore.IsSmokeTest || HeadlessMode.Active) return;
            await RestoreStartupAccountAsync();
            if (!_accountLifetime.IsCancellationRequested) _accountTimer.Start();
        };
        _accountCooldownTimer.Interval = TimeSpan.FromSeconds(1);
        _accountCooldownTimer.Tick += (_, _) => { RenderAccountCooldown(); RefreshModerationCountdowns(); };
        _accountCooldownTimer.Start();
        Closed += (_, _) => { _accountTimer.Stop(); _accountResendTimer.Stop(); _accountCooldownTimer.Stop(); _accountLifetime.Cancel(); ClearAccountPasswords(); _account.Dispose(); };
    }

    private async void FriendsNav_Click(object sender, RoutedEventArgs e) { SetActivePage("friends"); await RefreshSocialAsync(); }
    private void AccountShowLogin_Click(object sender, RoutedEventArgs e) => ShowAccountForm(register: false);
    private void AccountShowRegister_Click(object sender, RoutedEventArgs e) => ShowAccountForm(register: true);

    private void ShowAccountForm(bool register)
    {
        if (_accountBusy || ConfirmationActive) return;
        SetActivePage("account");
        _accountRegister = register; _accountConfirmation = false; _accountMessage = "";
        _accountRecoveryOpen = false; _accountEditor = "";
        ClearAccountPasswords(); RenderAccount(); Motion.Reveal(AccountFormCard);
        (register ? (Control)AccountDisplayNameInput : AccountEmailInput).Focus();
    }

    private void ClearAccountPasswords()
    {
        AccountPasswordInput.Clear(); AccountRepeatInput.Clear();
        AccountCurrentPasswordInput.Clear(); AccountNewPasswordInput.Clear(); AccountNewRepeatInput.Clear();
        AccountRecoveryProofInput.Clear(); AccountRecoveryPasswordInput.Clear(); AccountRecoveryRepeatInput.Clear();
        AccountConfirmationCodeInput.Clear();
        ResetPasswordReveal();
    }

    private void ApplyAccountLanguage()
    {
        PrivacyPolicyButton.Content = AccountPrivacyButton.Content = T("Конфиденциальность", "Privacy policy");
        AccountPrivacyText.Text = T(
            "Вход необязателен. Аккаунт использует сервер для профиля, сообщений и статуса в игре. После входа друзья видят применённые настройки патча. Выход из аккаунта отключает эти функции.",
            "Signing in is optional. Your account uses a server for your profile, messages and game status. While signed in, friends can see your applied patch settings. Sign out to disable these features.");
        OwnProfileNameButton.ToolTip = T("Посмотреть свою карточку", "View your player card");
        FriendsNav.Content = T("Друзья", "Friends");
        FriendsTitleText.Text = T("Друзья", "Friends");
        FriendsGuestText.Text = T("Друзья доступны после входа в аккаунт. Патч и игру можно использовать без входа.", "Friends are available after signing in. The patch and game can be used without an account.");
        FriendsLoginButton.Content = T("Войти", "Sign in");
        FriendsRegisterButton.Content = T("Зарегистрироваться", "Register");
        ApplySocialLanguage();
        AccountShowLoginButton.Content = T("Вход", "Sign in");
        AccountShowRegisterButton.Content = T("Регистрация", "Register");
        AccountNicknameLabel.Text = T("Username · уникальный", "Username · unique");
        AccountDisplayNameLabel.Text=T("Отображаемое имя", "Display name");
        AccountEmailLabel.Text = _accountRegister?T("Почта", "Email"):T("Почта или username","Email or username");
        AccountPasswordLabel.Text = T("Пароль", "Password");
        AccountRepeatLabel.Text = T("Повторите пароль", "Repeat password");
        AccountRememberCheck.Content = T("Запомнить меня", "Remember me");
        ApplyAccountProfileLanguage();
        AccountResendButton.Content = T("Отправить код повторно", "Resend code");
        AccountProfileTitleText.Text = T("Ваш аккаунт", "Your account");
        AccountLogoutButton.Content = T("Выйти из аккаунта", "Sign out");
        AccountRetryButton.Content = T("Повторить подключение", "Reconnect");
        foreach (var (control, label) in new (Control, string)[] { (AccountEmailInput, AccountEmailLabel.Text), (AccountNicknameInput, AccountNicknameLabel.Text), (AccountPasswordInput, AccountPasswordLabel.Text), (AccountRepeatInput, AccountRepeatLabel.Text) })
            System.Windows.Automation.AutomationProperties.SetName(control, label);
        ApplyPasswordRevealLanguage();
        RenderAccount();
    }

    private void RenderAccount()
    {
        RenderConnectionUi();
        var guest = _account.State == AccountState.Guest;
        AccountFormCard.Visibility = guest && !_accountRecoveryOpen && !_accountConfirmation ? Visibility.Visible : Visibility.Collapsed;
        SetNavState(AccountShowLoginButton, !_accountRegister);
        SetNavState(AccountShowRegisterButton, _accountRegister);
        AccountSignedInCard.Visibility = guest ? Visibility.Collapsed : Visibility.Visible;
        AccountNicknamePanel.Visibility = AccountRepeatPanel.Visibility = _accountRegister ? Visibility.Visible : Visibility.Collapsed;
        AccountEmailLabel.Text = _accountRegister?T("Почта", "Email"):T("Почта или username","Email or username");
        AccountFormTitleText.Text = _accountRegister ? T("Регистрация", "Register") : T("Вход в аккаунт", "Sign in");
        AccountFormHintText.Text = "";
        AccountFormHintText.Visibility = Visibility.Collapsed;
        AccountSubmitButton.Content = _accountRegister ? T("Создать аккаунт", "Create account") : T("Войти", "Sign in");
        AccountProfileNameText.Text = string.IsNullOrWhiteSpace(_account.DisplayName) ? T("Игрок", "Player") : _account.DisplayName;
        AccountProfileEmailText.Text = _account.Email;
        AccountProfileUsernameText.Text="@"+_account.Nickname;
        AccountRetryButton.Visibility = _account.State == AccountState.Offline ? Visibility.Visible : Visibility.Collapsed;
        AccountFieldsPanel.IsEnabled = AccountSubmitButton.IsEnabled = !_accountBusy;
        AccountShowLoginButton.IsEnabled = AccountShowRegisterButton.IsEnabled = AccountLogoutButton.IsEnabled = AccountRetryButton.IsEnabled = !_accountBusy;
        AccountLogoutButton.IsEnabled = !_accountBusy && !_busy && !FeedBlocksActions;
        AccountResendButton.Visibility = _accountConfirmation ? Visibility.Visible : Visibility.Collapsed;
        AccountResendButton.IsEnabled = !_accountBusy && DateTimeOffset.UtcNow >= _accountResendAfter;
        AccountMessageCard.Visibility = _accountMessage.Length > 0 && !AccountSuccess(_accountMessage) ? Visibility.Visible : Visibility.Collapsed;
        AccountMessageText.Text = AccountMessage(_accountMessage);
        UpdateAccountMessageAppearance();
        RenderAccountProfile();
        RenderEmailConfirmation();
        RenderSocialIdentity();
        RenderModerationState();
        var access = CanSelectArcaneWars;
        if (_renderedTeamAccess != access || _renderedLiveTeamAccess != LiveArcaneAccess)
        { _renderedTeamAccess = access; _renderedLiveTeamAccess = LiveArcaneAccess; RefreshStatus(); }
    }

    private async Task AccountOperationAsync(Func<Task> action, bool background = false)
    {
        if (_accountBusy || ConfirmationActive || _accountLifetime.IsCancellationRequested || !background && AccountConnectionBlocked) return;
        var owner = _account.UserId;
        var entered = false;
        if (!background) { _accountBusy = true; _accountMessage = ""; RenderAccount(); }
        try
        {
            if (background) { entered = await _accountGate.WaitAsync(0, _accountLifetime.Token); if (!entered) return; _accountRefreshing = true; }
            else { await _accountGate.WaitAsync(_accountLifetime.Token); entered = true; }
            if (_account.UserId != owner || ConfirmationActive) return;
            await action();
        }
        catch (OperationCanceledException) when (_accountLifetime.IsCancellationRequested) { }
        catch (AccountException error) { _accountMessage = error.Code; if (error.Code == "email_not_confirmed") BeginEmailConfirmation(); HandleEndedAccount(error.Code); }
        catch { _accountMessage = "local_error"; } // No input, credentials or raw HTTP errors in launcher logs.
        finally
        {
            if (entered) _accountGate.Release();
            if (background) { if (entered) _accountRefreshing = false; }
            else _accountBusy = false;
            if (!_accountLifetime.IsCancellationRequested)
            {
                RenderAccount();
                if (entered) LoadInitialFriendsAfterAccount();
                if (!background && _accountMessage.Length > 0)
                {
                    var code = _accountMessage;
                    ShowToast(() => AccountMessage(code), !AccountSuccess(code) && !AccountInformation(code));
                    if (_activePage == "account" && AccountMessageCard.Visibility == Visibility.Visible)
                    {
                        Motion.Reveal(AccountMessageCard); AccountMessageCard.BringIntoView();
                        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (_activePage == "account") CardHighlight.Pulse(AccountMessageCard); }));
                    }
                }
            }
        }
    }

    private async void AccountSubmit_Click(object sender, RoutedEventArgs e) => await SubmitAccountAsync();
    private async Task SubmitAccountAsync()
    {
        if (_accountBusy) return;
        var email = AccountEmailInput.Text;
        var password = AccountPasswordInput.Password;
        var confirmation = AccountRepeatInput.Password;
        var nickname = AccountNicknameInput.Text;
        var remember = AccountRememberCheck.IsChecked == true;
        await AccountOperationAsync(async () =>
        {
            try
            {
                if (_accountRegister)
                {
                    var signedIn = await _account.RegisterAsync(email, password, confirmation, nickname, _accountLifetime.Token, remember,AccountDisplayNameInput.Text);
                    if (!signedIn)
                    {
                        BeginEmailConfirmation(); _accountRegister = false; _accountMessage = "confirmation_sent";
                        _accountResendAfter = DateTimeOffset.UtcNow.AddSeconds(60);
                        _accountResendTimer.Start();
                    }
                }
                else await _account.SignInAsync(email, password, _accountLifetime.Token, remember);
                if (_account.State == AccountState.SignedIn) { _accountMessage = "signed_in"; Motion.Reveal(AccountSignedInCard); await RefreshAccountAvatarAsync(); }
            }
            finally { if (_account.State != AccountState.Guest || _accountMessage == "confirmation_sent") ClearAccountPasswords(); }
        });
        if (_accountConfirmation) { Motion.Reveal(AccountConfirmationCard); AccountConfirmationCodeInput.Focus(); }
    }

    private async void AccountPassword_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await SubmitAccountAsync(); } }

    private async void AccountResend_Click(object sender, RoutedEventArgs e)
    {
        if (DateTimeOffset.UtcNow < _accountResendAfter) return;
        var email = _accountConfirmation ? AccountConfirmationEmailInput.Text : AccountEmailInput.Text;
        try{AccountService.ValidateEmail(email);}
        catch(AccountException){
            ShowToast(()=>T("Для повторного письма укажите почту аккаунта.", "To resend the email, enter your account email."),true);
            AccountConfirmationEmailInput.Focus();return;
        }
        await AccountOperationAsync(async () =>
        {
            await _account.ResendConfirmationAsync(email, _accountLifetime.Token);
            _accountResendAfter = DateTimeOffset.UtcNow.AddSeconds(60); _accountMessage = "confirmation_sent";
            _accountResendTimer.Start();
        });
    }

    private async void AccountLogout_Click(object sender, RoutedEventArgs e) => await ConfirmAccountLogoutAsync();
    private async Task ConfirmAccountLogoutAsync()
    {
        if (_busy || FeedBlocksActions || _accountBusy || _account.State == AccountState.Guest || ConfirmationActive) return;
        var owner = _account.UserId;
        var confirmation = ConfirmActionAsync(T("Выйти из аккаунта?", "Sign out?"),
            T("Для следующего входа понадобится почта и пароль. Игра и сохранения останутся на месте.",
              "You will need your email and password to sign in again. Your game and saves will stay on this computer."),
            T("АККАУНТ", "ACCOUNT"), _account.Nickname, T("Выйти", "Sign out"));
        LauncherIcon.SetKind(ConfirmationDeleteButton, IconKind.Logout);
        ConfirmationActionIcon.Kind = IconKind.Logout;
        if (!await confirmation || owner != _account.UserId) return;
        await AccountOperationAsync(async () =>
        {
            var revoked = await _account.SignOutAsync(_accountLifetime.Token);
            ClearAccountPasswords(); AccountEmailInput.Clear(); AccountNicknameInput.Clear();
            _accountConfirmation = false; _accountEditor = ""; _accountRecoveryOpen = false;
            _accountMessage = revoked ? "signed_out" : "signed_out_offline";
        });
    }

    private void HandleEndedAccount(string code)
    {
        if (code is not ("session_replaced" or "session_expired" or "unauthorized") || _account.State != AccountState.Guest) return;
        _accountMessage = code;
        ClearAccountPasswords(); AccountEmailInput.Clear(); AccountNicknameInput.Clear();
        _accountEditor = ""; _accountRecoveryOpen = false; _accountConfirmation = false;
        CloseSocialMenu(); RenderAccount();
        ShowToast(() => AccountMessage(code), true);
    }

    private async void AccountRetry_Click(object sender, RoutedEventArgs e) => await RestoreAccountAsync(background: false);
    private async Task RestoreAccountAsync(bool background)
    {
        if (_accountBusy) return;
        await AccountOperationAsync(async () => { await _account.RestoreAsync(_accountLifetime.Token, reuseRecent: background); await RefreshAccountAvatarAsync(); }, background);
    }

    private string AccountMessage(string code) => code switch
    {
        "" => "",
        "session_replaced" => T("В аккаунт вошли в другом лаунчере. Здесь сеанс завершён.", "This account was opened in another launcher. You have been signed out here."),
        "signed_in" => _account.Remembered ? T("Вход выполнен и сохранён на этом компьютере.", "Signed in. Your session is saved on this computer.") : T("Вход выполнен только до закрытия лаунчера. Сеанс не сохраняется на диск.", "Signed in until the launcher closes. This session is not saved to disk."),
        "signed_out" => T("Вы вышли из аккаунта. Лаунчер работает в гостевом режиме.", "Signed out. The launcher is in guest mode."),
        "signed_out_offline" => T("Сохранённый вход на этом компьютере удалён. Сервер не ответил, поэтому завершение сеанса на сервере не подтверждено.", "The saved sign-in was removed from this computer. The server did not respond, so server-side session revocation is not confirmed."),
        "confirmation_sent" => T("Проверьте почту и введите код из письма. Если аккаунт уже подтверждён, используйте вход.", "Check your email and enter the code. If your account is already confirmed, use sign in."),
        "email_not_confirmed" => T("Подтвердите почту кодом из письма.", "Confirm your email with the code from the message."),
        "invalid_confirmation_code" => T("Код неверный или устарел. Проверьте последние 6 цифр из письма либо запросите новый код.", "The code is invalid or expired. Enter the six-digit code from the latest email or request a new code."),
        "invalid_email" => T("Введите корректный адрес электронной почты.", "Enter a valid email address."),
        "invalid_nickname" => T("Username: 3–24 символа. Только английские буквы, цифры, точка, дефис и подчёркивание; первый символ — буква или цифра.", "Username: 3–24 characters. Use English letters, digits, dots, hyphens and underscores; start with a letter or digit."),
        "nickname_taken" => T("Этот username уже занят. Выберите другой.", "This username is taken. Choose another one."),
        "email_banned" => T("Эта почта заблокирована на площадке. Создать аккаунт на неё нельзя.","This email is banned on the platform. Registration is unavailable."),
        "account_banned" => T("Аккаунт заблокирован на площадке. Игра и обновления остаются доступны.","This account is banned on the platform. The game and updates remain available."),
        "admin_required" or "higher_role_required" => T("Недостаточно прав администратора. Обновите страницу.","Insufficient administrator privileges. Refresh this page."),
        "protected_account" => T("Главный аккаунт защищён от удаления, блокировки и снятия прав.","The founder account is protected from deletion, bans and role removal."),
        "admin_cannot_block" => T("Администратора площадки нельзя заблокировать.","Platform administrators cannot be blocked."),
        "self_moderation" => T("Это действие нельзя применить к своему аккаунту.","You cannot apply this action to your own account."),
        "invalid_ban" => T("Укажите причину и будущую дату окончания либо постоянную блокировку.","Enter a reason and a future end date, or select a permanent ban."),
        "restore_expired" => T("Срок восстановления закончился — прошло 7 дней.","The seven-day recovery period has expired."),
        "invalid_display_name"=>T("Введите имя от 1 до 32 символов, без пробелов по краям и невидимых символов.","Use a display name of 1–32 characters, without surrounding spaces or invisible characters."),
        "display_name_cooldown"=>T("Имя можно менять раз в 5 минут.","You can change your display name every five minutes."),
        "profile_missing" => T("Вход сохранён, но профиль игрока пока недоступен. Попробуйте подключиться повторно.", "Your sign-in is saved, but the player profile is unavailable. Try reconnecting."),
        "weak_password" => T("Нужен пароль от 6 до 128 символов. Если сервер отклоняет пароль, выберите более надёжный.", "Use a password of 6 to 128 characters. If the server rejects it, choose a stronger password."),
        "password_mismatch" => T("Пароли не совпадают.", "Passwords do not match."),
        "invalid_credentials" => T("Не удалось войти. Проверьте почту и пароль.", "Could not sign in. Check your email and password."),
        "session_expired" or "unauthorized" => T("Сеанс завершён. Войдите снова с почтой и паролем.", "Your session has ended. Sign in again with your email and password."),
        "session_unreadable" => T("Windows не смогла прочитать сохранённый вход. Войдите заново на этом компьютере.", "Windows could not read the saved sign-in. Sign in again on this computer."),
        "network" => T("Нет связи с сервисом аккаунтов. Сохранённый вход не удалён. Попробуйте позже; патч и игру можно использовать без подключения.", "The account service is unavailable. Your saved sign-in was kept. Try again later; the patch and game can be used without it."),
        "email_rate_limit" => T("Не удалось отправить письмо: временно превышен лимит отправки. Попробуйте позже. Если помните пароль, можете войти без письма.", "Could not send the email: the sending limit was temporarily exceeded. Try again later. If you remember your password, you can sign in without an email."),
        "rate_limit" => T("Слишком много запросов. Подождите немного и повторите.", "Too many requests. Wait a while and try again."),
        "mail_not_configured" => T("Отправка писем для игроков ещё не настроена. Администратору проекта нужно подключить почтовый сервис в Supabase.", "Email delivery for players is not configured yet. The project administrator must configure an email service in Supabase."),
        "signup_disabled" => T("Регистрация сейчас отключена на сервере.", "Registration is currently disabled on the server."),
        "captcha_required" => T("Сервер запросил проверку CAPTCHA, которую эта тестовая сборка пока не поддерживает.", "The server requested CAPTCHA, which this test build does not support yet."),
        "local_error" => T("Не удалось сохранить или прочитать вход на компьютере. Проверьте доступ к папке лаунчера и свободное место.", "Could not save or read the sign-in on this computer. Check folder access and free disk space."),
        _ => ProfileAccountMessage(code) ?? T("Сервис аккаунтов вернул неожиданный ответ. Попробуйте позже.", "The account service returned an unexpected response. Try again later.")
    };
}
