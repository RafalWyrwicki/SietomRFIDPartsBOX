using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PartsBox;
public sealed class Settings
{
 public string ConnectionString { get; set; } = "";
 public bool DemoMode { get; set; } = false;
 public string ReaderHost { get; set; } = "";
 public string ReaderIp { get; set; } = "192.168.103.45";
 public string ReaderAddressMode { get; set; } = "Auto";
 public uint ReaderPort { get; set; } = 5084;
 public int SessionTimeoutMinutes { get; set; } = 3;
 public string CardReaderDeviceId { get; set; } = "VID_09D8&PID_0410";
 public bool RequireSapConnection { get; set; } = false;
}
public record Employee(long Id, string Name, string Barcode = "", string Position = "");
public record Order(string Number,string Description,DateTime? CreatedDate=null,string UserStatus="",string Equipment="",string EquipmentDescription="",string LocationDescription="",string FunctionalLocation="")
{
 public static bool HasInit(string status)=>status.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Any(s=>s.Equals("INIT",StringComparison.OrdinalIgnoreCase));
 public override string ToString()=>$"{Number} {Description} {Equipment} {EquipmentDescription} {LocationDescription} {FunctionalLocation} {UserStatus}";
}
public record Part(string Epc, string Material, string Name, string State);
public record ScanRow(string EPC, string Indeks, string Nazwa, string Status);
public record Receipt(Guid Id, string Order, DateTime Date) { public override string ToString() => $"{Order} / {Date:dd.MM.yyyy HH:mm} / {Id.ToString()[..8]}"; }
public record HistoryRow(Guid Id, DateTime Data, string Operacja, string Pracownik, string Zlecenie, string EPC, string Indeks, string Nazwa, string Rozliczenie);
public abstract class Observable : INotifyPropertyChanged
{
 public event PropertyChangedEventHandler? PropertyChanged;
 protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
 protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (Equals(field,value)) return false; field=value; Changed(name); return true; }
}
public sealed class Command(Func<Task> action, Func<bool>? allowed = null) : ICommand
{
 bool running;
 public bool CanExecute(object? p) => !running && (allowed?.Invoke() ?? true);
 public async void Execute(object? p) { if (!CanExecute(p)) return; running=true; CommandManager.InvalidateRequerySuggested(); try { await action(); } finally { running=false; CommandManager.InvalidateRequerySuggested(); } }
 public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
