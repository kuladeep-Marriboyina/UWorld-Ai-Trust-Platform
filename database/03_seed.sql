/*  Synthetic data with deliberately planted anomalies so every rule fires.
    All times are relative to @now, so the demo is reproducible any day.     */

USE UWorldTrust;
GO
SET NOCOUNT ON;

IF OBJECT_ID('dbo.CaseEntities') IS NOT NULL DELETE dbo.CaseEntities;
DELETE dbo.CaseActivity;  DELETE dbo.CaseEvidence;  DELETE dbo.RiskCases;
DELETE dbo.Deployments;   DELETE dbo.Cves;          DELETE dbo.AssetInventory;
DELETE dbo.AccessEvents;  DELETE dbo.AuthOutcomes;   DELETE dbo.Entitlements;
DELETE dbo.Discounts;     DELETE dbo.Payments;       DELETE dbo.Orders;
DELETE dbo.Accounts;      DELETE dbo.RuleDefinitions;
GO

DECLARE @now DATETIME2 = SYSUTCDATETIME();

/* ---- Rule catalogue ------------------------------------------------ */
INSERT dbo.RuleDefinitions (RuleId, Name, Version, Category, Description) VALUES
 ('authorization-gap',   'Authorization Gap',          'v1', 'Access',       'Protected content returned despite a Denied/Missing authorization outcome.'),
 ('entitlement-leakage', 'Used After Subscription Ended', 'v1', 'Subscription', 'Content opened after the subscription expired, was cancelled or refunded.'),
 ('revenue-abuse',       'Revenue & Discount Abuse',   'v1', 'Revenue',      'Unexpected zero-pay orders, price-below-policy, or reused discounts across linked accounts.'),
 ('content-scraping',    'Content Scraping',           'v1', 'IP',           'High-volume sequential harvesting of the question bank.'),
 ('coordinated-account', 'Coordinated Account Abuse',  'v1', 'Account',      'Multi-account sharing scored on weighted signals (never shared-IP alone).'),
 ('cve-exposure',        'CVE Exposure',               'v1', 'Security',     'Known CVE matched to a live asset inventory version.'),
 ('release-regression',  'Release Regression',         'v1', 'Reliability',  'Spike in denials/errors immediately after a deployment.');

/* ---- Accounts (5,6,7 are one linked group) ------------------------- */
INSERT dbo.Accounts (AccountId, Email, LinkedAccountGroupId, CreatedUtc) VALUES
 (1,'alice@example.com',  NULL, DATEADD(DAY,-200,@now)),
 (2,'bob@example.com',    NULL, DATEADD(DAY,-180,@now)),
 (3,'carol@example.com',  NULL, DATEADD(DAY,-90, @now)),
 (4,'dan@example.com',    NULL, DATEADD(DAY,-60, @now)),
 (5,'ringA@example.com',  100,  DATEADD(DAY,-20, @now)),
 (6,'ringB@example.com',  100,  DATEADD(DAY,-20, @now)),
 (7,'ringC@example.com',  100,  DATEADD(DAY,-19, @now)),
 (8,'eve@example.com',    NULL, DATEADD(DAY,-40, @now));

/* ---- Orders -------------------------------------------------------- */
INSERT dbo.Orders (OrderId, AccountId, PackageId, OrderType, ListPrice, PaidAmount, PolicyMinPrice, OrderUtc) VALUES
 (1001,1,10,'Purchase',199.00,199.00,149.00, DATEADD(DAY,-30,@now)),   -- clean
 (1002,2,10,'Trial',   199.00,  0.00,149.00, DATEADD(DAY,-25,@now)),   -- zero-pay but Trial => OK
 (1003,3,20,'Purchase',299.00,  0.00,199.00, DATEADD(DAY,-10,@now)),   -- ANOMALY: unexpected zero-pay purchase
 (1004,4,10,'Purchase',199.00, 99.00,149.00, DATEADD(DAY,-8, @now)),   -- ANOMALY: price below policy min
 (1005,5,10,'Purchase',199.00, 99.50,149.00, DATEADD(DAY,-5, @now)),   -- reused discount (ring)
 (1006,6,10,'Purchase',199.00, 99.50,149.00, DATEADD(DAY,-5, @now)),   -- reused discount (ring)
 (1007,7,10,'Purchase',199.00, 99.50,149.00, DATEADD(DAY,-4, @now)),   -- reused discount (ring)
 (1008,8,30,'Purchase',149.00,149.00,119.00, DATEADD(DAY,-35,@now));   -- later refunded

/* ---- Payments ------------------------------------------------------ */
INSERT dbo.Payments (PaymentId, OrderId, Amount, Status, PaidUtc) VALUES
 (2001,1001,199.00,'Captured', DATEADD(DAY,-30,@now)),
 (2002,1004, 99.00,'Captured', DATEADD(DAY,-8, @now)),
 (2003,1005, 99.50,'Captured', DATEADD(DAY,-5, @now)),
 (2004,1006, 99.50,'Captured', DATEADD(DAY,-5, @now)),
 (2005,1007, 99.50,'Captured', DATEADD(DAY,-4, @now)),
 (2006,1008,149.00,'Refunded', DATEADD(DAY,-6, @now));   -- refund drives entitlement leakage

/* ---- Discounts (SAVE50 reused across linked accounts 5/6/7) -------- */
INSERT dbo.Discounts (DiscountId, Code, OrderId, AccountId, [Percent], AppliedUtc) VALUES
 (3001,'SAVE50',1005,5,50.00, DATEADD(DAY,-5,@now)),
 (3002,'SAVE50',1006,6,50.00, DATEADD(DAY,-5,@now)),
 (3003,'SAVE50',1007,7,50.00, DATEADD(DAY,-4,@now)),
 (3004,'WELCOME10',1001,1,10.00, DATEADD(DAY,-30,@now));   -- normal single use

/* ---- Entitlements -------------------------------------------------- */
INSERT dbo.Entitlements (EntitlementId, AccountId, PackageId, Status, StartUtc, EndUtc, RefundedUtc) VALUES
 (4001,1,10,'Active',   DATEADD(DAY,-30,@now), DATEADD(DAY,+60,@now), NULL),
 (4002,2,10,'Expired',  DATEADD(DAY,-25,@now), DATEADD(DAY,-10,@now), NULL),   -- expired 10d ago
 (4003,3,20,'Active',   DATEADD(DAY,-10,@now), DATEADD(DAY,+80,@now), NULL),
 (4004,8,30,'Refunded', DATEADD(DAY,-35,@now), DATEADD(DAY,+30,@now), DATEADD(DAY,-6,@now));

/* ---- Authorization outcomes + matching access events --------------- */
INSERT dbo.AuthOutcomes (AuthId, AccountId, Resource, AuthResult, CorrelationId, OccurredUtc) VALUES
 (5001,1,'/qbank/package/10/q/5','Allowed','C-OK-1',  DATEADD(DAY,-1,@now)),
 (5002,4,'/qbank/package/40/q/1','Denied', 'C-GAP-1', DATEADD(HOUR,-3,@now)),   -- denied...
 (5003,8,'/qbank/package/30/q/9','Missing','C-GAP-2', DATEADD(HOUR,-2,@now));   -- no authz check

/* Access events: clean + planted gaps/leakage                          */
INSERT dbo.AccessEvents (AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc) VALUES
 (9001,1,'/qbank/package/10/q/5', 10,   5,'STUDY-1', 42000,'203.0.113.10','DEV-ALICE','C-OK-1',  DATEADD(DAY,-1,@now)),
 (9002,4,'/qbank/package/40/q/1', 40,   1,'SESS-DAN',54000,'198.51.100.7','DEV-DAN',  'C-GAP-1', DATEADD(HOUR,-3,@now)),  -- ANOMALY: denied but 54KB returned
 (9003,8,'/qbank/package/30/q/9', 30,   9,'SESS-EVE',38000,'198.51.100.9','DEV-EVE',  'C-GAP-2', DATEADD(HOUR,-2,@now)),  -- ANOMALY: missing authz but content returned
 (9004,8,'/qbank/package/30/q/4', 30,   4,'SESS-EVE2',22000,'198.51.100.9','DEV-EVE', 'C-LEAK-1',DATEADD(DAY,-2,@now)),   -- ANOMALY: access 4d after refund
 (9005,2,'/qbank/package/10/q/3', 10,   3,'SESS-BOB',31000,'203.0.113.22','DEV-BOB',  'C-LEAK-2',DATEADD(HOUR,-6,@now));  -- ANOMALY: access after entitlement expired

/* Coordinated ring: accounts 5/6/7 share one device + IP                */
INSERT dbo.AccessEvents (AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc) VALUES
 (9101,5,'/qbank/package/10/q/11',10,11,'RING-5',12000,'192.0.2.50','DEV-SHARED','C-RING-5',DATEADD(HOUR,-9,@now)),
 (9102,6,'/qbank/package/10/q/12',10,12,'RING-6',12000,'192.0.2.50','DEV-SHARED','C-RING-6',DATEADD(HOUR,-8,@now)),
 (9103,7,'/qbank/package/10/q/13',10,13,'RING-7',12000,'192.0.2.50','DEV-SHARED','C-RING-7',DATEADD(HOUR,-7,@now));

/* Content scraping: 120 sequential questions in ~4 minutes, one session  */
DECLARE @i INT = 1;
WHILE @i <= 120
BEGIN
    INSERT dbo.AccessEvents (AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc)
    VALUES (20000+@i, 6, '/qbank/package/10/q/'+CAST(@i AS NVARCHAR(10)), 10, @i, 'SCRAPE-1',
            9000, '192.0.2.77', 'DEV-BOT', 'C-SCRAPE', DATEADD(SECOND, @i*2, DATEADD(MINUTE,-20,@now)));
    SET @i += 1;
END

/* ---- Asset inventory + CVE feed ----------------------------------- */
INSERT dbo.AssetInventory (AssetId, Name, Component, Version) VALUES
 (6001,'qbank-api','Microsoft.AspNetCore.App','8.0.3'),
 (6002,'qbank-api','Newtonsoft.Json','12.0.1'),       -- ANOMALY: vulnerable version
 (6003,'billing-api','Microsoft.AspNetCore.App','8.0.8');

INSERT dbo.Cves (CveId, Component, AffectedVersion, Severity, PublishedUtc) VALUES
 ('CVE-2024-21907','Newtonsoft.Json','12.0.1','High', DATEADD(DAY,-15,@now)),
 ('CVE-2024-30045','Microsoft.AspNetCore.App','8.0.3','Medium', DATEADD(DAY,-12,@now));

/* ---- Deployment + post-deploy denial spike (release regression) ---- */
INSERT dbo.Deployments (DeploymentId, Service, Version, DeployedUtc) VALUES
 (7001,'qbank-api','26.7.2.0', DATEADD(HOUR,-4,@now));

DECLARE @j INT = 1;
WHILE @j <= 18
BEGIN
    INSERT dbo.AuthOutcomes (AuthId, AccountId, Resource, AuthResult, CorrelationId, OccurredUtc)
    VALUES (5100+@j, 1 + (@j % 7), '/qbank/package/10/q/'+CAST(@j AS NVARCHAR(10)),
            'Denied', 'C-REG-'+CAST(@j AS NVARCHAR(10)), DATEADD(MINUTE, @j*2, DATEADD(HOUR,-4,@now)));  -- clustered within the hour after deploy (-4h)
    SET @j += 1;
END

PRINT 'Seed complete.';
GO
