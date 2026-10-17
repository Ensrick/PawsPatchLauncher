using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    // Independent of install/download status: copying must not hide a failure or finish a transfer.
    private readonly OperationFeedback _toast = new();
    // This timer only checks expiry/hover; the render clock drives every progress frame.
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private sealed class ToastNotice(OperationFeedback state,ToastVisual view)
    {
        public readonly OperationFeedback State=state;
        public readonly ToastVisual View=view;
        public long VisualVersion=-1;
        public bool Closing;
        public int Repetitions = 1;
    }
    private ToastNotice? _primaryToast;
    private readonly List<ToastNotice> _archivedToasts=[];
    private int _toastLayoutRevision;
    private CancellationTokenSource? _clipboardRequest;
    private bool _notificationClosed;
    private Action<string>? _clipboardWrite = null; // Simulated writer in smoke tests only.
    private Func<string> _clipboardRead = () => Clipboard.GetText();

    private void InitializeNotifications()
    {
        _primaryToast=new(_toast,new(ToastPanel,ToastSlide,ToastIcon,ToastText,ToastCloseButton,ToastProgress,ToastProgressScale,ToastCountBadge,ToastCountText));
        _toastTimer.Tick += (_, _) => RefreshToast();
        Closed += (_, _) =>
        {
            _notificationClosed = true;
            ClearToastStack();
            _clipboardRequest?.Cancel();
            AccountCopyUsernameButton.ResetFeedback();
            SocialDetailsCopyUsernameButton.ResetFeedback();
            CopyConfigurationButton.ResetFeedback();
        };
    }

    private void ShowToast(Func<string> message, bool failure = false)
    {
        // Headless commands print every result and error instead of showing it.
        if (_headless) { HeadlessReport(message(), failure); return; }
        if (_notificationClosed) return;
        // Repeated reports of one error renew its notice instead of stacking copies.
        // Keep different messages and severities as independent notifications.
        var text = message();
        var duplicate = _archivedToasts.Append(_primaryToast).FirstOrDefault(notice => notice is { Closing: false }
            && notice.View.Panel.Visibility == Visibility.Visible && notice.State.Failed == failure && notice.State.Message == text);
        if (duplicate is not null)
        {
            if (duplicate.Repetitions < int.MaxValue) duplicate.Repetitions++;
            duplicate.State.Show(message, failure, TimeSpan.FromSeconds(5), expireFailure: true);
            RenderToast(duplicate, entrance:false);
            PulseToastCount(duplicate.View);
            RefreshToast();
            return;
        }
        var positions=ToastPositions();
        if(_primaryToast is {Closing:false} && ToastPanel.Visibility==Visibility.Visible&&_toast.Message is not null)
        {
            var archived=new ToastNotice(_toast.Snapshot(),ToastVisual.Create(this)) { Repetitions=_primaryToast.Repetitions };
            if(positions.TryGetValue(ToastPanel,out var top))positions[archived.View.Panel]=top;
            _archivedToasts.Add(archived);ToastStack.Children.Insert(ToastStack.Children.Count-1,archived.View.Panel);
            archived.View.Close.Click+=(_,_)=> { archived.State.Clear();RefreshToast(); };
            RenderToast(archived,entrance:false);
        }
        // A pathological local error loop must not create an unbounded visual tree.
        while(_archivedToasts.Count>49)
        { var oldest=_archivedToasts[0];StopToast(oldest);_archivedToasts.RemoveAt(0);ToastStack.Children.Remove(oldest.View.Panel); }
        positions.Remove(ToastPanel);
        _primaryToast!.Repetitions = 1;
        _toast.Show(message, failure, TimeSpan.FromSeconds(5), expireFailure: true);
        RefreshToast();
        ToastHost.ScrollToEnd();ReflowToasts(positions);_toastLayoutRevision++;
    }

    private void RefreshToast()
    {
        if(_primaryToast is null)return;
        foreach(var notice in _archivedToasts.ToArray())RenderToast(notice);
        RenderToast(_primaryToast);
        if(_toast.HasExpiry||_archivedToasts.Any(n=>n.State.HasExpiry))_toastTimer.Start();else _toastTimer.Stop();
    }

    private void RenderToast(ToastNotice notice,bool entrance=true)
    {
        var view=notice.View;var state=notice.State;
        view.Close.ToolTip=T("Закрыть уведомление", "Dismiss notification");
        System.Windows.Automation.AutomationProperties.SetName(view.Close,(string)view.Close.ToolTip);
        if(notice.VisualVersion==state.Version&&(view.Panel.IsMouseOver||view.Panel.IsKeyboardFocusWithin))return;
        var message=state.Message;
        if (message is null)
        {
            if(!notice.Closing&&view.Panel.Visibility==Visibility.Visible)_=DismissToastAsync(notice);
            return;
        }
        var icon = state.Failed ? IconKind.Warning : IconKind.Check;
        if (view.Text.Text != message || view.Icon.Kind != icon)
        {
            view.Text.Text = message;view.Icon.Kind = icon;
            view.Icon.Foreground=(Brush)FindResource(state.Failed?"DangerBrush":"SuccessBrush");
            view.Progress.Background=view.Icon.Foreground;
            view.Panel.BorderBrush=SocialBrush(state.Failed?"#C97764":"#4D9B75");
            view.Panel.Background=SocialBrush(state.Failed?"#332024":"#142F28");
        }
        view.Panel.Visibility = Visibility.Visible;
        view.CountText.Text = notice.Repetitions > 1 ? "×" + notice.Repetitions : "";
        view.CountText.Foreground = view.Icon.Foreground;
        view.CountBadge.Visibility = notice.Repetitions > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (notice.Repetitions == 1) ResetToastCount(view);
        if (notice.VisualVersion != state.Version)
        {
            notice.VisualVersion=state.Version;notice.Closing=false;
            view.Panel.UpdateLayout();
            if(entrance)Motion.RevealFromBottom(view.Panel,view.Slide);
            StopToastProgress(view);
            view.Scale.ScaleX=state.Progress;
            if (state.Remaining is { } remaining && remaining > TimeSpan.Zero)
                view.Scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(state.Progress, 1, remaining) { FillBehavior = FillBehavior.HoldEnd },
                    HandoffBehavior.SnapshotAndReplace);
        }
    }

    private static void StopToastProgress(ToastVisual view)
    {
        var progress=view.Scale.ScaleX;view.Scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);view.Scale.ScaleX=progress;
    }

    private static void ResetToastCount(ToastVisual view)
    {
        view.CountBadge.BeginAnimation(OpacityProperty,null);view.CountBadge.Opacity=1;
        var scale=(ScaleTransform)view.CountBadge.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        scale.ScaleX=scale.ScaleY=1;
    }

    private static void PulseToastCount(ToastVisual view)
    {
        ResetToastCount(view);
        if (!SystemParameters.ClientAreaAnimation) return;
        var duration=TimeSpan.FromMilliseconds(180);
        view.CountBadge.BeginAnimation(OpacityProperty,new DoubleAnimation(.45,1,duration));
        var scale=(ScaleTransform)view.CountBadge.RenderTransform;
        var pulse=new DoubleAnimation(.86,1,duration) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,pulse);scale.BeginAnimation(ScaleTransform.ScaleYProperty,pulse);
    }

    private Dictionary<Border,double> ToastPositions()=>ToastStack.Children.OfType<Border>().Where(p=>p.Visibility==Visibility.Visible&&p.ActualHeight>0)
        .ToDictionary(p=>p,p=>p.TranslatePoint(new Point(),(FrameworkElement)Content).Y);

    private void ReflowToasts(Dictionary<Border,double> positions)
    {
        ToastStack.UpdateLayout();
        foreach(var notice in _archivedToasts.Append(_primaryToast!))
            if(!notice.Closing&&notice.View.Panel.Visibility==Visibility.Visible&&positions.TryGetValue(notice.View.Panel,out var old))
            {
                var delta=old-notice.View.Panel.TranslatePoint(new Point(),(FrameworkElement)Content).Y;
                if(Math.Abs(delta)>.5)Motion.Reposition(notice.View.Panel,notice.View.Slide,notice.View.Slide.Y+delta);
            }
    }

    private async Task DismissToastAsync(ToastNotice notice)
    {
        notice.Closing=true;var version=notice.State.Version;var revision=_toastLayoutRevision;
        var positions=ToastPositions();StopToastProgress(notice.View);
        if(!await Motion.HideToRightAsync(notice.View.Panel,notice.View.Slide)||notice.State.Version!=version)return;
        if(_notificationClosed)return;
        if(_archivedToasts.Remove(notice))ToastStack.Children.Remove(notice.View.Panel);
        if(revision==_toastLayoutRevision)ReflowToasts(positions);
    }

    private static void StopToast(ToastNotice notice)
    {
        notice.Repetitions=1; ResetToastCount(notice.View);notice.View.CountBadge.Visibility=Visibility.Collapsed;
        notice.State.Clear();StopToastProgress(notice.View);Motion.Collapse(notice.View.Panel);
        notice.View.Slide.BeginAnimation(TranslateTransform.XProperty,null);notice.View.Slide.BeginAnimation(TranslateTransform.YProperty,null);
        notice.View.Slide.X=notice.View.Slide.Y=0;notice.Closing=false;
    }

    private void ClearToastStack()
    {
        _toastLayoutRevision++;_toastTimer.Stop();
        foreach(var notice in _archivedToasts) { StopToast(notice);ToastStack.Children.Remove(notice.View.Panel); }
        _archivedToasts.Clear();
        if(_primaryToast is not null)StopToast(_primaryToast);
    }

    private void ToastCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _toast.Clear();
        RefreshToast();
    }

    private async Task<bool> ClipboardActionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, Action<TResult> success,
        Action<Func<string>, bool>? notice = null)
    {
        if (_notificationClosed) return false;
        _clipboardRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _clipboardRequest = request;
        notice ??= ShowToast;
        try
        {
            var result = await operation(request.Token);
            if (request.IsCancellationRequested || _notificationClosed) return false;
            success(result);
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested && !_notificationClosed)
            {
                ActivityStore.Log(error);
                notice(() => ClipboardRetry.IsBusy(error)
                    ? T("Буфер обмена занят. Подождите немного и повторите действие.", "The clipboard is busy. Wait a moment and try again.")
                    : T("Не удалось обратиться к буферу обмена. Попробуйте ещё раз.", "Could not access the clipboard. Please try again."), true);
            }
            return false;
        }
        finally { if (ReferenceEquals(_clipboardRequest, request)) _clipboardRequest = null; }
    }

    private Task<bool> CopyTextAsync(string text, Func<string> copied, Action<Func<string>, bool>? notice = null)
    {
        notice ??= ShowToast;
        var simulated = _clipboardWrite;
        var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return ClipboardActionAsync(token => simulated is not null
            ? ClipboardRetry.RunAsync(() => { Dispatcher.VerifyAccess(); simulated(text); return true; }, token)
            : ActivityStore.IsSmokeTest ? Task.FromException<bool>(new InvalidOperationException("Smoke tests must supply a clipboard writer."))
            : WindowsClipboard.WriteTextAsync(owner, text, token), _ => notice(copied, false), notice);
    }

    private Task<bool> PasteTextAsync(TextBox target)
    {
        var previous = target.Text;
        return ClipboardActionAsync(token => ClipboardRetry.RunAsync(_clipboardRead, token), text =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowToast(() => T("В буфере обмена нет текста.", "There is no text in the clipboard."));
                return;
            }
            if (target.Text != previous)
            {
                ShowToast(() => T("Поле уже изменено. Повторите вставку, если она всё ещё нужна.", "The field was edited. Paste again if you still need it."));
                return;
            }
            if (target.MaxLength > 0 && text.Length > target.MaxLength)
            {
                ShowToast(() => T("Текст слишком длинный. Скопируйте только код или отпечаток.", "The text is too long. Copy only the code or fingerprint."), true);
                return;
            }
            target.Text = text;
            ShowToast(() => T("Текст вставлен. Проверьте его перед применением.", "Text pasted. Review it before applying."));
        });
    }
}
