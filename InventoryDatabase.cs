using Microsoft.Data.SqlClient;
using System.Data;

namespace PartsBox;
public sealed partial class Database
{
 public async Task SaveInventory(Guid id,Employee employee,IReadOnlyList<ScanRow> rows)
 {
  if(rows.Count==0||rows.Select(r=>r.EPC).Distinct().Count()!=rows.Count)throw new InvalidOperationException("Pusta lista lub powtórzone EPC.");
  using var c=await Open();
  using var t=c.BeginTransaction(IsolationLevel.Serializable);
  using(var exists=Cmd(c,"SELECT COUNT(*) FROM Operations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",t,("@id",id)))if((int)(await exists.ExecuteScalarAsync())!>0){t.Commit();return;}
  using(var active=Cmd(c,"SELECT COUNT(*) FROM Employees WHERE Id=@id AND Active=1",t,("@id",employee.Id)))if((int)(await active.ExecuteScalarAsync())!!=1)throw new InvalidOperationException("Pracownik nieaktywny.");
  using(var header=Cmd(c,"INSERT Operations VALUES(@id,'Inventory',@e,NULL,NULL,SYSUTCDATETIME(),N'Raport inwentaryzacyjny')",t,("@id",id),("@e",employee.Id)))await header.ExecuteNonQueryAsync();
  foreach(var row in rows)
  {
   using var q=Cmd(c,"""
    IF COL_LENGTH('dbo.InventoryReadings','Tid') IS NOT NULL
      INSERT InventoryReadings(OperationId,Epc,Tid,Material,Name,Status) VALUES(@id,@epc,N'',@material,@name,@status);
    ELSE
      INSERT InventoryReadings(OperationId,Epc,Material,Name,Status) VALUES(@id,@epc,@material,@name,@status);
    """,t,("@id",id),("@epc",row.EPC),("@material",row.Indeks),("@name",row.Nazwa),("@status",row.Status));await q.ExecuteNonQueryAsync();
  }
  using(var audit=Cmd(c,"INSERT Audit(EmployeeId,Event,Details) VALUES(@e,'InventoryConfirmed',@id)",t,("@e",employee.Id),("@id",id.ToString())))await audit.ExecuteNonQueryAsync();
  t.Commit();
 }
}
