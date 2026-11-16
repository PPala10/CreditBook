using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CreditBook.UI.Views;

internal static class LedgerDialogs
{
    public static async Task<(string Reason, DateTime? Due)?> ReasonAsync(Window owner, string title, DateTime? due = null)
    {
        var dialog = NewWindow(title);
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        DatePicker? picker = null;
        if (due.HasValue)
        {
            panel.Children.Add(new TextBlock { Text = "Yeni vade" });
            picker = new DatePicker { SelectedDate = new DateTimeOffset(due.Value) }; panel.Children.Add(picker);
        }
        var reason = new TextBox { PlaceholderText = "İşlem gerekçesi (zorunlu)", AcceptsReturn = true, MinHeight = 80, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.Firebrick, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(reason); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Vazgeç" };cancel.Click += (_,_) => dialog.Close();
        var save = new Button { Content = "Kaydet" };save.Classes.Add("primary");
        save.Click += (_,_) => {
            if(string.IsNullOrWhiteSpace(reason.Text)) { error.Text="Gerekçe yazın.";return; }
            if(picker is not null && picker.SelectedDate is null) {error.Text="Vade seçin.";return;}
            dialog.Close(((string,DateTime?)?)(reason.Text.Trim(),picker?.SelectedDate?.Date));
        };
        buttons.Children.Add(cancel);buttons.Children.Add(save);panel.Children.Add(buttons);dialog.Content=panel;
        return await dialog.ShowDialog<(string,DateTime?)?>(owner);
    }

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog=NewWindow(title);
        var panel=new StackPanel {Spacing=16,Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text=message,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Vazgeç"};cancel.Click+=(_,_)=>dialog.Close(false);
        var ok=new Button{Content="Onayla"};ok.Click+=(_,_)=>dialog.Close(true);
        buttons.Children.Add(cancel);buttons.Children.Add(ok);panel.Children.Add(buttons);dialog.Content=panel;
        return await dialog.ShowDialog<bool>(owner);
    }
    private static Window NewWindow(string title)=>new(){Title=title,Width=480,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
}
