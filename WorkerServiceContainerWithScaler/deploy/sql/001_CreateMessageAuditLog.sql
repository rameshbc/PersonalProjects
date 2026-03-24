-- Standalone SQL migration (fallback if EF Core migrations are not used)
-- Compatible with SQL Server 2019+

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MessageAuditLog')
BEGIN
    CREATE TABLE dbo.MessageAuditLog
    (
        Id               BIGINT          IDENTITY(1,1) NOT NULL,
        ClientId         NVARCHAR(128)   NOT NULL,
        ServiceName      NVARCHAR(256)   NOT NULL,
        HostName         NVARCHAR(256)   NOT NULL,
        OperationType    NVARCHAR(32)    NOT NULL,
        DestinationType  NVARCHAR(16)    NOT NULL,
        DestinationName  NVARCHAR(260)   NOT NULL,
        MessageId        NVARCHAR(128)   NULL,
        CorrelationId    NVARCHAR(128)   NULL,
        Subject          NVARCHAR(512)   NULL,
        Body             VARBINARY(MAX)  NULL,
        IsBodyCompressed BIT             NOT NULL CONSTRAINT DF_Audit_IsBodyCompressed DEFAULT 0,
        BodySizeBytes    INT             NULL,
        Status           NVARCHAR(32)    NOT NULL,
        StatusDetail     NVARCHAR(1024)  NULL,
        PendingCount     BIGINT          NULL,
        CreatedAt        DATETIME2(7)    NOT NULL CONSTRAINT DF_Audit_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt        DATETIME2(7)    NOT NULL CONSTRAINT DF_Audit_UpdatedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_MessageAuditLog PRIMARY KEY CLUSTERED (Id ASC)
    );

    -- Pending-check hot-path index (ClientId, DestinationName, Subject, Status, CreatedAt DESC)
    CREATE NONCLUSTERED INDEX IX_Audit_PendingCheck
        ON dbo.MessageAuditLog (ClientId, DestinationName, Subject, Status, CreatedAt DESC)
        INCLUDE (Id);

    -- General destination/status lookup
    CREATE NONCLUSTERED INDEX IX_Audit_Destination_Status
        ON dbo.MessageAuditLog (DestinationName, Status, CreatedAt DESC);

    -- MessageId lookup
    CREATE NONCLUSTERED INDEX IX_Audit_MessageId
        ON dbo.MessageAuditLog (MessageId);

    -- Retention/cleanup index
    CREATE NONCLUSTERED INDEX IX_Audit_CreatedAt
        ON dbo.MessageAuditLog (CreatedAt DESC);

    PRINT 'MessageAuditLog table and indexes created.';
END
ELSE
BEGIN
    PRINT 'MessageAuditLog already exists — skipping.';
END
GO
