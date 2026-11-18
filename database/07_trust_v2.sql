/*  Trust Platform v2 — identity & API signals, case lifecycle, governed rules.
    - USERS / API_LOGS: the account + request telemetry every real signup/login/checkout produces
    - PACKAGES: catalog with list prices (drives factual $ impact)
    - USER_PAYMENTS.CardFingerprint: one card funding many accounts is the strongest farm signal
    - RiskCases: category, disposition, detection latency, acknowledgement, entities
    - RuleDefinitions + RuleVersions: draft → approval → active lifecycle with full history
    Safe to re-run (drops/recreates the tables it owns, ALTERs the rest idempotently).   */

USE UWorldTrust;
GO
SET NOCOUNT ON;

DROP TABLE IF EXISTS dbo.API_LOGS;
DROP TABLE IF EXISTS dbo.USERS;
DROP TABLE IF EXISTS dbo.PACKAGES;
DROP TABLE IF EXISTS dbo.CaseEntities;
DROP TABLE IF EXISTS dbo.RuleVersions;
GO

CREATE TABLE dbo.PACKAGES (
    PackageId    SMALLINT      NOT NULL PRIMARY KEY,
    Name         NVARCHAR(128) NOT NULL,
    ListPrice    DECIMAL(10,2) NOT NULL,
    DurationDays INT           NOT NULL,
    TrialDays    INT           NOT NULL DEFAULT 0
);

CREATE TABLE dbo.USERS (
    UserId            INT IDENTITY(1001,1) NOT NULL PRIMARY KEY,
    Email             NVARCHAR(256) NOT NULL,
    FirstName         NVARCHAR(64)  NOT NULL,
    LastName          NVARCHAR(64)  NOT NULL,
    Phone             NVARCHAR(32)  NULL,
    CountryCode       NVARCHAR(3)   NULL,
    RegistrationIp    NVARCHAR(45)  NOT NULL,
    DeviceFingerprint NVARCHAR(64)  NOT NULL,
    UserAgent         NVARCHAR(256) NULL,
    PasswordHash      NVARCHAR(128) NULL,
    SignupSource      NVARCHAR(32)  NOT NULL DEFAULT 'Web',
    DateCreated       DATETIME2     NOT NULL
);
CREATE UNIQUE INDEX UX_Users_Email ON dbo.USERS(Email);
CREATE INDEX IX_Users_Ip     ON dbo.USERS(RegistrationIp, DateCreated);
CREATE INDEX IX_Users_Device ON dbo.USERS(DeviceFingerprint);

/* Every API call the customer apps make (register, login, subscribe, apply code, content). */
CREATE TABLE dbo.API_LOGS (
    Id                BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RequestId         NVARCHAR(64)  NOT NULL,
    UserId            INT           NULL,
    Email             NVARCHAR(256) NULL,          -- the identity attempted (logins can fail before a UserId exists)
    Method            NVARCHAR(10)  NOT NULL,
    Endpoint          NVARCHAR(256) NOT NULL,
    StatusCode        INT           NOT NULL,
    Outcome           NVARCHAR(24)  NOT NULL,      -- Success / Failed / Rejected
    IPAddress         NVARCHAR(45)  NOT NULL,
    CountryCode       NVARCHAR(3)   NULL,
    UserAgent         NVARCHAR(256) NULL,
    DeviceFingerprint NVARCHAR(64)  NULL,
    LatencyMs         INT           NOT NULL DEFAULT 0,
    DateCreated       DATETIME2     NOT NULL
);
CREATE INDEX IX_ApiLogs_Ip   ON dbo.API_LOGS(IPAddress, DateCreated);
CREATE INDEX IX_ApiLogs_User ON dbo.API_LOGS(UserId);
CREATE INDEX IX_ApiLogs_Date ON dbo.API_LOGS(DateCreated);

/* Entities a case touches — powers related-case lookup, hotspots and the link graph. */
CREATE TABLE dbo.CaseEntities (
    Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CaseId      INT           NOT NULL,
    EntityType  NVARCHAR(24)  NOT NULL,            -- ip / device / user / email / card / domain / code / subscription
    EntityValue NVARCHAR(256) NOT NULL
);
CREATE INDEX IX_CaseEntities_Case  ON dbo.CaseEntities(CaseId);
CREATE INDEX IX_CaseEntities_Value ON dbo.CaseEntities(EntityType, EntityValue);

/* Every proposed rule change is a version: author → reviewer (four-eyes) → active. */
CREATE TABLE dbo.RuleVersions (
    Id             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RuleId         NVARCHAR(64)   NOT NULL,
    VersionNo      INT            NOT NULL,
    Status         NVARCHAR(24)   NOT NULL,        -- PendingApproval / Approved / Rejected / Superseded
    DefinitionJson NVARCHAR(MAX)  NOT NULL,
    ChangeNote     NVARCHAR(1000) NULL,
    Author         NVARCHAR(128)  NOT NULL,
    Reviewer       NVARCHAR(128)  NULL,
    ReviewNote     NVARCHAR(1000) NULL,
    CreatedUtc     DATETIME2      NOT NULL,
    ReviewedUtc    DATETIME2      NULL
);
CREATE INDEX IX_RuleVersions_Rule ON dbo.RuleVersions(RuleId, VersionNo);
GO

/* ---- idempotent column additions ---- */
IF COL_LENGTH('dbo.USER_PAYMENTS','CardFingerprint') IS NULL ALTER TABLE dbo.USER_PAYMENTS ADD CardFingerprint NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.USER_PAYMENTS','CardLast4')       IS NULL ALTER TABLE dbo.USER_PAYMENTS ADD CardLast4 NVARCHAR(4) NULL;

IF COL_LENGTH('dbo.RiskCases','Category')           IS NULL ALTER TABLE dbo.RiskCases ADD Category NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.RiskCases','Disposition')        IS NULL ALTER TABLE dbo.RiskCases ADD Disposition NVARCHAR(32) NULL;
IF COL_LENGTH('dbo.RiskCases','DispositionBy')      IS NULL ALTER TABLE dbo.RiskCases ADD DispositionBy NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.RiskCases','FirstSignalUtc')     IS NULL ALTER TABLE dbo.RiskCases ADD FirstSignalUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.RiskCases','TriggerSignalUtc')   IS NULL ALTER TABLE dbo.RiskCases ADD TriggerSignalUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.RiskCases','UpdatedUtc')         IS NULL ALTER TABLE dbo.RiskCases ADD UpdatedUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.RiskCases','AcknowledgedUtc')    IS NULL ALTER TABLE dbo.RiskCases ADD AcknowledgedUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.RiskCases','AcknowledgedBy')     IS NULL ALTER TABLE dbo.RiskCases ADD AcknowledgedBy NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.RiskCases','DetectionLatencyMs') IS NULL ALTER TABLE dbo.RiskCases ADD DetectionLatencyMs INT NULL;
IF COL_LENGTH('dbo.RiskCases','ImpactFormula')      IS NULL ALTER TABLE dbo.RiskCases ADD ImpactFormula NVARCHAR(512) NULL;
IF COL_LENGTH('dbo.RiskCases','PlaybookJson')       IS NULL ALTER TABLE dbo.RiskCases ADD PlaybookJson NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.RiskCases','AiSummaryJson')      IS NULL ALTER TABLE dbo.RiskCases ADD AiSummaryJson NVARCHAR(MAX) NULL;
ALTER TABLE dbo.CaseActivity ALTER COLUMN Notes NVARCHAR(2000) NULL;
ALTER TABLE dbo.CaseActivity ALTER COLUMN Action NVARCHAR(40) NOT NULL;
ALTER TABLE dbo.RuleDefinitions ALTER COLUMN Description NVARCHAR(1000) NOT NULL;

IF COL_LENGTH('dbo.RuleDefinitions','Status')           IS NULL ALTER TABLE dbo.RuleDefinitions ADD Status NVARCHAR(24) NOT NULL CONSTRAINT DF_RuleDef_Status DEFAULT 'Active';
IF COL_LENGTH('dbo.RuleDefinitions','Owner')            IS NULL ALTER TABLE dbo.RuleDefinitions ADD Owner NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','VersionNo')        IS NULL ALTER TABLE dbo.RuleDefinitions ADD VersionNo INT NOT NULL CONSTRAINT DF_RuleDef_VersionNo DEFAULT 1;
IF COL_LENGTH('dbo.RuleDefinitions','ApprovedBy')       IS NULL ALTER TABLE dbo.RuleDefinitions ADD ApprovedBy NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','ApprovedUtc')      IS NULL ALTER TABLE dbo.RuleDefinitions ADD ApprovedUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.RuleDefinitions','Rationale')        IS NULL ALTER TABLE dbo.RuleDefinitions ADD Rationale NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','PlaybookJson')     IS NULL ALTER TABLE dbo.RuleDefinitions ADD PlaybookJson NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.RuleDefinitions','PendingVersionNo') IS NULL ALTER TABLE dbo.RuleDefinitions ADD PendingVersionNo INT NULL;
IF COL_LENGTH('dbo.RuleDefinitions','CreatedUtc')       IS NULL ALTER TABLE dbo.RuleDefinitions ADD CreatedUtc DATETIME2 NULL;
GO

DECLARE @now DATETIME2 = SYSUTCDATETIME();

/* ---- catalog ---- */
INSERT dbo.PACKAGES (PackageId, Name, ListPrice, DurationDays, TrialDays) VALUES
 (10,'USMLE Step 1 QBank · 90 days',      199.00,  90, 7),
 (11,'USMLE Step 2 CK QBank · 180 days',  299.00, 180, 7),
 (12,'NCLEX-RN QBank · 90 days',          149.00,  90, 7),
 (20,'Step 1 + Step 2 CK Bundle · 1 yr',  499.00, 365, 0),
 (30,'CPA Review · 12 months',            349.00, 365, 0),
 (40,'MCAT QBank · 6 months',             249.00, 180, 7);

/* ---- profiles for the users the payment/subscription fraud data already references (101-123) ---- */
SET IDENTITY_INSERT dbo.USERS ON;
DECLARE @u INT = 101;
WHILE @u <= 123
BEGIN
    INSERT dbo.USERS (UserId, Email, FirstName, LastName, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, SignupSource, DateCreated)
    VALUES (@u,
            CHOOSE(1 + (@u % 8),'amelia.r','noah.kim','sofia.m','liam.p','ava.chen','ethan.w','mia.lopez','lucas.b') + CAST(@u AS NVARCHAR(8)) + '@' +
            CHOOSE(1 + (@u % 4),'gmail.com','outlook.com','yahoo.com','icloud.com'),
            CHOOSE(1 + (@u % 8),'Amelia','Noah','Sofia','Liam','Ava','Ethan','Mia','Lucas'),
            CHOOSE(1 + (@u % 8),'Reyes','Kim','Martin','Patel','Chen','Walker','Lopez','Brown'),
            'US', '72.21.' + CAST(@u % 200 AS NVARCHAR(4)) + '.' + CAST((@u * 7) % 250 AS NVARCHAR(4)),
            'dev-' + LOWER(CONVERT(NVARCHAR(8), HASHBYTES('MD5', CAST(@u AS NVARCHAR(8))), 2)),
            'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0', 'Web', DATEADD(DAY, -60 - (@u % 30), @now));
    SET @u += 1;
END
SET IDENTITY_INSERT dbo.USERS OFF;

/* ---- baseline: 14 days of ordinary, independent signups (distinct people, IPs, devices, cards) ---- */
DECLARE @first TABLE (i INT IDENTITY(0,1), n NVARCHAR(32));
INSERT @first (n) VALUES ('Olivia'),('James'),('Emma'),('Benjamin'),('Charlotte'),('Henry'),('Amara'),('Mateo'),('Priya'),('Daniel'),
                         ('Harper'),('Samuel'),('Zoe'),('Aarav'),('Grace'),('Leo'),('Fatima'),('Julian'),('Chloe'),('Omar'),
                         ('Isla'),('Wei'),('Nora'),('Diego'),('Hannah'),('Kwame'),('Elena'),('Ravi'),('Lily'),('Tomas');
DECLARE @last TABLE (i INT IDENTITY(0,1), n NVARCHAR(32));
INSERT @last (n) VALUES ('Johnson'),('Nguyen'),('Garcia'),('Okafor'),('Schmidt'),('Rossi'),('Haddad'),('Sato'),('Iyer'),('Morales'),
                        ('Dubois'),('Kowalski'),('Andersen'),('Mensah'),('Silva'),('Novak'),('Hughes'),('Reddy'),('Costa'),('Fischer'),('Park'),('Ali'),('Moreau');
DECLARE @dom TABLE (i INT IDENTITY(0,1), n NVARCHAR(32));
INSERT @dom (n) VALUES ('gmail.com'),('outlook.com'),('yahoo.com'),('icloud.com'),('hotmail.com'),('proton.me'),('gmail.com'),('gmail.com');
DECLARE @cc TABLE (i INT IDENTITY(0,1), n NVARCHAR(3));
INSERT @cc (n) VALUES ('US'),('US'),('US'),('US'),('IN'),('GB'),('CA'),('US'),('PH'),('US');

DECLARE @k INT = 0, @fn NVARCHAR(32), @ln NVARCHAR(32), @d NVARCHAR(32), @c NVARCHAR(3), @when DATETIME2, @email NVARCHAR(256), @ip NVARCHAR(45), @dev NVARCHAR(64), @newId INT;
WHILE @k < 64
BEGIN
    SELECT @fn = n FROM @first WHERE i = (@k * 7) % 30;
    SELECT @ln = n FROM @last  WHERE i = (@k * 11) % 23;
    SELECT @d  = n FROM @dom   WHERE i = (@k * 3) % 8;
    SELECT @c  = n FROM @cc    WHERE i = (@k * 5) % 10;
    SET @when  = DATEADD(MINUTE, -((@k * 311) % (14 * 24 * 60)) - 90, @now);
    SET @email = LOWER(@fn) + CASE @k % 4 WHEN 0 THEN '.' + LOWER(@ln) WHEN 1 THEN LOWER(LEFT(@ln,1)) + CAST(1990 + @k % 9 AS NVARCHAR(4)) WHEN 2 THEN '_' + LOWER(@ln) ELSE LOWER(@ln) + CAST(@k AS NVARCHAR(4)) END + '@' + @d;
    SET @ip    = CAST(24 + (@k * 13) % 180 AS NVARCHAR(4)) + '.' + CAST((@k * 37) % 250 AS NVARCHAR(4)) + '.' + CAST((@k * 53) % 250 AS NVARCHAR(4)) + '.' + CAST(10 + (@k * 17) % 240 AS NVARCHAR(4));
    SET @dev   = 'dev-' + LOWER(CONVERT(NVARCHAR(8), HASHBYTES('MD5', 'base' + CAST(@k AS NVARCHAR(8))), 2));
    IF NOT EXISTS (SELECT 1 FROM dbo.USERS WHERE Email = @email)
    BEGIN
        INSERT dbo.USERS (Email, FirstName, LastName, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, SignupSource, DateCreated)
        VALUES (@email, @fn, @ln, @c, @ip, @dev,
                CHOOSE(1 + @k % 3, 'Mozilla/5.0 (Windows NT 10.0) Chrome/130.0', 'Mozilla/5.0 (Macintosh) Safari/17.5', 'UWorld-iOS/9.4 (iPhone; iOS 18)'),
                CASE WHEN @k % 3 = 2 THEN 'Mobile' ELSE 'Web' END, @when);
        SET @newId = SCOPE_IDENTITY();
        INSERT dbo.API_LOGS (RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, DeviceFingerprint, LatencyMs, DateCreated)
        VALUES ('seed-' + CAST(@k AS NVARCHAR(8)) + 'r', @newId, @email, 'POST', '/api/auth/register', 201, 'Success', @ip, @c, @dev, 180 + @k % 90, @when),
               ('seed-' + CAST(@k AS NVARCHAR(8)) + 'l', @newId, @email, 'POST', '/api/auth/login',    200, 'Success', @ip, @c, @dev,  90 + @k % 40, DATEADD(MINUTE, 3, @when));
        /* a typo'd password now and then — ordinary users fail logins too */
        IF @k % 9 = 0
            INSERT dbo.API_LOGS (RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, DeviceFingerprint, LatencyMs, DateCreated)
            VALUES ('seed-' + CAST(@k AS NVARCHAR(8)) + 'f', @newId, @email, 'POST', '/api/auth/login', 401, 'Failed', @ip, @c, @dev, 85, DATEADD(MINUTE, 2, @when));
        /* about half buy a subscription with their own card */
        IF @k % 2 = 0
        BEGIN
            DECLARE @sid INT = (SELECT ISNULL(MAX(SubscriptionId), 6000) + 1 FROM dbo.USER_SUBSCRIPTIONS);
            DECLARE @pid INT = (SELECT ISNULL(MAX(PaymentId), 6000) + 1 FROM dbo.USER_PAYMENTS);
            DECLARE @pkg SMALLINT = CHOOSE(1 + @k % 4, 10, 11, 12, 40);
            DECLARE @price DECIMAL(10,2) = (SELECT ListPrice FROM dbo.PACKAGES WHERE PackageId = @pkg);
            INSERT dbo.USER_SUBSCRIPTIONS (SubscriptionId, UserId, CourseId, PackageId, StatusId, Active, DateActivated, DateExpiration, DateFirstAccessed)
            VALUES (@sid, @newId, 1, @pkg, 1, 1, DATEADD(MINUTE, 8, @when), DATEADD(DAY, 90, @when), DATEADD(MINUTE, 12, @when));
            INSERT dbo.USER_PAYMENTS (PaymentId, UserId, FinalAmount, TransactionAmount, DiscountAmount, TransactionId, PaymentStatus, GatewayId, PaymentModeId, ResponseCode, SourceId, PaymentTypeId, IPAddress, CountryCode, DateCreated, DatePaymentReceived, CardFingerprint, CardLast4)
            VALUES (@pid, @newId, @price, @price, 0, 'bt_base' + CAST(@k AS NVARCHAR(8)), 1, 4, 1, 1, 2, 1, @ip, @c, DATEADD(MINUTE, 8, @when), DATEADD(MINUTE, 8, @when),
                    'card-' + LOWER(CONVERT(NVARCHAR(10), HASHBYTES('MD5', 'card' + CAST(@k AS NVARCHAR(8))), 2)), RIGHT('000' + CAST((@k * 389) % 10000 AS NVARCHAR(5)), 4));
            INSERT dbo.BRAINTREE_TRANSACTIONS (TransactionId, PaymentId, TransactionStatus, Amount, IsMismatch, IsVerified, IsFraudDetected, DateCreated)
            VALUES ('bt_base' + CAST(@k AS NVARCHAR(8)), @pid, 'settled', @price, 0, 1, 0, DATEADD(MINUTE, 8, @when));
            INSERT dbo.API_LOGS (RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, DeviceFingerprint, LatencyMs, DateCreated)
            VALUES ('seed-' + CAST(@k AS NVARCHAR(8)) + 's', @newId, @email, 'POST', '/api/orders/subscribe', 201, 'Success', @ip, @c, @dev, 420, DATEADD(MINUTE, 8, @when));
        END
    END
    SET @k += 1;
END

/* ---- PRECISION CHECK: a university cohort enrolling together from one campus IP ----
   6 real students, 40 minutes, one shared NAT address, but distinct names, distinct devices,
   own cards, institutional emails. The account-farm rule must NOT raise this. */
DECLARE @campus TABLE (fn NVARCHAR(32), ln NVARCHAR(32), mins INT);
INSERT @campus VALUES ('Aisha','Bello',0),('Marcus','Lindqvist',6),('Yuki','Tanaka',13),('Gabriel','Ferreira',21),('Niamh','Doyle',30),('Arjun','Menon',40);
DECLARE @cf NVARCHAR(32), @cl NVARCHAR(32), @cm INT, @ci INT = 0;
DECLARE campus_cur CURSOR LOCAL FAST_FORWARD FOR SELECT fn, ln, mins FROM @campus;
OPEN campus_cur; FETCH NEXT FROM campus_cur INTO @cf, @cl, @cm;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @when = DATEADD(MINUTE, @cm, DATEADD(DAY, -3, @now));
    SET @email = LOWER(LEFT(@cf,1) + @cl) + '@med.state-university.edu';
    SET @dev = 'dev-' + LOWER(CONVERT(NVARCHAR(8), HASHBYTES('MD5', 'campus' + @cf), 2));
    INSERT dbo.USERS (Email, FirstName, LastName, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, SignupSource, DateCreated)
    VALUES (@email, @cf, @cl, 'US', '128.95.10.4', @dev, 'Mozilla/5.0 (Macintosh) Safari/17.5', 'Web', @when);
    SET @newId = SCOPE_IDENTITY();
    INSERT dbo.API_LOGS (RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, DeviceFingerprint, LatencyMs, DateCreated)
    VALUES ('seed-campus-' + CAST(@ci AS NVARCHAR(4)), @newId, @email, 'POST', '/api/auth/register', 201, 'Success', '128.95.10.4', 'US', @dev, 210, @when);
    SET @ci += 1;
    FETCH NEXT FROM campus_cur INTO @cf, @cl, @cm;
END
CLOSE campus_cur; DEALLOCATE campus_cur;

/* ---- ATTACK (2 days ago): a trial farm — templated mailinator addresses, one device, one IP, all on 7-day trials ---- */
DECLARE @f INT = 1, @fid INT, @fsid INT, @fpid INT;
WHILE @f <= 7
BEGIN
    SET @when = DATEADD(SECOND, @f * 47, DATEADD(DAY, -2, @now));
    SET @email = 'studyhub.acc' + RIGHT('0' + CAST(@f AS NVARCHAR(3)), 2) + '@mailinator.com';
    INSERT dbo.USERS (Email, FirstName, LastName, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, SignupSource, DateCreated)
    VALUES (@email, 'Study', 'Hub' + CAST(@f AS NVARCHAR(3)), 'US', '185.220.101.33', 'dev-5f1c9e2a', 'python-requests/2.31', 'Web', @when);
    SET @fid = SCOPE_IDENTITY();
    SET @fsid = (SELECT ISNULL(MAX(SubscriptionId), 6000) + 1 FROM dbo.USER_SUBSCRIPTIONS);
    SET @fpid = (SELECT ISNULL(MAX(PaymentId), 6000) + 1 FROM dbo.USER_PAYMENTS);
    INSERT dbo.USER_SUBSCRIPTIONS (SubscriptionId, UserId, CourseId, PackageId, StatusId, Active, DateActivated, DateExpiration, DateFirstAccessed)
    VALUES (@fsid, @fid, 1, 11, 1, 1, DATEADD(SECOND, 20, @when), DATEADD(DAY, 7, @when), DATEADD(SECOND, 40, @when));
    INSERT dbo.USER_PAYMENTS (PaymentId, UserId, FinalAmount, TransactionAmount, DiscountAmount, TransactionId, PaymentStatus, GatewayId, PaymentModeId, ResponseCode, SourceId, PaymentTypeId, IPAddress, CountryCode, DateCreated, DatePaymentReceived, CardFingerprint, CardLast4)
    VALUES (@fpid, @fid, 0, 0, 299.00, NULL, 1, NULL, 6, 1, 2, 7, '185.220.101.33', 'US', DATEADD(SECOND, 20, @when), DATEADD(SECOND, 20, @when), NULL, NULL);
    INSERT dbo.API_LOGS (RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, UserAgent, DeviceFingerprint, LatencyMs, DateCreated)
    VALUES ('seed-farm-' + CAST(@f AS NVARCHAR(3)) + 'r', @fid, @email, 'POST', '/api/auth/register', 201, 'Success', '185.220.101.33', 'US', 'python-requests/2.31', 'dev-5f1c9e2a', 95, @when),
           ('seed-farm-' + CAST(@f AS NVARCHAR(3)) + 's', @fid, @email, 'POST', '/api/orders/subscribe', 201, 'Success', '185.220.101.33', 'US', 'python-requests/2.31', 'dev-5f1c9e2a', 140, DATEADD(SECOND, 20, @when));
    SET @f += 1;
END

/* ---- rule catalogue for the identity rules + governance metadata on every rule ---- */
MERGE dbo.RuleDefinitions AS t
USING (VALUES
 ('account-farm','Account Farm / Multi-Accounting','v2','Account',
  'Clusters of new accounts linked by registration IP, device fingerprint or payment card, scored on corroborating identity signals (templated emails, disposable domains, scripted cadence, trial/discount harvesting). A shared IP alone never raises a case.',
  'High','{"windowMinutes":60,"minAccounts":3,"alertScore":55,"lookbackHours":168}'),
 ('credential-stuffing','Credential Stuffing / Account Takeover','v1','Account Takeover',
  'Many failed logins from one source across many distinct accounts in a short window. Any success inside the burst is treated as a likely account takeover.',
  'Critical','{"windowMinutes":15,"minFailures":8,"minDistinctAccounts":5}')
) AS s (RuleId,Name,Version,Category,Description,Severity,ConfigJson)
ON t.RuleId = s.RuleId
WHEN MATCHED THEN UPDATE SET Name=s.Name,Version=s.Version,Category=s.Category,Description=s.Description,Severity=s.Severity,ConfigJson=s.ConfigJson,Enabled=1
WHEN NOT MATCHED THEN INSERT (RuleId,Name,Version,Category,Description,Severity,ConfigJson,Enabled,IsDeclarative)
  VALUES (s.RuleId,s.Name,s.Version,s.Category,s.Description,s.Severity,s.ConfigJson,1,0);

UPDATE dbo.RuleDefinitions SET Status = 'Active', VersionNo = 1, Owner = COALESCE(Owner, 'maya.chen'),
       ApprovedBy = COALESCE(ApprovedBy, 'maya.chen'), ApprovedUtc = COALESCE(ApprovedUtc, DATEADD(DAY, -30, @now)),
       CreatedUtc = COALESCE(CreatedUtc, DATEADD(DAY, -45, @now)), Severity = COALESCE(Severity, 'High'), PendingVersionNo = NULL;
UPDATE dbo.RuleDefinitions SET Severity = 'Critical' WHERE RuleId IN ('authorization-gap','paid-without-braintree','credential-stuffing');
UPDATE dbo.RuleDefinitions SET Rationale = CASE RuleId
   WHEN 'account-farm'          THEN 'Trial and discount programs are funded per real student. Farms convert one person (or a bot) into many free subscriptions and resell access.'
   WHEN 'credential-stuffing'   THEN 'Leaked password lists are replayed against our login. Each takeover is a stolen paid subscription and a support/chargeback cost.'
   WHEN 'paid-without-braintree' THEN 'An order marked Paid with no gateway capture is revenue we granted access for but never collected.'
   WHEN 'discount-code-fraud'   THEN 'Every improperly applied code is margin given away; abused codes leak onto coupon sites within hours.'
   WHEN 'same-subscription-region' THEN 'One licence used from several countries at once means the credentials are shared or resold.'
   WHEN 'blocked-ip-embargo'    THEN 'Compliance: embargoed regions and known-bad sources must never be served.'
   WHEN 'authorization-gap'     THEN 'Protected content served after a deny is a direct IP leak that no access log alone reveals.'
   ELSE Rationale END;

INSERT dbo.RuleVersions (RuleId, VersionNo, Status, DefinitionJson, ChangeNote, Author, Reviewer, ReviewNote, CreatedUtc, ReviewedUtc)
SELECT RuleId, 1, 'Approved',
       (SELECT r.Name AS name, r.Category AS category, r.Severity AS severity, r.Description AS description, r.ConfigJson AS configJson FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
       'Initial version', 'jordan.patel', 'maya.chen', 'Reviewed against 30 days of history.', DATEADD(DAY, -31, @now), DATEADD(DAY, -30, @now)
FROM dbo.RuleDefinitions r;

/* ---- 14 days of historical, already-worked cases (for trend, precision and $-protected baselines) ---- */
DECLARE @h INT = 1, @hr NVARCHAR(64), @hcat NVARCHAR(64), @ht NVARCHAR(256), @hsev NVARCHAR(16), @himp DECIMAL(12,2), @hc DATETIME2, @disp NVARCHAR(32), @hcid INT;
WHILE @h <= 26
BEGIN
    SET @hr = CHOOSE(1 + @h % 6, 'account-farm','discount-code-fraud','paid-without-braintree','same-subscription-region','credential-stuffing','account-farm');
    SET @hcat = CHOOSE(1 + @h % 6, 'Account','Discount','Payment','Account','Account Takeover','Account');
    SET @ht = CHOOSE(1 + @h % 6,
        CAST(3 + @h % 6 AS NVARCHAR(4)) + ' linked accounts harvesting trials (historical)',
        'Discount code over-used beyond its limit (historical)',
        'Order marked Paid with no Braintree capture (historical)',
        'Subscription used from 2 countries within 1h (historical)',
        'Login burst against ' + CAST(6 + @h % 9 AS NVARCHAR(4)) + ' accounts from one source (historical)',
        'Device shared across ' + CAST(3 + @h % 4 AS NVARCHAR(4)) + ' new accounts (historical)');
    SET @hsev = CHOOSE(1 + @h % 4, 'High','Critical','Medium','High');
    SET @himp = CHOOSE(1 + @h % 6, 597.00, 384.00, 299.00, 0.00, 1196.00, 448.00);
    SET @hc = DATEADD(MINUTE, -(@h * 701) % 1440, DATEADD(DAY, -(1 + @h % 14), @now));
    SET @disp = CASE WHEN @h % 7 = 3 THEN 'FalsePositive' WHEN @h % 11 = 5 THEN 'Benign' ELSE 'ConfirmedFraud' END;
    INSERT dbo.RiskCases (CaseKey, RuleId, RuleVersion, Title, Severity, Confidence, Score, BusinessImpactUsd, ScoreBreakdownJson, Status, AccountId, EntityRef,
                          ExpectedJson, ActualJson, AssignedTo, CreatedUtc, ResolvedUtc, Category, Disposition, DispositionBy, FirstSignalUtc, TriggerSignalUtc,
                          UpdatedUtc, AcknowledgedUtc, AcknowledgedBy, DetectionLatencyMs, ImpactFormula)
    VALUES ('history:' + CAST(@h AS NVARCHAR(8)), @hr, 'v1', @ht, @hsev, 0.86, CHOOSE(1 + @h % 4, 74, 91, 63, 79), @himp,
            '[]', 'Resolved', NULL, 'history:' + CAST(@h AS NVARCHAR(8)),
            'Historical case — worked before this demo window.', 'See resolution notes.', 'sam.ortiz', @hc, DATEADD(HOUR, 2 + @h % 20, @hc),
            @hcat, @disp, 'sam.ortiz', DATEADD(MINUTE, -14, @hc), DATEADD(SECOND, -(2 + @h % 9), @hc),
            DATEADD(HOUR, 2 + @h % 20, @hc), DATEADD(MINUTE, 4 + (@h * 7) % 50, @hc), 'sam.ortiz', (2 + @h % 9) * 1000 + (@h * 137) % 900,
            'Historical figure recorded at resolution.');
    SET @hcid = SCOPE_IDENTITY();
    INSERT dbo.CaseActivity (CaseId, Action, Actor, Notes, OccurredUtc) VALUES
      (@hcid, 'Created', 'system', 'Flagged by ' + @hr, @hc),
      (@hcid, 'Resolved', 'sam.ortiz', CASE @disp WHEN 'ConfirmedFraud' THEN 'Confirmed fraud — accounts suspended, access revoked.' WHEN 'FalsePositive' THEN 'False positive — legitimate shared office network.' ELSE 'Benign — expected behaviour, no action.' END, DATEADD(HOUR, 2 + @h % 20, @hc));
    SET @h += 1;
END

PRINT 'Trust platform v2 schema + seed complete.';
GO
