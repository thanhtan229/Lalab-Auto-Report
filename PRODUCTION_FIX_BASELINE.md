# Production Hardening Baseline & Verification

---

## 1. Historical Baseline — 2026-10-02 (Pre-Hardening Commit `bc54840`)

HEAD: `bc54840de5648db3c9a66cb927ed96f4d707218b` (master, pre-existing dirty tree preserved).

Evidence: `docs/production-evidence/` contains initial git status, source hashes, prior review copy and live reproducer output. Reproducers use temporary SQLite/filesystem fixtures, not customer data.

| Finding | Current evidence | Status |
|---|---|---|
| C1 | Rescan preserves exported quantity 15; direct locked rewrite persists 99 | PARTIALLY FIXED |
| C2 | Two roots return IDs 1,1; only one persisted order | OPEN |
| H1 | Access denied in deepest branch returns Resolved, quantity 18 | OPEN |
| H2 | 501 same-time events: first page 500, next 0; rejected DB apply advances checkpoint | OPEN |
| H3 | Unconfigured Worker accepts forged Admin JWT | OPEN |
| H4 | Staff payment mutation returns 200, two DB writes | OPEN |
| H5 | Injected original-bill update failure leaves order in two bills | OPEN |
| H6 | Worker fields differ from PWA bill rendering contract | OPEN |

Validation actually executed on 2026-10-02: Desktop Release 495/495; Worker 10/10 and TypeScript check passed. These suites did not cover the reproduced failures at that time.

---

## 2. Current HEAD Baseline & Finding Verification — 2026-10-04 (Commit `2855100`)

### 2.1 Repository Baseline
- **Git Commit (HEAD):** `28551003c601a16e731827244b12d76c67095006`
- **Git Branch:** `master` (tracking `origin/master`, clean working tree)
- **Git Status:** Working tree clean, 0 untracked / 0 uncommitted changes prior to Phase 0 documentation update.
- **Git Diff:** Empty before Phase 0 report.
- **.NET SDK:** `10.0.401` (Host: 10.0.12 x64, with .NET 8.0.424 SDK and runtimes 8.0.30 & 10.0.12 installed).
- **Node/npm Toolchain:** Node `v24.18.0`, npm `11.16.0`.
- **Worker Toolchain:** Wrangler `4.147.0`, Vitest `5.0.3`, TypeScript `5.7.3`, `@cloudflare/workers-types` `5.20261002.1`.
- **Critical Dependencies:**
  - Desktop: Target framework `net8.0` / `net8.0-windows`, `Microsoft.Data.Sqlite` 10.0.12, `Dapper` 2.1.89, `ClosedXML` 0.105.1, `CommunityToolkit.Mvvm` 8.4.2, `QRCoder` 1.8.0, `Serilog` 4.4.0, `xunit` 2.5.3, `FluentAssertions` 8.11.0.
  - Worker: Cloudflare Workers D1 Database (`lalab-db`), R2 Bucket (`lalab-media`), Assets.

### 2.2 Suite & Build Validation Results
1. **Desktop Solution Build (Debug):**
   - Command: `dotnet build LalabAutoReport.slnx -c Debug`
   - Result: **PASS** (0 Warnings, 0 Errors, Duration: 11.83s).
2. **Desktop Solution Build (Release):**
   - Command: `dotnet build LalabAutoReport.slnx -c Release`
   - Result: **PASS** (0 Warnings, 0 Errors, Duration: 32.39s).
3. **Desktop Full Test Suite (Debug):**
   - Command: `dotnet test --nologo -c Debug`
   - Result: **PASS** (Passed: 557, Failed: 0, Skipped: 0, Duration: 1m 4s).
4. **Desktop Full Test Suite (Release):**
   - Command: `dotnet test --nologo -c Release`
   - Result: **PASS** (Passed: 557, Failed: 0, Skipped: 0, Duration: 1m 9s).
5. **Worker Test Suite:**
   - Command: `npm test` in `cloud/worker` (Vitest v5.0.3)
   - Result: **PASS** (8 test files passed, 46 tests passed, Duration: 1.00s).
6. **Worker Typecheck:**
   - Command: `npx tsc --noEmit` in `cloud/worker`
   - Result: **PASS** (Exit code 0, 0 type errors).
7. **Worker Bundle / Dry-Run:**
   - Command: `npx wrangler deploy --dry-run` in `cloud/worker`
   - Result: **PASS** (Exit code 0, read 3 public assets, validated all bindings).

---

## 3. Finding-by-Finding Verification (C1, C2, H1–H6)

| Finding | Description | Current Status on HEAD | Primary Files / Code Paths | Test & Reproducer Evidence |
|---|---|---|---|---|
| **C1** | Locked/Exported bill có thể bị sửa qua rescan/edit/export không reopen | **VERIFIED FIXED** | `src/LalabAutoReport.Core/Services/CustomerBillingService.cs`<br>`src/LalabAutoReport.Infrastructure/Data/SqliteCustomerBillRepository.cs`<br>`src/LalabAutoReport.UI/ViewModels/CustomerBillReviewViewModel.cs` | `CustomerBillingTests.ProductionHistory_CannotRewriteOrDeleteWithoutReopen`<br>`CustomerBillingTests.Scenario6_RescanPreservesLockedQuantityAndWarns` |
| **C2** | Hai root cùng relative path bị gộp Order | **VERIFIED FIXED** | `src/LalabAutoReport.Core/Domain/Models.cs` (`RootFolderId`)<br>`src/LalabAutoReport.Core/Services/ScanService.cs` (`ScanOrderInRootAsync`)<br>`src/LalabAutoReport.Infrastructure/Data/SqliteOrderRepository.cs`<br>`src/LalabAutoReport.Infrastructure/Data/SqliteRootFolderRepository.cs`<br>`src/LalabAutoReport.Core/Services/PrintStatusService.cs` | `MultiRootFolderTests.ProductionIdentity_SameRelativePathInTwoRootsStaysSeparateAcrossRescans` |
| **H1** | Enumeration lỗi nhưng vẫn auto-resolve từ dữ liệu đọc được một phần | **VERIFIED FIXED** | `src/LalabAutoReport.Core/Services/PrintFolderResolver.cs` (`Incomplete`)<br>`src/LalabAutoReport.Core/Services/ScanService.cs` (`discoveredOrders.All(...)`, `order.Status = NeedsReview`) | `PrintFolderResolverTests.ProductionScan_UnreadableDeepestBranchCannotResolveParent`<br>`MultiRootFolderTests` (lines 70-75) |
| **H2** | Cloud checkpoint có thể bỏ event chưa tải/chưa apply | **VERIFIED FIXED** | `cloud/worker/src/operations.ts` (`getEventPage`, ID cursor)<br>`src/LalabAutoReport.Core/Services/CloudSyncService.cs`<br>`src/LalabAutoReport.Infrastructure/Data/SqliteCloudSyncStateRepository.cs` | `cloud/worker/test/versioned-sync.spec.ts` (0, 1, 500, 501, 1501 events)<br>`DurableCloudSyncTests.FailedWrite_RollsBackStateLedgerAndCursor_ThenRestartRetries`<br>`DurableCloudSyncTests.Pull501Events_CommitsEveryIdAndDoesNotOverwriteSettings` |
| **H3** | Worker fallback secret/PIN có thể biết từ source | **VERIFIED FIXED** | `cloud/worker/src/auth.ts` (`validSecret`, `getCryptoKey`)<br>`cloud/worker/src/index.ts` | `cloud/worker/test/auth-hardening.spec.ts` (6 tests fail-closed, rejects missing/default secrets) |
| **H4** | Staff có quyền payment/financial vượt contract | **VERIFIED FIXED** | `cloud/worker/src/index.ts`<br>`cloud/worker/src/mobile-bill.ts`<br>`src/LalabAutoReport.UI/Services/MobileWebServer.cs` | `cloud/worker/test/permission-hardening.spec.ts`<br>`MobileWebServerTests.ProductionPermission_StaffCannotAccessFinancialMediaOrMutations`<br>`MobileWebServerTests.ProductionContract_BillDetailMatchesPwaAndRedactsAmounts` |
| **H5** | Split bill không atomic, failure có thể để order thuộc hai bill | **VERIFIED FIXED** | `src/LalabAutoReport.Infrastructure/Data/SqliteCustomerBillRepository.cs` (`SplitBillAtomicAsync`)<br>`src/LalabAutoReport.Core/Services/CustomerBillingService.cs` (`SplitOrdersToNewBillAsync`) | `SplitBillTests.ProductionSplit_WriteFailureRollsBackNewBillAndMembership` |
| **H6** | Cloud bill response không khớp PWA viewBill | **VERIFIED FIXED** | `cloud/worker/src/mobile-bill.ts` (`toMobileBill`)<br>`cloud/worker/src/index.ts`<br>`cloud/worker/public/index.html` (`viewBill`) | `cloud/worker/test/bill-contract.spec.ts`<br>`cloud/worker/test/pwa-render.spec.ts`<br>`MobileWebServerTests.ProductionContract_BillDetailMatchesPwaAndRedactsAmounts` |

---

## 4. Chi tiết bằng chứng Reproducers đã kiểm chứng

1. **C1 — Historical Locked/Exported Rescan & Direct Rewrite:**
   - Khi cố ý gọi `SaveBillAsync` sửa đổi tiền trên hóa đơn `Locked` hoặc `Exported`, `SqliteCustomerBillRepository` chặn lại và ném `InvalidOperationException: Hóa đơn đã chốt. Hãy mở lại trước khi sửa nội dung.`
   - Khi quét lại thư mục đã khóa bill (`ScanOrderAsync`), snapshot số lượng in giữ nguyên (không bị ghi đè), cờ `FilesystemChangedAfterLock` được bật để cảnh báo người dùng.
   - Khi xóa bill (`DeleteBillAsync`), ném `InvalidOperationException: Hãy mở lại hóa đơn trước khi xóa.`
   - Hành động mở lại (`ReopenBillAsync`) bắt buộc cung cấp lý do tường minh, khôi phục trạng thái về `Draft`.

2. **C2 — Two Roots with Identical Relative Path:**
   - Hai kho độc lập `A` và `B` cùng chứa thư mục tương đối `2026-10-02\Customer\13x18 in` (Kho A có 1 ảnh, Kho B có 2 ảnh).
   - Quét phát hiện cả 2 đơn với ID phân biệt (`a.Id != b.Id`), số lượng ảnh độc lập (1 vs 2).
   - Các API quét/thao tác không chỉ định kho tường minh ném `InvalidOperationException` để chống nhập nhằng.
   - Thao tác đánh dấu in (Print status) trên đường dẫn thuộc Kho B chỉ cập nhật đơn của Kho B (`b.IsPrinted = true`), giữ nguyên đơn Kho A (`a.IsPrinted = false`).

3. **H1 — Unreadable / Failed Filesystem Branch:**
   - `PrintFolderResolver` gặp lỗi phân quyền (Access Denied / I/O error) trên thư mục con sâu nhất không tự động chọn thư mục cha hoặc dữ liệu đọc được một phần; trả về `PrintFolderResolutionStatus.NoPrintFolder`, `PrintCount = null`, và ghi rõ `ErrorMessage`.
   - `ScanService` gán trạng thái `OrderStatus.NeedsReview`, chặn tự động tạo bản nháp hóa đơn hoặc chốt tiền sai.
   - Quét ngày gặp lỗi đọc thư mục không prune/xóa các bản ghi đơn hàng đã lưu trong database.

4. **H2 — Cloud Event Pagination & Checkpoint Atomic:**
   - Worker hỗ trợ phân trang ID-based cursor (`v2/pull?cursor=...`). Kiểm thử 501 sự kiện có cùng timestamp được phân trang đầy đủ không sót hoặc nhảy cóc (`nextCursor` trỏ đúng ID cuối của trang).
   - Desktop `SqliteCloudSyncStateRepository` áp dụng sự kiện và cập nhật checkpoint trong cùng 1 transaction. Khi gặp lỗi ghi đĩa, transaction rollback hoàn toàn (`Cursor = 0`, 0 sự kiện đã áp dụng). Khi lỗi được khắc phục, quá trình retry áp dụng thành công và nâng checkpoint lên 1.
   - Sự kiện không rõ kiểu (unknown type) hoặc sai entity dừng xử lý an toàn và giữ nguyên checkpoint.

5. **H3 — Cloud Secrets Fail-Closed:**
   - Worker kiểm tra `validSecret`: nếu `JWT_SECRET` hoặc `SYNC_SECRET` bị thiếu, để trống, hoặc trùng với giá trị hằng số mã nguồn (`lalab-secret-salt-2026-tinix`, `lalab-sync-secret-2026`), Worker trả về HTTP 503 Service Unavailable ("Authentication secret is not configured").
   - Token giả mạo ký bằng key mặc định bị từ chối hoàn toàn (`verifyAuthToken` trả về `null`).
   - Không tồn tại mã PIN mặc định nào trong mã nguồn.

6. **H4 — Staff Permission Boundaries:**
   - Quyền Staff khi gọi trực tiếp API đổi trạng thái thanh toán (`/api/bills/{id}/toggle-payment`), xem ảnh bill (`/api/bills/{id}/image`), nhãn in bill (`/api/bills/{id}/label`, `/api/bills/{id}/print`), mã QR (`/api/bills/{id}/qr`), hay báo cáo tài chính trả về HTTP 403 Forbidden trên cả LAN và Cloud.
   - API chi tiết hóa đơn (`/api/bills/{id}`) đối với Staff trả về dữ liệu che giấu toàn bộ thông tin tài chính (`grandTotal = null`, `lineTotal = null`, không chứa số tiền), trong khi Admin nhận đầy đủ số tiền.

7. **H5 — Split Bill Atomic Execution:**
   - Phương thức `SplitBillAtomicAsync` bọc toàn bộ thao tác trong 1 transaction duy nhất: tạo bill mới, cập nhật bill gốc, di chuyển job link trong `order_item_scans`.
   - Khi inject lỗi (trigger SQLite fail) tại bước cập nhật bill gốc hoặc job link, toàn bộ transaction bị rollback: không tạo bill thừa, bill gốc giữ nguyên 2 đơn, không xảy ra tình trạng đơn hàng thuộc 2 bill cùng lúc, foreign keys toàn vẹn.

8. **H6 — Cloud Bill Response & PWA Contract:**
   - Endpoint Cloud `/api/bills/{id}` trả về cấu trúc DTO thống nhất với LAN (`customerName`, `periodEnd`, `grandTotal`, `lines` với `description` và `lineTotal`).
   - Script PWA `viewBill` phân tích và render chính xác trên VM context thực tế, không sinh lỗi `undefined`, escape ký tự HTML an toàn, hiển thị số tiền cho Admin và ẩn số tiền cho Staff.

---

## 5. Kết luận Phase 0
- Toàn bộ 8 finding (**C1, C2, H1, H2, H3, H4, H5, H6**) trên commit HEAD `2855100` đều ở trạng thái **VERIFIED FIXED**.
- Các suite kiểm thử tự động (.NET Desktop 557 tests Debug/Release, Cloud Worker 46 tests, TypeScript typecheck, Wrangler bundle) đều vượt qua 100% với 0 lỗi và 0 cảnh báo.
- Không phát hiện bất kỳ regression nào trong logic nghiệp vụ cốt lõi hay ranh giới an toàn.
