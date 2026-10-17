using System.Windows;
using System.Windows.Controls;

namespace PawsPatchLauncher;
public partial class MainWindow
{
    private void RenderConfigurationChanges(UserSettings before,UserSettings after,string? heading=null)
    {
        var rows=ConfigurationChanges.Compare(before,after,_text.Language);
        // Plain/accessibility representation stays aligned with the visual rows.
        ConfirmationPathText.Text=(heading is null?"":heading+"\n\n")+string.Join("\n",ConfigurationChanges.Describe(before,after,_text.Language));
        ConfirmationPathText.Visibility=Visibility.Collapsed;
        var panel=ConfirmationChangesPanel;panel.Visibility=Visibility.Visible;panel.Children.Clear();
        System.Windows.Automation.AutomationProperties.SetName(panel,ConfirmationPathText.Text);
        ConfirmationIconBadge.Background=ColorBrush("#39352C");ConfirmationIconBadge.BorderBrush=ColorBrush("#8F7746");ConfirmationActionIcon.Foreground=ColorBrush("#F0CD7D");
        if(heading is not null)panel.Children.Add(new TextBlock {Text=heading,TextWrapping=TextWrapping.Wrap,FontSize=13,Foreground=ColorBrush("#D9E6F5"),Margin=new Thickness(0,0,0,14)});
        if(rows.Count==0)
        {
            panel.Children.Add(new Border {Padding=new Thickness(12),Background=ColorBrush("#193B36"),CornerRadius=new CornerRadius(7),
                Child=new TextBlock {Text=T("Конфигурации совпадают.","Configurations match."),TextWrapping=TextWrapping.Wrap,Foreground=ColorBrush("#83DEB8")}});
            return;
        }
        Grid Columns()
        {
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(64)});
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(24)});
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(76)});
            return grid;
        }
        var header=Columns();header.Margin=new Thickness(10,0,10,8);
        header.Children.Add(new TextBlock {Text=T("ИЗМЕНЕНИЙ: ","CHANGES: ")+rows.Count,FontSize=11,Foreground=ColorBrush("#A8BBD2")});
        foreach(var (col,label) in new[]{(1,T("Сейчас","Current")),(3,T("Будет","New"))})
        {
            var text=new TextBlock {Text=label,FontSize=11,Foreground=ColorBrush(col==3?"#EAC978":"#A8BBD2"),HorizontalAlignment=HorizontalAlignment.Center};
            Grid.SetColumn(text,col);header.Children.Add(text);
        }
        panel.Children.Add(header);
        foreach(var change in rows)
        {
            var row=Columns();row.Tag=change;
            row.Children.Add(new TextBlock {Text=change.Name,TextWrapping=TextWrapping.Wrap,FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
            foreach(var (column,value) in new[]{(1,change.Before),(3,change.After)})
            {
                var chip=new Border {CornerRadius=new CornerRadius(5),Padding=new Thickness(4,6,4,6),VerticalAlignment=VerticalAlignment.Center,
                    Background=ColorBrush(column==3?"#23453E":"#26374D"),Child=new TextBlock {Text=value,FontSize=12,FontWeight=column==3?FontWeights.SemiBold:FontWeights.Normal,
                    Foreground=ColorBrush(column==3?"#91E4BB":"#AFBED0"),HorizontalAlignment=HorizontalAlignment.Center}};
                Grid.SetColumn(chip,column);row.Children.Add(chip);
            }
            var arrow=new TextBlock {Text="→",FontSize=15,Foreground=ColorBrush("#7892B1"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(arrow,2);row.Children.Add(arrow);
            panel.Children.Add(new Border {Background=ColorBrush("#142840"),CornerRadius=new CornerRadius(7),Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,6),Child=row});
        }
    }
}
