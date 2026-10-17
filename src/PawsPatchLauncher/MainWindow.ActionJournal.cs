using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private void InitializeActionJournal()
    {
        var editedFields = new HashSet<TextBox>();
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) =>
        {
            if (!_initializing && e.OriginalSource is TextBox input && input.IsKeyboardFocusWithin) editedFields.Add(input);
        }), true);
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (_initializing || e.OriginalSource is not ButtonBase control) return;
            // Use the code name, never Content/ToolTip/Tag (these may contain user data).
            ActionJournal.Record("ui.click", string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LogSelectionSnapshot));
        }), true);
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) =>
        {
            if (_initializing || _syncingLanguages || e.OriginalSource is not ComboBox control || !control.IsKeyboardFocusWithin) return;
            ActionJournal.Record("ui.select", control.Name);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LogSelectionSnapshot));
        }), true);
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, e) =>
        {
            if (!_initializing && e.OriginalSource is Slider control && (control.IsKeyboardFocusWithin || control.IsMouseCaptureWithin))
                ActionJournal.Record("ui.slider", control.Name + "=" + e.NewValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }), true);
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is TextBox input && editedFields.Remove(input))
            {
                ActionJournal.Record("ui.field.edited", input.Name);
            }
        }), true);
        Loaded += (_, _) => { ActionJournal.Record("window.open"); LogSelectionSnapshot(); };
        Closed += (_, _) => ActionJournal.Record("window.close");
    }

    private string _journalSelection = "";
    private void LogSelectionSnapshot()
    {
        var selection = $"mod={_settings.Mod};channel={_settings.Channel};text={(GameLanguages.Text(_settings))};voice={GameLanguages.Voice(_settings)};ui={_settings.Language};paw={GameMod.PawPatchSelected(_settings)}";
        var components = ConfigurationCode.Create(_settings);
        var release = _settings.PinnedRelease ?? "latest";
        if (_journalSelection == selection + components + release) return;
        _journalSelection = selection + components + release;
        ActionJournal.Record("selection", selection);
        ActionJournal.Record("components", components);
        ActionJournal.Record("release.selection", release);
    }
}
