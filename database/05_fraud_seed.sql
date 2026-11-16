/*  Phase 2 — synthetic data with planted fraud for the 4 new rules.
    Relative to @now so it's reproducible. Also recent USER_LOGS so the
    "currently being accessed" dashboard + top-50 populate.               */

USE UWorldTrust;
GO
SET NOCOUNT ON;

DELETE dbo.GA_DISCOUNTS; DELETE dbo.REFERRAL_CODE_USAGE; DELETE dbo.NCLEX_REFERRAL_CODES;
DELETE dbo.BRAINTREE_TRANSACTIONS; DELETE dbo.USER_PAYMENT_DETAILS; DELETE dbo.USER_PAYMENTS;
DELETE dbo.USER_LOGS; DELETE dbo.USER_SUBSCRIPTIONS; DELETE dbo.BLOCKED_IPS; DELETE dbo.COUNTRIES;
GO

DECLARE @now DATETIME2 = SYSUTCDATETIME();

/* ---- reference ---- */
INSERT dbo.COUNTRIES (CountryId, Name, IsoCode, IsEmbargoed) VALUES
 (1,'United States','US',0),(2,'India','IN',0),(3,'United Kingdom','GB',0),
 (4,'Iran','IR',1),(5,'North Korea','KP',1),(6,'Canada','CA',0);

INSERT dbo.BLOCKED_IPS (IPAddress, Reason, DateCreated) VALUES
 ('45.66.77.88','Known content-scraping source', DATEADD(DAY,-10,@now)),
 ('103.21.44.9','Repeat chargeback fraud', DATEADD(DAY,-20,@now));

/* ---- subscriptions (users 101..112) ---- */
DECLARE @u INT = 101;
WHILE @u <= 112
BEGIN
    INSERT dbo.USER_SUBSCRIPTIONS (SubscriptionId, UserId, CourseId, PackageId, StatusId, Active, DateActivated, DateExpiration, DateFirstAccessed)
    VALUES (5000+@u-100, @u, 1 + (@u % 4), CAST(10 + (@u % 3) AS SMALLINT), 1, 1,
            DATEADD(DAY,-40,@now), DATEADD(DAY,+50,@now), DATEADD(DAY,-39,@now));
    SET @u += 1;
END

/* ---- recent USER_LOGS (last ~10 min) so the dashboard shows active subscriptions ---- */
DECLARE @i INT = 1;
WHILE @i <= 90
BEGIN
    DECLARE @sub INT = 5001 + (@i % 12);
    DECLARE @usr INT = 101 + (@i % 12);
    -- one deterministic home country per subscription (so only the planted sub hops regions)
    DECLARE @cc NVARCHAR(3) = CASE WHEN @sub % 5 = 0 THEN 'IN' WHEN @sub % 7 = 0 THEN 'GB' ELSE 'US' END;
    INSERT dbo.USER_LOGS (UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated)
    VALUES (@usr, @sub, '203.0.113.' + CAST(@i % 250 AS NVARCHAR(10)), @cc, 1, 'Access',
            '/api/qbank/package/10/access', DATEADD(SECOND, -(@i*6), @now));
    SET @i += 1;
END

/* one expired subscription so a payment can show an expired association */
UPDATE dbo.USER_SUBSCRIPTIONS SET Active = 0, DateExpiration = DATEADD(DAY,-3,@now) WHERE SubscriptionId = 5004;

/* ANOMALY — same subscription from two regions within the hour (impossible travel) */
INSERT dbo.USER_LOGS (UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated) VALUES
 (101, 5001, '72.14.1.10',  'US', 1, 'Access', '/api/qbank/package/10/access', DATEADD(MINUTE,-55,@now)),
 (101, 5001, '49.36.2.20',  'IN', 2, 'Access', '/api/qbank/package/10/access', DATEADD(MINUTE,-4, @now));

/* ANOMALY — blocked IP access + embargoed-country access */
INSERT dbo.USER_LOGS (UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated) VALUES
 (113, 5002, '45.66.77.88', 'US', 1, 'Access', '/api/qbank/package/10/access', DATEADD(MINUTE,-3, @now)),
 (114, 5012, '2.1.1.1',     'IR', 1, 'Access', '/api/qbank/package/30/access', DATEADD(MINUTE,-2, @now));

/* discount-apply events (carry the applydiscountcode URL) — tied to the planted discount fraud */
INSERT dbo.USER_LOGS (UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated) VALUES
 (106, 5006, '203.0.113.6', 'US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=SAVE50&packageId=12',   DATEADD(DAY,-6,@now)),
 (107, 5007, '203.0.113.7', 'US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=FRIEND15',              DATEADD(DAY,-3,@now)),
 (107, 5007, '203.0.113.7', 'US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=FRIEND15',              DATEADD(DAY,-2,@now)),
 (108, 5008, '203.0.113.8', 'US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=EXPIRED20',             DATEADD(DAY,-1,@now)),
 (109, 5009, '203.0.113.9', 'US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=SAVE50',                DATEADD(SECOND,-12,@now)),
 (110, 5010, '203.0.113.10','US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=SAVE50',                DATEADD(SECOND,-9, @now)),
 (112, 5012, '203.0.113.5', 'US', 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=SAVE50&code=EXPIRED20', DATEADD(DAY,-1,@now));

/* ---- payments (Braintree gateway = 4; PaymentStatus 1 = Paid) ---- */
INSERT dbo.USER_PAYMENTS (PaymentId, UserId, FinalAmount, TransactionAmount, DiscountAmount, TransactionId, PaymentStatus, GatewayId, PaymentModeId, ResponseCode, SourceId, PaymentTypeId, IPAddress, CountryCode, DateCreated, DatePaymentReceived) VALUES
 (6001,101,199.00,199.00,  0.00,'bt_abc123',1,4,1,1,2,1,'72.14.1.10','US',DATEADD(DAY,-5,@now),DATEADD(DAY,-5,@now)),   -- legit (has braintree)
 (6002,103,299.00,299.00,  0.00,'0',        1,4,1,1,2,1,'203.0.113.9','US',DATEADD(DAY,-3,@now),DATEADD(DAY,-3,@now)),   -- FRAUD: paid, no braintree
 (6003,104,149.00,149.00,  0.00,'bt_zzz',   1,4,1,1,2,1,'203.0.113.8','US',DATEADD(DAY,-2,@now),DATEADD(DAY,-2,@now)),   -- FRAUD: braintree IsMismatch=1
 (6004,105,  0.00,  0.00,149.00,'0',        1,4,6,1,4,7,'203.0.113.7','US',DATEADD(DAY,-4,@now),DATEADD(DAY,-4,@now)),   -- legit $0 store-credit (NOT flagged)
 (6005,106, 99.00, 99.00,  0.00,'bt_ok',    1,4,1,1,2,1,'203.0.113.6','US',DATEADD(DAY,-6,@now),DATEADD(DAY,-6,@now)),   -- legit
 (6006,112,120.00,120.00, 79.00,'bt_six',   1,4,1,1,2,1,'203.0.113.5','US',DATEADD(DAY,-1,@now),DATEADD(DAY,-1,@now));   -- stacking (see details)

INSERT dbo.BRAINTREE_TRANSACTIONS (TransactionId, PaymentId, TransactionStatus, Amount, IsMismatch, IsVerified, IsFraudDetected, DateCreated) VALUES
 ('bt_abc123',6001,'settled',199.00,0,1,0,DATEADD(DAY,-5,@now)),
 ('bt_zzz',   6003,'settled',149.00,1,0,0,DATEADD(DAY,-2,@now)),   -- mismatch
 ('bt_ok',    6005,'settled', 99.00,0,1,0,DATEADD(DAY,-6,@now)),
 ('bt_six',   6006,'settled',120.00,0,1,0,DATEADD(DAY,-1,@now));
 -- NOTE: no row for payment 6002 (TransactionId '0') → paid-without-braintree

/* stacking: order 6006 carries two REGULAR discount codes */
INSERT dbo.USER_PAYMENT_DETAILS (PaymentId, PackageId, CourseId, DiscountCode, BaseAmount, DiscountAmount, FinalAmount) VALUES
 (6006,10,1,'SAVE50',   199.00,99.50,99.50),
 (6006,10,1,'EXPIRED20',199.00,39.80,60.00),
 (6001,10,1,NULL,199.00,0,199.00),
 (6005,11,2,'WELCOME10',110.00,11.00,99.00);

/* ---- discount / referral codes ---- */
INSERT dbo.NCLEX_REFERRAL_CODES (ReferralId, ReferralCode, ReferralCodeLimit, ReferralCodeUsed, CodeExpirationDate, DiscountGroupId, DiscountBehaviourTypeId, DiscountPercent) VALUES
 (1,'SAVE50',   100, 103, DATEADD(DAY,+30,@now), 10, 1, 50.00),   -- FRAUD: Used(103) > Limit(100)
 (2,'WELCOME10',1000,  5, DATEADD(DAY,+30,@now), 11, 2, 10.00),   -- ok (stackable)
 (3,'EXPIRED20',100,   3, DATEADD(DAY,-5, @now), 12, 1, 20.00),   -- expired
 (4,'FRIEND15', 500,  10, DATEADD(DAY,+60,@now), 13, 1, 15.00);   -- per-user reuse below

/* code → group → eligible packages (GA_DISCOUNTS). SAVE50=grp10, WELCOME10=grp11, EXPIRED20=grp12, FRIEND15=grp13 */
INSERT dbo.GA_DISCOUNTS (DiscountGroupId, PackageId, DiscountAmount, IsPercent, DiscountBehaviourTypeId, FloorValue) VALUES
 (10,10,50,1,1,NULL),(10,11,50,1,1,NULL),                 -- SAVE50 valid for packages 10,11
 (11,11,10,1,2,80.00),(11,12,10,1,2,80.00),               -- WELCOME10 valid for 11,12 (stackable, floor $80)
 (12,10,20,1,1,NULL),                                     -- EXPIRED20 valid for package 10 only
 (13,10,15,1,1,NULL),(13,11,15,1,1,NULL),(13,12,15,1,1,NULL); -- FRIEND15 valid for 10,11,12

/* ANOMALY — SAVE50 (valid for packages 10,11) applied to package 12 on payment 6005 */
INSERT dbo.USER_PAYMENT_DETAILS (PaymentId, PackageId, CourseId, DiscountCode, BaseAmount, DiscountAmount, FinalAmount) VALUES
 (6005, 12, 3, 'SAVE50', 199.00, 99.50, 99.50);

INSERT dbo.REFERRAL_CODE_USAGE (ReferralCode, UserId, PaymentId, DateUsed) VALUES
 ('FRIEND15', 107, 7001, DATEADD(DAY,-3,@now)),
 ('FRIEND15', 107, 7002, DATEADD(DAY,-2,@now)),    -- FRAUD: same user reused a single-use code
 ('EXPIRED20',108, 7003, DATEADD(DAY,-1,@now)),    -- FRAUD: used after expiry (-5d)
 ('SAVE50',   109, 7004, DATEADD(SECOND,-12,@now)),
 ('SAVE50',   110, 7005, DATEADD(SECOND,-9, @now)),-- FRAUD: race double-use within seconds near limit
 ('WELCOME10',111, 7006, DATEADD(DAY,-5,@now));

/* ---- rule catalogue: remove regression, add the 4 fraud rules (Severity + ConfigJson) ---- */
DELETE dbo.RuleDefinitions WHERE RuleId = 'release-regression';

MERGE dbo.RuleDefinitions AS t
USING (VALUES
 ('paid-without-braintree','Paid Without Braintree','v1','Payment',
  'Payment marked Paid with amount>0 on the Braintree gateway but no matching gateway transaction (or a mismatch).',
  'Critical','{"braintreeGatewayId":4,"nullTxnIds":["0","0000",""],"minAmount":0.01}'),
 ('discount-code-fraud','Discount Code Fraud','v1','Discount',
  'Referral/discount code abuse: over-limit, per-user reuse, expired-but-applied, stacking violation, or race double-use.',
  'High','{"multipleUseCodes":["apcmb"],"raceWindowSeconds":60}'),
 ('same-subscription-region','Same Subscription, Different Regions','v1','Account',
  'One subscription accessed from 2+ countries within a short window (impossible travel / sharing).',
  'High','{"windowHours":24,"minCountries":2}'),
 ('blocked-ip-embargo','Blocked IP / Embargo Access','v1','Security',
  'Access or order from a blocked IP or an embargoed country.',
  'Medium','{}')
) AS s (RuleId,Name,Version,Category,Description,Severity,ConfigJson)
ON t.RuleId = s.RuleId
WHEN MATCHED THEN UPDATE SET Name=s.Name,Version=s.Version,Category=s.Category,Description=s.Description,Severity=s.Severity,ConfigJson=s.ConfigJson,Enabled=1
WHEN NOT MATCHED THEN INSERT (RuleId,Name,Version,Category,Description,Severity,ConfigJson,Enabled,IsDeclarative)
  VALUES (s.RuleId,s.Name,s.Version,s.Category,s.Description,s.Severity,s.ConfigJson,1,0);

PRINT 'Fraud seed complete.';
GO
