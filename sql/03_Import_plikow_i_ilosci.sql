-- Zasób techniczny aplikacji. Nie uruchamiać ręcznie.
-- Pusta baza tworzona przez 01_Odtworz_pusta_baze.sql zawiera już te obiekty.
IF OBJECT_ID(N'dbo.FileImports',N'U') IS NULL
 CREATE TABLE dbo.FileImports(Id uniqueidentifier PRIMARY KEY,Kind nvarchar(30) NOT NULL,FileName nvarchar(260) NOT NULL,FileHash char(64) NOT NULL,ImportedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),[RowCount] int NOT NULL,EmployeeId bigint NULL,UNIQUE(Kind,FileHash));
IF OBJECT_ID(N'dbo.ImportedStockRows',N'U') IS NULL
 CREATE TABLE dbo.ImportedStockRows(ImportId uniqueidentifier NOT NULL REFERENCES dbo.FileImports(Id),RowNumber int NOT NULL,Material nvarchar(80) NOT NULL,Description nvarchar(1000) NOT NULL,Unit nvarchar(20) NOT NULL,InventoryNumber nvarchar(80) NOT NULL,StorageLocation nvarchar(80) NOT NULL,Plant nvarchar(80) NOT NULL,Epc nvarchar(128) NOT NULL,Barcode nvarchar(128) NOT NULL,Quantity decimal(18,3) NOT NULL,PRIMARY KEY(ImportId,RowNumber));
IF COL_LENGTH('dbo.Parts','StockQuantity') IS NULL ALTER TABLE dbo.Parts ADD StockQuantity decimal(18,3) NOT NULL CONSTRAINT DF_Parts_StockQuantity DEFAULT 0;
IF COL_LENGTH('dbo.Parts','Barcode') IS NULL ALTER TABLE dbo.Parts ADD Barcode nvarchar(128) NOT NULL CONSTRAINT DF_Parts_Barcode DEFAULT N'';
