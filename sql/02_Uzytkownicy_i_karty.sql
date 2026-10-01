-- Tworzy dwóch użytkowników i karty testowe.
-- Uruchom po 01_Odtworz_pusta_baze.sql na bazie SietomPartsBox.
USE [SietomPartsBox];
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE Id=1)
 INSERT dbo.Employees(Id,Name,SiteCode,CardNumber,Active) VALUES (1,N'Jabłoński Artur',240,38032,1);
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE Id=2)
 INSERT dbo.Employees(Id,Name,SiteCode,CardNumber,Active) VALUES (2,N'Wywrot Julia',240,38386,1);
UPDATE dbo.Employees SET Name=N'Jabłoński Artur',SiteCode=240,CardNumber=38032,Active=1 WHERE Id=1;
UPDATE dbo.Employees SET Name=N'Wywrot Julia',SiteCode=240,CardNumber=38386,Active=1 WHERE Id=2;
DELETE FROM dbo.LocalKeyboardCards WHERE Barcode=N'TARC19571' AND EmployeeId<>1;
DELETE FROM dbo.LocalKeyboardCards WHERE Barcode=N'TARC19687' AND EmployeeId<>2;
IF EXISTS (SELECT 1 FROM dbo.LocalKeyboardCards WHERE EmployeeId=1)
 UPDATE dbo.LocalKeyboardCards SET ReaderNumber=N'240,38032',Barcode=N'TARC19571',Position=N'Zastępca Managera Obszaru Produkcyjnego' WHERE EmployeeId=1;
ELSE
 INSERT dbo.LocalKeyboardCards(ReaderNumber,EmployeeId,Barcode,Position) VALUES (N'240,38032',1,N'TARC19571',N'Zastępca Managera Obszaru Produkcyjnego');
IF EXISTS (SELECT 1 FROM dbo.LocalKeyboardCards WHERE EmployeeId=2)
 UPDATE dbo.LocalKeyboardCards SET ReaderNumber=N'240,38386',Barcode=N'TARC19687',Position=N'Stażystka w Dziale Zapewnienia Jakości' WHERE EmployeeId=2;
ELSE
 INSERT dbo.LocalKeyboardCards(ReaderNumber,EmployeeId,Barcode,Position) VALUES (N'240,38386',2,N'TARC19687',N'Stażystka w Dziale Zapewnienia Jakości');
COMMIT;
SELECT N'Utworzono lub potwierdzono dwóch użytkowników i karty testowe.' AS Wynik;
