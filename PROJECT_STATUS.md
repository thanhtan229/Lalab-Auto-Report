> [!WARNING]
> # HISTORICAL / SUPERSEDED STATUS DOCUMENT
>
> **TÀI LIỆU LỊCH SỬ / ĐÃ ĐƯỢC THAY THẾ — KHÔNG SỬ DỤNG LÀM CURRENT SOURCE OF TRUTH**
>
> - Tài liệu này chứa các ảnh chụp trạng thái triển khai lịch sử (historical implementation snapshots tính đến ngày 2026-10-01) và **không được phép sử dụng để đánh giá mức độ sẵn sàng production hiện tại (current production readiness)**.
> - Tài liệu chứa các tuyên bố kiến trúc và cấu hình cũ đã lỗi thời (ví dụ: các thiết lập mã PIN mặc định trước đây đã bị xóa bỏ hoàn toàn ở Phase 5/10 để đảm bảo an toàn fail-closed, cũng như kiến trúc Cloud V1 cũ trước khi triển khai Cloud Sync Protocol V2).
> - Các nguồn sự thật bền vững hiện tại (current durable sources of truth) bao gồm:
>   - `PRODUCTION_FIX_PROGRESS.md`
>   - `FINAL_REVIEW_VERIFIED.md`
>   - `PLAN_PRODUCTION_HARDENING.md` execution outcome
>   - `ARCHITECTURE.md` / ADRs for current architecture.

---

# Trạng Thái Dự Án Lalab Auto Report V2

> Báo cáo tiến độ và kết quả nghiệm thu nâng cấp toàn diện Lalab Auto Report lên phiên bản V2 (bao gồm cơ chế Thư Mục Tính Số Lượng - Effective Billing Folder).
>
> Ngày cập nhật: **2026-10-01**
>
> Trạng thái tổng thể: **HOÀN THÀNH TOÀN DIỆN (100% COMPLETE)**

---

## 1. Kết Quả Kiểm Thử Tự Động (Automated Test Suite)

- **Tổng số ca kiểm thử:** `484 / 484 tests passed` (Tỷ lệ đỗ: **100%**)
- **Thời gian chạy toàn bộ:** `~39s`
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
| 11 | Tổng số kiểm thử tự động toàn hệ thống | **363/363 ĐẠT** | **100% tests PASS**, thời gian chạy ~24s, 0 warnings, 0 errors |

---

## 7. Bộ Nâng Cấp Toàn Diện Mới (7 Cải Tiến Nghiệp Vụ & Độ Tin Cậy)

| STT | Cải Tiến Triển Khai | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Khắc phục triệt để luồng WPF trong Bộ Test** | **ĐẠT** | Tách riêng luồng kiểm thử converter bằng `InvokeSTA` + `Dispatcher.CurrentDispatcher`, 100% pass kể cả chạy song song xUnit. |
| 2 | **Tự động sao chép ảnh Bill vào Clipboard khi xuất** | **ĐẠT** | Đưa song song `BitmapSource` (dán ảnh trực tiếp vào Zalo/Messenger/Word) và `FileDropList` (Ctrl+V vào File Explorer/Email đính kèm). |
| 3 | **Sao lưu dự phòng thứ hai (Secondary Cloud / NAS Backup)** | **ĐẠT** | Tự động mirror bản backup SQLite sang ổ mạng LAN/NAS hoặc Google Drive/OneDrive cục bộ, có fallback nếu ngắt kết nối. |
| 4 | **Tích hợp VietQR động trực tiếp lên Hóa Đơn Ảnh** | **ĐẠT** | Render vector Napas247 offline hoàn toàn bằng `QRCoder` chuẩn EMVCo Tag-Length-Value, nhúng mã bill và số tiền chính xác, không dùng API mạng. |
| 5 | **Theo dõi trạng thái thanh toán & Quản lý công nợ** | **ĐẠT** | Migration 14: Thêm `is_paid`, `paid_at`. Hỗ trợ đổi trạng thái thanh toán, thống kê tổng nợ theo khách hàng/studio, lọc hóa đơn chưa trả. |
| 6 | **Tìm kiếm toàn cục toàn bộ lịch sử (Ctrl + F)** | **ĐẠT** | Spotlight Modal `GlobalSearchWindow`: Tra cứu tức thì đa tiêu chí (mã đơn, mã bill, tên khách, số điện thoại, thư mục) không lag, mở trực tiếp bill. |
| 7 | **In Tem Dán / Phiếu Giao Hàng Bưu Kiện (Shipping Label 75x100mm)** | **ĐẠT** | Migration 15: Thêm `address` khách hàng và snapshot `shipping_address_snapshot`. Layout tem nhiệt tương phản cao 75x100mm, hiển thị rõ Người gửi/Người nhận, tóm tắt danh sách hàng, hộp cảnh báo COD (tránh shipper thu nhầm khi đã chuyển khoản), tích hợp mã QR tra cứu hoặc VietQR thanh toán COD, hỗ trợ gửi lệnh in máy in nhiệt trực tiếp và xuất file ảnh PNG/JPEG. |

---

## 8. Bộ 3 Tính Năng Chuyên Sâu Mới (Tiered Pricing, Split Bills, Batch Export)

| STT | Tính Năng Triển Khai | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Bảng Giá Theo Cấp Độ Khách Hàng (Tiered Pricing)** | **ĐẠT** | Hỗ trợ 3 cấp độ: `Retail` (Khách lẻ / Chuẩn), `Studio` (Khách quen / Studio), `Vip` (Thợ ruột / Đại lý). Cấu hình riêng cho cả Ảnh In (đơn giá theo file) và Album (giá mở bìa + giá tờ phát sinh). Tự động fallback về giá Khách Lẻ nếu chưa cấu hình giá ưu đãi. Migration 16 an toàn 100%. |
| 2 | **Tách Hóa Đơn Linh Hoạt Cho Cùng Khách Hàng (Split Bills per Customer)** | **ĐẠT** | Nút "✂️ Tách Đơn" trên từng card đơn hàng trong cửa sổ xem trước hóa đơn (`CustomerBillReviewWindow`). Hỗ trợ tách 1 hoặc nhiều đơn vật lý của cùng một thợ/khách sang hóa đơn độc lập mới với mã bill riêng biệt, tự động tính lại tổng tiền, bảo vệ tuyệt đối hóa đơn đã khóa (`Locked/Exported`). |
| 3 | **Xuất Hóa Đơn & In Tem Hàng Loạt (Batch Export & Batch Print)** | **ĐẠT** | Cửa sổ `BatchExportWindow` mở từ Dashboard ("⚡ Xuất & In Hàng Loạt"). Tự động phát hiện toàn bộ khách hàng có đơn chưa chốt trong ngày. Tùy chọn 1-click: Khóa bill tự động, Xuất ảnh JPEG vector, Xuất Excel, và In tem nhiệt giao hàng hàng loạt không qua từng popup. Có thanh tiến trình real-time và bảng nhật ký chi tiết từng hóa đơn. |
| 4 | **Tổng số kiểm thử tự động toàn hệ thống** | **394/394 ĐẠT** | **100% tests PASS**, bao gồm `TieredPricingTests.cs` (5/5), `SplitBillTests.cs` (5/5), `BatchBillingTests.cs` (4/4), `InvoicesViewModelTests.cs` (4/4). |

---

## 9. Tách Riêng Tab Hóa Đơn & Thanh Toán Độc Lập (Dedicated Invoices & Payment Hub)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Tách Tab HÓA ĐƠN riêng trên Sidebar chính** | **ĐẠT** | Vị trí thứ 2 tự nhiên: `ĐƠN HÀNG` ➔ `🧾 HÓA ĐƠN` ➔ `KHÁCH HÀNG` ➔ `BẢNG GIÁ` ➔ `BÁO CÁO` ➔ `CÀI ĐẶT`. |
| 2 | **Dashboard Tài Chính & KPI Cards trực quan** | **ĐẠT** | 4 thẻ thống kê: Tổng hóa đơn, Tổng doanh thu, Đã thanh toán (xanh), Còn nợ chưa thu (cam/hổ phách) tự động tính toán. |
| 3 | **Bộ lọc đa chiều toàn diện** | **ĐẠT** | Lọc theo thời gian (Toàn bộ / Tháng này / Hôm nay / Tùy chọn ngày), theo trạng thái thanh toán (Tất cả / Nợ / Đã trả), theo loại bill (Quen / Lẻ), tìm kiếm tức thì. |
| 4 | **Đổi trạng thái thanh toán 1-click** | **ĐẠT** | Nhấn trực tiếp trên DataGrid để đổi `⏳ Chưa trả` ↔ `✓ Đã trả`, tự động cập nhật SQLite và chỉ số KPI còn nợ. |
| 5 | **Phím tắt thao tác nhanh đầy đủ** | **ĐẠT** | `👁️ Mở Bill`, `🖼️ Ảnh`, `🏷️ Tem` (in tem bưu kiện nhiệt), `🔄 Xuất Lại`, `📁 Thư Mục`. |
| 6 | **Tích hợp Quick Bill & Xuất hàng loạt** | **ĐẠT** | Nút `⚡ Quick Bill` và `📦 Xuất & In Loạt` ngay trên đỉnh trang hóa đơn. |
| 7 | **Dọn dẹp làm gọn Báo Cáo** | **ĐẠT** | Gỡ bỏ Tab 4 dư thừa trong `ReportsView.xaml`, giúp tab Báo Cáo thuần túy cho báo cáo và phân tích số liệu. |
| 8 | **Tổng số kiểm thử tự động toàn hệ thống** | **394/394 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors. |

---

## 10. Ứng Dụng Web Di Động & Truy Cập Từ Xa (Mobile Web App & Remote Access)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Nhúng Web Server Kestrel siêu nhẹ trực tiếp trong Desktop App** | **ĐẠT** | Tích hợp ASP.NET Core Kestrel vào ứng dụng WPF (`Microsoft.AspNetCore.App`), lắng nghe mặc định cổng `5050` trên `0.0.0.0`, không tiêu tốn RAM khi nhàn rỗi (<20MB), tự động khởi động cùng ứng dụng desktop. |
| 2 | **Cơ chế xác thực an toàn bằng PIN & Token HMAC-SHA256 (30 ngày)** | **ĐẠT** | Cấu hình mã PIN độc lập cho Admin (mặc định: `123456`) và Nhân viên (mặc định: `000000`). Token ký số HMAC-SHA256 thời hạn 30 ngày, tự động gắn vào URL của mã QR giúp quét vào xem ngay mà không cần gõ mật khẩu lại nhiều lần. |
| 3 | **Phân quyền truy cập 2 cấp độ chặt chẽ (Admin vs Staff)** | **ĐẠT** | - **Chủ tiệm (Admin):** Toàn quyền xem mọi đơn hàng, danh sách nợ khách hàng, báo cáo ngày, báo cáo tháng và tổng doanh thu.<br>- **Nhân viên (Staff):** Xem danh sách đơn hàng, quy cách in ấn, xem hóa đơn và tem giao hàng để đóng gói bưu kiện; ẩn hoàn toàn các số liệu doanh thu, công nợ và báo cáo tài chính (API trả về HTTP 403 Forbidden). |
| 4 | **Truy cập từ xa không cần mở port modem qua Cloudflare Tunnel** | **ĐẠT** | Tự động tải và điều khiển tiến trình `cloudflared.exe` tạo đường hầm an toàn Quick Tunnel (`trycloudflare.com`) hoặc qua Custom Token của xưởng, cho phép xem điện thoại mọi lúc mọi nơi kể cả khi dùng 4G/5G hoặc mạng ngoài xưởng. |
| 5 | **Giao diện di động hiện đại Mobile PWA Single-Page App** | **ĐẠT** | Giao diện tối ưu hóa cho màn hình cảm ứng điện thoại: tìm kiếm không dấu tiếng Việt, lọc nhanh "Tất cả / Đã in / Chờ in", xem chi tiết đơn, xem ảnh JPEG hóa đơn độ nét cao, xem tem bưu kiện nhiệt 75x100mm, tạo mã VietQR thanh toán tức thì. Hỗ trợ cài đặt "Thêm vào Màn hình chính" như một app native. |
| 6 | **Tạo mã QR truy cập trực tiếp trên Desktop App (Admin / Nhân viên)** | **ĐẠT** | Card "Truy Cập Trên Điện Thoại" trong mục Cài đặt trên desktop: hiển thị trạng thái server, IP mạng LAN, nút chuyển đổi URL LAN/Cloudflare, và render sẵn 2 mã QR kèm token xác thực riêng biệt cho Admin và Nhân viên. |
| 7 | **Tổng số kiểm thử tự động toàn hệ thống** | **409/409 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors, bao gồm 15 bài test cho Web di động, Tab Hóa Đơn và điều hướng Khách Hàng. |

---

## 11. Bộ Lọc Hóa Đơn Theo Khách Hàng & Tối Ưu Quản Lý Khách Hàng

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Bộ lọc Khách hàng thông minh trên tab Hóa Đơn** | **ĐẠT** | ComboBox tìm kiếm và lọc hóa đơn: "Tất cả khách hàng", "⚡ Tất cả khách lẻ", và danh sách toàn bộ khách hàng sắp xếp A-Z. Hỗ trợ nút `[✕]` xóa nhanh bộ lọc. |
| 2 | **1-Click Quick Filter trên DataGrid hóa đơn** | **ĐẠT** | Bấm trực tiếp vào tên khách hàng trên bất kỳ dòng hóa đơn nào trong bảng để lọc ngay lập tức hóa đơn của khách đó. Có hiệu ứng hover gạch chân và trỏ chuột trực quan. |
| 3 | **Động hóa 4 chỉ số KPI tài chính theo khách hàng** | **ĐẠT** | Khi lọc theo khách hàng, 4 thẻ KPI (`Tổng hóa đơn`, `Doanh thu`, `Đã thanh toán`, `Còn nợ`) tự động co về đúng số liệu và công nợ của riêng khách hàng đó. |
| 4 | **Mini Card Tổng Quan Giao Dịch trên tab Khách Hàng** | **ĐẠT** | Thay thế danh sách hóa đơn dài dòng bằng Mini Card tinh gọn hiển thị: Số hóa đơn đã xuất, Doanh thu tích lũy, Công nợ hiện tại và thông tin bill gần nhất. |
| 5 | **Điều hướng 1-chạm (1-Touch Navigation)** | **ĐẠT** | Nút `[ 🧾 Xem toàn bộ hóa đơn của khách này ➔ ]` trên tab Khách Hàng tự động chuyển sang tab Hóa Đơn và kích hoạt bộ lọc khách hàng tương ứng. |
| 6 | **Tối đa hóa không gian quản lý Alias thư mục** | **ĐẠT** | Bỏ cơ chế TabControl chuyển qua lại; khu vực dưới của tab Khách Hàng được dành 100% chiều cao cho việc quản lý danh sách alias thư mục trên ổ cứng. |
| 7 | **Tổng số kiểm thử tự động toàn hệ thống** | **412/412 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors, bảo toàn toàn vẹn cơ sở dữ liệu và kiến trúc MVVM. |

---

## 12. Khắc Phục Lỗi XAML StaticResource & Kiểm Thử Toàn Vẹn Giao Diện

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Chuẩn hóa Resource Key TextBox trên SettingsView.xaml** | **ĐẠT** | Đổi toàn bộ các tham chiếu `Tinix.TextBox` tại các dòng 487, 493, 499 về đúng chuẩn `Tinix.TextBox.Standard`, loại bỏ hoàn toàn ngoại lệ `XamlParseException` khi mở tab Cài đặt. |
| 2 | **Cơ chế phòng vệ 2 lớp (Fallback Style Alias)** | **ĐẠT** | Khai báo Alias Style `<Style x:Key="Tinix.TextBox" BasedOn="{StaticResource Tinix.TextBox.Standard}" TargetType="TextBox"/>` ngay trong `TinixStyles.xaml`. Đảm bảo tương thích ngược và ngăn ngừa 100% lỗi tương tự trong tương lai. |
| 3 | **Bộ kiểm thử tự động kiểm tra toàn vẹn XAML (XamlResourceIntegrityTests)** | **ĐẠT** | Bổ sung 3 bài test tự động quét toàn bộ cây giao diện và kiểm tra chéo các khóa `StaticResource Tinix.*` với từ điển giao diện `TinixStyles.xaml` và `TinixTokens.xaml`. |
| 4 | **Tổng số kiểm thử tự động toàn hệ thống** | **418/418 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors. |

---

## 13. Khắc Phục Lỗi Nhận Diện IP Mạng LAN & Cấu Hình Tường Lửa Cho Mobile Web App

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Lọc bỏ triệt để card mạng ảo (WSL, Hyper-V, VMware, VirtualBox, TAP...)** | **ĐẠT** | Thuật toán trong `NetworkAddressHelper` tự động phân tích tên và mô tả adapter để loại trừ toàn bộ card mạng ảo nội bộ, không bao giờ nhầm IP `172.21.x.x` của WSL/Hyper-V. |
| 2 | **Ưu tiên card mạng có IPv4 Default Gateway thực của router tiệm** | **ĐẠT** | Chỉ card mạng vật lý kết nối vào modem/router Wi-Fi xưởng mới có Default Gateway ra mạng ngoài (`192.168.100.1`), đảm bảo chọn chính xác IP LAN vật lý (`192.168.100.24`). |
| 3 | **Cấu hình Windows Firewall mở thông cổng TCP 5050** | **ĐẠT** | Đã tạo rule inbound `Lalab Auto Report - Mobile Web Server` cho cổng TCP 5050 trên tất cả các mạng (Domain, Private, Public), loại bỏ hoàn toàn nguy cơ tường lửa chặn điện thoại trên mạng Wi-Fi. |
| 4 | **Bộ kiểm thử tự động `NetworkAddressHelperTests.cs` (6 tests mới)** | **ĐẠT** | Kiểm thử phân loại card mạng ảo, ưu tiên card mạng có Gateway, fallback an toàn và kiểm thử trực tiếp trên máy thật. Toàn bộ 6 tests PASS. |
| 5 | **Tổng số kiểm thử tự động toàn hệ thống** | **422/422 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors. |

---

## 14. Khắc Phục Lỗi Cú Pháp Script Mobile SPA & Kiểm Thử Toàn Vẹn JavaScript

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Khắc phục lỗi cú pháp đóng/mở ngoặc JavaScript trên trang di động** | **ĐẠT** | Loại bỏ hoàn toàn khối mã thừa `renderOrders` tại dòng 306-307 của `MobileSpaHtmlProvider.cs`, khôi phục cân bằng 100% các cặp ngoặc `{}` `()` `[]` (112/112 cặp), giúp trình duyệt điện thoại thực thi script trơn tru. |
| 2 | **Kích hoạt toàn diện các tương tác người dùng trên điện thoại** | **ĐẠT** | Sau khi sửa lỗi cú pháp, toàn bộ các hàm `checkAuthAndLoad()`, `switchTab()`, `loadOrders()`, `loadMonthlyReport()`, `loadDebts()` và các modal xem chi tiết hóa đơn, tem giao hàng, mã VietQR đều hoạt động ngay lập tức khi mở trang. |
| 3 | **Bộ kiểm thử tự động cú pháp script Mobile (`MobileSpaScriptSyntaxTests.cs` - 4 tests mới)** | **ĐẠT** | Kiểm thử cấu trúc HTML, kiểm thử cân bằng các cặp ngoặc, kiểm thử sự tồn tại của 16 hàm điều hướng cốt lõi, và kiểm thử cú pháp thực tế với Node.js runtime. Toàn bộ 4 bài test PASS 100%. |
| 4 | **Tổng số kiểm thử tự động toàn hệ thống** | **422/422 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors. |

---

## 15. Hiển Thị Ảnh Thumbnail Đơn Hàng & Tối Ưu Hóa Bộ Nhớ Đệm (Order Thumbnail Preview)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Tái cấu trúc ảo hóa giao diện (WPF UI Virtualization)** | **ĐẠT** | Chuyển đổi danh sách đơn hàng `DashboardView.xaml` từ `ItemsControl` (trong `ScrollViewer` không ảo hóa) sang `ListBox` ảo hóa (`VirtualizingPanel.IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`). Khi có hàng trăm đơn hàng, chỉ 8–10 đơn trong khung nhìn được khởi tạo, giảm 80% RAM và giữ mượt mà 60 FPS khi cuộn. |
| 2 | **Dịch vụ tạo thumbnail chuyên biệt (ThumbnailService)** | **ĐẠT** | Độc lập hoàn toàn với quá trình quét (scanner không giải mã pixel ảnh). Xử lý nền tuần tự với `SemaphoreSlim(1)` ở mức ưu tiên thấp nhất, chống nghẽn đĩa và phân mảnh đầu đọc trên ổ HDD / NAS LAN xưởng in. |
| 3 | **Cơ chế Pause/Resume ưu tiên quét số lượng** | **ĐẠT** | Tự động tạm dừng hàng đợi tạo thumbnail khi đang quét ngày (`ScanDate`), quét lại đơn (`RescanOrder`) hoặc khóa đơn (`LockOrder`). Hủy bỏ các yêu cầu ảnh dở dang khi đổi ngày hoặc áp bộ lọc. |
| 4 | **Bộ giải mã WIC & Bộ nhớ đệm 2 tầng (2-Tier Cache)** | **ĐẠT** | Giải mã thu nhỏ kích thước tối đa 180px JPEG (~15KB) qua Windows Imaging Component (WIC), lưu vào đĩa tại `%LocalAppData%\LalabAutoReport\Thumbnails\` theo mã băm MD5 và cache 100 ảnh gần nhất trên RAM. |
| 5 | **Bộ lọc định dạng nặng an toàn (.PSD, .RAW, .TIF)** | **ĐẠT** | Tuyệt đối không nạp hay giải mã file PSD hay RAW nặng hàng trăm MB; hiển thị icon huy hiệu định dạng (`PSD`, `RAW`, `TIF`), tránh tràn bộ nhớ và nghẽn CPU. |
| 6 | **Sắp xếp tự nhiên (Natural String Comparer)** | **ĐẠT** | `NaturalStringComparer` tự động chọn ảnh đại diện chuẩn xác theo thứ tự số (`img_1` trước `img_2`, `img_2` trước `img_10`, `DSC_0001` trước `DSC_0002`). |
| 7 | **Tương tác xem ảnh gốc 1-chạm** | **ĐẠT** | Nhấp chuột vào ảnh thumbnail để mở file ảnh gốc trực tiếp bằng trình xem ảnh mặc định của Windows (`UseShellExecute = true`). |
| 8 | **Quản lý cấu hình & Dọn dẹp cache tại Cài Đặt** | **ĐẠT** | Cho phép bật/tắt hiển thị thumbnail, hiển thị dung lượng đệm thực tế (KB/MB), nút dọn dẹp cache thủ công và tác vụ chạy ngầm tự động xóa cache thumbnail không dùng quá 30 ngày khi mở ứng dụng. |
| 9 | **Bộ kiểm thử tự động `ThumbnailServiceTests.cs` (16 tests)** | **ĐẠT** | Kiểm thử thuật toán NaturalStringComparer, lọc định dạng không hỗ trợ, nạp và giải mã ảnh JPEG, bộ nhớ đệm RAM & Disk, cơ chế tạm dừng / tiếp tục, xóa cache và dọn dẹp cache hết hạn. |
| 10 | **Tổng số kiểm thử tự động giai đoạn Thumbnail V1** | **438/438 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors. |

---

## 16. Popup Xem Ảnh Đại Diện Ngay Trên Ứng Dụng (In-App Lightbox Preview) & Tích Hợp Thumbnail Trên Web Di Động (Mobile Web App)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Nâng cấp độ phân giải Thumbnail lên chuẩn HD (320px)** | **ĐẠT** | Nâng kích thước tối đa cạnh ảnh từ `180px` lên `320px` (JPEG Quality 80, ~25KB/ảnh). Giúp ảnh đại diện hiển thị cực kỳ sắc nét trên cả màn hình máy tính và màn hình điện thoại Retina/OLED độ phân giải cao mà vẫn tối ưu dung lượng đĩa và RAM. |
| 2 | **Lightbox Preview Popup ngay trong ứng dụng Desktop** | **ĐẠT** | Bấm vào ảnh thumbnail trên danh sách đơn hàng sẽ mở ngay Modal Lightbox bán trong suốt nổi ngay trên màn hình ứng dụng: hiển thị ảnh ~360x360px tức thì từ bộ nhớ đệm (0ms delay, không giải mã lại file gốc 50MP). |
| 3 | **Hỗ trợ thao tác tiện ích trong Lightbox** | **ĐẠT** | Trong popup hiển thị Tên khách, Mã đơn hàng, Tên file ảnh; cung cấp nút `[📁 Mở Thư Mục]` (chọn file hoặc fallback mở folder đơn nếu file bị dời), nút `[🖼️ Mở Ảnh Gốc]` (bật Windows Photo Viewer khi cần soi chi tiết), tự động nhận focus bàn phím để phím `Esc` đóng tức thì, cùng nút `[✕]` và click ra ngoài backdrop. |
| 4 | **API truyền phát ảnh Thumbnail cho Web di động (Kestrel Endpoint)** | **ĐẠT** | Bổ sung endpoint `GET /api/orders/{id}/thumbnail` trên Kestrel: xác thực linh hoạt cả `token` lẫn `auth`, cơ chế tự phục hồi (auto-healing) tự động dò tìm ảnh trong thư mục cho các đơn hàng cũ, stream file JPEG từ cache đĩa (`Results.File`), kèm header `Cache-Control: public, max-age=86400` giúp điện thoại lưu cache 24h. |
| 5 | **Tích hợp hiển thị ảnh đại diện trên giao diện di động (Mobile Web SPA)** | **ĐẠT** | Mỗi thẻ đơn hàng trên điện thoại hiển thị ảnh thumbnail kích thước `48x48px` bo tròn góc, tự động hiển thị icon placeholder `🖼️` nếu ảnh đang tải hoặc bị lỗi. Bấm vào thumbnail mở modal preview sắc nét kèm tùy chọn `[🔍 Mở ảnh rời]` trong tab mới để hỗ trợ zoom đa điểm (pinch-to-zoom). Hỗ trợ chạm vùng nền mờ để đóng modal nhanh. |
| 6 | **Bộ kiểm thử tự động toàn hệ thống** | **441/441 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors, bao gồm kiểm thử bảo mật API `GetOrderThumbnail_Unauthorized_ShouldReturn401`, kiểm thử truyền phát ảnh có cache header với cả `?token=` và `?auth=`, kiểm thử tự phục hồi đường dẫn ảnh `GetOrderThumbnail_AutoHealing_WhenCandidatePathIsNull_ShouldResolveFromFolder` và kiểm tra toàn vẹn tài nguyên XAML `XamlResourceIntegrityTests`. |

---

## 17. Tính Năng Trạng Thái "Đã Giao" (Delivery Tracking) & "Ghi Chú Đơn Hàng" (Order Notes) Trên Desktop WPF và Mobile Web PWA

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Cơ sở dữ liệu & Migration 18** | **ĐẠT** | Bổ sung các cột `is_delivered`, `delivered_at`, `delivered_by`, `note` vào bảng `orders`. Tự động migrate an toàn khi mở ứng dụng, tương thích ngược 100% với dữ liệu cũ. |
| 2 | **Truy vết thời điểm và người bấm (Provenance Tracking)** | **ĐẠT** | Khi bấm "Đã giao", hệ thống lưu chính xác timestamp ISO 8601 (`delivered_at`) và định danh vai trò (`delivered_by`: "Chủ tiệm" với tài khoản Admin Desktop/Web, "Nhân viên" với tài khoản Mobile Staff). |
| 3 | **Cơ chế an toàn phòng ngừa giao nhầm (Safety Confirmation - Phương án A)** | **ĐẠT** | Nếu đơn hàng chưa được đánh dấu "Đã in", thao tác bấm "Giao hàng" sẽ hiển thị hộp thoại xác nhận: *"Đơn này chưa đánh dấu Đã in, bạn có chắc chắn đã đóng gói giao rồi không?"*. Giúp tránh tuyệt đối việc bấm nhầm khi đơn còn đang xử lý. |
| 4 | **Khả năng hoàn tác linh hoạt (Undo Capability)** | **ĐẠT** | Khi bấm vào đơn đã giao, hệ thống hiển thị hộp thoại xác nhận hoàn tác: *"Đơn hàng này đang ở trạng thái ĐÃ GIAO. Bạn có muốn chuyển lại thành CHƯA GIAO không?"*. Xóa `delivered_at` và `delivered_by` an toàn khi xác nhận. |
| 5 | **Ghi chú đơn hàng (Order Notes)** | **ĐẠT** | Hỗ trợ thêm/sửa/xóa ghi chú cho từng đơn hàng (VD: "thiếu 1 tấm", "giao gấp", "khách hẹn 5h chiều"...). Ghi chú hiển thị nổi bật dạng banner màu vàng hổ phách (amber) trên cả thẻ Desktop và Mobile. |
| 6 | **Tuân thủ quy định tem giao hàng (Shipping Label Preservation)** | **ĐẠT** | Theo đúng yêu cầu người dùng: Ghi chú chỉ phục vụ nội bộ tại xưởng và hiển thị trên màn hình ứng dụng, tuyệt đối **không in vào tem 75x100mm** để bảo toàn sự chuẩn mực và bố cục của nhãn in. |
| 7 | **Bộ lọc thông minh & Thẻ thống kê** | **ĐẠT** | Trên Desktop: Bổ sung 2 bộ lọc nhanh `Chưa giao (📦)` và `Đã giao (🚚)`. Trên Mobile: Bổ sung 5 Tab thống kê nhanh (`Tất cả`, `Chờ in`, `Đã in`, `Chưa giao`, `Đã giao`). Thanh tìm kiếm hỗ trợ lọc theo cả nội dung văn bản trong ghi chú. |
| 8 | **Kestrel API Endpoints cho Web Di Động** | **ĐẠT** | Bổ sung `POST /api/orders/{id}/toggle-delivered`, `POST /api/orders/{id}/toggle-printed`, và `POST /api/orders/{id}/note`. Tự động phân quyền và gán `deliveredBy` theo session đăng nhập. |
| 9 | **Bộ kiểm thử tự động toàn hệ thống** | **449/449 ĐẠT** | **100% tests PASS**, 0 warnings, 0 errors, bao gồm 4 tests mới trong `OrderDeliveryAndNoteTests.cs` (persistence, cập nhật trạng thái, mapping view model) và các tests API trong `MobileWebServerTests.cs`. |

---

## 18. Nâng Cấp Trải Nghiệm Mobile Web App: Điều Hướng Ngày Nhanh, Cử Chỉ Vuốt (Swipe Gesture) & Cố Định Cụm Bộ Lọc (Sticky Filter Bar)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Nút mũi tên điều hướng ngày (◀ / ▶) trên di động** | **ĐẠT** | Bổ sung nút ◀ (Ngày trước) và ▶ (Ngày kế) kẹp hai bên ô chọn ngày với kích thước nút chuẩn chạm (`w-9 h-9`), phản hồi chạm mượt mà, căn giữa ngày tháng cân đối. |
| 2 | **Cử chỉ vuốt màn hình (Touch Swipe Gesture)** | **ĐẠT** | Hỗ trợ vuốt ngón tay qua trái (Swipe Left 👈) để sang Ngày kế tiếp (+1) và vuốt qua phải (Swipe Right 👉) để về Ngày trước (-1). Tích hợp bộ lọc chống chạm nhầm thông minh: chỉ kích hoạt khi thao tác ngang dứt khoát (`|deltaX| >= 50px`, `|deltaX| > 1.8 * |deltaY|`, thời gian `< 500ms`), bỏ qua hoàn toàn khi đang cuộn dọc xem đơn hoặc chạm vào nút/ô nhập liệu. |
| 3 | **Cố định cụm điều khiển & bộ lọc (Sticky Top Controls)** | **ĐẠT** | Toàn bộ cụm điều khiển bao gồm: Thanh chọn ngày, Ô tìm kiếm, và 5 Tab trạng thái (`Tất cả`, `Chờ in`, `Đã in`, `Chưa giao`, `Đã giao`) được ghim cố định ở đỉnh màn hình (`sticky top-0 z-20`). Sử dụng nền `bg-slate-900/95` kính mờ `backdrop-blur-md` kèm viền `border-b border-slate-800` và đổ bóng, giúp danh sách đơn hàng cuộn bên dưới trượt mượt mà mà người dùng không bao giờ mất dấu các bộ lọc. |
| 4 | **Thẻ thông báo ngày nổi trực quan (Floating Date Toast)** | **ĐẠT** | Khi đổi ngày qua nút bấm, ô chọn ngày hoặc cử chỉ vuốt, thẻ thông báo bo tròn mềm mại `#dateToast` hiển thị nhanh thứ trong tuần, ngày tháng năm và nhãn tương đối (`Hôm nay`, `Hôm qua`, `Ngày mai`) trong 1.2s. |
| 5 | **Bộ kiểm thử tự động toàn diện** | **ĐẠT** | Cập nhật bộ test `MobileSpaScriptSyntaxTests.cs` và `MobileWebServerTests.cs`, kiểm tra toàn vẹn thẻ DOM, cân bằng dấu ngoặc và cú pháp JavaScript qua Node.js (100% PASS, 0 warnings, 0 errors). |
 
 ---
 
+## 19. Tính Năng In Tem Đơn Hàng / Bưu Kiện 75x100mm Từ Điện Thoại & Máy Tính (Thermal Shipping Label Printing & Auto Delivery)
+
+| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
+|---|---|---|---|
+| 1 | **Chuẩn tem nhiệt 75x100mm (Thermal Label)** | **ĐẠT** | Visual renderer được thiết kế chuẩn xác theo kích thước tem cuộn phổ biến 75x100mm (283x378 pt tại 96 DPI, tự động scale theo PrintableArea của PrintQueue driver). Tối ưu tương phản đen trắng cho đầu in nhiệt (thermal direct). |
+| 2 | **Hỗ trợ cả 2 loại đơn: Đã lên hóa đơn & Chưa lên hóa đơn** | **ĐẠT** | - Nếu đơn đã có hóa đơn: Tem in chi tiết theo hóa đơn khách hàng (Mã bill, người nhận, quy cách & đơn giá/tổng tiền, mã VietQR thanh toán tự động).<br>- Nếu đơn chưa lên hóa đơn: Tem in theo thông tin đơn hàng gốc (Mã đơn, tên khách/thư mục, liệt kê các phân loại sản phẩm trong đơn, tổng số lượng in, ghi chú đóng gói và mã QR tra cứu nội bộ). |
+| 3 | **Cơ chế in ngầm không hiển thị hộp thoại (Silent Direct Printing)** | **ĐẠT** | Khi nhân viên bấm "In tem" từ điện thoại di động (Mobile Web) hoặc từ PC, hệ thống đẩy lệnh in trực tiếp (`silent: true`) vào hàng đợi của máy in nhiệt đã cấu hình thông qua `LocalPrintServer` và `PrintQueue`, tuyệt đối không hiện hộp thoại Windows PrintDialog gây chặn hoặc treo máy chủ. |
+| 4 | **Tự động đánh dấu ĐÃ GIAO khi in tem (Auto-mark Delivered)** | **ĐẠT** | Tích hợp tùy chọn cấu hình `AutoMarkDeliveredOnPrint` (mặc định BẬT): Khi lệnh in tem gửi thành công, đơn hàng tự động chuyển sang trạng thái `IsDelivered = true`, cập nhật `DeliveredAt` và ghi nhận người thực hiện (`delivered_by`: "Chủ tiệm (Mobile)", "Nhân viên (Mobile)" hoặc "In tem PC"). |
+| 5 | **Vị trí nút bấm UI chuẩn xác theo yêu cầu** | **ĐẠT** | - Trên giao diện PC (`DashboardView.xaml`): Nút `[ 🏷️ In tem ]` được đặt ngay bên trái nút `[ 🚚 Chưa giao / Đã giao ]`.<br>- Trên giao diện Mobile Web (`MobileSpaHtmlProvider.cs`): Nút `[ 🏷️ In tem ]` được đặt ngay bên trái nút `[ 🚚 Chưa giao / Đã giao ]` với hiệu ứng rung phản hồi haptic, loading spinner và thông báo toast tức thì. |
+| 6 | **Cấu hình máy in nhiệt trong Cài đặt (Settings View)** | **ĐẠT** | Cung cấp thẻ cấu hình "Máy In Nhiệt Tem Bưu Kiện / Giao Hàng (Khổ Chuẩn 75x100mm)" trong Cài đặt: Tự động liệt kê danh sách máy in Windows, cho phép chọn máy in nhiệt mặc định, nút "In Thử Tem Mẫu 75x100mm" và checkbox bật/tắt tự động đánh dấu đã giao. |
+| 7 | **API Endpoint cho Web Di Động (Kestrel Mobile Web Server)** | **ĐẠT** | Bổ sung: `POST /api/orders/{id}/print-label` (In tem đơn hàng), `GET /api/orders/{id}/label` (Xem trước ảnh PNG tem đơn), `POST /api/bills/{id}/print` (In tem hóa đơn). |
+| 8 | **Bộ kiểm thử tự động toàn diện** | **453/453 ĐẠT** | Bổ sung các ca test tự động mới trong `ShippingLabelTests.cs`, `MobileWebServerTests.cs`, và `OrderDeliveryAndNoteTests.cs` kiểm tra toàn bộ luồng in ngầm, sinh ảnh tem đơn, lưu cấu hình máy in và tự động đánh dấu giao hàng (100% PASS). |

---

## 20. Tính Năng Chạy Ngầm Khay Hệ Thống (System Tray) & Tự Khởi Động Phục Vụ Điện Thoại 24/7

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Tích hợp Native Windows Forms Component không xung đột** | **ĐẠT** | Bật `<UseWindowsForms>true</UseWindowsForms>` kèm khử namespace collision (`<Using Remove="System.Windows.Forms"/>`, `<Using Remove="System.Drawing"/>`), sử dụng `System.Windows.Forms.NotifyIcon` chính chủ của Microsoft .NET 8 (0% thư viện ngoài). |
| 2 | **Dịch vụ quản lý khay hệ thống (`SystemTrayManager`)** | **ĐẠT** | Hiển thị biểu tượng `app.ico` dưới khay Taskbar gần đồng hồ Windows, quản lý menu chuột phải: 👁️ Mở giao diện chính (in đậm), 🌐 Mở Web Quản Lý (trình duyệt), ℹ️ Trạng thái Web Server & Cloudflare Tunnel, và ❌ Thoát hoàn toàn. |
| 3 | **Cơ chế thu nhỏ khi bấm đóng [X] (Minimize to Tray on Close)** | **ĐẠT** | Can thiệp sự kiện `MainWindow_Closing`: Khi bấm [X], ứng dụng ẩn cửa sổ (`Hide()`) và duy trì Web Server cùng Cloudflare Tunnel chạy ngầm liên tục; tự động hiển thị thông báo BalloonTip nhẹ nhàng lần đầu. |
| 4 | **Phục hồi cửa sổ 1-chạm & Đồng bộ IPC Named Pipe** | **ĐẠT** | Nhấp đúp hoặc nhấp chuột vào icon khay, hoặc mở shortcut ứng dụng ngoài Desktop (kích hoạt qua Named Pipe IPC `ACTIVATE|`) đều tự động khôi phục và đưa cửa sổ lên trên cùng mượt mà. |
| 5 | **Tự động khởi động cùng Windows ở chế độ chạy ngầm (`--minimized`)** | **ĐẠT** | Tự động đăng ký Windows Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LalabAutoReport` với tham số `--minimized`. Khi máy xưởng bật, app và server ngầm chạy ngay lập tức mà không bật cửa sổ làm phiền người dùng. |
| 6 | **Cấu hình trực quan trong tab Cài Đặt** | **ĐẠT** | Thêm khối "⚙️ Chạy Ngầm & Duy Trì Kết Nối 24/7" trong tab Cài Đặt với 2 tùy chọn checkbox: *Thu nhỏ xuống khay hệ thống khi đóng [X]* (mặc định Bật) và *Tự động khởi động cùng Windows ở chế độ chạy ngầm* (mặc định Bật). |
| 7 | **Bộ kiểm thử tự động toàn hệ thống** | **480/480 ĐẠT** | **100% tests PASS** (thời gian chạy ~39s, 0 warnings, 0 errors), bao gồm bộ test mới `SystemTrayAndBackgroundDaemonTests.cs` (4/4 tests). |

---

## 21. Tính Năng Cloudflare Read Replica & Mobile PWA (Xem Trên Điện Thoại 24/7 Dù PC Đã Tắt)

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Kiến trúc Cloud Read Replica (Local-First + Cloudflare)** | **ĐẠT** | Máy tính Desktop xưởng tiếp tục là nguồn sự thật duy nhất (Single Source of Truth) lưu trong SQLite. Cloudflare Worker + D1 Database đóng vai trò bản sao đọc (Read Replica), phục vụ truy cập 24/7 cho điện thoại di động kể cả khi máy tính xưởng tắt hoàn toàn. |
| 2 | **Cloudflare Worker API & Xác thực mã PIN** | **ĐẠT** | Worker viết bằng TypeScript, xử lý bảo mật bằng mã PIN (Admin: `123456`, Staff: `000000`), cung cấp API: `/api/auth/pin`, `/api/orders`, `/api/bills`, `/api/bills/{id}/jpeg`, `/api/reports/monthly`, `/api/stats/overview`, `/api/sync/batch`. |
| 3 | **Cloudflare D1 SQLite & R2 Object Storage** | **ĐẠT** | D1 lưu trữ các bảng `cloud_orders`, `cloud_bills`, `cloud_reports`, `cloud_sync_meta` với cấu trúc JSON projection linh hoạt. R2 lưu trữ ảnh hóa đơn JPEG độ phân giải cao phục vụ xem hóa đơn trên điện thoại. |
| 4 | **Mobile PWA Static Assets độc lập** | **ĐẠT** | Mobile SPA (HTML, CSS Tailwind, Service Worker, Manifest PWA) được đóng gói và phục vụ trực tiếp qua Cloudflare Worker Static Assets (`/public`), hỗ trợ cài đặt Add to Home Screen và truy cập tức thì. |
| 5 | **Dịch vụ đồng bộ Desktop C# (`CloudSyncService`)** | **ĐẠT** | Triển khai `ICloudSyncService` trong `LalabAutoReport.Infrastructure.Services`, tích hợp Migration 20 (`AddCloudSyncQueue`), cung cấp cơ chế đồng bộ HTTP một chiều lên Cloudflare kèm tải ảnh JPEG bill lên R2. Hoàn toàn chạy ngầm, không chặn UI, chịu lỗi mạng tốt (100% offline-tolerant). |
| 6 | **Tự động kích hoạt đồng bộ (Auto Sync Triggers)** | **ĐẠT** | Tự động đồng bộ ngầm khi: ứng dụng khởi động (sau 3s), khi quét thư mục ngày (`ScanDateAsync`), khi quét ngày thiếu (`ScanMissingDaysAsync`), khi quét lại đơn hàng (`RescanOrderAsync`), khi xuất/duyệt hóa đơn (`ExecuteExportBillAsync`), khi đổi trạng thái thanh toán (`TogglePaymentStatusAsync`), và khi xuất lại bill JPEG (`ReExportJpegAsync`). |
| 7 | **Giao diện cấu hình Cài Đặt (Desktop UI)** | **ĐẠT** | Bổ sung thẻ "🌐 Đồng Bộ Cloudflare (Xem Trên Điện Thoại 24/7)" trong Cài Đặt: Checkbox kích hoạt, ô nhập URL Worker API, ô nhập Sync Secret, nhãn hiển thị trạng thái và thời gian đồng bộ gần nhất, kèm nút bấm `[🔄 Đồng Bộ Toàn Bộ Ngay]`. |
| 8 | **Bộ kiểm thử tự động toàn diện** | **484/484 ĐẠT** | Toàn bộ 484 unit/integration tests trên Desktop (.NET 8) và 10/10 test Vitest trên Cloudflare Worker đều PASS 100% (0 warning, 0 error). |

---

## 22. Triển Khai Đồng Bộ 2 Chiều Cloud (Giao Hàng, Thanh Toán, Ghi Chú) & Dọn Sạch Cloudflare Tunnel

| STT | Nghiệp Vụ & Kỹ Thuật | Hiện Trạng | Ghi Chú Kỹ Thuật |
|---|---|---|---|
| 1 | **Dọn sạch hoàn toàn Cloudflare Tunnel** | **ĐẠT** | Xóa bỏ file `CloudflareTunnelService.cs`, gỡ bỏ `ICloudflareTunnelService`, `CloudflareTunnelStatus` khỏi Interfaces, loại bỏ các trường cấu hình Tunnel khỏi `Settings`, `SqliteSettingsRepository`, và giao diện WPF `SettingsView.xaml`. Hệ thống chỉ còn 2 kênh kết nối rõ ràng: Mạng nội bộ Wi-Fi xưởng (LAN Kestrel) và Cloudflare Cloud (Worker + D1 + R2). |
| 2 | **Cập nhật trạng thái 2 chiều trên Cloud (Worker & D1)** | **ĐẠT** | Tạo bảng `cloud_operational_events` trên D1 (`0002_operational_sync.sql`). Cung cấp các endpoint: `POST /api/orders/:id/toggle-delivered`, `POST /api/orders/:id/note`, `POST /api/bills/:id/toggle-payment`, và `GET /api/sync/pull` (bảo vệ bằng `X-Sync-Secret`). Ghi nhận event kèm timestamp chính xác. |
| 3 | **Cơ chế Kéo Sự Kiện về Desktop (`PullRemoteChangesAsync`)** | **ĐẠT** | Triển khai trong `CloudSyncService.cs` với nguyên lý *Pull-before-Push*. Tự động kéo các sự kiện đổi trạng thái giao hàng (`is_delivered`, `delivered_at`, `delivered_by`), thanh toán hóa đơn (`is_paid`, `paid_at`), và ghi chú đơn hàng (`note`) cập nhật trực tiếp vào cơ sở dữ liệu SQLite cục bộ theo chu kỳ 30 giây hoặc trước mỗi lần đồng bộ. |
| 4 | **Phân định thông minh môi trường Cloud vs LAN trên Mobile PWA** | **ĐẠT** | Thêm huy hiệu nhận diện rõ ràng trên Header: `☁️ Cloud` (khi truy cập qua tên miền internet) hoặc `📶 LAN` (khi truy cập mạng nội bộ xưởng). Cải thiện phản hồi người dùng với Toast và rung nhẹ (haptic feedback) khi thao tác thành công. |
| 5 | **Vô hiệu hóa nút In Tem trên Cloud theo yêu cầu** | **ĐẠT** | Nút `[🏷️ In tem]` trên giao diện Cloud được làm mờ (opacity-40, cursor-not-allowed) và hiển thị thông báo rõ ràng khi bấm: *"In tem chỉ hoạt động khi kết nối Wi-Fi xưởng (mạng nội bộ)"*. Lệnh in nhiệt máy in bưu kiện chỉ được phép kích hoạt từ mạng LAN nội bộ hoặc máy tính xưởng. |
| 6 | **Tích hợp xem hóa đơn & Thu tiền nhanh trên Mobile PWA** | **ĐẠT** | Thêm khối trạng thái thanh toán và nút bấm đổi trạng thái `[Xác nhận ĐÃ THU] / [Hủy đã thu]` ngay trong modal xem chi tiết hóa đơn `viewBill`. Thêm nút xem và nút `[Đã thu]` trực tiếp trên danh sách nợ `loadDebts` giúp chủ tiệm thu nợ trên điện thoại tức thì ở bất kỳ đâu. |
| 7 | **Bộ kiểm thử tự động toàn diện** | **485/485 ĐẠT** | Toàn bộ 485 unit/integration tests trên Desktop (.NET 8) và 10/10 test Vitest trên Cloudflare Worker đều PASS 100% (0 warning, 0 error). Build sạch sẽ, triển khai trực tiếp lên `https://lalab.tinix.io.vn`. |


