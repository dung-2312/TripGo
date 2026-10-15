-- ============================================================================
-- SCRIPT 03: SEED SOURCE-VERIFIED TRANSPORT DATA (POINTS & STOPS)
-- Project: Tripgo (Phase 2.5: Verified Transport Data Preparation)
-- Mode: SAFEGUARDED / REQUIRES EXPLICIT APPROVAL AND TARGET VERIFICATION
-- ============================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- ============================================================================
-- EXECUTION SAFEGUARD (DELIBERATE APPROVAL & TARGET VERIFICATION REQUIRED)
-- ============================================================================
DECLARE @CONFIRM_EXECUTION BIT = 0; -- SAFETY LOCK: Must be explicitly changed to 1 to run
DECLARE @EXPECTED_DATABASE SYSNAME = N'TripgoDb_IsolatedTest'; -- Must match DB_NAME() exactly

IF @CONFIRM_EXECUTION <> 1
BEGIN
    RAISERROR(N'EXECUTION HALTED: Accidental execution safeguard active. To proceed, review operations, set @CONFIRM_EXECUTION = 1, and set @EXPECTED_DATABASE to the intended DB_NAME().', 16, 1);
    RETURN;
END;

IF DB_NAME() <> @EXPECTED_DATABASE
BEGIN
    DECLARE @TargetError NVARCHAR(256) = FORMATMESSAGE(N'TARGET VERIFICATION FAILED: Current database context is [%s], but expected target was [%s]. Aborting.', DB_NAME(), @EXPECTED_DATABASE);
    RAISERROR(@TargetError, 16, 1);
    RETURN;
END;

PRINT '====================================================================';
PRINT 'TRIPGO PHASE 2.5: SEEDING SOURCE-VERIFIED TRANSPORT DATA';
PRINT 'Target Database Verified: ' + DB_NAME();
PRINT 'Timestamp: ' + CONVERT(NVARCHAR(30), SYSDATETIMEOFFSET(), 126);
PRINT '====================================================================';

BEGIN TRANSACTION;

BEGIN TRY
    -- 1. RESOLVE CORE FOREIGN KEY IDENTIFIERS
    DECLARE @OpTrongMinh BIGINT, @OpFuta BIGINT, @OpXeNha BIGINT, @OpSaoViet BIGINT, @OpAnPhu BIGINT;
    DECLARE @LocSGN BIGINT, @LocDLI BIGINT, @LocNHA BIGINT, @LocHAN BIGINT, @LocSAP BIGINT, @LocUIH BIGINT;

    SELECT @OpTrongMinh = Id FROM [dbo].[Operators] WHERE [Code] = 'TRONGMINH';
    SELECT @OpFuta      = Id FROM [dbo].[Operators] WHERE [Code] = 'FUTA';
    SELECT @OpXeNha     = Id FROM [dbo].[Operators] WHERE [Code] = 'XENHA';
    SELECT @OpSaoViet   = Id FROM [dbo].[Operators] WHERE [Code] = 'SAOVIET';
    SELECT @OpAnPhu     = Id FROM [dbo].[Operators] WHERE [Code] = 'ANPHU';

    SELECT @LocSGN = Id FROM [dbo].[Locations] WHERE [Code] = 'SGN';
    SELECT @LocDLI = Id FROM [dbo].[Locations] WHERE [Code] = 'DLI';
    SELECT @LocNHA = Id FROM [dbo].[Locations] WHERE [Code] = 'NHA';
    SELECT @LocHAN = Id FROM [dbo].[Locations] WHERE [Code] = 'HAN';
    SELECT @LocSAP = Id FROM [dbo].[Locations] WHERE [Code] = 'SAP';
    SELECT @LocUIH = Id FROM [dbo].[Locations] WHERE [Code] = 'UIH';

    -- 2. SEED VERIFIED PHYSICAL PICKUP/DROPOFF POINTS (PickupDropoffPoints)
    -- PointType: 1 = Pickup, 2 = Dropoff, 3 = Both
    DECLARE @Points TABLE (
        OperatorId BIGINT,
        LocationId BIGINT,
        Name NVARCHAR(200),
        Address NVARCHAR(500),
        PointType INT
    );

    -- TRONGMINH Points
    INSERT INTO @Points VALUES
    (@OpTrongMinh, @LocSGN, N'Bến xe Miền Tây', N'395 Kinh Dương Vương, Phường An Lạc, Quận Bình Tân, TP.HCM', 3),
    (@OpTrongMinh, @LocSGN, N'Văn phòng Quận 5', N'147 Lê Hồng Phong, Phường 3, Quận 5, TP.HCM', 3),
    (@OpTrongMinh, @LocSGN, N'Trạm Hàng Xanh', N'486E Điện Biên Phủ, Phường 21, Quận Bình Thạnh, TP.HCM', 3),
    (@OpTrongMinh, @LocDLI, N'Trạm Đức Trọng (Phi Nôm)', N'Thôn Quảng Hiệp, Xã Hiệp Thạnh, Huyện Đức Trọng, Lâm Đồng', 3),
    (@OpTrongMinh, @LocDLI, N'Bưu điện TP. Đà Lạt', N'02 Trần Phú, Phường 3, TP. Đà Lạt, Lâm Đồng', 3),
    (@OpTrongMinh, @LocDLI, N'Bến xe Đa Thiện', N'01 Mai Anh Đào, Phường 8, TP. Đà Lạt, Lâm Đồng', 3);

    -- FUTA Points (SGN, DLI, NHA)
    INSERT INTO @Points VALUES
    (@OpFuta, @LocSGN, N'Bến xe Miền Tây (FUTA)', N'395 Kinh Dương Vương, Phường An Lạc, Quận Bình Tân, TP.HCM', 3),
    (@OpFuta, @LocSGN, N'Văn phòng Lê Hồng Phong (FUTA)', N'231-233 Lê Hồng Phong, Phường 4, Quận 5, TP.HCM', 3),
    (@OpFuta, @LocSGN, N'Bến xe Miền Đông Mới (FUTA)', N'501 Hoàng Hữu Nam, Phường Long Bình, TP. Thủ Đức, TP.HCM', 3),
    (@OpFuta, @LocDLI, N'Bến xe Liên tỉnh Đà Lạt (FUTA)', N'01 Tô Hiến Thành, Phường 3, TP. Đà Lạt, Lâm Đồng', 3),
    (@OpFuta, @LocDLI, N'Văn phòng Trần Hưng Đạo (FUTA)', N'22 Trần Hưng Đạo, Phường 10, TP. Đà Lạt, Lâm Đồng', 3),
    (@OpFuta, @LocNHA, N'Bến xe Phía Nam Nha Trang (FUTA)', N'Đường 23/10, Xã Vĩnh Trung, TP. Nha Trang, Khánh Hòa', 3);

    -- XENHA Points (SGN, NHA)
    INSERT INTO @Points VALUES
    (@OpXeNha, @LocSGN, N'Văn phòng Phạm Ngũ Lão', N'313 Phạm Ngũ Lão, Phường Phạm Ngũ Lão, Quận 1, TP.HCM', 3),
    (@OpXeNha, @LocSGN, N'Văn phòng Trần Phú', N'295 Trần Phú, Phường 8, Quận 5, TP.HCM', 3),
    (@OpXeNha, @LocSGN, N'Trạm Đón Hàng Xanh', N'Điện Biên Phủ, Phường 21, Quận Bình Thạnh, TP.HCM', 3),
    (@OpXeNha, @LocNHA, N'Văn phòng Cam Ranh', N'08 Tố Hữu, Phường Cam Thuận, TP. Cam Ranh, Khánh Hòa', 3),
    (@OpXeNha, @LocNHA, N'Văn phòng CT3 VCN Phước Hải', N'Chung cư CT3 Tố Hữu, KĐT VCN Phước Hải, TP. Nha Trang, Khánh Hòa', 3),
    (@OpXeNha, @LocNHA, N'Văn phòng Hoàng Hoa Thám', N'12D Hoàng Hoa Thám, Phường Lộc Thọ, TP. Nha Trang, Khánh Hòa', 3);

    -- SAOVIET Points (HAN, SAP)
    INSERT INTO @Points VALUES
    (@OpSaoViet, @LocHAN, N'Văn phòng Giải Phóng', N'789 Giải Phóng, Phường Giáp Bát, Quận Hoàng Mai, Hà Nội', 3),
    (@OpSaoViet, @LocHAN, N'Văn phòng Trần Nhật Duật', N'114 Trần Nhật Duật, Phường Hàng Buồm, Quận Hoàn Kiếm, Hà Nội', 3),
    (@OpSaoViet, @LocHAN, N'Văn phòng Cầu Giấy', N'07 Phạm Văn Đồng, Phường Mai Dịch, Quận Cầu Giấy, Hà Nội', 3),
    (@OpSaoViet, @LocHAN, N'Trạm Ngã tư Sân bay Nội Bài', N'Trạm thu phí Võ Nguyên Giáp, Sóc Sơn, Hà Nội', 3),
    (@OpSaoViet, @LocSAP, N'Bến xe Trung tâm Lào Cai', N'Tổ 19, Phường Bình Minh, TP. Lào Cai, Lào Cai', 3),
    (@OpSaoViet, @LocSAP, N'Bến xe Sa Pa', N'Đường Điện Biên Phủ, Thị xã Sa Pa, Lào Cai', 3),
    (@OpSaoViet, @LocSAP, N'Văn phòng 571 Điện Biên Phủ', N'571 Điện Biên Phủ, Thị xã Sa Pa, Lào Cai', 3);

    -- ANPHU Points (SGN, UIH)
    INSERT INTO @Points VALUES
    (@OpAnPhu, @LocSGN, N'Văn phòng Celadon City Tân Phú', N'E1.30 Celadon City, 36 Bờ Bao Tân Thắng, Tân Phú, TP.HCM', 3),
    (@OpAnPhu, @LocSGN, N'Bến xe Miền Đông Cũ', N'292 Đinh Bộ Lĩnh, Phường 26, Quận Bình Thạnh, TP.HCM', 3),
    (@OpAnPhu, @LocSGN, N'Trạm Ngã 4 Bình Phước', N'Quốc lộ 13, Phường Hiệp Bình Phước, TP. Thủ Đức, TP.HCM', 3),
    (@OpAnPhu, @LocUIH, N'Bến xe Phú Tài', N'Ngã 3 Phú Tài, Phường Trần Quang Diệu, TP. Quy Nhơn, Bình Định', 3),
    (@OpAnPhu, @LocUIH, N'Bến xe Trung tâm Quy Nhơn', N'71 Tây Sơn, Phường Ghềnh Ráng, TP. Quy Nhơn, Bình Định', 3);

    -- Insert Points if not already present
    INSERT INTO [dbo].[PickupDropoffPoints] (OperatorId, LocationId, Name, Address, PointType, IsActive)
    SELECT p.OperatorId, p.LocationId, p.Name, p.Address, p.PointType, 1
    FROM @Points p
    WHERE NOT EXISTS (
        SELECT 1 FROM [dbo].[PickupDropoffPoints] ep
        WHERE ep.OperatorId = p.OperatorId
          AND ep.Name = p.Name
    );

    PRINT '>> Verified Pickup/Dropoff Points seeded: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';

    -- 3. LINK VERIFIED ROUTESTOPS FOR PRIMARY CORRIDORS
    -- Trọng Minh SGN -> DLI
    DECLARE @RouteTrongMinhSgnDli BIGINT, @RouteTrongMinhDliSgn BIGINT;
    SELECT @RouteTrongMinhSgnDli = Id FROM [dbo].[Routes] WHERE Code = 'TRONGMINH-SGN-DLI';
    SELECT @RouteTrongMinhDliSgn = Id FROM [dbo].[Routes] WHERE Code = 'TRONGMINH-DLI-SGN';

    IF @RouteTrongMinhSgnDli IS NOT NULL
    BEGIN
        -- Insert SGN -> DLI stops if not present
        -- Note: Origin departure (offset 0) and terminus arrival (offset 420 mins) are verified.
        -- Intermediate transit/dropoff offsets are set to NULL (unknown/dynamic) to avoid fabricating schedules.
        INSERT INTO [dbo].[RouteStops] (RouteId, PointId, StopOrder, DepartureOffsetMinutes, ArrivalOffsetMinutes, AllowPickup, AllowDropoff)
        SELECT @RouteTrongMinhSgnDli, s.Id, row_num, dep_offset, arr_offset, allow_p, allow_d
        FROM (
            SELECT pt.Id, 1 AS row_num, 0 AS dep_offset, NULL AS arr_offset, 1 AS allow_p, 0 AS allow_d FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpTrongMinh AND pt.Name LIKE N'%Miền Tây%'
            UNION ALL
            SELECT pt.Id, 2, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpTrongMinh AND pt.Name LIKE N'%Quận 5%'
            UNION ALL
            SELECT pt.Id, 3, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpTrongMinh AND pt.Name LIKE N'%Hàng Xanh%'
            UNION ALL
            SELECT pt.Id, 4, NULL, NULL, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpTrongMinh AND pt.Name LIKE N'%Đức Trọng%'
            UNION ALL
            SELECT pt.Id, 5, NULL, NULL, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpTrongMinh AND pt.Name LIKE N'%Bưu điện%'
            UNION ALL
            SELECT pt.Id, 6, NULL, 420, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpTrongMinh AND pt.Name LIKE N'%Đa Thiện%'
        ) s(Id, row_num, dep_offset, arr_offset, allow_p, allow_d)
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[RouteStops] rs WHERE rs.RouteId = @RouteTrongMinhSgnDli AND rs.PointId = s.Id
        );
        PRINT '>> RouteStops linked for TRONGMINH SGN->DLI: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';
    END

    -- Xe Nhà SGN -> NHA
    DECLARE @RouteXeNhaSgnNha BIGINT;
    SELECT @RouteXeNhaSgnNha = Id FROM [dbo].[Routes] WHERE Code = 'XENHA-SGN-NHA';
    IF @RouteXeNhaSgnNha IS NOT NULL
    BEGIN
        -- Note: Origin departure (offset 0) and terminus arrival (offset 480 mins) are verified.
        -- Intermediate transit/dropoff offsets are set to NULL (unknown/dynamic).
        INSERT INTO [dbo].[RouteStops] (RouteId, PointId, StopOrder, DepartureOffsetMinutes, ArrivalOffsetMinutes, AllowPickup, AllowDropoff)
        SELECT @RouteXeNhaSgnNha, s.Id, row_num, dep_offset, arr_offset, allow_p, allow_d
        FROM (
            SELECT pt.Id, 1 AS row_num, 0 AS dep_offset, NULL AS arr_offset, 1 AS allow_p, 0 AS allow_d FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpXeNha AND pt.Name LIKE N'%Phạm Ngũ Lão%'
            UNION ALL
            SELECT pt.Id, 2, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpXeNha AND pt.Name LIKE N'%Trần Phú%'
            UNION ALL
            SELECT pt.Id, 3, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpXeNha AND pt.Name LIKE N'%Hàng Xanh%'
            UNION ALL
            SELECT pt.Id, 4, NULL, NULL, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpXeNha AND pt.Name LIKE N'%Cam Ranh%'
            UNION ALL
            SELECT pt.Id, 5, NULL, NULL, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpXeNha AND pt.Name LIKE N'%Phước Hải%'
            UNION ALL
            SELECT pt.Id, 6, NULL, 480, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpXeNha AND pt.Name LIKE N'%Hoàng Hoa Thám%'
        ) s(Id, row_num, dep_offset, arr_offset, allow_p, allow_d)
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[RouteStops] rs WHERE rs.RouteId = @RouteXeNhaSgnNha AND rs.PointId = s.Id
        );
        PRINT '>> RouteStops linked for XENHA SGN->NHA: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';
    END

    -- Sao Việt HAN -> SAP
    DECLARE @RouteSaoVietHanSap BIGINT;
    SELECT @RouteSaoVietHanSap = Id FROM [dbo].[Routes] WHERE Code = 'SAOVIET-HAN-SAP';
    IF @RouteSaoVietHanSap IS NOT NULL
    BEGIN
        -- Note: Origin departure (offset 0) and terminus arrival (offset 360 mins) are verified.
        -- Intermediate transit/dropoff offsets are set to NULL (unknown/dynamic).
        INSERT INTO [dbo].[RouteStops] (RouteId, PointId, StopOrder, DepartureOffsetMinutes, ArrivalOffsetMinutes, AllowPickup, AllowDropoff)
        SELECT @RouteSaoVietHanSap, s.Id, row_num, dep_offset, arr_offset, allow_p, allow_d
        FROM (
            SELECT pt.Id, 1 AS row_num, 0 AS dep_offset, NULL AS arr_offset, 1 AS allow_p, 0 AS allow_d FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%Giải Phóng%'
            UNION ALL
            SELECT pt.Id, 2, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%Trần Nhật Duật%'
            UNION ALL
            SELECT pt.Id, 3, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%Cầu Giấy%'
            UNION ALL
            SELECT pt.Id, 4, NULL, NULL, 1, 0 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%Nội Bài%'
            UNION ALL
            SELECT pt.Id, 5, NULL, NULL, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%Lào Cai%'
            UNION ALL
            SELECT pt.Id, 6, NULL, 360, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%Bến xe Sa Pa%'
            UNION ALL
            SELECT pt.Id, 7, NULL, NULL, 0, 1 FROM [dbo].[PickupDropoffPoints] pt WHERE pt.OperatorId = @OpSaoViet AND pt.Name LIKE N'%571 Điện Biên Phủ%'
        ) s(Id, row_num, dep_offset, arr_offset, allow_p, allow_d)
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[RouteStops] rs WHERE rs.RouteId = @RouteSaoVietHanSap AND rs.PointId = s.Id
        );
        PRINT '>> RouteStops linked for SAOVIET HAN->SAP: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';
    END

    COMMIT TRANSACTION;
    PRINT '====================================================================';
    PRINT 'SEEDING SOURCE-VERIFIED DATA COMPLETED SUCCESSFULLY.';
    PRINT '====================================================================';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT '!! ERROR OCCURRED DURING SEEDING: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO
