/*  Stored procedures used by the Data layer (raw ADO.NET calls these).
    Writes go through procs; the idempotency guard lives in usp_UpsertRiskCase. */

USE UWorldTrust;
GO

DROP PROCEDURE IF EXISTS dbo.usp_UpsertRiskCase;
DROP PROCEDURE IF EXISTS dbo.usp_AddCaseEvidence;
DROP PROCEDURE IF EXISTS dbo.usp_AddCaseActivity;
DROP PROCEDURE IF EXISTS dbo.usp_UpdateCaseStatus;
DROP PROCEDURE IF EXISTS dbo.usp_AddLedgerEntry;
GO

/* Insert a case only if its CaseKey is new. Returns the CaseId and whether
   it was newly created so the worker stays idempotent across runs.         */
CREATE PROCEDURE dbo.usp_UpsertRiskCase
    @CaseKey            NVARCHAR(200),
    @RuleId             NVARCHAR(64),
    @RuleVersion        NVARCHAR(16),
    @Title              NVARCHAR(256),
    @Severity           NVARCHAR(16),
    @Confidence         DECIMAL(4,3),
    @Score              DECIMAL(6,2),
    @BusinessImpactUsd  DECIMAL(12,2),
    @ScoreBreakdownJson NVARCHAR(MAX),
    @AccountId          INT,
    @EntityRef          NVARCHAR(128),
    @ExpectedJson       NVARCHAR(MAX),
    @ActualJson         NVARCHAR(MAX),
    @CreatedUtc         DATETIME2,
    @CaseId             INT OUTPUT,
    @Created            BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @CaseId = CaseId FROM dbo.RiskCases WHERE CaseKey = @CaseKey;

    IF @CaseId IS NULL
    BEGIN
        INSERT dbo.RiskCases
            (CaseKey, RuleId, RuleVersion, Title, Severity, Confidence, Score, BusinessImpactUsd,
             ScoreBreakdownJson, Status, AccountId, EntityRef, ExpectedJson, ActualJson, CreatedUtc)
        VALUES
            (@CaseKey, @RuleId, @RuleVersion, @Title, @Severity, @Confidence, @Score, @BusinessImpactUsd,
             @ScoreBreakdownJson, 'New', @AccountId, @EntityRef, @ExpectedJson, @ActualJson, @CreatedUtc);

        SET @CaseId = SCOPE_IDENTITY();
        SET @Created = 1;
    END
    ELSE
        SET @Created = 0;
END
GO

CREATE PROCEDURE dbo.usp_AddCaseEvidence
    @CaseId     INT,
    @SourceType NVARCHAR(64),
    @SourceId   NVARCHAR(64),
    @Label      NVARCHAR(256),
    @DetailJson NVARCHAR(MAX),
    @OccurredUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.CaseEvidence (CaseId, SourceType, SourceId, Label, DetailJson, OccurredUtc)
    VALUES (@CaseId, @SourceType, @SourceId, @Label, @DetailJson, @OccurredUtc);
END
GO

CREATE PROCEDURE dbo.usp_AddCaseActivity
    @CaseId     INT,
    @Action     NVARCHAR(32),
    @Actor      NVARCHAR(128),
    @Notes      NVARCHAR(512),
    @OccurredUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.CaseActivity (CaseId, Action, Actor, Notes, OccurredUtc)
    VALUES (@CaseId, @Action, @Actor, @Notes, @OccurredUtc);
END
GO

CREATE PROCEDURE dbo.usp_UpdateCaseStatus
    @CaseId     INT,
    @Status     NVARCHAR(16),
    @AssignedTo NVARCHAR(128),
    @ResolvedUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RiskCases
       SET Status      = @Status,
           AssignedTo  = COALESCE(@AssignedTo, AssignedTo),
           ResolvedUtc = @ResolvedUtc
     WHERE CaseId = @CaseId;
END
GO

CREATE PROCEDURE dbo.usp_AddLedgerEntry
    @RequestId         NVARCHAR(64),
    @Kind              NVARCHAR(32),
    @Operation         NVARCHAR(200),
    @CaseId            INT,
    @RuleId            NVARCHAR(64),
    @Model             NVARCHAR(64),
    @InputTokens       INT,
    @OutputTokens      INT,
    @AiCostUsd         DECIMAL(12,6),
    @BusinessImpactUsd DECIMAL(12,2),
    @DurationMs        INT,
    @OccurredUtc       DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.RequestLedger
        (RequestId, Kind, Operation, CaseId, RuleId, Model, InputTokens, OutputTokens, AiCostUsd, BusinessImpactUsd, DurationMs, OccurredUtc)
    VALUES
        (@RequestId, @Kind, @Operation, @CaseId, @RuleId, @Model, @InputTokens, @OutputTokens, @AiCostUsd, @BusinessImpactUsd, @DurationMs, @OccurredUtc);
END
GO

PRINT 'Procedures created.';
