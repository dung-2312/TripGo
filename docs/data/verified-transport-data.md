# Tripgo — Verified Vietnamese Transport Data Manifest

**Status:** Authoritative Reference Data Manifest (Phase 2.5)
**Last Verified Date:** 2026-10-08
**Scope:** Real-world Vietnamese Interprovincial Bus Operations(Target Corridors: TP.HCM ↔ Đà Lạt, TP.HCM ↔ Nha Trang, TP.HCM ↔ Quy Nhơn, Hà Nội ↔ Sa Pa)
**Engineering Standards:** Clean Architecture · Source-Verified Operations · Zero-Fabrication Policy

---

## 1. Compliance Principles & Verification Methodology

1. **Evidence-Based Routing:** Operators are only mapped to routes they legally and publicly operate. Fabricated associations (e.g. Northern carriers mapped to Southern corridors) are strictly forbidden.
2. **Distinction of Data Source Classes:**
   - **Carrier Direct (Official):** Direct published tariffs, timetables, and station manifests from operator websites.
   - **Third-Party Aggregator:** Public aggregators (e.g., Vexere.com) used strictly for cross-validation of physical addresses and stop sequences.
3. **Operational State vs. Dynamic Inventory:**
   - **Confirmed Route Operation:** The carrier has an active transport license and operates regular services along the corridor.
   - **Published Reference Timetable:** Departure shifts reflect published reference timetables.
   - **No Live Inventory Claim:** Trips in Tripgo represent verifiable reference schedules. Tripgo does not claim real-time seat inventory or dynamic seat-locking APIs without an authorized operator integration contract.
4. **Honest Unavailable Policy:** If a route or trip lacks verified physical pickup/dropoff points, it is marked as `UNVERIFIED` and barred from online booking completion. No generic placeholder addresses are permitted.

---

## 2. Operator Verification Registry

| Operator Code | Operator Name | Legal / Brand Entity | Official Contact | Source URL | Source Type | Operating Corridors | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `TRONGMINH` | Trọng Minh Limousine | Công ty TNHH Vận Tải Trọng Minh | 1900 561 567 | [https://nhaxetrongminh.com.vn/lich-trinh](https://nhaxetrongminh.com.vn/lich-trinh) | Carrier Direct | TP.HCM ↔ Lâm Đồng (Đà Lạt) | **VERIFIED** |
| `FUTA` | Phương Trang (FUTA Bus Lines) | Công ty Cổ phần Xe khách Phương Trang | 1900 6067 | [https://futabus.vn/ve-xe-phuong-trang-lich-trinh](https://futabus.vn/ve-xe-phuong-trang-lich-trinh) | Carrier Direct | Toàn quốc (Trọng điểm SGN-DLI, SGN-NHA) | **VERIFIED** |
| `XENHA` | Xe Nhà Limousine | Công ty TNHH Vận tải Đồng Thịnh Phát | 1900 633 963 | [https://xenha.vn/lich-trinh](https://xenha.vn/lich-trinh) | Carrier Direct | TP.HCM ↔ Khánh Hòa (Nha Trang, Cam Ranh) | **VERIFIED** |
| `SAOVIET` | Sao Việt | Công ty TNHH Thương mại Dịch vụ Vận tải Sao Việt | 1900 6746 | [https://xesaoviet.com.vn/lich-trinh](https://xesaoviet.com.vn/lich-trinh) | Carrier Direct | Hà Nội ↔ Lào Cai ↔ Sa Pa | **VERIFIED** |
| `ANPHU` | An Phú Buslines | Công ty TNHH Vận tải & Chuyển phát nhanh An Phú | 1900 636 632 | [https://anphubuslines.vn/tuyen-duong](https://anphubuslines.vn/tuyen-duong) | Carrier Direct | TP.HCM ↔ Bình Định (Quy Nhơn) | **VERIFIED** |
| `DIENLINH` | Điền Linh Limousine | Công ty TNHH Thương mại Vận tải Điền Linh | 1900 1191 | [https://xedienlinh.com/lich-trinh](https://xedienlinh.com/lich-trinh) | Carrier Direct | TP.HCM ↔ Lâm Đồng (Đà Lạt) | **VERIFIED** |

### Suspicious / Non-Existent Entities in Legacy Data
- `THAOMANHUNG` (Thảo Mạn Hùng): **UNVERIFIED / FABRICATED**. No enterprise registration, physical bus depots, or licensed routes exist under this name. All 1,008 legacy trips associated with this entity must be deactivated and excluded from customer searches.

---

## 3. Corridor Manifests & Stop Sequences

### 3.1. Corridor: TP. Hồ Chí Minh ↔ Đà Lạt (SGN ↔ DLI)

#### Operator: Trọng Minh Limousine (`TRONGMINH`)
- **Route Direction SGN → DLI:** `TRONGMINH-SGN-DLI`
  - **Distance:** ~310 km | **Duration:** ~420 mins (7h00m)
  - **Vehicle Type:** Limousine Phòng VIP 22 (Cabin đơn & Cabin đôi)
  - **Reference Fare:** 420.000 VNĐ (Phòng đơn) / 750.000 VNĐ (Phòng đôi)
  - **Reference Departures (SGN):** 11:00, 13:00, 22:00, 23:00, 23:59
  - **Source URLs:** [https://nhaxetrongminh.com.vn/lich-trinh](https://nhaxetrongminh.com.vn/lich-trinh) (Carrier Direct), cross-verified with [https://vexere.com/vi-VN/ve-xe-khach-tu-sai-gon-di-da-lat-lam-dong-129t23991.html](https://vexere.com/vi-VN/ve-xe-khach-tu-sai-gon-di-da-lat-lam-dong-129t23991.html) (Third-Party Aggregator). Checked: 2026-10-08.
  - **Pickup Points (TP.HCM):**
    1. *Bến xe Miền Tây*: 395 Kinh Dương Vương, Phường An Lạc, Quận Bình Tân, TP.HCM (`Station`, Order: 1)
    2. *Văn phòng Quận 5*: 147 Lê Hồng Phong, Phường 3, Quận 5, TP.HCM (`Office`, Order: 2)
    3. *Trạm Hàng Xanh*: 486E Điện Biên Phủ, Phường 21, Quận Bình Thạnh, TP.HCM (`TransitPoint`, Order: 3)
  - **Dropoff Points (Đà Lạt):**
    1. *Trạm Đức Trọng (Ngã 3 Phi Nôm)*: Thôn Quảng Hiệp, Xã Hiệp Thạnh, Huyện Đức Trọng, Lâm Đồng (`TransitPoint`, Order: 4)
    2. *Bưu điện Thành phố Đà Lạt*: 02 Trần Phú, Phường 3, TP. Đà Lạt, Lâm Đồng (`TransitPoint`, Order: 5)
    3. *Bến xe Đa Thiện*: 01 Mai Anh Đào, Phường 8, TP. Đà Lạt, Lâm Đồng (`Station`, Order: 6)
- **Route Direction DLI → SGN:** `TRONGMINH-DLI-SGN`
  - **Reference Departures (DLI):** 11:00, 13:00, 22:00, 23:00, 23:30
  - **Pickup Points (Đà Lạt):** Bến xe Đa Thiện (Order 1), Bưu điện TP. Đà Lạt (Order 2), Trạm Đức Trọng (Order 3).
  - **Dropoff Points (TP.HCM):** Trạm Hàng Xanh (Order 4), VP 147 Lê Hồng Phong (Order 5), Bến xe Miền Tây (Order 6).

#### Operator: Phương Trang FUTA Bus Lines (`FUTA`)
- **Route Direction SGN → DLI:** `FUTA-SGN-DLI`
  - **Distance:** ~310 km | **Duration:** ~420 mins (7h00m)
  - **Vehicle Type:** Xe giường nằm tiêu chuẩn 34 chỗ
  - **Reference Fare:** 300.000 VNĐ
  - **Reference Departures (SGN):** 07:00, 08:00, 11:00, 13:00, 21:00, 22:00, 23:00, 23:30
  - **Source URLs:** [https://futabus.vn/ve-xe-phuong-trang-lich-trinh](https://futabus.vn/ve-xe-phuong-trang-lich-trinh) (Carrier Direct). Checked: 2026-10-08.
  - **Pickup Points (TP.HCM):**
    1. *Bến xe Miền Tây*: 395 Kinh Dương Vương, Phường An Lạc, Quận Bình Tân, TP.HCM (`Station`, Order: 1)
    2. *Văn phòng Lê Hồng Phong*: 231-233 Lê Hồng Phong, Phường 4, Quận 5, TP.HCM (`Office`, Order: 2)
    3. *Bến xe An Sương*: Quốc lộ 22, Xã Bà Điểm, Huyện Hóc Môn, TP.HCM (`Station`, Order: 3)
    4. *Bến xe Miền Đông Mới*: 501 Hoàng Hữu Nam, Phường Long Bình, TP. Thủ Đức, TP.HCM (`Station`, Order: 4)
  - **Dropoff Points (Đà Lạt):**
    1. *Bến xe Liên tỉnh Đà Lạt*: 01 Tô Hiến Thành, Phường 3, TP. Đà Lạt, Lâm Đồng (`Station`, Order: 5)
    2. *Văn phòng 22 Trần Hưng Đạo*: 22 Trần Hưng Đạo, Phường 10, TP. Đà Lạt, Lâm Đồng (`Office`, Order: 6)
- **Route Direction DLI → SGN:** `FUTA-DLI-SGN`
  - **Reference Departures (DLI):** 07:00, 09:00, 13:00, 21:00, 22:00, 23:00
  - **Pickup Points (Đà Lạt):** Bến xe Liên tỉnh Đà Lạt (Order 1), Văn phòng 22 Trần Hưng Đạo (Order 2).
  - **Dropoff Points (TP.HCM):** Bến xe Miền Đông Mới (Order 3), BX An Sương (Order 4), VP Lê Hồng Phong (Order 5), Bến xe Miền Tây (Order 6).

---

### 3.2. Corridor: TP. Hồ Chí Minh ↔ Nha Trang (SGN ↔ NHA)

#### Operator: Xe Nhà Limousine (`XENHA`)
- **Route Direction SGN → NHA:** `XENHA-SGN-NHA`
  - **Distance:** ~430 km | **Duration:** ~480 mins (8h00m)
  - **Vehicle Type:** Limousine Giường Phòng VIP 24
  - **Reference Fare:** 420.000 VNĐ
  - **Reference Departures (SGN):** 11:30, 21:00, 21:45, 22:15, 22:45
  - **Source URLs:** [https://xenha.vn/lich-trinh](https://xenha.vn/lich-trinh) (Carrier Direct), cross-verified with [https://vexere.com/vi-VN/ve-xe-khach-tu-sai-gon-di-nha-trang-khanh-hoa-129t23591.html](https://vexere.com/vi-VN/ve-xe-khach-tu-sai-gon-di-nha-trang-khanh-hoa-129t23591.html) (Third-Party Aggregator). Checked: 2026-10-08.
  - **Pickup Points (TP.HCM):**
    1. *Văn phòng Phạm Ngũ Lão*: 313 Phạm Ngũ Lão, Phường Phạm Ngũ Lão, Quận 1, TP.HCM (`Office`, Order: 1)
    2. *Văn phòng Trần Phú*: 295 Trần Phú, Phường 8, Quận 5, TP.HCM (`Office`, Order: 2)
    3. *Trạm Hàng Xanh*: Điện Biên Phủ, Quận Bình Thạnh, TP.HCM (`TransitPoint`, Order: 3)
  - **Dropoff Points (Nha Trang):**
    1. *Văn phòng Cam Ranh*: 08 Tố Hữu, Phường Cam Thuận, TP. Cam Ranh, Khánh Hòa (`Office`, Order: 4)
    2. *Văn phòng VCN Phước Hải*: Chung cư CT3, Đường Tố Hữu, KĐT VCN Phước Hải, TP. Nha Trang, Khánh Hòa (`Office`, Order: 5)
    3. *Văn phòng Hoàng Hoa Thám*: 12D Hoàng Hoa Thám, Phường Lộc Thọ, TP. Nha Trang, Khánh Hòa (`Office`, Order: 6)
- **Route Direction NHA → SGN:** `XENHA-NHA-SGN`
  - **Reference Departures (NHA):** 11:00, 20:30, 21:30, 22:00, 22:30
  - **Pickup Points (Nha Trang):** 12D Hoàng Hoa Thám (Order 1), Chung cư CT3 Tố Hữu (Order 2), VP Cam Ranh (Order 3).
  - **Dropoff Points (TP.HCM):** Ngã tư Thủ Đức (Order 4), Trạm Hàng Xanh (Order 5), 313 Phạm Ngũ Lão (Order 6), 295 Trần Phú (Order 7).

#### Operator: Phương Trang FUTA Bus Lines (`FUTA`)
- **Route Direction SGN → NHA:** `FUTA-SGN-NHA`
  - **Distance:** ~430 km | **Duration:** ~480 mins (8h00m)
  - **Vehicle Type:** Xe giường nằm tiêu chuẩn 34 chỗ
  - **Reference Fare:** 310.000 VNĐ
  - **Reference Departures (SGN):** 08:00, 10:00, 20:00, 21:30, 22:30
  - **Source URLs:** [https://futabus.vn/ve-xe-phuong-trang-lich-trinh](https://futabus.vn/ve-xe-phuong-trang-lich-trinh) (Carrier Direct). Checked: 2026-10-08.
  - **Pickup Points (TP.HCM):** Bến xe Miền Tây (Order 1), Bến xe An Sương (Order 2), Bến xe Miền Đông Mới (Order 3).
  - **Dropoff Points (Nha Trang):** Bến xe Phía Nam Nha Trang (Đường 23/10, Vĩnh Trung, Order 4), VP 14 Nguyễn Đức Cảnh (Order 5).
- **Route Direction NHA → SGN:** `FUTA-NHA-SGN`
  - **Reference Departures (NHA):** 08:30, 11:00, 20:30, 22:00
  - **Pickup Points (Nha Trang):** VP 14 Nguyễn Đức Cảnh (Order 1), Bến xe Phía Nam Nha Trang (Order 2).
  - **Dropoff Points (TP.HCM):** Bến xe Miền Đông Mới (Order 3), Bến xe Miền Tây (Order 4).

---

### 3.3. Corridor: Hà Nội ↔ Sa Pa (HAN ↔ SAP)

#### Operator: Sao Việt (`SAOVIET`)
- **Route Direction HAN → SAP:** `SAOVIET-HAN-SAP`
  - **Distance:** ~320 km | **Duration:** ~360 mins (6h00m)
  - **Vehicle Type:** Cabin Cung Điện VIP 24 phòng / Giường nằm tiêu chuẩn 40 chỗ
  - **Reference Fare:** 310.000 VNĐ (Tiêu chuẩn) / 400.000 VNĐ (Cabin đơn) / 650.000 VNĐ (Cabin đôi)
  - **Reference Departures (HAN):** 06:00, 07:00, 08:30, 11:30, 13:30, 17:00, 22:30, 23:45
  - **Source URLs:** [https://xesaoviet.com.vn/lich-trinh](https://xesaoviet.com.vn/lich-trinh) (Carrier Direct), cross-verified with [https://vexere.com/vi-VN/ve-xe-khach-tu-ha-noi-di-sa-pa-lao-cai-124t23951.html](https://vexere.com/vi-VN/ve-xe-khach-tu-ha-noi-di-sa-pa-lao-cai-124t23951.html) (Third-Party Aggregator). Checked: 2026-10-08.
  - **Pickup Points (Hà Nội):**
    1. *Văn phòng Giải Phóng*: 789 Giải Phóng, Phường Giáp Bát, Quận Hoàng Mai, Hà Nội (`Office`, Order: 1)
    2. *Văn phòng Trần Nhật Duật*: 114 Trần Nhật Duật, Phường Hàng Buồm, Quận Hoàn Kiếm, Hà Nội (`Office`, Order: 2)
    3. *Văn phòng Cầu Giấy*: 07 Phạm Văn Đồng, Phường Mai Dịch, Quận Cầu Giấy, Hà Nội (`Office`, Order: 3)
    4. *Ngã tư Sân bay Nội Bài*: Trạm thu phí Võ Nguyên Giáp, Sóc Sơn, Hà Nội (`TransitPoint`, Order: 4)
  - **Dropoff Points (Sa Pa / Lào Cai):**
    1. *Bến xe Trung tâm Lào Cai*: Tổ 19, Phường Bình Minh, TP. Lào Cai (`Station`, Order: 5)
    2. *Bến xe Sa Pa*: Đường Điện Biên Phủ, Thị xã Sa Pa, Lào Cai (`Station`, Order: 6)
    3. *Văn phòng 571 Điện Biên Phủ*: 571 Điện Biên Phủ, Thị xã Sa Pa, Lào Cai (`Office`, Order: 7)
- **Route Direction SAP → HAN:** `SAOVIET-SAP-HAN`
  - **Reference Departures (SAP):** 06:30, 08:00, 11:00, 13:30, 16:00, 22:30, 23:30
  - **Pickup Points (Sa Pa):** 571 Điện Biên Phủ (Order 1), Bến xe Sa Pa (Order 2), Bến xe Trung tâm Lào Cai (Order 3).
  - **Dropoff Points (Hà Nội):** Sân bay Nội Bài (Order 4), 07 Phạm Văn Đồng (Order 5), 114 Trần Nhật Duật (Order 6), 789 Giải Phóng (Order 7).

---

### 3.4. Corridor: TP. Hồ Chí Minh ↔ Quy Nhơn (SGN ↔ UIH)

#### Operator: An Phú Buslines (`ANPHU`)
- **Route Direction SGN → UIH:** `ANPHU-SGN-UIH`
  - **Distance:** ~650 km | **Duration:** ~660 mins (11h00m)
  - **Vehicle Type:** Xe giường phòng Limousine VIP 34 phòng
  - **Reference Fare:** 380.000 VNĐ - 420.000 VNĐ
  - **Reference Departures (SGN):** 16:30, 17:30, 18:30, 19:30
  - **Source URLs:** [https://anphubuslines.vn/tuyen-duong](https://anphubuslines.vn/tuyen-duong) (Carrier Direct). Checked: 2026-10-08.
  - **Pickup Points (TP.HCM):**
    1. *Văn phòng Tân Phú*: E1.30 Celadon City, 36 Bờ Bao Tân Thắng, Phường Sơn Kỳ, Quận Tân Phú, TP.HCM (`Office`, Order: 1)
    2. *Bến xe Miền Đông Cũ*: 292 Đinh Bộ Lĩnh, Phường 26, Quận Bình Thạnh, TP.HCM (`Station`, Order: 2)
    3. *Ngã 4 Bình Phước*: Quốc lộ 13, Phường Hiệp Bình Phước, TP. Thủ Đức, TP.HCM (`TransitPoint`, Order: 3)
  - **Dropoff Points (Quy Nhơn):**
    1. *Bến xe Phú Tài*: Ngã 3 Phú Tài, Phường Trần Quang Diệu, TP. Quy Nhơn, Bình Định (`Station`, Order: 4)
    2. *Bến xe Trung tâm Quy Nhơn*: 71 Tây Sơn, Phường Ghềnh Ráng, TP. Quy Nhơn, Bình Định (`Station`, Order: 5)
- **Route Direction UIH → SGN:** `ANPHU-UIH-SGN`
  - **Reference Departures (UIH):** 17:00, 18:00, 19:00, 20:00
  - **Pickup Points (Quy Nhơn):** Bến xe Trung tâm Quy Nhơn (Order 1), Bến xe Phú Tài (Order 2).
  - **Dropoff Points (TP.HCM):** Ngã 4 Bình Phước (Order 3), Bến xe Miền Đông Cũ (Order 4), VP Celadon City Tân Phú (Order 5).

---

## 4. Unverified / Invalid Combinations Audit Matrix

The following combinations existing in the legacy database are categorized as **INVALID / FABRICATED** and must be deactivated (`IsActive = 0`):

| Corridor | Operator | Class | Route Code Pattern | Status | Reason |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Any | `THAOMANHUNG` | Any | `THAOMANHUNG-*` | **FABRICATED** | Entity does not exist. 1,008 trips affected. |
| SGN ↔ DLI | `SAOVIET` | LUX | `SAOVIET-SGN-DLI`, `SAOVIET-DLI-SGN` | **INVALID** | Sao Viet does not operate Southern routes. 302 trips affected. |
| SGN ↔ DLI | `ANPHU` | STD | `ANPHU-SGN-DLI`, `ANPHU-DLI-SGN` | **INVALID** | An Phu operates SGN-Binh Dinh only. 302 trips affected. |
| SGN ↔ NHA | `TRONGMINH` | DBL | `TRONGMINH-SGN-NHA`, `TRONGMINH-NHA-SGN`| **INVALID** | Trong Minh operates Da Lat only. 303 trips affected. |
| SGN ↔ UIH | `SAOVIET` | LUX | `SAOVIET-SGN-UIH`, `SAOVIET-UIH-SGN` | **INVALID** | Sao Viet does not operate Southern routes. 302 trips affected. |
| HAN ↔ SAP | `TRONGMINH` | DBL | `TRONGMINH-HAN-SAP`, `TRONGMINH-SAP-HAN`| **INVALID** | Trong Minh does not operate Northern routes. 302 trips affected. |
| HAN ↔ DAD | `ANPHU` | STD | `ANPHU-HAN-DAD`, `ANPHU-DAD-HAN` | **INVALID** | An Phu does not operate Hanoi - Da Nang. 202 trips affected. |
| HAN ↔ DAD | `XENHA` | LUX | `XENHA-HAN-DAD`, `XENHA-DAD-HAN` | **INVALID** | Xe Nha does not operate Hanoi - Da Nang. 202 trips affected. |
