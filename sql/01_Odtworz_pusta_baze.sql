-- UWAGA: usuwa CAŁĄ bazę SietomPartsBox, jej historię i dane.
-- Uruchom ręcznie na właściwym serwerze jako administrator, po zamknięciu aplikacji.
USE [master];
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_ID(N'SietomPartsBox') IS NOT NULL
BEGIN
 ALTER DATABASE [SietomPartsBox] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
 DROP DATABASE [SietomPartsBox];
END;
CREATE DATABASE [SietomPartsBox];
EXEC(N'USE [SietomPartsBox];
CREATE TABLE Employees(Id bigint PRIMARY KEY, Name nvarchar(150) NOT NULL, SiteCode int NOT NULL, CardNumber bigint NOT NULL, Active bit NOT NULL, UNIQUE(SiteCode,CardNumber));
CREATE TABLE Orders(Number nvarchar(50) PRIMARY KEY, Description nvarchar(1000) NOT NULL, Active bit NOT NULL, CreatedDate date NULL, UserStatus nvarchar(120) NOT NULL DEFAULT N'''', Equipment nvarchar(80) NOT NULL DEFAULT N'''', EquipmentDescription nvarchar(500) NOT NULL DEFAULT N'''', LocationDescription nvarchar(500) NOT NULL DEFAULT N'''', FunctionalLocation nvarchar(150) NOT NULL DEFAULT N'''', ImportId uniqueidentifier NULL);
CREATE TABLE Parts(Epc nvarchar(128) PRIMARY KEY, Material nvarchar(80) NOT NULL, Name nvarchar(250) NOT NULL, State nvarchar(30) NOT NULL, StockQuantity decimal(18,3) NOT NULL DEFAULT 0, Barcode nvarchar(128) NOT NULL DEFAULT N'''');
CREATE TABLE Operations(Id uniqueidentifier PRIMARY KEY, Kind nvarchar(20) NOT NULL, EmployeeId bigint NOT NULL REFERENCES Employees(Id), OrderNumber nvarchar(50) NULL REFERENCES Orders(Number), SourceId uniqueidentifier NULL REFERENCES Operations(Id), CreatedUtc datetime2 NOT NULL, Settlement nvarchar(60) NOT NULL);
CREATE TABLE Items(OperationId uniqueidentifier REFERENCES Operations(Id), Epc nvarchar(128) REFERENCES Parts(Epc), Material nvarchar(80) NOT NULL, Name nvarchar(250) NOT NULL, PRIMARY KEY(OperationId,Epc));
CREATE TABLE Audit(Id bigint IDENTITY PRIMARY KEY, OccurredUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), EmployeeId bigint NULL, Event nvarchar(40) NOT NULL, Details nvarchar(max) NOT NULL);
CREATE TABLE Outbox(OperationId uniqueidentifier PRIMARY KEY REFERENCES Operations(Id), Status nvarchar(30) NOT NULL, CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE TABLE InventoryReadings(OperationId uniqueidentifier NOT NULL REFERENCES Operations(Id),Epc nvarchar(128) NOT NULL,Material nvarchar(80) NOT NULL,Name nvarchar(250) NOT NULL,Status nvarchar(60) NOT NULL,PRIMARY KEY(OperationId,Epc));
CREATE TABLE LocalKeyboardCards(ReaderNumber nvarchar(40) PRIMARY KEY, EmployeeId bigint NOT NULL REFERENCES Employees(Id), Barcode nvarchar(40) NOT NULL UNIQUE, Position nvarchar(250) NOT NULL);
CREATE TABLE OrderParts(OrderNumber nvarchar(50) NOT NULL REFERENCES Orders(Number),Epc nvarchar(128) NOT NULL REFERENCES Parts(Epc),PRIMARY KEY(OrderNumber,Epc));
CREATE TABLE FileImports(Id uniqueidentifier PRIMARY KEY,Kind nvarchar(30) NOT NULL,FileName nvarchar(260) NOT NULL,FileHash char(64) NOT NULL,ImportedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),[RowCount] int NOT NULL,EmployeeId bigint NULL,UNIQUE(Kind,FileHash));
CREATE TABLE ImportedStockRows(ImportId uniqueidentifier NOT NULL REFERENCES FileImports(Id),RowNumber int NOT NULL,Material nvarchar(80) NOT NULL,Description nvarchar(1000) NOT NULL,Unit nvarchar(20) NOT NULL,InventoryNumber nvarchar(80) NOT NULL,StorageLocation nvarchar(80) NOT NULL,Plant nvarchar(80) NOT NULL,Epc nvarchar(128) NOT NULL,Barcode nvarchar(128) NOT NULL,Quantity decimal(18,3) NOT NULL,PRIMARY KEY(ImportId,RowNumber));
CREATE TABLE TagReads(Id bigint IDENTITY PRIMARY KEY,OperationId uniqueidentifier NOT NULL REFERENCES Operations(Id),Epc nvarchar(128) NOT NULL,Source nvarchar(20) NOT NULL,ReadUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),UNIQUE(OperationId,Epc));
');
USE [SietomPartsBox];
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
 CASE WHEN OBJECT_ID(N'dbo.InventoryReadings',N'U') IS NULL THEN N'BRAK' ELSE N'OK' END AS InventoryReadings,
 CASE WHEN OBJECT_ID(N'dbo.Audit',N'U') IS NULL THEN N'BRAK' ELSE N'OK' END AS Audit,
 CASE WHEN OBJECT_ID(N'dbo.Outbox',N'U') IS NULL THEN N'BRAK' ELSE N'OK' END AS Outbox,
 N'Utworzono bazę SietomPartsBox.' AS Wynik;
