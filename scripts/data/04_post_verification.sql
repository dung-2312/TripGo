-- ============================================================================
-- SCRIPT 04: POST-UPDATE VERIFICATION AUDIT
-- Project: Tripgo (Phase 2.5: Verified Transport Data Preparation)
-- Mode: SAFEGUARDED / READ-ONLY AUDIT VERIFICATION
-- ============================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- ============================================================================
-- TARGET VERIFICATION
-- ============================================================================
DECLARE @EXPECTED_DATABASE SYSNAME = N'TripgoDb_IsolatedTest'; -- Default check target

IF DB_NAME() NOT IN (N'TripgoDb', N'TripgoDb_IsolatedTest')
BEGIN
    DECLARE @TargetError NVARCHAR(256) = FORMATMESSAGE(N'TARGET VERIFICATION FAILED: Current database context is [%s]. Script must run against TripgoDb or TripgoDb_IsolatedTest.', DB_NAME());
    RAISERROR(@TargetError, 16, 1);
    RETURN;
END;

PRINT '====================================================================';
PRINT 'TRIPGO PHASE 2.5: POST-UPDATE VERIFICATION AUDIT';
PRINT 'Target Database Verified: ' + DB_NAME();
PRINT 'Timestamp: ' + CONVERT(NVARCHAR(30), SYSDATETIMEOFFSET(), 126);
PRINT '====================================================================';

-- 1. VERIFY ZERO ACTIVE TRIPS ON INVALID OPERATOR-ROUTE COMBINATIONS
DECLARE @InvalidActiveTrips INT;

SELECT @InvalidActiveTrips = COUNT(*)
FROM [dbo].[Trips] t
INNER JOIN [dbo].[Routes] r ON t.RouteId = r.Id
INNER JOIN [dbo].[Operators] o ON r.OperatorId = o.Id
WHERE r.[IsActive] = 1
  AND (
      o.Code = 'THAOMANHUNG'
      OR (o.Code = 'SAOVIET' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%SGN-UIH%' OR r.Code LIKE '%UIH-SGN%'))
      OR (o.Code = 'ANPHU' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
      OR (o.Code = 'TRONGMINH' AND (r.Code LIKE '%SGN-NHA%' OR r.Code LIKE '%NHA-SGN%' OR r.Code LIKE '%HAN-SAP%' OR r.Code LIKE '%SAP-HAN%'))
      OR (o.Code = 'XENHA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
      OR (o.Code = 'FUTA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
  );

PRINT 'AUDIT CHECK 1: Active Trips with Invalid Operator-Route Combinations:';
PRINT '  >> Count: ' + CAST(@InvalidActiveTrips AS NVARCHAR(10)) + ' (Required: 0)';
IF @InvalidActiveTrips = 0
    PRINT '  >> STATUS: PASS [OK]';
ELSE
    PRINT '  >> STATUS: FAIL [INVALID COMBINATIONS STILL ACTIVE]';

-- 2. VERIFY STATUS OF FABRICATED OPERATOR (THAOMANHUNG)
DECLARE @ThaoManHungActive INT;
SELECT @ThaoManHungActive = COUNT(*) FROM [dbo].[Operators] WHERE [Code] = 'THAOMANHUNG' AND [IsActive] = 1;

PRINT 'AUDIT CHECK 2: Active Fabricated Operator (THAOMANHUNG):';
PRINT '  >> Count: ' + CAST(@ThaoManHungActive AS NVARCHAR(10)) + ' (Required: 0)';
IF @ThaoManHungActive = 0
    PRINT '  >> STATUS: PASS [OK]';
ELSE
    PRINT '  >> STATUS: FAIL [THAOMANHUNG STILL ACTIVE]';

-- 3. AUDIT ACTIVE ROUTES AND VERIFIED STOPS COUNT
PRINT '--------------------------------------------------------------------';
PRINT 'AUDIT CHECK 3: ACTIVE ROUTES & ASSOCIATED STOPS';
PRINT '--------------------------------------------------------------------';

SELECT 
    r.Code AS [RouteCode],
    o.Name AS [OperatorName],
    r.Name AS [RouteName],
    r.IsActive,
    COUNT(DISTINCT rs.Id) AS [RouteStopsCount],
    COUNT(DISTINCT t.Id) AS [ActiveTripsCount]
FROM [dbo].[Routes] r
INNER JOIN [dbo].[Operators] o ON r.OperatorId = o.Id
LEFT JOIN [dbo].[RouteStops] rs ON r.Id = rs.RouteId
LEFT JOIN [dbo].[Trips] t ON r.Id = t.RouteId AND t.Status = 2 -- Scheduled
WHERE r.IsActive = 1
GROUP BY r.Code, o.Name, r.Name, r.IsActive
ORDER BY o.Name, r.Code;

-- 4. VERIFY NO ORPHANED FOREIGN KEYS OR CORRUPT ASSOCIATIONS
DECLARE @OrphanTrips INT, @OrphanStops INT;
SELECT @OrphanTrips = COUNT(*) FROM [dbo].[Trips] t WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Routes] r WHERE r.Id = t.RouteId);
SELECT @OrphanStops = COUNT(*) FROM [dbo].[RouteStops] rs WHERE NOT EXISTS (SELECT 1 FROM [dbo].[PickupDropoffPoints] p WHERE p.Id = rs.PointId);

PRINT '--------------------------------------------------------------------';
PRINT 'AUDIT CHECK 4: REFERENTIAL INTEGRITY';
PRINT '  >> Orphan Trips without Route: ' + CAST(@OrphanTrips AS NVARCHAR(10)) + ' (Required: 0)';
PRINT '  >> Orphan RouteStops without Point: ' + CAST(@OrphanStops AS NVARCHAR(10)) + ' (Required: 0)';

IF @OrphanTrips = 0 AND @OrphanStops = 0
    PRINT '  >> STATUS: PASS [REFERENTIAL INTEGRITY INTACT]';
ELSE
    PRINT '  >> STATUS: FAIL [ORPHANED RECORDS FOUND]';

PRINT '====================================================================';
PRINT 'POST-UPDATE VERIFICATION COMPLETED.';
PRINT '====================================================================';
GO
