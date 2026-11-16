/*  UWorld AI Trust & Revenue Protection - schema
    Source signal tables (what the business already produces) +
    platform tables (RiskCases and the audit trail the platform owns).
    Safe to re-run: drops in dependency order, then recreates.          */

IF DB_ID('UWorldTrust') IS NULL
    CREATE DATABASE UWorldTrust;
GO
USE UWorldTrust;
GO

DROP TABLE IF EXISTS dbo.RequestLedger;
DROP TABLE IF EXISTS dbo.CaseActivity;
DROP TABLE IF EXISTS dbo.CaseEvidence;
DROP TABLE IF EXISTS dbo.RiskCases;
DROP TABLE IF EXISTS dbo.RuleDefinitions;
DROP TABLE IF EXISTS dbo.Deployments;
DROP TABLE IF EXISTS dbo.Cves;
DROP TABLE IF EXISTS dbo.AssetInventory;
DROP TABLE IF EXISTS dbo.AccessEvents;
DROP TABLE IF EXISTS dbo.AuthOutcomes;
DROP TABLE IF EXISTS dbo.Entitlements;
DROP TABLE IF EXISTS dbo.Discounts;
DROP TABLE IF EXISTS dbo.Payments;
DROP TABLE IF EXISTS dbo.Orders;
DROP TABLE IF EXISTS dbo.Accounts;
GO

/* ------------------------------------------------------------------ */
/* Source signals                                                     */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.Accounts (
    AccountId            INT            NOT NULL PRIMARY KEY,
    Email                NVARCHAR(256)  NOT NULL,
    LinkedAccountGroupId INT            NULL,          -- same real person / household
    CreatedUtc           DATETIME2      NOT NULL
);

CREATE TABLE dbo.Orders (
    OrderId        INT            NOT NULL PRIMARY KEY,
    AccountId      INT            NOT NULL,
    PackageId      INT            NOT NULL,
    OrderType      NVARCHAR(32)   NOT NULL,            -- Purchase / Trial / Renewal
    ListPrice      DECIMAL(10,2)  NOT NULL,
    PaidAmount     DECIMAL(10,2)  NOT NULL,
    PolicyMinPrice DECIMAL(10,2)  NOT NULL,            -- lowest price policy allows for this package
    OrderUtc       DATETIME2      NOT NULL
);

CREATE TABLE dbo.Payments (
    PaymentId INT           NOT NULL PRIMARY KEY,
    OrderId   INT           NOT NULL,
    Amount    DECIMAL(10,2) NOT NULL,
    Status    NVARCHAR(32)  NOT NULL,                  -- Captured / Refunded / Failed
    PaidUtc   DATETIME2     NOT NULL
);

CREATE TABLE dbo.Discounts (
    DiscountId INT           NOT NULL PRIMARY KEY,
    Code       NVARCHAR(64)  NOT NULL,
    OrderId    INT           NOT NULL,
    AccountId  INT           NOT NULL,
    [Percent]  DECIMAL(5,2)  NOT NULL,
    AppliedUtc DATETIME2     NOT NULL
);

CREATE TABLE dbo.Entitlements (
    EntitlementId INT           NOT NULL PRIMARY KEY,
    AccountId     INT           NOT NULL,
    PackageId     INT           NOT NULL,
    Status        NVARCHAR(32)  NOT NULL,              -- Active / Expired / Cancelled / Refunded
    StartUtc      DATETIME2     NOT NULL,
    EndUtc        DATETIME2     NOT NULL,
    RefundedUtc   DATETIME2     NULL
);

CREATE TABLE dbo.AuthOutcomes (
    AuthId        INT            NOT NULL PRIMARY KEY,
    AccountId     INT            NOT NULL,
    Resource      NVARCHAR(256)  NOT NULL,
    AuthResult    NVARCHAR(16)   NOT NULL,             -- Allowed / Denied / Missing
    CorrelationId NVARCHAR(64)   NOT NULL,
    OccurredUtc   DATETIME2      NOT NULL
);

CREATE TABLE dbo.AccessEvents (
    AccessId            INT            NOT NULL PRIMARY KEY,
    AccountId           INT            NOT NULL,
    Resource            NVARCHAR(256)  NOT NULL,
    PackageId           INT            NULL,
    QuestionId          INT            NULL,
    SessionId           NVARCHAR(64)   NOT NULL,
    ContentReturnedBytes BIGINT        NOT NULL,       -- 0 = nothing left the building
    IpAddress           NVARCHAR(45)   NOT NULL,
    DeviceId            NVARCHAR(64)   NOT NULL,
    CorrelationId       NVARCHAR(64)   NOT NULL,
    OccurredUtc         DATETIME2      NOT NULL
);

CREATE TABLE dbo.AssetInventory (
    AssetId INT           NOT NULL PRIMARY KEY,
    Name    NVARCHAR(128) NOT NULL,
    Component NVARCHAR(128) NOT NULL,                  -- e.g. Microsoft.AspNetCore / Newtonsoft.Json
    Version NVARCHAR(32)  NOT NULL
);

CREATE TABLE dbo.Cves (
    CveId           NVARCHAR(32)  NOT NULL PRIMARY KEY,
    Component       NVARCHAR(128) NOT NULL,
    AffectedVersion NVARCHAR(32)  NOT NULL,
    Severity        NVARCHAR(16)  NOT NULL,            -- Critical / High / Medium / Low
    PublishedUtc    DATETIME2     NOT NULL
);

CREATE TABLE dbo.Deployments (
    DeploymentId INT          NOT NULL PRIMARY KEY,
    Service      NVARCHAR(64) NOT NULL,
    Version      NVARCHAR(32) NOT NULL,
    DeployedUtc  DATETIME2    NOT NULL
);

/* ------------------------------------------------------------------ */
/* Platform tables                                                    */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.RuleDefinitions (
    RuleId      NVARCHAR(64)  NOT NULL PRIMARY KEY,
    Name        NVARCHAR(128) NOT NULL,
    Version     NVARCHAR(16)  NOT NULL,
    Category    NVARCHAR(64)  NOT NULL,
    Description NVARCHAR(512) NOT NULL,
    Enabled     BIT           NOT NULL DEFAULT 1
);

CREATE TABLE dbo.RiskCases (
    CaseId             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CaseKey            NVARCHAR(200)  NOT NULL,         -- ruleId:entity:windowStart  (idempotency key)
    RuleId             NVARCHAR(64)   NOT NULL,
    RuleVersion        NVARCHAR(16)   NOT NULL,
    Title              NVARCHAR(256)  NOT NULL,
    Severity           NVARCHAR(16)   NOT NULL,         -- Critical / High / Medium / Low
    Confidence         DECIMAL(4,3)   NOT NULL,         -- 0..1
    Score              DECIMAL(6,2)   NOT NULL,
    BusinessImpactUsd  DECIMAL(12,2)  NOT NULL DEFAULT 0,
    ScoreBreakdownJson NVARCHAR(MAX)  NULL,
    Status             NVARCHAR(16)   NOT NULL,         -- New / Assigned / Escalated / Resolved
    AccountId          INT            NULL,
    EntityRef          NVARCHAR(128)  NULL,
    ExpectedJson       NVARCHAR(MAX)  NULL,
    ActualJson         NVARCHAR(MAX)  NULL,
    AssignedTo         NVARCHAR(128)  NULL,
    CreatedUtc         DATETIME2      NOT NULL,
    ResolvedUtc        DATETIME2      NULL,
    CONSTRAINT UQ_RiskCases_CaseKey UNIQUE (CaseKey)
);

CREATE TABLE dbo.CaseEvidence (
    EvidenceId  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CaseId      INT            NOT NULL REFERENCES dbo.RiskCases(CaseId),
    SourceType  NVARCHAR(64)   NOT NULL,                -- AccessEvent / Order / Entitlement ...
    SourceId    NVARCHAR(64)   NOT NULL,
    Label       NVARCHAR(256)  NOT NULL,
    DetailJson  NVARCHAR(MAX)  NULL,
    OccurredUtc DATETIME2      NULL
);

CREATE TABLE dbo.CaseActivity (
    ActivityId  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CaseId      INT           NOT NULL REFERENCES dbo.RiskCases(CaseId),
    Action      NVARCHAR(32)  NOT NULL,                 -- Created / Assigned / Escalated / Resolved / AiSummary
    Actor       NVARCHAR(128) NOT NULL,
    Notes       NVARCHAR(512) NULL,
    OccurredUtc DATETIME2     NOT NULL
);

/* Request + cost ledger: one RequestId ties an HTTP call / ingestion pass / AI
   investigation / raised case together, each row carrying AI spend and business impact. */
CREATE TABLE dbo.RequestLedger (
    LedgerId          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RequestId         NVARCHAR(64)   NOT NULL,
    Kind              NVARCHAR(32)   NOT NULL,   -- HttpRequest / IngestionPass / CaseRaised / AiInvestigation
    Operation         NVARCHAR(200)  NOT NULL,
    CaseId            INT            NULL,
    RuleId            NVARCHAR(64)   NULL,
    Model             NVARCHAR(64)   NULL,
    InputTokens       INT            NOT NULL DEFAULT 0,
    OutputTokens      INT            NOT NULL DEFAULT 0,
    AiCostUsd         DECIMAL(12,6)  NOT NULL DEFAULT 0,
    BusinessImpactUsd DECIMAL(12,2)  NOT NULL DEFAULT 0,
    DurationMs        INT            NOT NULL DEFAULT 0,
    OccurredUtc       DATETIME2      NOT NULL
);
CREATE INDEX IX_RequestLedger_RequestId ON dbo.RequestLedger(RequestId);
CREATE INDEX IX_RequestLedger_Kind      ON dbo.RequestLedger(Kind);

CREATE INDEX IX_RiskCases_Status   ON dbo.RiskCases(Status);
CREATE INDEX IX_RiskCases_RuleId   ON dbo.RiskCases(RuleId);
CREATE INDEX IX_CaseEvidence_CaseId ON dbo.CaseEvidence(CaseId);
CREATE INDEX IX_CaseActivity_CaseId ON dbo.CaseActivity(CaseId);
GO

PRINT 'Schema created.';
