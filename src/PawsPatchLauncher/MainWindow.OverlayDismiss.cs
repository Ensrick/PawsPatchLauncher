using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PawsPatchLauncher;
public partial class MainWindow
{
    private static bool InsideCard(DependencyObject? source, DependencyObject card)
    {
        var pending=new Stack<DependencyObject>();
        var visited=new HashSet<DependencyObject>();
        if(source is not null)pending.Push(source);
        while(pending.TryPop(out var node))
        {
            if(!visited.Add(node))continue;
            if(ReferenceEquals(node,card))return true;
            // Dropdowns have their own visual root but still belong to the control in the card.
            if(node is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is { } owner)pending.Push(owner);
            if(node is Popup {PlacementTarget: { } target})pending.Push(target);
            if(node is Visual or Visual3D && VisualTreeHelper.GetParent(node) is { } visual)pending.Push(visual);
            if(LogicalTreeHelper.GetParent(node) is { } logical)pending.Push(logical);
            if(node is FrameworkContentElement {Parent: { } parent})pending.Push(parent);
        }
        return false;
    }

    private async void Overlay_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is not FrameworkElement overlay||overlay.Visibility!=Visibility.Visible)return;
        var card=ReferenceEquals(overlay,ConfirmationOverlay)?ConfirmationCard:HelpCard;
        if(InsideCard(e.OriginalSource as DependencyObject,card))return;
        // Consume the initial press: never activate an underlying row/button, and close only the top layer.
        e.Handled=true;
        if(ReferenceEquals(overlay,ConfirmationOverlay))await CompleteConfirmationAsync(false);
        else if(!ConfirmationActive)await CloseHelpAsync();
    }
}
