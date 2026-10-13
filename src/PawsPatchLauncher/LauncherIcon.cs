using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PawsPatchLauncher;

public enum IconKind { None, Home, Components, Multiplayer, Settings, Shield, Language, Palette, Sync, Swords, Clock, Route, Siege, Copy, Paste, Check, Warning, Help, Play, Download, Folder, Diagnostics, Undo, Trash, Compare, Save, Search, Close, Minimize, Person, Logout, More, Send, Profile, Camera, AddFriend, Requests, BlockedUsers, Discord, Smile, ChevronUp, Bot, Gear, History, ReleaseNotes, Chat, Bell, BellMuted }

/// <summary>Font-independent vector icons on a shared 24-unit grid.</summary>
public sealed class LauncherIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached("Kind", typeof(IconKind), typeof(LauncherIcon),
        new FrameworkPropertyMetadata(IconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(LauncherIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public IconKind Kind { get => GetKind(this); set => SetKind(this, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public static IconKind GetKind(DependencyObject o) => (IconKind)o.GetValue(KindProperty);
    public static void SetKind(DependencyObject o, IconKind value) => o.SetValue(KindProperty, value);

    private static readonly IReadOnlyDictionary<IconKind, Geometry> Shapes = CreateShapes();
    private static IReadOnlyDictionary<IconKind, Geometry> CreateShapes()
    {
        var paths = new Dictionary<IconKind, string>
        {
            [IconKind.Gear] = "M9,1 L15,1 15.5,4 17,5 20,4 23,9 20.5,11 20.5,13 23,15 20,20 17,19 15.5,20 15,23 9,23 8.5,20 7,19 4,20 1,15 3.5,13 3.5,11 1,9 4,4 7,5 8.5,4 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
            [IconKind.ChevronUp] = "M4,16 L12,8 20,16",
            [IconKind.Smile] = "M12,2 A10,10 0 1 1 11.99,2 M8,8 L8,9 M16,8 L16,9 M7,13 C8,19 16,19 17,13",
            [IconKind.Discord] = "F0 M20.2,4.6 C18.7,3.9 17.2,3.5 15.6,3.3 L15,4.6 C13,4.3 11,4.3 9,4.6 L8.4,3.3 C6.8,3.5 5.3,3.9 3.8,4.6 C1.5,8.1 0.6,11.7 0.8,15.3 C2.5,16.6 4.3,17.5 6.4,18.1 L7.5,16.3 L6.1,15.6 L6.5,15.3 C10,16.9 14,16.9 17.5,15.3 L17.9,15.6 L16.5,16.3 L17.6,18.1 C19.7,17.5 21.5,16.6 23.2,15.3 C23.4,11.7 22.5,8.1 20.2,4.6 Z M9.6,11.7 A1.7,1.9 0 1 1 6.2,11.7 A1.7,1.9 0 1 1 9.6,11.7 Z M17.8,11.7 A1.7,1.9 0 1 1 14.4,11.7 A1.7,1.9 0 1 1 17.8,11.7 Z",
            [IconKind.Camera] = "M3,7 L7,7 9,4 15,4 17,7 21,7 21,20 3,20 Z M12,9 A4,4 0 1 1 11.99,9",
            [IconKind.Profile] = "M3,4 L21,4 21,20 3,20 Z M8,7 A2,2 0 1 1 7.99,7 M5,16 C5,12 11,12 11,16 M14,9 L18,9 M14,13 L18,13 M14,17 L17,17",
            [IconKind.More] = "M5,11 A1,1 0 1 1 4.99,11 M12,11 A1,1 0 1 1 11.99,11 M19,11 A1,1 0 1 1 18.99,11",
            [IconKind.Send] = "M3,3 L22,12 3,21 6,12 Z M6,12 L22,12",
            [IconKind.Home] = "M3,11 L12,3 21,11 M5,10 L5,21 10,21 10,15 14,15 14,21 19,21 19,10",
            [IconKind.Components] = "M12,3 L21,8 12,13 3,8 Z M3,12 L12,17 21,12 M3,16 L12,21 21,16",
            [IconKind.Multiplayer] = "M12,3 A3,3 0 1 1 11.99,3 M5,21 L5,18 Q5,13 12,13 Q19,13 19,18 L19,21 M3,7 A2.5,2.5 0 0 1 3,12 M3,15 Q1,16 1,19 M21,7 A2.5,2.5 0 0 0 21,12 M21,15 Q23,16 23,19",
            [IconKind.Person] = "M12,3 A4,4 0 1 1 11.99,3 M4,21 L4,19 C4,12 20,12 20,19 L20,21",
            [IconKind.Bot] = "M12,3 L12,7 M10.5,2.5 A1.5,1.5 0 1 1 13.5,2.5 A1.5,1.5 0 1 1 10.5,2.5 M7,7 L17,7 Q20,7 20,10 L20,18 Q20,21 17,21 L7,21 Q4,21 4,18 L4,10 Q4,7 7,7 Z M1,12 L1,16 M23,12 L23,16 M8,11 L8,13 M16,11 L16,13 M9,17 L15,17",
            [IconKind.Settings] = "M4,5 L20,5 M4,12 L20,12 M4,19 L20,19 M9,2 L9,8 M16,9 L16,15 M8,16 L8,22",
            [IconKind.Shield] = "M12,2 L21,6 20,14 Q18,19 12,22 Q6,19 4,14 L3,6 Z M8,12 L11,15 17,9",
            [IconKind.Language] = "M3,5 L14,5 M8,2 L8,5 M12,5 Q11,12 3,16 M5,8 Q7,12 12,14 M13,21 L17,11 21,21 M14.5,18 L19.5,18",
            [IconKind.Palette] = "M12,3 C6,3 2,7 2,12 C2,18 7,22 12,21 C16,20 11,17 15,15 C17,14 22,17 22,11 C22,6 17,3 12,3 Z M7,8 L7,8.2 M12,6 L12,6.2 M17,8 L17,8.2 M6,13 L6,13.2",
            [IconKind.Sync] = "M3,10 A9,9 0 0 1 19,6 L21,9 M21,3 L21,9 15,9 M21,14 A9,9 0 0 1 5,18 L3,15 M3,21 L3,15 9,15",
            // The rear blade ends at the front blade's edges, preserving the crossing without stray strokes.
            [IconKind.Swords] = "M3,3 L7,4 18,15 15,18 4,7 Z M12,9 L17,4 21,3 20,7 15,12 M9,12 L6,15 9,18 12,15 M14,19 L19,14 M16.5,16.5 L21,21 M19.5,22.5 L22.5,19.5 M5,14 L10,19 M7.5,16.5 L3,21 M1.5,19.5 L4.5,22.5",
            [IconKind.Chat] = "M3,3 L17,3 17,14 8,14 3,18 Z M20,8 L22,8 22,22 17,18 11,18",
            [IconKind.Bell] = "M5,17 L7,14 7,9 C7,2 17,2 17,9 L17,14 19,17 Z M9,20 Q12,24 15,20 M12,2 L12,3",
            [IconKind.BellMuted] = "M3,3 L21,21 M7,7 L7,14 5,17 15,17 M11,4 C16,3 17,6 17,9 L17,13 M9,20 Q12,24 15,20",
            [IconKind.ReleaseNotes] = "M5,2 L15,2 20,7 20,22 5,22 Z M15,2 L15,7 20,7 M8,11 L16,11 M8,15 L16,15 M8,19 L13,19",
            [IconKind.History] = "M3,10 A9,9 0 1 1 5,18 M3,4 L3,10 9,10 M12,7 L12,12 16,14",
            [IconKind.Clock] = "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,12 16,14",
            [IconKind.Route] = "M5,20 A2,2 0 1 1 5.01,20 M5,16 L5,13 Q5,10 9,10 L15,10 Q19,10 19,6 L19,3 M15,6 L19,2 23,6",
            [IconKind.Siege] = "M5,21 A2,2 0 1 1 5.01,21 M18,21 A2,2 0 1 1 18.01,21 M3,17 L20,17 M7,16 L10,8 16,16 M11,10 L5,3 M3,4 L7,2 M10,9 L19,4 21,5",
            [IconKind.Copy] = "M8,8 L21,8 21,21 8,21 Z M16,5 L16,3 3,3 3,16 5,16",
            [IconKind.Paste] = "M8,5 L4,5 4,22 20,22 20,5 16,5 M8,3 L16,3 16,7 8,7 Z M8,12 L16,12 M8,16 L14,16",
            [IconKind.Check] = "M4,12 L9,17 20,6",
            [IconKind.Warning] = "M12,3 L22,21 2,21 Z M12,9 L12,14 M12,17 L12,17.3",
            [IconKind.Help] = "M8,8 C8,2 18,2 17,8 C17,11 12,11 12,15 M12,19 L12,19.3",
            [IconKind.Play] = "M7,3 L21,12 7,21 Z",
            [IconKind.Download] = "M12,3 L12,16 M7,11 L12,16 17,11 M3,16 L3,21 21,21 21,16",
            [IconKind.Folder] = "M3,8 L3,4 10,4 13,7 21,7 21,10 M3,10 L22,10 19,21 2,21 Z",
            [IconKind.Diagnostics] = "M5,2 L15,2 20,7 20,22 5,22 Z M15,2 L15,7 20,7 M7,14 L10,14 12,10 14,18 16,14 18,14",
            [IconKind.Undo] = "M3,3 L3,10 10,10 M3,10 Q8,2 16,6 Q24,11 18,18 Q13,23 7,19",
            [IconKind.Trash] = "M3,6 L21,6 M8,6 L8,3 16,3 16,6 M5,6 L6,22 18,22 19,6 M10,10 L10,18 M14,10 L14,18",
            [IconKind.Compare] = "M9,3 L3,3 3,21 9,21 M15,3 L21,3 21,21 15,21 M8,10 L11,7 8,4 M11,7 L6,7 M16,14 L13,17 16,20 M13,17 L18,17",
            [IconKind.Save] = "M3,3 L18,3 21,6 21,21 3,21 Z M7,3 L7,9 17,9 17,3 M7,21 L7,14 17,14 17,21",
            [IconKind.Search] = "M10,3 A7,7 0 1 1 9.99,3 M15,15 L22,22",
            [IconKind.Close] = "M6,6 L18,18 M6,18 L18,6",
            [IconKind.Minimize] = "M5,12 L19,12",
            [IconKind.AddFriend] = "M13,6 A4,4 0 1 1 5,6 A4,4 0 1 1 13,6 M2,22 L2,19 C2,12 16,12 16,19 M19,8 L19,16 M15,12 L23,12",
            [IconKind.Requests] = "M3,5 L21,5 21,19 3,19 Z M3,6 L12,13 21,6",
            [IconKind.BlockedUsers] = "M12,6 A3,3 0 1 1 6,6 A3,3 0 1 1 12,6 M3,21 L3,18 C3,13 9,12 12,14 M23,18 A5,5 0 1 1 13,18 A5,5 0 1 1 23,18 M14.5,21.5 L21.5,14.5",
            [IconKind.Logout] = "M10,3 L3,3 3,21 10,21 M8,12 L22,12 M17,7 L22,12 17,17"
        };
        return paths.ToDictionary(p => p.Key, p => { var g = Geometry.Parse(p.Value); g.Freeze(); return g; });
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!Shapes.TryGetValue(Kind, out var geometry)) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0) return;
        var pen = new Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(Kind == IconKind.Discord ? Foreground : null, Kind == IconKind.Discord ? null : pen, geometry);
        dc.Pop(); dc.Pop();
    }
}
