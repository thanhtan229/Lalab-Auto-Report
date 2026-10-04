# Final Production Review

> Audit baseline ngày 2026-10-02. Sau yêu cầu sửa lỗi, C1/H1/H2/H3 đã có implementation và regression tests; xem [báo cáo sửa ngày 2026-10-03](docs/RELEASE_BLOCKER_FIXES_20261003.md) để biết kết quả xác minh cuối và runtime. Phần dưới giữ bằng chứng audit gốc; chưa thể xác nhận Cloud production/hardware READY chỉ từ fixes local.

## STATUS

**NOT READY** — 4 release blockers đã tái hiện: **1 CRITICAL, 3 HIGH**. Source giữ nguyên trong task này; chỉ audit, chạy tests/build, mô phỏng trên fixture và cập nhật báo cáo.

Ngày: **2026-10-02**, UTC+7. Phạm vi: WPF Desktop, scanner/billing/reports, SQLite/migrations, LAN API/PWA, Cloud Worker/D1/R2 projection, sync/recovery, Windows integration và updater.

## BASELINE

- Branch `master`, HEAD `bc54840de5648db3c9a66cb927ed96f4d707218b`. Working tree có nhiều modified/deleted/untracked files trước review, gồm cả cloud/mobile và hardening. Không revert, sửa source, commit hoặc deploy.
- 202 hashes trong manifest lần nghiệm thu trước được đối chiếu: **không có source/test/migration/runtime-script nào khác**. Vì vậy các lỗi dưới đây có mặt trong baseline; chưa có bằng chứng gọi chúng là regression mới phát sinh trong task này. Hai kết luận VERIFIED FIXED về quyền Staff và mobile contract trước đây rộng hơn coverage thực tế.
- Đã đọc `agents.md`, `DEVELOPMENT_WORKFLOW.md`, `ARCHITECTURE.md`, `PROJECT_STATUS.md`, các phần nghiệp vụ/acceptance của PLAN V1/V2/Printed Explorer và plan hardening, đối chiếu code và tài liệu sync/release candidate. Không có `RELEASE_WORKFLOW.md` hoặc `RESEARCH*.md` trong tree hiện tại.
- Quy tắc quantity áp dụng là **V2 Effective Billing Folder**, không phải V1 Source!=Print phải review. `agents.md`/review cũ còn mô tả V1; PLAN V2 và implementation mới đã supersede.
- Tài liệu chưa phản ánh đúng trạng thái: `PROJECT_STATUS.md` còn ghi default Cloud PIN, protocol một chiều/legacy, 484–495 tests; `ARCHITECTURE.md` chỉ liệt kê migration đến V7, version kiến trúc 2.0 khác version artifact 1.0.0. `DEVELOPMENT_WORKFLOW.md` tham chiếu restart batch files đã xóa. Đây là hạn chế tài liệu, không được dùng làm bằng chứng safety.
- `FINAL_REVIEW.md` READY trước task được lưu nguyên bản tại `docs/review-evidence-20261002/FINAL_REVIEW_previous.md`. Báo cáo này thay kết luận đó theo bằng chứng hiện tại.
- Runtime: .NET SDK **10.0.401**, Node **24.18.0**, Python **3.11.9**, Windows x64; app target `net8.0-windows`. SQLite schema **21**; Worker migrations **0001–0005**.

Evidence của lần review: `docs/review-evidence-20261002/`. Reproducers gọi service/repository thật, compiled Worker và SQLite thực. C# harness/Node harness ở `artifacts/audit-probe/`; không thêm tests vào source tree.

## TEST / BUILD RESULTS

| Check | Result | Evidence |
|---|---|---|
| Desktop Debug full suite | PASS **534/534**, 0 skipped | `audit-debug.trx`; --no-build trên binary có manifest source khớp |
| Desktop Release full suite | PASS **534/534**, 0 skipped | `audit-release.trx`; rebuild và chạy lại |
| Debug build | PASS, **0 warnings/errors** | Output riêng `artifacts/audit-debug/`, không ghi đè binary đang phục vụ |
| Release build | PASS, **0 warnings/errors** | `dotnet build ... -c Release --nologo -m:1` |
| Worker tests | PASS **43/43**, 7 files | `npm test` |
| Worker TypeScript | PASS | `npx tsc --noEmit` |
| Worker bundle | PASS, dry-run, không deploy | `artifacts/audit-worker/index.js`, 43.07 KiB |
| Dependency audit | PASS, 0 vulnerabilities | `npm audit --audit-level=moderate` |
| Published Windows smoke | PASS startup/shutdown/restart, duplicate IPC, persistent LAN token, locked bill 75.000 không đổi | `published-runtime-acceptance.json` |
| Real-user DB check | PASS integrity OK, FK violations 0 | Chỉ đọc, `final-audit-baseline.json`; schema 21, legacy reconciliation flag vẫn true |
| Adversarial scenarios | **4 FAIL safety criteria** | JSON/text/PNG reproducers bên dưới; hiện chưa được existing suites bảo vệ |

Tests pass không phủ định reproducer. Không sửa assertions để làm các lỗi biến mất. Không thực hiện fresh standalone publish trong lần audit này: dùng artifact đã publish, SHA256 `d7e5dd766f2d8e4ea0486876323861cc1bbdf444f7d69fe3523b4de68d194c0b`, source manifest khớp. Publish/updater copied-EXE acceptance trước đó được kiểm tra lại bằng evidence, không trình bày như vừa chạy remote update.

## CRITICAL FINDINGS

### C1 — Reset dữ liệu tái sử dụng identity và giữ Cloud state cũ, làm payment của khách cũ áp vào khách mới

**Severity:** CRITICAL. **Release decision: MUST FIX BEFORE RELEASE.**

**Problem:** Operational reset xóa orders/bills và `sqlite_sequence`, nhưng giữ nguyên `cloud_operational_outbox`, revisions, applied events, cursor/stream và cấu hình Cloud. IDs mới trùng entity Cloud cũ. Event ACK chỉ xác định entity bằng type + integer ID; projection upsert theo ID giữ nguyên operational state đã revisioned, nên bill mới kế thừa payment cũ.

**Evidence:**

- `src/LalabAutoReport.Infrastructure/Data/DatabaseResetService.cs:88` danh sách bảng reset không có các bảng V2 sync; dòng **128** xóa sequences.
- `src/LalabAutoReport.Infrastructure/Data/SqliteCloudSyncStateRepository.cs:31` ApplyEvent không có generation/immutable identity của entity; ACK payment matching ID/opID ghi vào bill hiện tại.
- `cloud/worker/src/db.ts:216` upsert conflict theo `id`: cập nhật customer/amount nhưng giữ payment state/revision theo thiết kế V2.
- `docs/review-evidence-20261002/live-reproducers.txt`: old bill ID 1 có payment pending → reset thật bằng service → new unpaid customer bill ID **1**, **200.000** → ACK thao tác cũ → `afterPaid:true`; outbox cũ vẫn 1, Cloud vẫn bật, stream vẫn `old-stream`.
- `worker-reproducers.txt`: sau projection bill mới `NEW_UNPAID_CUSTOMER`, Cloud row amount **200000** nhưng `is_paid:1`, `payment_revision:1` của bill cũ.

**User impact:** Khách mới chưa trả tiền bị đánh dấu đã thu, biến mất khỏi công nợ. Note/delivery cũng có thể bị gán sai entity khi integer ID được tái sử dụng. Safety backup không tự ngăn dữ liệu sai được tiếp tục sync.

**Reproduction:** Bật Cloud với stream đã bind; thay payment offline; dùng Cài đặt → reset dữ liệu vận hành; tạo bill mới; reconnect/ACK pending cũ hoặc push projection mới cùng ID. Harness đã thực hiện toàn tuyến repository/service và Worker riêng bằng fixture, không reset DB thật.

**Recommended fix:** Phải có identity/generation bền vững qua reset/restore, không tái sử dụng entity identity Cloud. Mức bảo vệ tối thiểu trước release: chặn reset destructive khi còn binding/pending Cloud và bắt buộc quy trình archive/reset/reconciliation hai phía có backup; serialize lifecycle operations với sync gate. Không chỉ xóa outbox/cursor hoặc giữ ID sequence rồi coi mọi trường hợp đã an toàn. Bổ sung test reset + pending ACK + Cloud row cũ + bill mới, và kiểm tra lifecycle restore/purge/delete tương ứng.

Phạm vi đã chứng minh là operational reset. Restore/purge cần review theo cùng boundary; không tuyên bố đã tái hiện mọi dạng corruption của chúng.

## HIGH FINDINGS

### H1 — Staff vượt quyền financial qua endpoint tem theo order

**Severity:** HIGH. **Release decision: MUST FIX BEFORE RELEASE.**

**Problem:** `/api/orders/{id}/label` và `/print-label` chỉ yêu cầu login. Nếu order gắn bill, chúng tự chọn bill label có tiền/QR thay vì order label không financial. Admin guard ở `/api/bills/{id}/label` không bảo vệ đường order này.

**Evidence:** `src/LalabAutoReport.UI/Web/KestrelMobileWebServer.cs:463` và **526**; lựa chọn bill exporter ở các dòng **487–489**, **543–549**. `WpfShippingLabelExporter.cs:465` in GrandTotal; dòng **493** còn đưa số tiền vào QR. Permission contract phase 6 yêu cầu financial media Admin-only.

HTTP thực trên published fixture: Staff login → GET `/api/orders/1001/label` trả **200 image/png** (41.474 bytes), GET `/api/bills/1001/label` trả **403**. PNG đọc rõ **75.000 đ COD** và có QR. Bằng chứng: `staff-order-label-reproducer.json`, `staff-order-bill-label.png`. Không gọi print endpoint trên máy in thật.

**User impact:** Nhân viên đọc/in financial bill qua ID order dù API bill đã redact. Đây là bypass backend, không thể sửa chỉ bằng ẩn nút UI.

**Reproduction:** Staff request label của order có customer_bill_id. Không cần Admin token hoặc giả chữ ký.

**Recommended fix:** Khi Staff xem/in label order, chỉ dùng exporter order không financial; hoặc deny nếu workflow buộc chọn bill label. Resolve label type rồi áp quyền trước render/print. Test Staff order chưa bill và đã bill, cả GET/POST; Admin vẫn in bill label. Giữ quyền Staff workflow order theo yêu cầu.

### H2 — Remote print lặp lại sau mất ACK, có thể in liên tục cùng command

**Severity:** HIGH. **Release decision: MUST FIX BEFORE RELEASE.**

**Problem:** Worker trả mọi PENDING command mà không claim/lease; Desktop in rồi mới gửi ACK. ACK lỗi HTTP/network bị bỏ qua (HTTP status không kiểm tra), không có ledger local đã in theo command ID. Lần poll sau cùng command lại được in.

**Evidence:**

- `CloudSyncService.cs:643`, print ở **713–717**, ACK ở **741–750**; không persist command completion trước retry và không retry ACK độc lập.
- `cloud/worker/src/db.ts:384`: SELECT PENDING; không transition PROCESSING khi lấy.
- `live-reproducers.txt`: cùng command **88**, mất ACK giả lập, gọi processor hai lần → **actualPrintCalls:2**, expected 1.
- `worker-reproducers.txt`: hai lần GET print commands đều nhận cùng command PENDING. Timer Desktop poll 15 giây.

**User impact:** Nhãn in trùng/rác giấy và lệnh tồn tại lâu, lỗi mạng không phải edge case hiếm. Nếu mất kết nối kéo dài, có thể tiếp tục in sau mỗi poll/retry hoặc restart. Physical-print effect không nằm trong SQLite transaction.

**Reproduction:** Cho printer fake nhận job thành công rồi HTTP ACK throw; gọi processor lại. Không dùng hardware production.

**Recommended fix:** Claim/lease command atomic, lưu durable local command/job state và ACK retry độc lập. Đã in thì retry ACK, không in lại. Crash ở ranh giới spooler không thể được giải quyết bằng một boolean trong memory: trạng thái in không rõ cần human retry/confirmation hoặc spooler job identity. Kiểm tra ACK status. Test mất ACK/500, restart, hai consumers và crash boundary, không hứa exactly-once physical printing khi chưa có chứng cứ.

### H3 — Mobile hiển thị dòng/order đã loại khỏi bill và Cloud tính subtotal sai

**Severity:** HIGH. **Release decision: MUST FIX BEFORE RELEASE.**

**Problem:** Desktop tổng tiền/JPEG chỉ tính `line.IsIncluded` trong `order.IsIncluded`. LAN detail và Cloud projection xuất mọi `bill.Lines` mà không truyền inclusion. Cloud adapter cộng tất cả lineTotal để suy subtotal, khiến bill mobile không khớp bill đã review.

**Evidence:**

- `CustomerBillingService.cs:439–445`: tổng hợp đúng inclusion; `WpfJpegBillExporter.cs:224–237` lọc đúng.
- `KestrelMobileWebServer.cs:609`: detail map mọi bill.Lines.
- `CloudSyncService.cs:828`: projection map mọi lines, bỏ IsIncluded/order inclusion.
- `cloud/worker/src/mobile-bill.ts:21`: subtotal là sum mọi lineTotal.
- `live-reproducers.txt`: bill gồm included **75.000** và excluded **25.000**, Desktop total **75.000**, projection vẫn hai dòng.
- `lan-excluded-line-reproducer.json`: HTTP Admin bill fixture total/subtotal **75.000**, nhưng vẫn có `EXCLUDED FIXTURE LINE` **25.000** như dòng được tính.
- `worker-reproducers.txt`: HTTP Cloud 200, `productSubtotal:100000`, `grandTotal:75000`, cả hai dòng hiển thị và không có dấu hiệu excluded.

**User impact:** Bill gửi/xem trên điện thoại mâu thuẫn với JPEG và quyết định của chủ tiệm; người thu tiền/khách có thể hiểu sai số lượng và cách tính. GrandTotal persisted vẫn đúng trong fixture; đây là lỗi presentation/projection, chưa có chứng cứ total DB bị sửa.

**Reproduction:** Review bill → bỏ chọn một line hoặc order nhưng giữ snapshot để audit → lock/export → xem detail LAN/Cloud. Fixture chỉ sửa DB synthetic để tạo snapshot excluded; đã hoàn trả fixture sau thử.

**Recommended fix:** Dùng cùng tập included orders/lines cho mobile projection và LAN, hoặc contract explicit excluded + render riêng rõ ràng. Project persisted ProductSubtotal/AdjustmentsTotal thay vì suy lại từ tập lines mất inclusion. Giữ các dòng excluded trong lịch sử DB. Test excluded line, excluded whole order, adjustments và đối chiếu JPEG/LAN/Cloud cùng bill.

## MEDIUM FINDINGS

Không giữ finding MEDIUM độc lập nào chỉ để tăng số lượng. Tài liệu/release gates và chưa kiểm tra thiết bị được ghi đúng nhóm bên dưới, không giả thành lỗi code đã chứng minh.

## REGRESSION RISKS

- Các guards mới của historical save/reopen/split, root scope và scanner partial failures vẫn đạt suite. Không tái báo 8 finding cũ là OPEN nếu đường đó đã được bảo vệ.
- Hai gap thực tế: guards financial chưa bao phủ attached-order label; contract đơn giản chưa bao phủ inclusion. Đánh giá cũ về permission/mobile contract cần giới hạn lại.
- V2 sync ngăn stale projection khi identity ổn định, nhưng reset/restore/delete lifecycle chưa có generation/tombstone contract hoàn chỉnh. C1 chứng minh thay đổi này cần nghiệm thu ở boundary dùng chung.
- Remote print queue là effect bên ngoài DB; suite chỉ có happy path ACK, không phủ mất ACK.

## DATA INTEGRITY

PASS: monetary snapshots/quantity overrides, locked/exported guard và explicit reopen; split atomic rollback; scan lỗi không prune phần quan sát thiếu; multi-root fixture; migration transactions/FK checks; apply-event/checkpoint/outbox rollback/idempotency tests; backup/restore service tests.

FAIL: C1 sai mapping payment sau reset. `PRAGMA foreign_key_check`/integrity vẫn OK trong kiểu lỗi này vì reference là ID hợp lệ của **sai entity**; không được dùng WAL/FK như bằng chứng tuyệt đối về nghiệp vụ.

DB thật chỉ đọc: integrity OK, FK 0, schema 21. Cờ legacy reconciliation vẫn true, không tự sửa cursor/payment/history. Snapshot/backup/inventory cũ không tự chứng minh provenance đã từng bị flatten chưa mất.

## SECURITY

PASS: configured Cloud JWT/sync fail closed, role/expiry validation, Staff direct payment/report/financial bill-media guard, LAN persistent HMAC, không đưa LAN token vào Cloud QR. Worker tests/API fixtures dùng secrets synthetic; không log/export secrets thật.

FAIL: H1 attached-order financial label bypass. Không có kết luận thêm về account compromise/public R2 vì không tái hiện hoặc có quyền kiểm tra deployment thật trong lần review.

LAN dùng HTTP trên LAN theo thiết kế; không xem đó là bảo đảm an toàn khi exposed ra Internet. Thumbnail Cloud đặt public cache lâu là điểm cần đối chiếu privacy/deployment trước rollout, nhưng chưa có shared-cache leak reproducer nên không thêm release blocker riêng.

## STABILITY / RECOVERY

PASS: standalone startup/restart/duplicate handoff, token qua restart, snapshot giữ nguyên; missing configuration/local Worker fail closed; sync HTTP/SQLite apply failure giữ checkpoint; pagination/unknown event/stream change/reconciliation tests; updater file-lock/tamper/failure rollback tests.

FAIL: remote print retry effect (H2); reset Cloud lifecycle (C1). Restore UI hiện cho tải lại màn hình/khởi động lại, không phối hợp với sync gate; cần kiểm tra cùng boundary C1 trước chốt recovery.

Không sửa source nên không cần restart để nạp code mới. Full suite có test register/unregister HKCU context menu; đã restart **đúng owned Desktop**, thay PID 93452 bằng **109728**, LAN health/PID ownership OK để app phục hồi integration theo cấu hình. Không biết task xưởng nào bị gián đoạn. Worker project khác/port 8787 không bị can thiệp; Worker local vẫn configuration_required do không có dev secrets. Các fixture process đã dừng.

## PERFORMANCE

Không phát hiện blocker performance có reproducer đại diện. Scanner dùng metadata và traversal theo scope; báo cáo tổng hợp số liệu từ database. Báo cáo tháng vẫn gọi GetMissingScanDaysAsync để khám phá thư mục ngày trên các root, nên không thể khẳng định không truy cập disk; đường này không quét lại order/image để tính tiền. Thumbnail decoding là feature riêng, không dùng đếm quantity.

Không tuyên bố đã benchmark NAS/dataset lớn hoặc zero idle tuyệt đối: AutoScanCoordinator poll 15 giây theo cấu hình, Cloud timer 15 giây; optional smart idle scan có tài liệu Printed Explorer mới hơn quy tắc V1 manual-only. Không đề xuất micro-optimization/N+1 refactor khi chưa có workload chứng minh.

## RELEASE / UPDATE

- Artifact đang version **1.0.0**, assembly/file **1.0.0.0**, hash nêu ở baseline. Không tạo tag/release hoặc remote deploy. Cần chọn version mới đúng trước publish nếu existing release trùng version; chưa xác minh latest release remote trong task này.
- Fresh Debug/Release build PASS. Published standalone smoke dùng artifact trước có hash/source phù hợp.
- Full suite bao gồm 6 tests chạy replacement PowerShell thực: atomic replacement, file lock, tampered download, unrelated owner PID, invalid download, failed restart rollback. Copied-EXE/profile update + data-preservation evidence trước đó còn khớp; không chạy self-update trên ứng dụng phục vụ người dùng.
- Updater giữ previous/download/log, kiểm tra PE và hash. SHA256 tự sinh sau download kiểm tra integrity local, không phải chữ ký publisher. Không phát hiện exploit cụ thể đủ để thêm finding chỉ vì chưa ký binary.
- Actual remote download/UI update, migration/deploy Worker production, rotation secret và rollback remote: **NOT TESTED**. D1 migrations local/idempotency evidence có; không gọi --remote.
- Data backup + protocol deployment order/reconciliation là gate cần làm sau fixes. Không xóa DB để upgrade hoặc rollback về legacy cho tiếp tục ghi operational state.

## MANUAL ACCEPTANCE

| Scenario | Result | Scope / lý do |
|---|---|---|
| Scanner bình thường, ambiguous leaf, denied/disappeared folder, multi-root | PASS (simulation) | Fresh fixture suites, không scan data xưởng |
| Customer/Guest overrides, lock/history/re-export/reopen/split failure | PASS (simulation) | Service/repository/JPEG tests |
| Startup/shutdown/restart, duplicate instance/IPC | PASS | Fresh published synthetic profile |
| LAN login giữ token sau restart | PASS | Fresh published API smoke |
| Staff financial detail/payment/bill media | PASS cho direct bill routes | Fresh suite; không suy ra mọi alias route |
| Staff attached-order label | **FAIL** | HTTP 200 + PNG có 75.000 COD; direct bill label 403 |
| Reset + pending old payment + new bill | **FAIL** | Service thật + real SQLite; Worker projection fixture |
| Remote print mất ACK | **FAIL** | Fake printer nhận cùng command 2 lần |
| Bill excluded line/order mobile contract | **FAIL** | LAN/Cloud HTTP và projection; total 75.000 nhưng excluded 25.000 vẫn có |
| Event 501/1501, write rollback, opID retry/restart, concurrent sync | PASS (simulation) | Fresh C#/Worker suites |
| Legacy reconciliation, endpoint/stream change | PASS (simulation) | Fresh tests; DB thật vẫn chờ chủ tiệm duyệt |
| Backup/restore và updater failure boundaries | PASS (simulation) | Fresh tests; previous copied-EXE evidence inspected |
| Physical printer/75×100 stock, NAS workload, phone hardware | NOT TESTED | Chưa có acceptance thiết bị được phép/cung cấp |
| Windows Explorer thao tác bằng chuột, reboot toàn máy | NOT TESTED | Registry/IPC tests không thay thế interaction/reboot thật |
| Cloud production offline→online / secrets / migration / remote rollback | NOT TESTED | Không deploy/đổi dữ liệu Cloud thật |
| Actual updater remote download + UI apply | NOT TESTED | Chỉ tests và copied-EXE evidence; không cập nhật binary xưởng |

Kết quả FAIL là expected safety behavior không đạt, độc lập với 534/43 automated PASS. Không gộp simulation thành physical acceptance.

## MUST FIX BEFORE RELEASE

**4 issues:**

1. C1: entity lifecycle/reset không được để old Cloud payment áp vào bill mới.
2. H1: Staff order-label path không được phát financial bill label.
3. H2: mất print ACK không được tự in lại cùng command; durable claim/completion/ACK recovery.
4. H3: mobile bill phải giữ đúng inclusion và subtotal của snapshot.

Sau fixes chạy targeted regression, full relevant suites/build, fresh scenarios trên fixture và review diff. Sau đó hoàn tất production gates đã ghi trong manual acceptance; không dùng rollback/đối soát để che lỗi lifecycle.

## SAFE TO DEFER

Không có recommendation refactor/style/polish bắt buộc. Cải thiện naming/abstraction hoặc micro-optimization không thuộc phạm vi release decision. Đồng bộ tài liệu/version/checklists cần làm cùng release preparation, nhưng không thêm thành lỗi code thứ năm.

## FINAL DECISION

**NOT READY. SAFE TO USE NOW: No cho phạm vi production đầy đủ.**

Historical billing/scanner và nhiều guards đã được cải thiện, nhưng payment có thể gán sai khách sau reset, Staff financial bypass và print retry không an toàn là các lỗi thực sự cần sửa. Bill mobile inclusion cũng cần khớp snapshot trước khi dùng để thu tiền/gửi khách. Không có sửa code trong lần review này và không thể xác nhận READY chỉ từ tổng test count.
