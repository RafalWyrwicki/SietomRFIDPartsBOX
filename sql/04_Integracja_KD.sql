-- Run manually as database administrator on the agreed SQL Server instance.
-- SQL Server 2016 SP1 or newer. No logins, passwords or server-wide permissions are created.
-- Additive migration of the EXISTING PartsBOX database. Never creates or replaces a database.
USE SietomPartsBox;
GO
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
    THROW 51000, 'Existing dbo.Employees is required. Run the PartsBOX schema first.', 1;
IF COLUMNPROPERTY(OBJECT_ID(N'dbo.Employees'), 'Id', 'IsIdentity') = 1
    THROW 51000, 'This migration requires the existing non-identity Employees.Id schema.', 1;
-- Optional primary card remains visible in Employees; all current cards live in AccessCardUsers.
-- NULL is used for an employee with no active cards, never a fictitious card number.
BEGIN TRY
    BEGIN TRAN;
    IF EXISTS(SELECT 1 FROM sys.foreign_key_columns WHERE referenced_object_id=OBJECT_ID(N'dbo.Employees')
        AND referenced_column_id IN (SELECT column_id FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Employees')
                                     AND name IN ('SiteCode','CardNumber')))
        THROW 51000, 'A foreign key references the primary card; review the schema before migration.', 1;
    DECLARE @Drop nvarchar(max)=N'';
    SELECT @Drop += CASE WHEN i.is_unique_constraint=1
        THEN N'ALTER TABLE dbo.Employees DROP CONSTRAINT '+QUOTENAME(i.name)+N';'
        ELSE N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.Employees;' END
    FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.Employees') AND i.is_unique=1 AND i.is_primary_key=0
        AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
        AND (SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
             WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0 AND c.name IN ('SiteCode','CardNumber'))=2;
    IF @Drop<>N'' EXEC sys.sp_executesql @Drop;
    ALTER TABLE dbo.Employees ALTER COLUMN SiteCode int NULL;
    ALTER TABLE dbo.Employees ALTER COLUMN CardNumber bigint NULL;
    CREATE UNIQUE INDEX UX_Employees_PrimaryCard ON dbo.Employees(SiteCode,CardNumber)
        WHERE SiteCode IS NOT NULL AND CardNumber IS NOT NULL;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
GO
IF OBJECT_ID(N'dbo.EmployeeApiLinks', N'U') IS NULL
    CREATE TABLE dbo.EmployeeApiLinks
    (
        ApiEmployeeId bigint NOT NULL CONSTRAINT PK_EmployeeApiLinks PRIMARY KEY,
        LocalEmployeeId bigint NOT NULL CONSTRAINT UQ_EmployeeApiLinks_Local UNIQUE
            REFERENCES dbo.Employees(Id)
    );
-- Capture the original card represented by a legacy USB reader alias BEFORE changing primary cards.
IF OBJECT_ID(N'dbo.AccessCardAliases', N'U') IS NULL
    CREATE TABLE dbo.AccessCardAliases
    (ReaderNumber nvarchar(40) NOT NULL PRIMARY KEY, SiteCode int NOT NULL, CardNumber bigint NOT NULL);
IF OBJECT_ID(N'dbo.LocalKeyboardCards', N'U') IS NOT NULL
    EXEC(N'INSERT dbo.AccessCardAliases(ReaderNumber,SiteCode,CardNumber)
        SELECT k.ReaderNumber,e.SiteCode,e.CardNumber FROM dbo.LocalKeyboardCards k JOIN dbo.Employees e ON e.Id=k.EmployeeId
        WHERE e.SiteCode IS NOT NULL AND e.CardNumber IS NOT NULL
            AND NOT EXISTS(SELECT 1 FROM dbo.AccessCardAliases a WHERE a.ReaderNumber=k.ReaderNumber)');
GO
IF OBJECT_ID(N'dbo.AccessCardUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessCardUsers
    (
        Environment varchar(3) NOT NULL CHECK (Environment IN ('STG','PRD')),
        SiteCode int NOT NULL,
        CardNumber bigint NOT NULL,
        EmployeeId bigint NOT NULL,
        FirstName nvarchar(200) NOT NULL,
        LastName nvarchar(200) NOT NULL,
        Active bit NOT NULL,
        LastSynchronizedUtc datetime2(3) NOT NULL,
        ChangedUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_AccessCardUsers PRIMARY KEY (Environment, SiteCode, CardNumber)
    );
    CREATE INDEX IX_AccessCardUsers_Employee ON dbo.AccessCardUsers(Environment, EmployeeId);
END;
GO
IF OBJECT_ID(N'dbo.AccessCardSyncRuns', N'U') IS NULL
    CREATE TABLE dbo.AccessCardSyncRuns
    (
        RunId uniqueidentifier NOT NULL CONSTRAINT PK_AccessCardSyncRuns PRIMARY KEY,
        Environment varchar(3) NOT NULL CHECK (Environment IN ('STG','PRD')),
        StartedUtc datetime2(3) NOT NULL,
        FinishedUtc datetime2(3) NULL,
        Status varchar(16) NOT NULL CHECK (Status IN ('Running','Succeeded','Failed','Abandoned')),
        DownloadedCount int NULL,
        DeactivatedCount int NULL,
        ErrorCode varchar(64) NULL
    );
GO
IF OBJECT_ID(N'dbo.AccessCardSyncState', N'U') IS NULL
    CREATE TABLE dbo.AccessCardSyncState
    (
        Environment varchar(3) NOT NULL CONSTRAINT PK_AccessCardSyncState PRIMARY KEY CHECK(Environment IN ('STG','PRD')),
        LastSuccessfulUtc datetime2(3) NULL,
        LastSuccessfulRunId uniqueidentifier NULL REFERENCES dbo.AccessCardSyncRuns(RunId)
    );
GO
IF TYPE_ID(N'dbo.CardSnapshot') IS NULL
    EXEC(N'CREATE TYPE dbo.CardSnapshot AS TABLE
    (EmployeeId bigint NOT NULL, FirstName nvarchar(200) NOT NULL, LastName nvarchar(200) NOT NULL,
     SiteCode int NOT NULL, CardNumber bigint NOT NULL, PRIMARY KEY(SiteCode, CardNumber))');
GO
IF TYPE_ID(N'dbo.CardConflictKey') IS NULL
    EXEC(N'CREATE TYPE dbo.CardConflictKey AS TABLE(SiteCode int NOT NULL,CardNumber bigint NOT NULL,PRIMARY KEY(SiteCode,CardNumber))');
IF COL_LENGTH('dbo.AccessCardSyncRuns','SourceRecordCount') IS NULL
    ALTER TABLE dbo.AccessCardSyncRuns ADD SourceRecordCount int NULL;
IF COL_LENGTH('dbo.AccessCardSyncRuns','ConflictingCardCount') IS NULL
    ALTER TABLE dbo.AccessCardSyncRuns ADD ConflictingCardCount int NULL;
IF COL_LENGTH('dbo.AccessCardSyncRuns','IdenticalDuplicateCount') IS NULL
    ALTER TABLE dbo.AccessCardSyncRuns ADD IdenticalDuplicateCount int NULL;
IF OBJECT_ID(N'dbo.AccessCardConflicts',N'U') IS NULL
    CREATE TABLE dbo.AccessCardConflicts
    (Environment varchar(3) NOT NULL,SiteCode int NOT NULL,CardNumber bigint NOT NULL,
     FirstDetectedUtc datetime2(3) NOT NULL,LastDetectedUtc datetime2(3) NOT NULL,ResolvedUtc datetime2(3) NULL,
     LastRunId uniqueidentifier NOT NULL REFERENCES dbo.AccessCardSyncRuns(RunId),
     CONSTRAINT PK_AccessCardConflicts PRIMARY KEY(Environment,SiteCode,CardNumber));
GO
CREATE OR ALTER PROCEDURE dbo.AcquireSyncLock @Environment varchar(3)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Environment NOT IN ('STG','PRD') THROW 51000, 'Invalid environment.', 1;
    DECLARE @Result int, @Resource nvarchar(255) = N'SietomAccessControl:' + @Environment;
    EXEC @Result = sys.sp_getapplock @Resource=@Resource, @LockMode='Exclusive',
        @LockOwner='Session', @LockTimeout=0;
    SELECT @Result;
END;
GO
CREATE OR ALTER PROCEDURE dbo.BeginSync @Environment varchar(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Resource nvarchar(255)=N'SietomAccessControl:' + @Environment;
    IF ISNULL(APPLOCK_MODE('public', @Resource, 'Session'), 'NoLock') <> 'Exclusive'
        THROW 51000, 'Synchronisation lock required.', 1;
    DECLARE @RunId uniqueidentifier=NEWID(), @Now datetime2(3)=SYSUTCDATETIME();
    BEGIN TRAN;
    -- A process crash leaves a Running row; holding the lock proves its owner has exited.
    UPDATE dbo.AccessCardSyncRuns SET Status='Abandoned', FinishedUtc=@Now, ErrorCode='PreviousProcessExited'
        WHERE Environment=@Environment AND Status='Running';
    IF NOT EXISTS(SELECT 1 FROM dbo.AccessCardSyncState WHERE Environment=@Environment)
        INSERT dbo.AccessCardSyncState(Environment) VALUES(@Environment);
    INSERT dbo.AccessCardSyncRuns(RunId,Environment,StartedUtc,Status)
        VALUES(@RunId,@Environment,@Now,'Running');
    COMMIT;
    SELECT @RunId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ApplySnapshot
    @Environment varchar(3), @RunId uniqueidentifier, @Cards dbo.CardSnapshot READONLY,
    @AllowEmpty bit=0, @MaxDeactivationFraction float=0.20,
    @Conflicts dbo.CardConflictKey READONLY,@SourceRecordCount int=NULL,@IdenticalDuplicateCount int=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Resource nvarchar(255)=N'SietomAccessControl:' + @Environment;
    IF ISNULL(APPLOCK_MODE('public', @Resource, 'Session'), 'NoLock') <> 'Exclusive'
        THROW 51000, 'Synchronisation lock required.', 1;
    IF @MaxDeactivationFraction IS NULL OR @MaxDeactivationFraction < 0 OR @MaxDeactivationFraction > 1
        THROW 51000, 'Invalid deactivation limit.', 1;
    IF EXISTS(SELECT 1 FROM @Cards c JOIN @Conflicts x ON x.SiteCode=c.SiteCode AND x.CardNumber=c.CardNumber)
        THROW 51000, 'A disputed card cannot also be authorized.', 1;
    IF NOT EXISTS(SELECT 1 FROM @Cards) AND NOT EXISTS(SELECT 1 FROM @Conflicts) AND ISNULL(@AllowEmpty,0)=0
        THROW 51001, 'Empty snapshot blocked.', 1;
    IF EXISTS(SELECT 1 FROM @Cards WHERE LEN(LTRIM(RTRIM(FirstName)))=0 OR LEN(LTRIM(RTRIM(LastName)))=0)
        THROW 51000, 'Invalid card name.', 1;
    IF EXISTS(SELECT 1 FROM @Cards WHERE LEN(FirstName+N' '+LastName)>150)
        THROW 51000, 'Card data cannot be represented by Employees.', 1;
    IF EXISTS(SELECT EmployeeId FROM @Cards GROUP BY EmployeeId
        HAVING MIN(FirstName COLLATE Latin1_General_100_BIN2)<>MAX(FirstName COLLATE Latin1_General_100_BIN2)
            OR MIN(LastName COLLATE Latin1_General_100_BIN2)<>MAX(LastName COLLATE Latin1_General_100_BIN2))
        THROW 51000, 'Conflicting names for one employee.', 1;
    BEGIN TRY
        BEGIN TRAN;
        IF NOT EXISTS(SELECT 1 FROM dbo.AccessCardSyncRuns WHERE RunId=@RunId AND Environment=@Environment AND Status='Running')
            THROW 51000, 'Invalid run.', 1;
        DECLARE @OldCount int=(SELECT COUNT(*) FROM dbo.AccessCardUsers WHERE Environment=@Environment AND Active=1);
        DECLARE @Removed int=(SELECT COUNT(*) FROM dbo.AccessCardUsers u WHERE u.Environment=@Environment AND u.Active=1
            AND NOT EXISTS(SELECT 1 FROM @Cards c WHERE c.SiteCode=u.SiteCode AND c.CardNumber=u.CardNumber));
        DECLARE @UnexpectedRemoved int=(SELECT COUNT(*) FROM dbo.AccessCardUsers u WHERE u.Environment=@Environment AND u.Active=1
            AND NOT EXISTS(SELECT 1 FROM @Cards c WHERE c.SiteCode=u.SiteCode AND c.CardNumber=u.CardNumber)
            AND NOT EXISTS(SELECT 1 FROM @Conflicts x WHERE x.SiteCode=u.SiteCode AND x.CardNumber=u.CardNumber));
        IF @OldCount>0 AND CONVERT(float,@UnexpectedRemoved)/@OldCount > @MaxDeactivationFraction
            THROW 51002, 'Deactivation limit exceeded.', 1;
        DECLARE @Now datetime2(3)=SYSUTCDATETIME();
        UPDATE x SET LastDetectedUtc=@Now,ResolvedUtc=NULL,LastRunId=@RunId
            FROM dbo.AccessCardConflicts x JOIN @Conflicts c ON c.SiteCode=x.SiteCode AND c.CardNumber=x.CardNumber
            WHERE x.Environment=@Environment;
        INSERT dbo.AccessCardConflicts(Environment,SiteCode,CardNumber,FirstDetectedUtc,LastDetectedUtc,LastRunId)
            SELECT @Environment,c.SiteCode,c.CardNumber,@Now,@Now,@RunId FROM @Conflicts c
            WHERE NOT EXISTS(SELECT 1 FROM dbo.AccessCardConflicts x WHERE x.Environment=@Environment AND x.SiteCode=c.SiteCode AND x.CardNumber=c.CardNumber);
        UPDATE x SET ResolvedUtc=@Now FROM dbo.AccessCardConflicts x WHERE x.Environment=@Environment AND x.ResolvedUtc IS NULL
            AND NOT EXISTS(SELECT 1 FROM @Conflicts c WHERE c.SiteCode=x.SiteCode AND c.CardNumber=x.CardNumber);
        UPDATE u SET EmployeeId=c.EmployeeId, FirstName=c.FirstName, LastName=c.LastName, Active=1,
            ChangedUtc=CASE WHEN u.Active=0 OR u.EmployeeId<>c.EmployeeId
                OR u.FirstName COLLATE Latin1_General_100_BIN2 <> c.FirstName COLLATE Latin1_General_100_BIN2
                OR u.LastName COLLATE Latin1_General_100_BIN2 <> c.LastName COLLATE Latin1_General_100_BIN2
                THEN @Now ELSE u.ChangedUtc END,
            LastSynchronizedUtc=@Now
        FROM dbo.AccessCardUsers u JOIN @Cards c ON c.SiteCode=u.SiteCode AND c.CardNumber=u.CardNumber
        WHERE u.Environment=@Environment;
        INSERT dbo.AccessCardUsers(Environment,SiteCode,CardNumber,EmployeeId,FirstName,LastName,Active,LastSynchronizedUtc,ChangedUtc)
            SELECT @Environment,c.SiteCode,c.CardNumber,c.EmployeeId,c.FirstName,c.LastName,1,@Now,@Now
            FROM @Cards c WHERE NOT EXISTS(SELECT 1 FROM dbo.AccessCardUsers u
                WHERE u.Environment=@Environment AND u.SiteCode=c.SiteCode AND u.CardNumber=c.CardNumber);
        UPDATE u SET Active=0, ChangedUtc=@Now, LastSynchronizedUtc=@Now
        FROM dbo.AccessCardUsers u WHERE u.Environment=@Environment AND u.Active=1
            AND NOT EXISTS(SELECT 1 FROM @Cards c WHERE c.SiteCode=u.SiteCode AND c.CardNumber=u.CardNumber);
        -- STG snapshots NEVER change production Employees or their card mappings.
        IF @Environment='PRD'
        BEGIN
            -- Lock Id allocation and all primary-card changes until the complete snapshot commits.
            DECLARE @MaxId bigint;
            SELECT @MaxId=ISNULL(MAX(Id),0) FROM dbo.Employees WITH(TABLOCKX,HOLDLOCK);
            DECLARE @People TABLE(ApiEmployeeId bigint PRIMARY KEY,Name nvarchar(150),ReverseName nvarchar(150));
            INSERT @People SELECT EmployeeId,MIN(FirstName)+N' '+MIN(LastName),MIN(LastName)+N' '+MIN(FirstName)
                FROM @Cards GROUP BY EmployeeId;
            DECLARE @Candidates TABLE(ApiEmployeeId bigint,LocalEmployeeId bigint,PRIMARY KEY(ApiEmployeeId,LocalEmployeeId));
            INSERT @Candidates SELECT DISTINCT p.ApiEmployeeId,e.Id
                FROM @People p JOIN @Cards c ON c.EmployeeId=p.ApiEmployeeId
                JOIN dbo.Employees e ON e.SiteCode=c.SiteCode AND e.CardNumber=c.CardNumber
                WHERE NOT EXISTS(SELECT 1 FROM dbo.EmployeeApiLinks l WHERE l.ApiEmployeeId=p.ApiEmployeeId)
                    AND NOT EXISTS(SELECT 1 FROM dbo.EmployeeApiLinks l WHERE l.LocalEmployeeId=e.Id);
            -- Initial mapping by card is accepted only if the stored person's name agrees.
            IF EXISTS(SELECT 1 FROM @Candidates c JOIN @People p ON p.ApiEmployeeId=c.ApiEmployeeId
                JOIN dbo.Employees e ON e.Id=c.LocalEmployeeId
                WHERE LTRIM(RTRIM(e.Name)) NOT IN (p.Name,p.ReverseName))
                THROW 51003, 'Existing card owner differs; explicitly map the employee before importing.', 1;
            IF EXISTS(SELECT ApiEmployeeId FROM @Candidates GROUP BY ApiEmployeeId HAVING COUNT(*)>1)
                OR EXISTS(SELECT LocalEmployeeId FROM @Candidates GROUP BY LocalEmployeeId HAVING COUNT(*)>1)
                THROW 51003, 'Ambiguous employee mapping; preserve history and resolve manually.', 1;
            INSERT dbo.EmployeeApiLinks(ApiEmployeeId,LocalEmployeeId)
                SELECT ApiEmployeeId,LocalEmployeeId FROM @Candidates;
            DECLARE @NewPeople TABLE(ApiEmployeeId bigint PRIMARY KEY,LocalEmployeeId bigint,Name nvarchar(150));
            INSERT @NewPeople SELECT p.ApiEmployeeId,@MaxId+ROW_NUMBER() OVER(ORDER BY p.ApiEmployeeId),p.Name
                FROM @People p WHERE NOT EXISTS(SELECT 1 FROM dbo.EmployeeApiLinks l WHERE l.ApiEmployeeId=p.ApiEmployeeId);
            INSERT dbo.Employees(Id,Name,SiteCode,CardNumber,Active)
                SELECT LocalEmployeeId,Name,NULL,NULL,1 FROM @NewPeople;
            INSERT dbo.EmployeeApiLinks(ApiEmployeeId,LocalEmployeeId)
                SELECT ApiEmployeeId,LocalEmployeeId FROM @NewPeople;
            -- Remember a still-active primary card; otherwise choose a deterministic replacement.
            DECLARE @PrimaryCards TABLE(LocalEmployeeId bigint PRIMARY KEY,SiteCode int,CardNumber bigint);
            INSERT @PrimaryCards SELECT l.LocalEmployeeId,c.SiteCode,c.CardNumber
                FROM dbo.EmployeeApiLinks l JOIN dbo.Employees e ON e.Id=l.LocalEmployeeId
                CROSS APPLY(SELECT TOP(1) s.SiteCode,s.CardNumber FROM @Cards s WHERE s.EmployeeId=l.ApiEmployeeId
                    ORDER BY CASE WHEN s.SiteCode=e.SiteCode AND s.CardNumber=e.CardNumber THEN 0 ELSE 1 END,
                        s.SiteCode,s.CardNumber) c;
            -- Clear managed primary cards first so swaps/reassignments cannot hit the unique index.
            UPDATE e SET SiteCode=NULL,CardNumber=NULL,Active=0
                FROM dbo.Employees e JOIN dbo.EmployeeApiLinks l ON l.LocalEmployeeId=e.Id;
            UPDATE e SET Name=p.Name,SiteCode=c.SiteCode,CardNumber=c.CardNumber,Active=1
                FROM dbo.Employees e JOIN dbo.EmployeeApiLinks l ON l.LocalEmployeeId=e.Id
                JOIN @People p ON p.ApiEmployeeId=l.ApiEmployeeId
                JOIN @PrimaryCards c ON c.LocalEmployeeId=e.Id;
        END;
        UPDATE dbo.AccessCardSyncRuns SET Status='Succeeded', FinishedUtc=@Now,
            DownloadedCount=(SELECT COUNT(*) FROM @Cards), DeactivatedCount=@Removed, ErrorCode=NULL,
            SourceRecordCount=COALESCE(@SourceRecordCount,(SELECT COUNT(*) FROM @Cards)),
            ConflictingCardCount=(SELECT COUNT(*) FROM @Conflicts),IdenticalDuplicateCount=@IdenticalDuplicateCount WHERE RunId=@RunId;
        UPDATE dbo.AccessCardSyncState SET LastSuccessfulUtc=@Now,LastSuccessfulRunId=@RunId WHERE Environment=@Environment;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.FailSync @Environment varchar(3), @RunId uniqueidentifier, @ErrorCode varchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Resource nvarchar(255)=N'SietomAccessControl:' + @Environment;
    IF ISNULL(APPLOCK_MODE('public', @Resource, 'Session'), 'NoLock') <> 'Exclusive'
        THROW 51000, 'Synchronisation lock required.', 1;
    UPDATE dbo.AccessCardSyncRuns SET Status='Failed', FinishedUtc=SYSUTCDATETIME(), ErrorCode=@ErrorCode
        WHERE RunId=@RunId AND Environment=@Environment AND Status='Running';
END;
GO
-- Authorize from the last successfully committed PRD snapshot, regardless of its age.
CREATE OR ALTER VIEW dbo.vAuthorizedCards
AS
    SELECT u.SiteCode,u.CardNumber,l.LocalEmployeeId AS EmployeeId,u.EmployeeId AS ApiEmployeeId,
        u.FirstName,u.LastName,e.Name,u.LastSynchronizedUtc
    FROM dbo.AccessCardUsers u JOIN dbo.AccessCardSyncState s ON s.Environment=u.Environment
        JOIN dbo.EmployeeApiLinks l ON l.ApiEmployeeId=u.EmployeeId
        JOIN dbo.Employees e ON e.Id=l.LocalEmployeeId
    WHERE u.Environment='PRD' AND u.Active=1 AND e.Active=1
        AND s.LastSuccessfulUtc IS NOT NULL;
GO
CREATE OR ALTER PROCEDURE dbo.GetCardSyncStatus
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT LastSuccessfulUtc,CAST(CASE WHEN LastSuccessfulUtc IS NULL
        OR LastSuccessfulUtc < DATEADD(MINUTE,-10,SYSUTCDATETIME()) THEN 1 ELSE 0 END AS bit) AS IsStale
    FROM (SELECT MAX(LastSuccessfulUtc) AS LastSuccessfulUtc FROM dbo.AccessCardSyncState WHERE Environment='PRD') s;
END;
GO
-- One entry point for PartsBOX, including existing raw-number USB reader aliases.
-- Until the first successful PRD import, legacy logins keep working. Afterwards only current PRD cards authorize.
CREATE OR ALTER PROCEDURE dbo.FindAuthorizedEmployee
    @SiteCode int=NULL,@CardNumber bigint=NULL,@ReaderNumber nvarchar(40)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM dbo.AccessCardSyncState WHERE Environment='PRD' AND LastSuccessfulUtc IS NOT NULL)
    BEGIN
        IF @ReaderNumber IS NOT NULL
        BEGIN
            IF OBJECT_ID(N'dbo.LocalKeyboardCards',N'U') IS NOT NULL
                EXEC sys.sp_executesql N'SELECT v.EmployeeId AS Id,v.Name,COALESCE(k.Barcode,N'''') AS Barcode,COALESCE(k.Position,N'''') AS Position
                    FROM dbo.AccessCardAliases a JOIN dbo.vAuthorizedCards v ON v.SiteCode=a.SiteCode AND v.CardNumber=a.CardNumber
                    LEFT JOIN dbo.LocalKeyboardCards k ON k.ReaderNumber=a.ReaderNumber AND k.EmployeeId=v.EmployeeId
                    WHERE a.ReaderNumber=@reader',N'@reader nvarchar(40)',@reader=@ReaderNumber;
            ELSE
                SELECT v.EmployeeId AS Id,v.Name,N'' AS Barcode,N'' AS Position FROM dbo.AccessCardAliases a
                    JOIN dbo.vAuthorizedCards v ON v.SiteCode=a.SiteCode AND v.CardNumber=a.CardNumber WHERE a.ReaderNumber=@ReaderNumber;
        END
        ELSE SELECT EmployeeId AS Id,Name,N'' AS Barcode,N'' AS Position FROM dbo.vAuthorizedCards
            WHERE SiteCode=@SiteCode AND CardNumber=@CardNumber;
        RETURN;
    END;
    IF @ReaderNumber IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'dbo.LocalKeyboardCards',N'U') IS NOT NULL
            EXEC sys.sp_executesql N'SELECT e.Id,e.Name,k.Barcode,k.Position FROM dbo.LocalKeyboardCards k
                JOIN dbo.Employees e ON e.Id=k.EmployeeId WHERE k.ReaderNumber=@reader AND e.Active=1',
                N'@reader nvarchar(40)',@reader=@ReaderNumber;
        ELSE SELECT Id,Name,N'' AS Barcode,N'' AS Position FROM dbo.Employees WHERE 1=0;
    END
    ELSE SELECT Id,Name,N'' AS Barcode,N'' AS Position FROM dbo.Employees
        WHERE SiteCode=@SiteCode AND CardNumber=@CardNumber AND Active=1;
END;
GO
IF DATABASE_PRINCIPAL_ID('SietomSyncWriter') IS NULL CREATE ROLE SietomSyncWriter;
IF DATABASE_PRINCIPAL_ID('SietomCardReader') IS NULL CREATE ROLE SietomCardReader;
IF DATABASE_PRINCIPAL_ID('SietomSyncMonitor') IS NULL CREATE ROLE SietomSyncMonitor;
GRANT EXECUTE ON dbo.AcquireSyncLock TO SietomSyncWriter;
GRANT EXECUTE ON dbo.BeginSync TO SietomSyncWriter;
GRANT EXECUTE ON dbo.ApplySnapshot TO SietomSyncWriter;
GRANT EXECUTE ON dbo.FailSync TO SietomSyncWriter;
GRANT EXECUTE ON TYPE::dbo.CardSnapshot TO SietomSyncWriter;
GRANT REFERENCES ON TYPE::dbo.CardSnapshot TO SietomSyncWriter;
GRANT EXECUTE ON TYPE::dbo.CardConflictKey TO SietomSyncWriter;
GRANT REFERENCES ON TYPE::dbo.CardConflictKey TO SietomSyncWriter;
GRANT SELECT ON dbo.vAuthorizedCards TO SietomCardReader;
GRANT EXECUTE ON dbo.FindAuthorizedEmployee TO SietomCardReader;
GRANT EXECUTE ON dbo.GetCardSyncStatus TO SietomCardReader;
GRANT SELECT ON dbo.AccessCardSyncRuns TO SietomSyncMonitor;
GRANT SELECT ON dbo.AccessCardSyncState TO SietomSyncMonitor;
GRANT SELECT ON dbo.AccessCardConflicts TO SietomSyncMonitor;
GO
