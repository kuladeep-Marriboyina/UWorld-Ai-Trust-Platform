/*  Dev info — every API call's request and response, captured in Development only so a
    developer can replay what happened and trace it through the code. Safe to re-run.   */

USE UWorldTrust;
GO

DROP TABLE IF EXISTS dbo.DevRequestLog;
GO

CREATE TABLE dbo.DevRequestLog (
    Id               BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RequestId        NVARCHAR(64)   NOT NULL,
    Method           NVARCHAR(10)   NOT NULL,
    Path             NVARCHAR(512)  NOT NULL,
    QueryString      NVARCHAR(1024) NULL,
    RequestHeaders   NVARCHAR(MAX)  NULL,     -- JSON of the headers we keep (never auth secrets)
    RequestBody      NVARCHAR(MAX)  NULL,
    StatusCode       INT            NOT NULL,
    ResponseHeaders  NVARCHAR(MAX)  NULL,
    ResponseBody     NVARCHAR(MAX)  NULL,
    DurationMs       INT            NOT NULL,
    Role             NVARCHAR(32)   NULL,
    Controller       NVARCHAR(256)  NULL,     -- full type name of the controller that handled it
    Action           NVARCHAR(128)  NULL,
    RoutePattern     NVARCHAR(256)  NULL,
    ExceptionType    NVARCHAR(256)  NULL,
    ExceptionMessage NVARCHAR(MAX)  NULL,
    StackTrace       NVARCHAR(MAX)  NULL,
    OccurredUtc      DATETIME2      NOT NULL
);
CREATE INDEX IX_DevRequestLog_Status ON dbo.DevRequestLog(StatusCode);
CREATE INDEX IX_DevRequestLog_Request ON dbo.DevRequestLog(RequestId);
GO

PRINT 'Dev schema created.';
