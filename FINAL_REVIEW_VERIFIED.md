# Final Production Release Acceptance Review — 2026-10-05

**Kết luận sơ bộ: BLOCKED / NOT READY cho phát hành production toàn tuyến.**
Toàn bộ các release gate cục bộ, cô lập và tự động hóa (local / isolated / automatable release gates) đã hoàn thành 100% với kết quả PASS.
Trạng thái các Production Gates thực tế:
- **Gate 1 (Thermal Printer Acceptance):** `NOT TESTED — USER DEFERRED` (Chưa có máy in; không ngăn tiếp tục Gate 2–5; có release limitation).
- **Gate 2 (Real LAN Mobile PWA):** `PASS` (Đã nghiệm thu trên iPhone thật: truy cập LAN, phân quyền Admin/Staff, đột biến note/delivery, Wi-Fi reconnect).
- **Gate 3 (Workshop Storage / Multi-Root):** `PASS for current deployment` (2 local roots phân tách order, scoped rescan, NAS vật lý không áp dụng cho topology hiện tại).
- **Gate 4 (Production Cloud Acceptance):** `PASS` (Worker V2 deployed version `5028af59-2fce-4952-9ea3-8c8b88abbc4f`, D1 backup tạo, 24/24 automated smoke PASS; manual production acceptance verified by ChatGPT Project Control: Real phone Cloud Admin login PASS, Admin Cloud bill detail PASS, Real phone Cloud Staff permission matrix PASS, Cloud -> Desktop note sync trên order test PASS, Desktop -> Cloud note sync trên cùng order test PASS).
- **Gate 5 (GitHub Release & Auto-Update):** `BLOCKED / PENDING` (Chưa thực hiện; Gate 4 đã hoàn tất, chờ quyết định cho Gate 5).

---

## 1. Trạng Thái 8 Finding Hardening Gốc (C1, C2, H1–H6)

| ID | Trạng Thái | Bằng Chứng Kiểm Thử Tự Động & Hành Vi Thực Tế |
|---|---|---|
| **C1** | **PASS / VERIFIED FIXED** | Guard theo trạng thái database cho Customer & Guest `Locked`/`Exported`. Rescan bảo toàn số lượng chốt, đặt cờ cảnh báo `FilesystemChangedAfterLock`. Sửa nội dung hoặc xóa bắt buộc phải mở lại (`ReopenBillAsync`) kèm lý do tường minh. Re-export tái xuất trực tiếp từ snapshot lịch sử, không quét lại filesystem. Test: `CustomerBillingTests.ProductionHistory_CannotRewriteOrDeleteWithoutReopen`, `CustomerBillingTests.Scenario6_RescanPreservesLockedQuantityAndWarns` (2/2 PASS). |
| **C2** | **PASS / VERIFIED FIXED** | Định danh đơn hàng phân biệt tuyệt đối qua cặp `(RootFolderId, RelativePath)`. Thư mục cùng đường dẫn tương đối trên hai kho độc lập A và B được quét thành 2 đơn phân biệt (`a.Id != b.Id`), số lượng ảnh độc lập (1 vs 2). Thao tác in ấn/cập nhật chỉ tác động đúng kho được chỉ định. Test: `MultiRootFolderTests.ProductionIdentity_SameRelativePathInTwoRootsStaysSeparateAcrossRescans` (1/1 PASS). |
| **H1** | **PASS / VERIFIED FIXED** | Lỗi phân quyền hoặc I/O (Access Denied) tại bất kỳ nhánh con nào của cây thư mục lập tức chuyển trạng thái đơn hàng sang `OrderStatus.NeedsReview`, trả về `PrintFolderResolutionStatus.NoPrintFolder`, `PrintCount = null`, và ghi rõ `ErrorMessage`. Quét ngày gặp lỗi không tự ý prune/xóa các bản ghi đã lưu. Test: `PrintFolderResolverTests.ProductionScan_UnreadableDeepestBranchCannotResolveParent` (2/2 PASS). |
| **H2** | **PASS / VERIFIED FIXED** | Giao thức đồng bộ V2 dựa trên monotonic event ID cursor, phân trang 500 sự kiện/trang không sót hay nhảy cóc khi có 501+ sự kiện cùng timestamp. Việc áp dụng sự kiện, ghi sổ `cloud_applied_events`, và nâng checkpoint diễn ra nguyên tử trong một transaction SQLite. Gặp lỗi ghi sẽ rollback toàn bộ và thử lại khi khởi động; không bao giờ nâng checkpoint trước khi commit an toàn. Test: `DurableCloudSyncTests.Pull501Events_CommitsEveryIdAndDoesNotOverwriteSettings`, `DurableCloudSyncTests.FailedWrite_RollsBackStateLedgerAndCursor_ThenRestartRetries`, Worker `versioned-sync.spec.ts` (26/26 PASS). |
| **H3** | **PASS / VERIFIED FIXED** | Loại bỏ toàn bộ fallback secret/PIN nguồn. Worker fail-closed trả về HTTP 503 khi thiếu cấu hình `ADMIN_PIN` hoặc `STAFF_PIN`, hoặc khi hai PIN trùng nhau. QR code trên Cloud loại trừ LAN token. Test: Worker `auth-hardening.spec.ts` (16/16 PASS). |
| **H4** | **PASS / VERIFIED FIXED** | Phân quyền vai trò Staff được thực thi độc lập ở tầng backend: mọi yêu cầu cập nhật thanh toán (`mark-paid`) từ Staff bị chặn với HTTP 403. Dữ liệu tài chính trong JSON bill detail bị ẩn (`null`), các nút thu tiền/xem báo cáo doanh thu bị ẩn trên giao diện PWA. Staff chỉ được phép thực hiện nghiệp vụ giao nhận, ghi chú và in tem đơn hàng. Test: Worker `permission-hardening.spec.ts` (7/7 PASS), Desktop `MobileWebServerTests.ProductionPermission_StaffCannotAccessFinancialMediaOrMutations` (7/7 PASS). |
| **H5** | **PASS / VERIFIED FIXED** | Tách hóa đơn (`SplitBillAtomicAsync`) được bọc hoàn toàn trong một transaction SQLite duy nhất: tạo bill mới, cập nhật bill gốc, và di chuyển liên kết order. Lỗi cưỡng bức ở bất kỳ bước nào sẽ rollback 100%, không để xảy ra tình trạng order thuộc hai bill hoặc mất liên kết. Test: `SplitBillTests.ProductionSplit_WriteFailureRollsBackNewBillAndMembership` (4/4 PASS). |
| **H6** | **PASS / VERIFIED FIXED** | Khế ước dữ liệu hóa đơn giữa Cloud và Desktop LAN hoàn toàn đồng nhất: DTO chuẩn `toMobileBill`, giữ nguyên tổng tiền chuẩn, các trường liên hệ/ngày không hỗ trợ trả về `null`, tài chính Staff trả về `null`. Modal PWA render an toàn (thoát mã HTML chống XSS, xử lý mượt mà khi thiếu ảnh JPEG/QR). Test: Worker `bill-contract.spec.ts` (6/6 PASS), Worker `pwa-render.spec.ts` (8/8 PASS), Desktop `MobileWebServerTests.ProductionContract_BillDetailMatchesPwaAndRedactsAmounts` (2/2 PASS). |

---

## 2. Giải Quyết Vấn Đề Bảo Mật Phase-5: Mã PIN Mặc Định Trên Desktop LAN

- **Phát hiện:** Trước Phase 10, `AppSettings` chứa giá trị mặc định nguồn `AdminPin = "123456"` và `StaffPin = "000000"`.
- **Đánh giá phạm vi:** Xác định là **PRODUCTION-REACHABLE (RELEASE BLOCKER)** trên các bản cài đặt mới hoặc khi người dùng chưa cấu hình PIN tùy chỉnh trong Cài đặt. Bất kỳ ai trong mạng LAN xưởng đều có thể đăng nhập Admin bằng mã `123456` để xem toàn bộ báo cáo doanh thu và sửa trạng thái thanh toán.
- **Biện pháp khắc phục đã thực hiện (Smallest Coherent Remediation):**
  1. Xóa bỏ hoàn toàn giá trị mặc định hardcoded trong `Models.cs` và `SettingsViewModel.cs` (khởi tạo mặc định `string.Empty`).
  2. Cấu hình cơ chế **Fail-Closed** tại endpoint `/api/auth/login` (`KestrelMobileWebServer.cs`): Nếu `AdminPin` chưa được thiết lập, trả về ngay HTTP 503 (`Chưa cấu hình mã PIN Admin trên ứng dụng Desktop. Vui lòng vào Cài đặt để thiết lập mã PIN`).
  3. Kiểm tra tính phân biệt: Nếu `StaffPin` được cấu hình trùng với `AdminPin`, trả về ngay HTTP 503 (`Mã PIN Admin và Nhân viên không được trùng nhau`).
  4. Nâng cấp `MobileAuthService.AuthenticateWithPin`: Từ chối xác thực (trả về `null`) nếu PIN rỗng hoặc hai PIN trùng nhau.
  5. Cập nhật `SettingsViewModel.SaveSettingsAsync`: Cảnh báo và chặn lưu nếu người dùng nhập trùng mã PIN Admin và Staff.
  6. Bổ sung các bài kiểm thử hồi quy: `MobileAuthServiceTests` (3 bài test mới) và `MobileWebServerTests` (2 bài test mới).
- **Kết quả:** Vấn đề bảo mật đã được giải quyết triệt để và được kiểm chứng tự động 100%.

---

## 3. Tổng Hợp Kết Quả Kiểm Thử Toàn Tuyến (Full Automated Validation)

### Desktop (.NET 8.0 / C#)
- **Full Debug Test Suite:** PASS (574/574 tests passed, 0 failed, 0 skipped, Duration: 1m 47s).
- **Full Release Test Suite:** PASS (574/574 tests passed, 0 failed, 0 skipped, Duration: 1m 53s).
- **Debug Build Solution:** PASS (0 Warnings, 0 Errors, Clean build).
- **Release Build Solution:** PASS (0 Warnings, 0 Errors, Clean build).

### Worker & PWA (TypeScript / Cloudflare Workers D1 / R2)
- **Worker Test Suite (`npm test`):** PASS (76/76 tests passed in 8 test files, Duration: 546ms).
- **Worker Typecheck (`npx tsc --noEmit`):** PASS (0 type errors).
- **Worker Dry-Run Bundle (`npx wrangler deploy --dry-run`):** PASS (Đọc 3 tệp public assets, xác thực bindings D1, R2, Assets).
- **Worker Audit (`npm audit`):** PASS (0 vulnerabilities).

### Các Suite Kiểm Thử Trọng Điểm (Targeted Suites)
- **Historical Bill Protection:** 69 passed.
- **Multi-Root Architecture:** 11 passed.
- **Incomplete / Error Scanning:** 17 passed.
- **Split Bill Atomicity:** 12 passed.
- **Auth Hardening:** Worker 16 passed, Desktop 8 passed.
- **Permission Hardening:** Worker 7 passed, Desktop 7 passed.
- **Bill Contract & PWA Rendering:** Worker 14 passed, Desktop 9 passed.
- **Versioned Sync Protocol V2:** Worker 26 passed.
- **Durable Desktop Sync & Outbox Recovery:** Desktop 33 passed.

---

## 4. Bản Đóng Gói Production Standalone & Khói Kiểm Thử (Publish & Standalone Smoke)

- **Lệnh đóng gói:**
  `dotnet publish src\LalabAutoReport.UI\LalabAutoReport.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\publish_release`
- **Target RID:** `win-x64`
- **Hình thức đóng gói:** Self-contained, Single-File Executable.
- **Thư mục đầu ra:** `artifacts\publish_release\`
- **File thực thi chính:** `LalabAutoReport.UI.exe` (Dung lượng: 203,000,236 bytes ~ 193.6 MB)
- **Mã băm SHA256:** `222F348B5A7ECA71FCA7ACF61D026E980FC52F18CF30996D19699516A1BB744C`
- **Thông tin phiên bản:**
  - `AssemblyVersion`: `1.0.0.0`
  - `FileVersion`: `1.0.0.0`
  - `ProductVersion`: `1.0.0+28551003c601a16e731827244b12d76c67095006`
  - Desktop `/api/status`: `1.0.0`
  - Worker `/api/health`: `1.0.0`
- **Kiểm thử khói trên môi trường độc lập (Standalone Smoke Test):**
  - Sao chép file EXE ra thư mục tạm thời độc lập, sử dụng profile riêng `--isolated-data-directory "<temp_dir>" --minimized`.
  - Khởi động thành công PID 92944; Kestrel Web Server khởi chạy trên cổng 5050; `/api/status` phản hồi HTTP 200 (`1.0.0`).
  - Toàn bộ 22 migrations SQLite khởi tạo trơn tru; log file ghi nhận khởi động sạch sẽ (không có unhandled exception).
  - Khởi chạy phiên bản thứ hai (Second Instance): Kích hoạt IPC qua Named Pipe `LalabAutoReport_IpcPipe_v1_<hash>`, phiên bản thứ hai nhường quyền và thoát với mã lỗi 0.
  - Kiểm thử cơ chế Fail-Closed: Gửi yêu cầu đăng nhập khi chưa cấu hình PIN nhận mã HTTP 503 như mong đợi.
  - Tắt ứng dụng sạch sẽ và khởi động lại (PID 71384); `/api/status` tiếp tục phản hồi HTTP 200; toàn vẹn dữ liệu được bảo toàn.
  - Hoàn toàn độc lập, không phụ thuộc vào thư mục `bin`/`obj` của mã nguồn.

---

## 5. Kiểm Thử Nâng Cấp, Toàn Vẹn & Khôi Phục Cơ Sở Dữ Liệu (Database Lifecycle)

Đã xây dựng và thực thi thành công bài test tự động `DatabaseUpgradeBackupRestoreFixtureTests` trên fixture database chứa dữ liệu thực tế phức tạp:
1. **Dữ liệu hạt giống:** Hóa đơn `Locked` (500.000 đ, chưa thanh toán), hóa đơn `Exported` (1.200.000 đ, đã thanh toán lúc 15:30), các đơn hàng đa kho (Kho 1 và Kho 2 có cùng đường dẫn tương đối), trạng thái vận hành (`is_delivered=1`, `delivered_by="StaffA"`, `note="Giao gấp buổi sáng"`), trạng thái đồng bộ Cloud (`stream_id="test-stream-fixture"`, `cursor=42`).
2. **Sao lưu (Backup):** `DatabaseBackupService.CreateBackupAsync` tạo bản sao lưu thành công.
3. **Tính lũy kế / Bất biến di chuyển (Idempotency):** Chạy lặp lại `MigrateAsync` 2 lần liên tiếp, schema và dữ liệu được bảo toàn 100%.
4. **Kiểm tra toàn vẹn:** `PRAGMA integrity_check` trả về `"ok"`.
5. **Kiểm tra khóa ngoại:** `PRAGMA foreign_key_check` trả về 0 vi phạm.
6. **Bất biến số liệu tài chính:** Các số liệu `product_subtotal`, `adjustments_total`, `grand_total` của cả 2 hóa đơn lịch sử giữ nguyên tuyệt đối.
7. **Bảo toàn trường vận hành:** Các trường `is_paid`, `paid_at`, `is_delivered`, `delivered_by`, `note`, liên kết đa kho `root_folder_id`, và `cloud_sync_state` được giữ nguyên vẹn.
8. **Bảo vệ vòng đời Cloud (`DatabaseLifecycleGuard`):** Thử nghiệm khôi phục khi database đang gắn kết Cloud bị từ chối với lỗi `InvalidOperationException` đúng chuẩn bảo mật (bảo vệ khỏi việc ghi đè vô ý).
9. **Khôi phục dữ liệu cục bộ (Restore):** Sau khi xác nhận môi trường cục bộ, lệnh khôi phục `RestoreBackupAsync` hoàn tất thành công.
10. **Kiểm chứng sau khôi phục:** Toàn bộ dữ liệu bị cố tình xóa (hóa đơn 201) được phục hồi nguyên vẹn; bản ghi đơn hàng giả định chèn thêm (order 999) biến mất; `PRAGMA integrity_check` đạt `"ok"`.

---

## 6. Xuất Bản File Đầu Ra Thực Tế (Export Output Acceptance)

- **Hóa đơn Excel (.xlsx):** `ClosedXmlBillExporter` xuất file Excel bảng kê chi tiết thành công; xác thực đầy đủ tiêu đề, mã bill, tên khách hàng, bảng dòng sản phẩm, quy cách, số lượng, phụ phí/chiết khấu và tổng tiền thanh toán.
- **Tem nhiệt bưu kiện / giao hàng (75x100mm PNG):** `WpfShippingLabelExporter` xuất ảnh tem nhiệt độ phân giải cao; hiển thị rõ ràng thông tin người nhận, địa chỉ, số điện thoại, quy cách ảnh, ghi chú và mã VietQR thanh toán (đối với đơn chưa thanh toán).
- **Ảnh Hóa Đơn JPEG (.jpg):** `WpfJpegBillExporter` kết xuất ảnh hóa đơn bán hàng chuẩn xác; re-export từ snapshot lịch sử mà không cần quét lại ổ đĩa.
- **Lưu ý kiểm tra bằng mắt:** Bố cục thẩm mỹ thực tế trên giấy in nhiệt (độ đậm nhạt mực in nhiệt, căn lề lề giấy thực tế của máy in Xprinter/HPRT) thuộc phạm vi nghiệm thu thủ công của chủ tiệm theo danh mục kiểm tra bên dưới.

---

## 7. Cơ Chế Tự Động Cập Nhật Trong Môi Trường Cô Lập (Updater Logic)

- **So sánh phiên bản:** Đã kiểm thử các định dạng tag `v1.0`, `v1.0.0`, `v1.1`, tiền tố `v`, và các chuỗi tag không hợp lệ.
- **Tải về và xác thực tệp:** `UpdateReplacement.PrepareAsync` kiểm tra cấu trúc Windows PE header (chữ ký `MZ` và `PE\0\0`), tính toán mã băm SHA256 của file tải về.
- **Thay thế theo giai đoạn & Phục hồi:** Kịch bản PowerShell `apply-update.ps1` kiểm tra PID tiến trình chủ, sao lưu file hiện tại thành `.previous-<guid>`, thay thế file mới nguyên tử, và tự động rollback nếu khởi động thất bại.
- **An toàn tuyệt đối:** Updater không tự ý thay thế file thực thi đang phục vụ người dùng trong suốt quá trình kiểm thử.
- **Giới hạn cần nghiệm thu thực tế:** Việc kiểm tra kết nối tải thực tế từ endpoint GitHub Releases (`https://api.github.com/repos/thanhtan229/Lalab-Auto-Report/releases/latest`) cần một release tag thực trên repository công khai, do đó thuộc về gate thủ công có sự cho phép của chủ tiệm.

---

## 8. Danh Mục Các Cổng Nghiệm Thu Thực Tế (Production Gate Inventory)

1. **Gate 1 — Máy In Tem Nhiệt 75x100mm (Thermal Printer Acceptance):**
   - **Trạng thái:** `NOT TESTED — USER DEFERRED`
   - *Bằng chứng & Lý do:* Không có máy in phần cứng tại thời điểm kiểm thử; người dùng đã explicit hoãn nghiệm thu máy in nhiệt.
   - *Quy tắc:* Không gọi PASS. Không ngăn cản tiếp tục Gate 2–5.
   - *Hạn chế (Limitation):* Bản phát hành full printer-supported vẫn giữ trạng thái hạn chế cho tới khi kiểm thử phần cứng máy in nhiệt thực tế.

2. **Gate 2 — Ứng Dụng PWA Trên Điện Thoại Di Động Nội Bộ (Real LAN Mobile PWA):**
   - **Trạng thái:** `PASS`
   - *Bằng chứng kiểm thử thực tế (Manual QA Evidence):*
     - iPhone thật truy cập thành công giao diện LAN PWA qua mạng Wi-Fi xưởng.
     - Admin: Truy cập báo cáo tài chính/doanh thu thành công (`PASS`).
     - Staff: Chặn truy cập tài chính/nút thu tiền đúng phân quyền (`PASS`).
     - Staff: Đột biến ghi chú đơn hàng (`order_note`) hoạt động thành công (`PASS`).
     - Staff: Đột biến trạng thái giao nhận (`order_delivered`) hoạt động thành công (`PASS`).
     - Kiểm thử ngắt kết nối Wi-Fi, kết nối lại và refresh trang web hoạt động mượt mà (`PASS`).

3. **Gate 3 — Kho Nguồn Đa Thư Mục & Ổ Cứng Xưởng (Workshop Storage / Multi-Root):**
   - **Trạng thái:** `PASS for current deployment`
   - *Bằng chứng kiểm thử thực tế (Manual QA Evidence):*
     - 2 thư mục gốc (Root 1 và Root 2) cùng online cục bộ.
     - Cùng đường dẫn tương đối (`RelativePath`) phân tách thành 2 đơn hàng độc lập (3 ảnh vs 5 ảnh) đạt 100% (`PASS`).
     - Quét theo phạm vi (scoped rescan) Root 1 từ 3 -> 4 ảnh, Root 2 vẫn giữ nguyên 5 ảnh (`PASS`).
     - Khi Root 2 inactive, quét lại Root 1 không bị merge, delete hoặc fallback sai (`PASS`).
     - *Phần cứng NAS vật lý (SMB share):* `NOT TESTED / NOT APPLICABLE` đối với topology triển khai hiện tại của xưởng (sử dụng multi-root trên các ổ đĩa cục bộ).

4. **Gate 4 — Triển Khai & Khói Sản Xuất Cloudflare Worker / D1 / R2 (Production Cloud Acceptance):**
   - **Trạng thái:** `PASS`
   - *Ủy quyền:* Người dùng đã explicit ủy quyền: "Cho phép Gate 4 production deploy".
   - *Deployed Worker Version:* `5028af59-2fce-4952-9ea3-8c8b88abbc4f` (Thời gian: 2026-10-05T08:33:20Z / 15:33:20+07:00; phiên bản trước đó: `982d60eb-a645-49d6-a452-21c3868b1178`).
   - *Custom Domain / Routes:* `https://lalab.tinix.io.vn`.
   - *Cloudflare D1 Backup:* Đã xuất sao lưu thành công tại `artifacts/cloud-production-backup-20261005.sql` (Dung lượng: 65,123 bytes, SHA256: `46FC7FD1A754D256220703C477AADF92647D30C2AEFFE8497804148E37A28212`, Database: `lalab-db` `ab8e3864-3544-4d89-85cb-0afe9ce1f615`).
   - *D1 Migrations:* Toàn bộ migrations 0001 đến 0006 đã được áp dụng từ trước; 0 pending migrations; cấu trúc schema hoàn toàn tương thích hai chiều (forward/backward compatible).
   - *Cấu hình Cloudflare (Bảo mật - Existence Only):*
     - `ADMIN_PIN`: Configured (Secret text)
     - `STAFF_PIN`: Configured (Secret text)
     - `JWT_SECRET`: Configured (Secret text)
     - `SYNC_SECRET`: Configured (Secret text)
     - Bindings: D1 Database (`lalab-db`), R2 Bucket (`lalab-media`), Assets (`./public`).
     - Tuyệt đối không có fallback secrets/PIN trong mã nguồn.
   - *Kết quả Automated / Read-Only Smoke Test (24/24 PASS):*
     - `/api/health`: HTTP 200 (`status="ok"`, `configurationReady=true`, `version="1.0.0"`).
     - PWA Root (`/`): HTTP 200 (`const IS_CLOUD_BUILD = true;`, badge `☁️ Cloud`).
     - Service Worker (`/sw.js`): HTTP 200 (`skipWaiting()`, `clients.claim()`, passthrough fetch).
     - Fail-Closed Auth: PIN rỗng hoặc PIN sai trả HTTP 401; `/api/auth/me` không token trả HTTP 401.
     - Login Admin & Staff: Đăng nhập thành công với PIN cấu hình, cấp JWT hợp lệ và phân vai đúng.
     - Phân quyền tài chính: Admin xem được `/api/reports/monthly` và `/api/customers/unpaid`; Staff bị chặn với HTTP 403.
     - Khế ước chi tiết Bill: Admin nhận đầy đủ `grandTotal`, `productSubtotal` và giá từng dòng; Staff nhận dữ liệu đã ẩn hoàn toàn số tiền (`null`).
     - Đột biến thanh toán Staff: Gọi `POST /api/bills/2/toggle-payment` từ Staff bị từ chối với HTTP 403.
     - Đọc ảnh Bill từ R2: Admin gọi `GET /api/bills/2/image` tải thành công ảnh hóa đơn JPEG (161,397 bytes, `image/jpeg`); Staff bị từ chối với HTTP 403.
     - Giao thức Sync V2: `/api/sync/v2/pull` với `X-Sync-Secret` trả `protocolVersion: 2`, `streamId` hợp lệ, cursor đơn điệu; secret sai bị từ chối HTTP 403; cursor sai format bị từ chối HTTP 400; `/api/sync/v2/reconciliation` trả HTTP 200 với 76 trường.
     - Đồng bộ Outbox thực tế: Drained toàn bộ 11 outbox mutations tồn đọng từ Desktop xuống 0, Desktop log không còn lỗi 400/5xx.
   - *Bằng chứng kiểm thử thủ công thực tế (Manual Cloud Acceptance Evidence — ChatGPT Project Control xác nhận PASS):*
     - Real phone Cloud Admin login: PASS
     - Admin Cloud bill detail: PASS
     - Real phone Cloud Staff permission matrix: PASS
     - Cloud -> Desktop note sync trên order test: PASS
     - Desktop -> Cloud note sync trên cùng order test: PASS
   - *Kế hoạch Rollback:* Khi cần hoàn tác mã nguồn Worker, thực hiện `npx wrangler rollback --version-id 681ef6eb-3938-49bc-a865-b53f433e61a0`. D1 không cần rollback do schema đã tương thích.

5. **Gate 5 — Phát Hành Phiên Bản & Thử Nghiệm Tự Động Cập Nhật (GitHub Release & Auto-Update):**
   - **Trạng thái:** `BLOCKED / PENDING`
   - Chưa thực hiện; Gate 4 đã hoàn tất nghiệm thu sản xuất; chờ lập kế hoạch và ủy quyền chính thức từ người dùng cho Gate 5.

---

## 9. Kết Luận Chung

- **Trạng thái Gate 4:** `PASS`
- **Trạng thái Phase 10:** `BLOCKED / NOT READY` (Gate 4 Cloud acceptance đã PASS; Gate 5 GitHub Release & Auto-Update chưa thực hiện và Gate 1 máy in nhiệt USER DEFERRED).
- **Tiếp theo:** Không bắt đầu Gate 5; hoàn tất đối soát danh tính mã nguồn và chờ chỉ thị tiếp theo.
