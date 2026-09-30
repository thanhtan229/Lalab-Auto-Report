# Trạng Thái Dự Án Lalab Auto Report V2

> Báo cáo tiến độ và kết quả nghiệm thu nâng cấp toàn diện Lalab Auto Report lên phiên bản V2 (bao gồm cơ chế Thư Mục Tính Số Lượng - Effective Billing Folder).
>
> Ngày cập nhật: **2026-09-29**
>
> Trạng thái tổng thể: **HOÀN THÀNH TOÀN DIỆN (100% COMPLETE)**

---

## 1. Kết Quả Kiểm Thử Tự Động (Automated Test Suite)

- **Tổng số ca kiểm thử:** `187 / 187 tests passed` (Tỷ lệ đỗ: **100%**)
- **Thời gian chạy toàn bộ:** `~4.0s`
- **Kết quả Build:** `0 Warning(s), 0 Error(s)` trên toàn bộ Solution (`Core`, `Infrastructure`, `Tests`, `UI`) ở cả chế độ Debug và Release.

### Chi tiết các bộ test suite:
1. **EffectiveBillingFolderTests.cs (18 tests - MỚI):**
   - Kiểm thử toàn diện 18 kịch bản Effective Billing Folder:
     - Nhận diện trực tiếp tại Product Folder khi chưa sửa (`AutoResolved`).
     - Bỏ qua thư mục con rỗng/chứa file không phải ảnh.
     - Tiến trình động (Dynamic Progression): Tự động di chuyển từ Product Folder sang `sua/`, rồi sang `sua/sua lai/` khi quét lại, không bị dính vào thư mục cũ.
     - Đa nhánh lá tranh chấp -> `AmbiguousPrintFolder` (`NeedsReview`).
     - Lựa chọn thủ công (`ManuallySelected`) được bảo toàn khi quét lại nếu nhánh vẫn còn hợp lệ.
     - Lựa chọn thủ công bị xóa/rỗng -> Tự động đánh giá lại, không bị crash hoặc dính vào folder cũ.
     - Lựa chọn thủ công có thư mục con sâu hơn -> Tự động di chuyển xuống nhánh lá sâu nhất mới.
     - Dòng Album tính file trực tiếp (`BillQuantity = 1`, `SheetCount = N`).
     - Dòng Album có thư mục sửa sâu hơn tính đúng số tờ và phụ phí.
     - Draft Bill tự động cập nhật số lượng và tổng tiền khi rescan mà không sinh bản ghi trùng lặp.
     - Locked Bill bất biến hoàn toàn: Không bị sửa khi rescan, bật cờ `FilesystemChangedAfterLock` khi đĩa thay đổi; từ chối gọi hàm tính lại bill trên đơn đã khóa.
     - Lọc file phi ảnh, hỗ trợ so sánh đuôi ảnh không phân biệt hoa thường.
     - Xử lý mượt mà thư mục không tồn tại / biến mất trong lúc quét.
     - Khách hàng nhiều đơn vật lý với các nhánh tính số lượng độc lập.
2. **SizeNormalizerTests.cs (40 tests):**
   - Kiểm thử toàn diện 40 kịch bản chuẩn hoá kích thước: chiều xoay `min x max`, các dấu phân cách (`x`, `*`, `×`, `-`, `_`, `cm`), tách kích thước và phần còn lại, hỗ trợ tiếng Việt có dấu.
3. **ProductResolverTests.cs (17 tests):**
   - Kiểm thử mức ưu tiên Priority 1 (Product-Specific Alias thắng Priority 2).
   - Kiểm thử phát hiện xung đột trùng lặp (Alias Conflict -> AmbiguousCollision).
   - Kiểm thử Family Alias độc lập vị trí: `ab 20x30`, `20x30 ab`, `alb_30-20`, `20×30 alb`, `album.20x30`.
   - Kiểm thử phát hiện mơ hồ khi chỉ có kích thước (`20x30` trùng giữa Ảnh in và Album -> `AmbiguousCollision`).
4. **OrderDetectionTests.cs (4 tests):**
   - Kiểm thử phân biệt đơn trực tiếp (Case A / Implicit), nhiều đơn tường minh (Case B / Explicit), và hỗn hợp (Case C / Mixed).
5. **FinalPrintFolderAndBillingTests.cs (12 tests):**
   - Scenarios 7-12: Lá trực tiếp, chuỗi thư mục, đa lá xung đột, tái sử dụng lựa chọn thư mục in đã lưu, loại bỏ so sánh source count, thư mục in rỗng.
   - Scenarios 43-45: Đơn giá ảnh in `FILE_COUNT`, bỏ qua file phi ảnh, tổng nhiều mặt hàng ảnh in.
   - Scenarios 46-52: Album số tờ chuẩn, album phát sinh thêm tờ, album dưới số tờ chuẩn (cảnh báo nhắc nhở), không trừ bìa (no cover deduction), 2 album cùng size trong 1 đơn tính thành 2 dòng riêng, đơn kết hợp ảnh in + album, bảo toàn bất biến bill đã khóa sau khi đổi giá.
6. **CustomerResolutionTests.cs, CustomerNormalizerTests.cs (23 tests):**
   - Chuẩn hoá tên khách, alias chính xác, alias chuẩn hoá, alias xung đột, 3 thư mục vật lý cùng khách hàng.
7. **SmartScanAndSnapshotTests.cs, LockingServiceTests.cs (18 tests):**
   - Quét thủ công có phạm vi, scan snapshot, quy trình khóa đơn và phát hiện thay đổi trên đĩa sau khi khóa.
8. **ReportServiceTests.cs, BillingEngineTests.cs, V2UpgradeFeatureTests.cs (30 tests):**
   - Báo cáo ngày, báo cáo tháng từ SQLite không quét đĩa, phát hiện ngày thiếu, tính toán bill.

---

## 2. Danh Mục Yêu Cầu Nghiệm Thu V2 (Acceptance Matrix)

| STT | Yêu cầu nghiệp vụ | Hiện trạng | Ghi chú kỹ thuật |
|---|---|---|---|
| 1 | Thư mục tính số lượng (Effective Billing Folder) | **ĐẠT** | Thư mục ảnh hợp lệ sâu nhất; xuất bill trước in ngay tại Product Folder |
| 2 | Tiến trình thư mục động không bị dính (Anti-Stickiness) | **ĐẠT** | Tự động chuyển Product Folder -> `sua/` -> `sua lai/` khi rescan |
| 3 | Phân biệt `AutoResolved` vs `ManuallySelected` | **ĐẠT** | 1 lá -> tự động; nhiều lá -> chọn thủ công (bảo toàn nếu vẫn là lá hợp lệ) |
| 4 | Draft Bill tự cập nhật khi rescan | **ĐẠT** | Tự cập nhật số lượng, tính lại line total và subtotal theo ID hiện có |
| 5 | Locked Bill bất biến hoàn toàn | **ĐẠT** | Không ghi đè khi rescan; cờ `filesystem_changed_after_lock = true` |
| 6 | Hỗ trợ 1 Customer có nhiều đơn (Explicit Orders) | **ĐẠT** | Nhận diện `Date -> Customer -> Order -> Product` |
| 7 | Hỗ trợ cấu trúc cũ song song (Implicit Orders) | **ĐẠT** | Gom sản phẩm trực tiếp dưới Customer thành Đơn trực tiếp |
| 8 | Kiến trúc Dòng sản phẩm -> Biến thể -> Bảng giá | **ĐẠT** | `ProductFamily` -> `ProductVariant` -> `ProductSpecificAlias` |
| 9 | Family Aliases dùng chung cho mọi kích thước | **ĐẠT** | `ab`, `alb`, `album` áp dụng cho mọi kích thước Album |
| 10 | Size Normalizer tự động khử chiều xoay | **ĐẠT** | `20x30` = `30x20` -> quy về `min(w,h) x max(w,h)` |
| 11 | Product-Specific Alias override ưu tiên cao nhất | **ĐẠT** | Priority 1 ghi đè Priority 2, phát hiện trùng lặp an toàn |
| 12 | Tính giá ảnh in `FILE_COUNT` | **ĐẠT** | Bỏ qua file phi ảnh, nhân đơn giá chuẩn |
| 13 | Tính giá album `ALBUM_BASE_PLUS_EXTRA` | **ĐẠT** | Base sheets + extra sheets, **không trừ bìa**, 2 album cùng size tính 2 job riêng |
| 14 | Migration an toàn giữ nguyên dữ liệu V1/V2 | **ĐẠT** | Migration 5 bổ sung `folder_resolution_mode` và snapshot |
| 15 | Smart Scan thủ công, không background loop | **ĐẠT** | Chỉ quét khi bấm nút hoặc chọn phạm vi |
| 16 | Báo cáo đọc từ SQLite, không quét lại ổ đĩa | **ĐẠT** | Truy vấn DB trực tiếp, tự động phát hiện ngày thiếu |
| 17 | Giao diện hiển thị rõ ràng từng đơn con và thư mục tính số lượng | **ĐẠT** | Nút "Mở thư mục" (tooltip: Mở thư mục tính số lượng), badge Tự động / Thủ công |

---

## 3. Các Thành Phần Mã Nguồn Cốt Lõi Được Thay Đổi / Bổ Sung

- `src/LalabAutoReport.Core/Domain/Enums.cs`: Bổ sung `BillingFolderResolutionMode` (`AutoResolved`, `ManuallySelected`).
- `src/LalabAutoReport.Core/Interfaces/Interfaces.cs`: Bổ sung `EffectiveBillingFolderFullPath`, `EffectiveBillingFolderRelativePath`, `EffectiveBillingCount`, `ResolveEffectiveBillingFolder(...)`.
- `src/LalabAutoReport.Core/Domain/Models.cs`: Bổ sung `FolderResolutionMode` trên `OrderItemScan` và `FolderResolutionModeSnapshot` trên `BillLine`.
- `src/LalabAutoReport.Core/Services/PrintFolderResolver.cs`: Thuật toán Effective Billing Folder không bị dính (non-sticky leaf resolution), hỗ trợ tính bill trước in và giải quyết đa lá.
- `src/LalabAutoReport.Core/Services/ScanService.cs`: Chỉ truyền đường dẫn cũ khi `FolderResolutionMode == ManuallySelected`, tự động cập nhật Draft Bill khi rescan, kiểm tra thay đổi thư mục tính số lượng trên Locked Bill.
- `src/LalabAutoReport.Core/Services/BillingService.cs`: Tái sử dụng Draft Bill hiện có, bảo vệ tuyệt đối Locked Bill, chụp snapshot `FolderResolutionModeSnapshot`.
- `src/LalabAutoReport.Infrastructure/Data/DatabaseMigrator.cs`: Migration 5 (`V2EffectiveBillingFolderAndResolutionMode`).
- `src/LalabAutoReport.Infrastructure/Data/SqliteOrderRepository.cs` & `SqliteBillRepository.cs`: Ánh xạ và lưu trữ `folder_resolution_mode` và `folder_resolution_mode_snapshot`.
- `src/LalabAutoReport.UI/ViewModels/OrderDisplayModel.cs` & `DashboardView.xaml`: Hiển thị "Thư mục tính số lượng", badge "Tự động" / "Thủ công", tooltip và nút "Mở thư mục".
- `tests/LalabAutoReport.Tests/EffectiveBillingFolderTests.cs`: Bộ 18 test kịch bản toàn diện cho Effective Billing Folder.

---

## 4. Tính Năng Bổ Sung: Customer-Centric Billing & Xuất Hóa Đơn JPEG

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú |
|---|---|---|---|
| 1 | Gom đơn theo Canonical Customer | **ĐẠT** | Gom mọi alias folder và mọi ngày chưa khóa vào 1 bill draft |
| 2 | Phát hiện đơn chưa bill từ kỳ trước | **ĐẠT** | Cảnh báo rõ ràng `HasOrdersFromPreviousPeriod` |
| 3 | Scoped Smart Rescan khi bấm tính bill | **ĐẠT** | Quét lại đúng các đơn tham gia draft, cập nhật giá và số lượng |
| 4 | Cửa sổ duyệt hóa đơn (Bill Review Modal) | **ĐẠT** | Giữ nhóm từng đơn vật lý, cho phép bật/tắt đơn hoặc dòng |
| 5 | Ghi đè số lượng không phá hủy | **ĐẠT** | `ScannedQuantity` giữ nguyên, ghi đè `BilledQuantity` kèm lý do |
| 6 | Ghi đè đơn giá không phá hủy | **ĐẠT** | `ConfiguredUnitPrice` giữ nguyên, ghi đè `BilledUnitPrice` kèm lý do |
| 7 | Các khoản điều chỉnh (Adjustments) | **ĐẠT** | Ship (+), Phụ phí (+), Giảm giá (-), Tùy chỉnh (+/-) số nguyên VND |
| 8 | Xuất hóa đơn ảnh JPEG vector | **ĐẠT** | WPF DrawingVisual 1080px, typography chuẩn Tinix, KHÔNG dùng Excel |
| 9 | Tên file xuất tất định | **ĐẠT** | `BILL-yyyyMMdd-xxxx_{Tên-Khách-Bỏ-Dấu}.jpg` |
| 10 | Bất biến & Xuất lại (Re-export) | **ĐẠT** | Đọc từ snapshot SQLite, không quét lại ổ cứng |
| 11 | Mở lại hóa đơn (Reopen Bill) | **ĐẠT** | Chuyển Draft an toàn, tự động hủy liên kết job |
| 12 | Tổng số kiểm thử tự động toàn diện | **187/187 ĐẠT** | 162 bài test V1/V2 + 15 bài test CustomerBilling + 10 bài test GuestBilling |

---

## 5. Tính Năng Mở Rộng: Quick Bill / Tính Bill Khách Lẻ (Guest Bill)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú |
|---|---|---|---|
| 1 | Tính bill không tạo Customer | **ĐẠT** | `bill_type = GUEST`, `customer_id = NULL`, bảng `customers` sạch 100% |
| 2 | Hỗ trợ nhiều thư mục nguồn (Multi-Source) | **ĐẠT** | Chọn 1 hoặc nhiều folder (vd: `Chi Lan 1`, `Chi Lan them`) vào cùng 1 bill |
| 3 | Theo dõi nguồn gốc thư mục (Origin Tracking) | **ĐẠT** | Lưu `source_folder_path` trên từng đơn và bảng `guest_bill_source_folders` |
| 4 | Tên khách lẻ mặc định & sửa trực tiếp | **ĐẠT** | Tự lấy tên folder đầu tiên, cho phép sửa inline không đổi tên đĩa hay tạo alias |
| 5 | Cảnh báo thư mục trùng lặp (Duplicate Detection) | **ĐẠT** | Chuẩn hóa Windows path (`PathNormalizer`), kiểm tra bill đã Locked/Exported |
| 6 | Hộp thoại cảnh báo với nút "Mở bill cũ" | **ĐẠT** | Hiển thị mã bill, ngày, tổng tiền, file ảnh, cho phép mở xem lại hoặc tiếp tục |
| 7 | Chuyển Khách Lẻ thành Khách Hàng | **ĐẠT** | `ConvertGuestBillToCustomerAsync`: tạo/khớp customer, cập nhật bill và orders |
| 8 | Cách ly khách lẻ tuyệt đối (Customer Isolation) | **ĐẠT** | Không lọt vào danh sách khách hàng, unbilled summary, hay lịch sử khách hàng |
| 9 | Xuất hóa đơn ảnh JPEG vector chuẩn hóa | **ĐẠT** | Tên file tất định: `BILL-yyyyMMdd-xxxx_{Ten-Khach-Bo-Dau}.jpg`, 1080px |
| 10 | Giao diện tiện dụng mọi nơi | **ĐẠT** | Nút `⚡ Quick Bill` trên cả Dashboard Toolbar và Khách Hàng Toolbar |
| 11 | Bộ kiểm thử tự động Guest Billing | **10/10 ĐẠT** | `GuestBillingTests.cs` kiểm tra toàn bộ 10 ca nghiệp vụ khắt khe |

---

## 6. Tính Năng Mới: Tích Hợp Windows File Explorer (Quick Bill & Đã In)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | Menu chuột phải 2 lệnh tĩnh chuẩn Windows 10 | **ĐẠT** | `⚡ QUICK BILL (Lalab)` và `🏷️ ĐÃ IN (Lalab)` trong `HKCU`, không dùng COM DLL |
| 2 | Toggle 2 chiều "ĐÃ IN" trên cùng 1 command | **ĐẠT** | Click 1: Đánh dấu ĐÃ IN; Click 2: Gỡ đánh dấu ĐÃ IN |
| 3 | Bảo toàn tuyệt đối tên thư mục (Zero Rename) | **ĐẠT** | Giữ nguyên 100% tên thư mục, không đổi tên đĩa, bảo toàn audit trail và DB path |
| 4 | Tách biệt hoàn toàn trạng thái In và Billing | **ĐẠT** | `IsPrinted` độc lập; không ép khóa bill khi in và không tự đánh dấu in khi xuất bill |
| 5 | Visual Marker đỏ trên Windows 10 Explorer | **ĐẠT** | Ghi `desktop.ini` (`Hidden|System`), gán `ReadOnly` trên folder, tự sinh icon chuẩn Windows 32-bit ICO |
| 6 | Cập nhật tức thì icon không giật màn hình | **ĐẠT** | Gọi `SHChangeNotify` (`SHCNE_UPDATEITEM \| SHCNE_ATTRIBUTES`), không restart Explorer |
| 7 | Tối ưu hóa Auto-Scan (>95% I/O) | **ĐẠT** | Bỏ qua sớm 100% các folder `PRINTED` trước khi đọc đĩa và tính fingerprint |
| 8 | Hỗ trợ Fast Headless Mode (~200ms) | **ĐẠT** | Nếu app đóng: xử lý toggle ngầm và thoát ngay, không bật cửa sổ WPF |
| 9 | Single-Instance IPC qua Named Pipe | **ĐẠT** | Nếu app mở: gửi IPC `TOGGLE_PRINTED\|<path>`, cập nhật UI ngầm không cướp focus |
| 10 | Hiển thị badge trực quan trên Dashboard | **ĐẠT** | Badge `🏷️ ĐÃ IN` hiển thị rõ ràng trên từng đơn hàng đã in |
| 11 | Tổng số kiểm thử tự động toàn hệ thống | **312/312 ĐẠT** | **100% tests PASS**, thời gian chạy ~11s, 0 warnings, 0 errors |


