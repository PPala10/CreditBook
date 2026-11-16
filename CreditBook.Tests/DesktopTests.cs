using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CreditBook.Storage;
using CreditBook.UI.ViewModels;
using CreditBook.UI.Views;

public static class DesktopTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Desktop customer editor, filters, statement and backup screens render and bind", () =>
        {
            AppBuilder.Configure<CreditBook.UI.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var directory=Path.Combine(Path.GetTempPath(),"creditbook-desktop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                var ledger=new Ledger(Path.Combine(directory,"screen.db"));var customer=ledger.AddCustomer("Şule Işık","05551234567");
                ledger.AddDebt(customer,125.50m,"Ekran testi",DateTime.Today);
                var vm=new MainWindowViewModel(ledger);
                var window=new MainWindow { DataContext=vm };
                try
                {
                    window.Show();Pump(()=>!vm.IsBusy && vm.IsReady);
                    var dateFilters=window.GetVisualDescendants().OfType<CalendarDatePicker>().ToArray();
                    dateFilters[0].SelectedDate=DateTime.Today;Dispatcher.UIThread.RunJobs();
                    if(vm.FilterFrom?.Date!=DateTime.Today)throw new Exception("Date filter binding did not update");
                    vm.ClearDebtFiltersCommand.Execute(null);
                    var tabs=window.GetVisualDescendants().OfType<TabControl>().Single();
                    foreach(var size in new[]{(1200d,900d),(980d,740d)})
                    {
                        window.Width=size.Item1;window.Height=size.Item2;
                        foreach(var header in new[]{"Borçlar","Tahsilat geçmişi","Müşteri","Ekstre","Yakın hesapları"})
                        {
                            tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>(string?)t.Header==header);
                            Dispatcher.UIThread.RunJobs();
                            using var frame=window.CaptureRenderedFrame()??throw new Exception("Screen did not render");
                            if(header=="Borçlar" && size.Item1==1200)frame.Save(Path.Combine(Path.GetTempPath(),"creditbook-desktop.png"));
                        }
                    }
                    tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>(string?)t.Header=="Müşteri");Dispatcher.UIThread.RunJobs();
                    var note=window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.PlaceholderText=="Müşteri notları");
                    note.Text="Ekrandan müşteri notu";Dispatcher.UIThread.RunJobs();
                    if(vm.EditCustomerNote!="Ekrandan müşteri notu")throw new Exception("Customer note binding did not update");
                    var save=vm.SaveCustomerCommand.ExecuteAsync(null);Pump(()=>save.IsCompleted);save.GetAwaiter().GetResult();
                    if(ledger.GetAccount(customer).Customer.Note!="Ekrandan müşteri notu")throw new Exception("Customer editor did not persist");
                    var backup=new BackupWindow(vm);backup.Measure(new Size(740,850));
                    if(backup.Content is null)throw new Exception("Backup settings content missing");
                }
                finally{window.Close();Dispatcher.UIThread.RunJobs();}
            }
            finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
        });
    }
    private static void Pump(Func<bool> done)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(!done())
        {
            Dispatcher.UIThread.RunJobs();if(DateTime.UtcNow>deadline)throw new Exception("Desktop operation timed out");Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
