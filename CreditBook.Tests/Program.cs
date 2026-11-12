using CreditBook.Core;
using CreditBook.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

var failures = 0;
void Test(string name, Action test) { try { test(); Console.WriteLine($"PASS {name}"); } catch(Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation error"); }
DebtRecord Debt(int id, decimal amount, int day) => new() { Id=id, CustomerId=1, Amount=amount, Date=new DateTime(2026,1,day) };

Test("FIFO preserves principal and allocates across oldest debts", () => {
 var first=Debt(1,100,1); var second=Debt(2,80,2);
 var result=LedgerRules.Allocate(new[]{second,first},120);
 Equal(2,result.Count); Equal(1,result[0].DebtId); Equal(100m,result[0].Amount); Equal(20m,result[1].Amount); Equal(100m,first.Amount);
});
Test("FIFO same-date tie uses ID", () => { var r=LedgerRules.Allocate(new[]{Debt(2,10,1),Debt(1,10,1)},5); Equal(1,r[0].DebtId); });
Test("Partial allocations reduce outstanding without changing principal", () => { var d=Debt(1,100,1); d.Allocations.Add(new PaymentAllocation { Amount=30 }); Equal(70m,d.Outstanding); Equal(100m,d.Amount); });
Test("Excess payment rejected", () => Reject(()=>LedgerRules.Allocate(new[]{Debt(1,10,1)},11)));
Test("Invalid money rejected", () => { foreach(var a in new[]{0m,-1m,1.001m}) Reject(()=>LedgerRules.ValidateAmount(a)); });
Test("Paid and void debts excluded", () => { var d=Debt(1,100,1); d.IsPaid=true; var v=Debt(2,100,1); v.IsVoided=true; Reject(()=>LedgerRules.Allocate(new[]{d,v},1)); });
Test("Score is deterministic and bounded", () => { var d=Debt(1,100,1); var now=new DateTime(2026,3,1); Equal(90,LedgerRules.Score(new[]{d},now)); Equal(90,LedgerRules.Score(new[]{d},now)); });
Test("Late determination uses dates, not notification flag", () => { var d=Debt(1,100,1); d.IsPaid=true; d.Allocations.Add(new PaymentAllocation { Amount=100, Payment=new Payment { Date=new DateTime(2026,3,1) } }); Equal(75,LedgerRules.Score(new[]{d},new DateTime(2026,3,2))); });

var folder=Path.Combine(Path.GetTempPath(),"creditbook-tests-"+Guid.NewGuid());
Directory.CreateDirectory(folder);
Ledger Open(string name) => new(Path.Combine(folder,name+".db"));
Test("Payment survives reopen with original principal and history", () => {
 var l=Open("history"); var c=l.AddCustomer("Ali","05551234567"); l.AddDebt(c,100,"Satış",DateTime.Today); l.RecordPayment(c,30,DateTime.Today);
 var a=Open("history").GetAccount(c); Equal(70m,a.Customer.Balance); Equal(100m,a.Debts.Single().Amount); Equal(30m,a.Payments.Single().Amount);
 l.RecordPayment(c,70,DateTime.Today); a=l.GetAccount(c); Equal(0m,a.Customer.Balance); Equal(1,a.Debts.Count); Equal(2,a.Payments.Count);
});
Test("Excess collection leaves database unchanged", () => {
 var l=Open("atomic");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,10,null,DateTime.Today);
 Reject(()=>l.RecordPayment(c,11,DateTime.Today));Equal(0,l.GetAccount(c).Payments.Count);Equal(10m,l.GetAccount(c).Customer.Balance);
});
Test("Family total is separate; unlink does not delete customer", () => {
 var l=Open("family");var a=l.AddCustomer("Ali","05551234567");var b=l.AddCustomer("Ayşe","05551234568");l.AddDebt(a,10,null,DateTime.Today);l.AddDebt(b,20,null,DateTime.Today);l.LinkRelative(a,b);
 var account=l.GetAccount(a);Equal(10m,account.Customer.Balance);Equal(30m,account.FamilyBalance);Equal(1,account.Debts.Count);
 l.UnlinkRelative(a,b);Equal(2,l.GetCustomers().Count);Equal(10m,l.GetAccount(a).FamilyBalance);
});
Test("Settlement is idempotent and cannot erase payment history", () => {
 var l=Open("settle");var c=l.AddCustomer("Ali","05551234567");var d=l.AddDebt(c,10,null,DateTime.Today);l.SettleDebt(d);l.SettleDebt(d);
 Equal(1,l.GetAccount(c).Payments.Count);Reject(()=>l.VoidDebt(d));Reject(()=>l.ArchiveCustomer(c));
});
Test("SQLite backup can reopen while source ledger is in use", () => {
 var l=Open("backup");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,19.99m,null,DateTime.Today);var target=Path.Combine(folder,"copy.db");l.Backup(target);
 var copy=new Ledger(target);Equal(19.99m,copy.GetAccount(c).Customer.Balance);
});
Test("Voided uncollected debt is retained and excluded from balance", () => {
 var l=Open("void");var c=l.AddCustomer("Ali","05551234567");var d=l.AddDebt(c,10,null,DateTime.Today);l.VoidDebt(d);
 Equal(0m,l.GetAccount(c).Customer.Balance);Equal(true,l.GetAccount(c).Debts.Single().IsVoided);
});
Test("Link is symmetric and unique", () => {var l=Open("links");var a=l.AddCustomer("Ali","05551234567");var b=l.AddCustomer("Ayşe","05551234568");l.LinkRelative(a,b);l.LinkRelative(b,a);Equal(1,l.GetAccount(a).Relatives.Count);Reject(()=>l.LinkRelative(a,a));});
Test("Customers with open debt cannot be archived", () => {var l=Open("archive");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,10,null,DateTime.Today);Reject(()=>l.ArchiveCustomer(c));});
Test("Reversing a payment preserves history and reopens original debts", () => {
 var l=Open("reverse");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,100,null,DateTime.Today);var payment=l.RecordPayment(c,100,DateTime.Today);
 l.ReversePayment(payment);l.ReversePayment(payment);var a=l.GetAccount(c);Equal(100m,a.Customer.Balance);Equal(1,a.Payments.Count);Equal(true,a.Payments[0].ReversedAt.HasValue);
 l.RecordPayment(c,25,DateTime.Today);Equal(75m,l.GetAccount(c).Customer.Balance);
});
Test("Viewmodel records customer-level payment and shows separate family total", () => {
 var l=Open("viewmodel");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,100,null,DateTime.Today);
 var vm=new CreditBook.UI.ViewModels.MainWindowViewModel(l);vm.InitializeAsync().GetAwaiter().GetResult();
 vm.NewPaymentAmount="25";vm.RecordPaymentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
 Equal(75m,vm.TotalDebtOfSelectedCustomer);Equal(75m,vm.FamilyTotalDebt);Equal(1,vm.Payments.Count);
 vm.NewPaymentAmount="999";vm.RecordPaymentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
 Equal(75m,vm.TotalDebtOfSelectedCustomer);Equal(false,string.IsNullOrEmpty(vm.ErrorMessage));
});
Test("Legacy EnsureCreated database is backed up and adopted without losing debt", () => {
 var path=Path.Combine(folder,"legacy.db");
 var options=new DbContextOptionsBuilder<CreditBookDbContext>().UseSqlite("Data Source="+path).Options;
 using(var db=new CreditBookDbContext(options)) {
  db.GetService<IMigrator>().Migrate("20260707062214_AddCustomerScore");
  db.Database.ExecuteSqlRaw("INSERT INTO Customers (Name,Phone,Score) VALUES ('Eski müşteri','05551234567',100)");
  db.Database.ExecuteSqlRaw("INSERT INTO DebtRecords (CustomerId,Amount,Date,IsPaid,OverdueNotificationSent) VALUES (1,'125.50','2026-01-01',0,0)");
  db.Database.ExecuteSqlRaw("DROP TABLE __EFMigrationsHistory");
 }
 var ledger=new Ledger(path);Equal(125.50m,ledger.GetAccount(1).Customer.Balance);
 Equal(1,Directory.GetFiles(folder,"legacy.db.before-ledger-*.bak").Length);
 ledger.RecordPayment(1,25.50m,DateTime.Today);Equal(100m,ledger.GetAccount(1).Customer.Balance);
});
Test("Unknown legacy schema fails before baseline or data changes", () => {
 var path=Path.Combine(folder,"unknown.db");
 using(var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+path)) { c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Customers VALUES (1,'Keep me')";cmd.ExecuteNonQuery(); }
 try { _=new Ledger(path);throw new Exception("Expected schema rejection"); }catch(InvalidOperationException) { }
 using var check=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+path);check.Open();using var q=check.CreateCommand();q.CommandText="SELECT Name FROM Customers WHERE Id=1";Equal("Keep me",(string)q.ExecuteScalar()!);
});
Test("Money precision and duplicate phone failures write nothing", () => {
 var l=Open("validation");var c=l.AddCustomer("Ali","05551234567");Reject(()=>l.AddCustomer("Ali again","05551234567"));Reject(()=>l.AddDebt(c,1.001m,null,DateTime.Today));Equal(1,l.GetCustomers().Count);Equal(0,l.GetAccount(c).Debts.Count);
});
Test("Overdue reminders survive reopening", () => {
 var l=Open("overdue");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,100,null,DateTime.Today.AddDays(-40));Equal(1,l.GetOverdue().Count);Equal(1,Open("overdue").GetOverdue().Count);
 l.RecordPayment(c,100,DateTime.Today);Equal(0,l.GetOverdue().Count);
});
Test("Filtering selected customer clears stale debts and totals", () => {
 var l=Open("filter");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,100,null,DateTime.Today);
 var vm=new CreditBook.UI.ViewModels.MainWindowViewModel(l);vm.InitializeAsync().GetAwaiter().GetResult();vm.SearchText="missing";
 Equal<CustomerSummary?>(null,vm.SelectedCustomer);Equal(0m,vm.TotalDebtOfSelectedCustomer);Equal(0,vm.Debts.Count);
});
Test("Failed account switch clears old records and disables edits", () => {
 var l=Open("failed-switch");var a=l.AddCustomer("Ali","05551234567");var b=l.AddCustomer("Bora","05551234568");l.AddDebt(a,100,null,DateTime.Today);
 var vm=new CreditBook.UI.ViewModels.MainWindowViewModel(l);vm.InitializeAsync().GetAwaiter().GetResult();
 using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+Path.Combine(folder,"failed-switch.db"))) {db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE Payments";cmd.ExecuteNonQuery();}
 vm.SelectedCustomer=vm.Customers.Single(c=>c.Id==b);
 if(!SpinWait.SpinUntil(()=>!vm.IsBusy,TimeSpan.FromSeconds(5)))throw new Exception("Load timeout");
 Equal(0,vm.Debts.Count);Equal(0m,vm.TotalDebtOfSelectedCustomer);Equal(false,vm.CanEdit);Equal(false,string.IsNullOrEmpty(vm.ErrorMessage));
});
foreach(var nullableName in new[]{true,false}) Test(nullableName ? "Same-column legacy with incompatible nullability is rejected" : "Same-column legacy with missing foreign keys is rejected", () => {
 var path=Path.Combine(folder,"incompatible-"+nullableName+".db");
 using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+path)) {
  db.Open();using var cmd=db.CreateCommand();
  var required=nullableName ? "" : "NOT NULL";
  cmd.CommandText=$"CREATE TABLE Customers (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT {required}, Phone TEXT, Note TEXT, Score INTEGER NOT NULL); CREATE TABLE DebtRecords (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, Amount TEXT NOT NULL, Description TEXT, Date TEXT NOT NULL, IsPaid INTEGER NOT NULL, OverdueNotificationSent INTEGER NOT NULL); CREATE TABLE CustomerRelatives (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, RelativeId INTEGER NOT NULL)";
  cmd.ExecuteNonQuery();
 }
 try { _=new Ledger(path);throw new Exception("Expected incompatible schema rejection"); }catch(InvalidOperationException) { }
 using var verify=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+path);verify.Open();using var q=verify.CreateCommand();q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='__EFMigrationsHistory'";Equal(0L,(long)q.ExecuteScalar()!);
});
Test("Cancellation eligibility matches storage after payment reversal", () => {
 var l=Open("cancel-eligibility");var c=l.AddCustomer("Ali","05551234567");l.AddDebt(c,100,null,DateTime.Today);var payment=l.RecordPayment(c,20,DateTime.Today);l.ReversePayment(payment);
 var debt=l.GetAccount(c).Debts.Single();var row=new CreditBook.UI.ViewModels.DebtItemViewModel(debt);Equal(false,row.CanVoid);Reject(()=>l.VoidDebt(debt.Id));
});
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
Directory.Delete(folder,true);
Console.WriteLine($"Failures: {failures}");
return failures == 0 ? 0 : 1;
