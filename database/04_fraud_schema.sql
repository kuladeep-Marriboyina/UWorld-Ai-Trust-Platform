/*  Phase 2 — fraud tables mirroring the REAL UWorld schema (orders.api / support.api).
    Real column names so rules are written against them and can later repoint at
    uw_dev_in_usmleworldcom_db. Safe to re-run.                                      */

USE UWorldTrust;
GO

DROP TABLE IF EXISTS dbo.GA_DISCOUNTS;
DROP TABLE IF EXISTS dbo.REFERRAL_CODE_USAGE;
DROP TABLE IF EXISTS dbo.NCLEX_REFERRAL_CODES;
DROP TABLE IF EXISTS dbo.BRAINTREE_TRANSACTIONS;
DROP TABLE IF EXISTS dbo.USER_PAYMENT_DETAILS;
DROP TABLE IF EXISTS dbo.USER_PAYMENTS;
DROP TABLE IF EXISTS dbo.USER_LOGS;
DROP TABLE IF EXISTS dbo.USER_SUBSCRIPTIONS;
DROP TABLE IF EXISTS dbo.BLOCKED_IPS;
DROP TABLE IF EXISTS dbo.COUNTRIES;
GO

CREATE TABLE dbo.COUNTRIES (
    CountryId   INT           NOT NULL PRIMARY KEY,
    Name        NVARCHAR(64)  NOT NULL,
    IsoCode     NVARCHAR(3)   NOT NULL,
    IsEmbargoed BIT           NOT NULL DEFAULT 0
);

CREATE TABLE dbo.BLOCKED_IPS (
    Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IPAddress   NVARCHAR(45)  NOT NULL,
    Reason      NVARCHAR(128) NULL,
    DateCreated DATETIME2     NOT NULL
);

CREATE TABLE dbo.USER_SUBSCRIPTIONS (
    SubscriptionId   INT          NOT NULL PRIMARY KEY,
    UserId           INT          NOT NULL,
    CourseId         INT          NOT NULL,
    PackageId        SMALLINT     NOT NULL,
    StatusId         TINYINT      NOT NULL DEFAULT 1,
    Active           BIT          NOT NULL,
    DateActivated    DATETIME2    NOT NULL,
    DateExpiration   DATETIME2    NOT NULL,
    DateFirstAccessed DATETIME2   NULL
);

/* USER_PAYMENTS — PaymentStatus 1=Paid,2=Unpaid,3=Partial,4=Cancelled · GatewayId 4=Braintree */
CREATE TABLE dbo.USER_PAYMENTS (
    PaymentId         INT           NOT NULL PRIMARY KEY,
    UserId            INT           NOT NULL,
    FinalAmount       DECIMAL(10,2) NOT NULL,
    TransactionAmount DECIMAL(10,2) NOT NULL,
    DiscountAmount    DECIMAL(10,2) NOT NULL DEFAULT 0,
    TransactionId     NVARCHAR(64)  NULL,          -- gateway txn id; '0'/'0000'/NULL = none
    PaymentStatus     TINYINT       NOT NULL,
    GatewayId         TINYINT       NULL,
    PaymentModeId     TINYINT       NOT NULL,       -- 1=CreditCard,2=Check,3=WireAch,6=StoreCredit
    ResponseCode      TINYINT       NULL,
    SourceId          TINYINT       NULL,           -- 2=UWorldCheckout,4=SupportEnrollment,6=FirmBilling
    PaymentTypeId     TINYINT       NULL,           -- 1=Standard,5=StudentOrder,7=Complimentary
    IPAddress         NVARCHAR(45)  NULL,
    CountryCode       NVARCHAR(3)   NULL,
    DateCreated       DATETIME2     NOT NULL,
    DatePaymentReceived DATETIME2   NULL
);

CREATE TABLE dbo.USER_PAYMENT_DETAILS (
    Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PaymentId     INT           NOT NULL,
    PackageId     SMALLINT      NOT NULL,
    CourseId      INT           NULL,
    DiscountCode  NVARCHAR(64)  NULL,
    BaseAmount    DECIMAL(10,2) NOT NULL DEFAULT 0,
    DiscountAmount DECIMAL(10,2) NOT NULL DEFAULT 0,
    FinalAmount   DECIMAL(10,2) NOT NULL DEFAULT 0
);

/* BraintreeTransactionInfo — the gateway's record of a transaction */
CREATE TABLE dbo.BRAINTREE_TRANSACTIONS (
    TransactionId     NVARCHAR(64)  NOT NULL PRIMARY KEY,
    PaymentId         INT           NULL,
    TransactionStatus NVARCHAR(32)  NOT NULL,       -- settled, submitted_for_settlement, failed, voided
    Amount            DECIMAL(10,2) NULL,
    IsMismatch        BIT           NOT NULL DEFAULT 0,
    IsVerified        BIT           NOT NULL DEFAULT 0,
    IsFraudDetected   BIT           NOT NULL DEFAULT 0,
    DateCreated       DATETIME2     NOT NULL
);

CREATE TABLE dbo.NCLEX_REFERRAL_CODES (
    ReferralId             INT           NOT NULL PRIMARY KEY,
    ReferralCode           NVARCHAR(64)  NOT NULL,
    ReferralCodeLimit      INT           NOT NULL,   -- global cap across ALL users
    ReferralCodeUsed       INT           NOT NULL,   -- per-code counter (non-atomic increment = race)
    CodeExpirationDate     DATETIME2     NOT NULL,
    DiscountGroupId        INT           NULL,
    DiscountBehaviourTypeId TINYINT      NOT NULL DEFAULT 1,  -- 1=Regular,2=Stackable,3=MultiStackable
    DiscountPercent        DECIMAL(5,2)  NOT NULL DEFAULT 0
);

/* GA_DISCOUNTS — a discount group's eligible packages (code → group → these packages only) */
CREATE TABLE dbo.GA_DISCOUNTS (
    DiscountId              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    DiscountGroupId         INT           NOT NULL,
    PackageId              SMALLINT       NOT NULL,
    DiscountAmount         DECIMAL(10,2)  NOT NULL,
    IsPercent              BIT            NOT NULL DEFAULT 1,
    DiscountBehaviourTypeId TINYINT       NOT NULL DEFAULT 1,
    FloorValue             DECIMAL(10,2)  NULL
);
CREATE INDEX IX_GaDiscounts_Group ON dbo.GA_DISCOUNTS(DiscountGroupId);

CREATE TABLE dbo.REFERRAL_CODE_USAGE (
    Id           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ReferralCode NVARCHAR(64)  NOT NULL,
    UserId       INT           NOT NULL,
    PaymentId    INT           NULL,
    DateUsed     DATETIME2     NOT NULL
);

/* USER_LOGS — access/login activity with geo (drives region + active-subscription views) */
CREATE TABLE dbo.USER_LOGS (
    Id             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId         INT           NOT NULL,
    SubscriptionId INT           NULL,
    IPAddress      NVARCHAR(45)  NOT NULL,
    CountryCode    NVARCHAR(3)   NULL,
    DeviceTypeId   TINYINT       NULL,
    LogType        NVARCHAR(32)  NULL,            -- Access / Login / ApplyDiscountCode
    Url            NVARCHAR(256) NULL,            -- request URL the action came from
    DateCreated    DATETIME2     NOT NULL
);

CREATE INDEX IX_UserLogs_Sub   ON dbo.USER_LOGS(SubscriptionId);
CREATE INDEX IX_UserLogs_Date  ON dbo.USER_LOGS(DateCreated);
CREATE INDEX IX_Payments_User  ON dbo.USER_PAYMENTS(UserId);
CREATE INDEX IX_RefUsage_Code  ON dbo.REFERRAL_CODE_USAGE(ReferralCode);
GO

/* Extend RULE_DEFINITIONS for config-driven thresholds, severity, and declarative (NL-authored) rules */
IF COL_LENGTH('dbo.RuleDefinitions','Severity')        IS NULL ALTER TABLE dbo.RuleDefinitions ADD Severity       NVARCHAR(16)  NULL;
IF COL_LENGTH('dbo.RuleDefinitions','ConfigJson')      IS NULL ALTER TABLE dbo.RuleDefinitions ADD ConfigJson     NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','IsDeclarative')   IS NULL ALTER TABLE dbo.RuleDefinitions ADD IsDeclarative  BIT           NOT NULL DEFAULT 0;
IF COL_LENGTH('dbo.RuleDefinitions','DeclarativeJson') IS NULL ALTER TABLE dbo.RuleDefinitions ADD DeclarativeJson NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','Author')          IS NULL ALTER TABLE dbo.RuleDefinitions ADD Author         NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','DateUpdated')     IS NULL ALTER TABLE dbo.RuleDefinitions ADD DateUpdated    DATETIME2     NULL;
GO

PRINT 'Fraud schema created.';
