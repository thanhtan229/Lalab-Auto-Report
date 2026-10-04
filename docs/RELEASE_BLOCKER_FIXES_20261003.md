# Release blocker fixes — 2026-10-03

Baseline: `FINAL_REVIEW.md`, C1/H1/H2/H3. Đây là đợt sửa sau audit; kết luận audit trước đó được giữ để truy nguyên.

## Phase 1 — C1: bảo vệ lifecycle database

Reset operational/factory, restore và purge dùng cùng gate theo database path với Cloud sync. In-flight sync phải kết thúc trước thao tác lifecycle. Khi database có Cloud credentials, stream/cursor, legacy reconciliation, pending outbox hoặc lịch sử Cloud, thao tác destructive bị chặn với thông báo rõ ràng. Tắt checkbox Cloud không bỏ qua liên kết cũ. Restore kiểm tra cả database hiện tại và bản backup.

Operational reset local giữ `sqlite_sequence`; ID đã dùng không được cấp lại. Database chưa từng cấu hình Cloud vẫn reset/restore được, kể cả cờ Cloud mặc định bật nhưng chưa có credentials. Không xóa outbox, cursor hoặc payment để làm biến mất lỗi.

Acceptance: bound/pending reset cả hai scope bị chặn và giữ dữ liệu; restore bound source/target bị chặn; ID mới lớn hơn high-water cũ; reset chờ in-flight HTTP sync; backup/restore local vẫn khôi phục chính xác. Không đổi payment resolution V2 hoặc snapshot đã khóa.

Giới hạn có chủ ý: không cung cấp workflow tự động reset/restore database đã liên kết Cloud. Workflow này cần archive/reconciliation hai phía riêng; không hướng dẫn xóa các bảng Cloud để vượt guard. Delete từng draft không reset sequence; không thêm tombstone protocol trong batch này.

## Phase 2 — H1: quyền tem order

Staff GET label/POST print-label của order luôn dùng exporter order không có financial bill. Admin vẫn có thể lấy/in bill label của order đã gắn bill. Guard chọn loại tem ở backend trước render hoặc gọi printer.

Acceptance HTTP: Admin/Staff × attached/unattached order, cả preview và print. Staff không gọi bill exporter/printer; workflow order và auto-delivery vẫn chạy. Các endpoint financial riêng vẫn Admin-only.

## Phase 3 — H2: durable remote print

SQLite migration **22** thêm ledger key `(endpoint, stream_id, command_id)` và claim token. Desktop persist STARTED trước HTTP claim/spooler; Worker claim là atomic PENDING→PROCESSING. Hai consumers chỉ một claim thắng. Claim không tự hết hạn/requeue vì không thể suy ra giấy đã in từ HTTP timeout.

Desktop ghi COMPLETED ngay sau printer thành công, trước auto-delivery và ACK. ACK HTTP lỗi/mất response chỉ retry ACK từ ledger, kể cả restart và Worker không trả PROCESSING trong queue. Ledger giữ terminal IDs để chống duplicate poll. ACK kiểm tra HTTP status; Worker yêu cầu đúng claim token, terminal status và cho retry cùng terminal status idempotently.

STARTED còn lại sau gián đoạn thành FAILED với cảnh báo kiểm tra thủ công; không tự spool lần hai. Cảnh báo vẫn hiện trong Cloud status sau restart và sau các status sync bình thường. Nếu tem đã in nhưng delivery write lỗi, giữ COMPLETED và yêu cầu cập nhật giao hàng thủ công. Mobile confirm nhắc kiểm tra lệnh trước nếu in bị gián đoạn.

Acceptance: ACK network loss/500 + service restart; cùng queue lặp lại; hai services; competing Worker consumers; crash STARTED; claim conflict; delivery DB abort sau spooling; wrong-owner ACK, legacy unclaimed ACK và terminal→PENDING bị chặn. Tests dùng fake printer; không gọi máy in xưởng.

Giới hạn: không hứa exactly-once physical printing. Khi kết quả không rõ, kiểm tra tem/spooler rồi mới tạo lệnh mới. Nếu mất cả DB/ledger, PROCESSING vẫn không được tự requeue; cần đối soát riêng.

## Phase 4 — H3: projection hóa đơn

Shared `BillSnapshotLines.Included` lọc line inclusion và included orders; LAN và Cloud cùng dùng helper. Các dòng excluded vẫn ở DB để audit. Projection gửi persisted ProductSubtotal/AdjustmentsTotal/GrandTotal. Worker migration **0006** lưu hai subtotal snapshots; mobile dùng snapshot thay vì cộng lại toàn bộ lines. Dữ liệu Cloud cũ chưa có snapshot dùng GrandTotal trừ adjustments làm fallback, không dùng các dòng có thể đã mất inclusion.

Acceptance: excluded line + excluded whole order, negative adjustment, Staff redaction, C# mapper→JSON và Worker SQL→mobile contract. Source historical lines vẫn được giữ nguyên. Phải re-project các bill cũ lên Cloud để sửa tập lines cũ mất inclusion; không suy đoán inclusion từ totals.

## Validation and runtime

**STATUS: COMPLETE cho implementation và áp dụng local.** C1/H1/H2/H3 đạt acceptance automated; review source/migration không còn lỗi actionable trong các scope đã sửa. Cloud production/hardware acceptance vẫn chưa hoàn tất, nên chưa đổi kết luận full production thành READY.

| Check cuối | Kết quả |
|---|---|
| Desktop Debug full suite, output thông thường | **557/557 PASS**, 0 skipped; `artifacts/blocker-fixes-tests/debug-final.trx` |
| Desktop Release targeted fixes + backup + thumbnail + HTTP | **65/65 PASS** |
| Build WPF Debug và Release | **PASS**, 0 warnings, 0 errors |
| Worker full suite | **46/46 PASS**, 8 files |
| TypeScript `tsc --noEmit` | PASS |
| Wrangler deployment dry-run | PASS, bundle 44.48 KiB; không deploy remote |
| D1 local migration 0006 | PASS |
| Desktop local startup | **PID 108676**, API `/api/status` trên 5050 thuộc đúng process/project; start sau DLL build cuối |
| SQLite database thật, chỉ đọc sau startup migration | Schema **22**, integrity `ok`, FK violations **0**, legacy reconciliation vẫn true; không reset/replay payment |
| Worker local restart/start | **PID 92232**, ownership health verified; `configuration_required`, chưa có local secrets |

Desktop/server cũ không còn chạy khi hoàn tất tests; đã start bằng executable Debug và normal local profile theo DEV_START, hidden/minimized. Không có task đang chạy nào được ghi nhận bị gián đoạn; không dừng process project khác. Full suite có register/unregister context-menu test; startup Desktop khôi phục integration theo cấu hình. Build frontend WPF nằm trong cùng process mới.

Evidence: `docs/review-evidence-20261003/local-runtime.json`, `local-database-check.json`, `blocker-fixes-source-hashes.json`. Manifest mới ghi rõ các path chưa có trong baseline, không suy rằng XAML cũ là file mới. Chỉ các file thuộc fixes/tests/docs được chỉnh; giữ nguyên dirty tree có sẵn. Git diff review còn ba trailing spaces pre-existing ở constructor backup/migration SQL ngoài các hunk sửa; không cleanup unrelated.

Một lỗi hồi quy guard đã được phát hiện và sửa trước nghiệm thu: checkbox Cloud mặc định true không đủ để coi DB đã bind. Test local restore giữ nguyên assertion. Một lỗi test baseline khác: mock thumbnail dùng List trong upload concurrency làm mất request; đổi sang ConcurrentQueue, giữ assertion đủ hai uploads.

Full Debug chạy ở custom OutDir từng thất bại ở các tests đọc XAML do chúng suy đường source từ vị trí binary. Không coi đây là product finding; chạy suite cuối ở output thông thường. Không sửa resource assertions để né lỗi.

Các run trung gian từng FAIL không được gọi là PASS: Release đầu tiên 1 fail ở local restore (guard đã sửa), lần Release tiếp theo 1 fail ở thumbnail mock concurrency (mock đã sửa). Debug cuối 557/557 xác minh toàn bộ fixes sau cả hai corrections. Không tuyên bố fresh full Release PASS; run Release cuối cùng cho các scope ảnh hưởng là targeted 65/65.

### Key files changed

- `src/LalabAutoReport.Infrastructure/Data/DatabaseLifecycleGuard.cs`, `DatabaseResetService.cs`, `DatabaseBackupService.cs`, `DataPurgeService.cs`: lifecycle guard/gate và high-water identity.
- `src/LalabAutoReport.Core/Domain/BillSnapshotLines.cs`, `src/LalabAutoReport.UI/Web/KestrelMobileWebServer.cs`: inclusion và Staff order labels.
- `src/LalabAutoReport.Infrastructure/Services/CloudSyncService.cs`, `Data/SqliteRemotePrintLedger.cs`, `Data/DatabaseMigrator.cs`: durable command effect/ACK và SQLite migration 22.
- `cloud/worker/src/{db,index,mobile-bill,types}.ts`, `migrations/0006_bill_snapshots_and_print_claims.sql`, `public/index.html`: atomic claim, authoritative subtotals và manual retry confirmation.
- `tests/LalabAutoReport.Tests/{CloudPrintRecoveryTests,CloudSyncServiceTests,DatabaseResetServiceTests,MobileWebServerTests,CloudMediaSyncServiceTests}.cs`, `cloud/worker/test/{print-recovery,api}.spec.ts`: regression coverage và thread-safe mock.

**Next recommended phase:** rollout Worker/Desktop production đồng bộ phiên bản và nghiệm thu máy in/điện thoại theo các bước dưới đây. Không cần viết thêm code để bỏ qua guard Cloud reset.

## Áp dụng Cloud production và nghiệm thu cần chủ tiệm tham gia

**Cập nhật sau khi user ủy quyền deploy:** Worker production, migrations 0004–0006 và cấu hình auth đã hoàn tất; health/login/V2/permissions đạt. Xem [CLOUD_PRODUCTION_DEPLOY_20261003.md](CLOUD_PRODUCTION_DEPLOY_20261003.md). Các bước bên dưới giữ mô tả rollout; migration/deploy/config đã thực hiện, còn reconciliation/re-projection và hardware acceptance.

Chưa deploy hoặc đổi D1 production trong batch này. Sau khi bản Desktop mới được chuẩn bị cho máy xưởng:

1. Tạm ngừng gửi remote-print và đóng các Desktop bản cũ đang sync Cloud. Kiểm tra tem/spooler và ghi nhận các lệnh in đang dở; không tự gửi lại.
2. Backup Desktop bằng chức năng sao lưu; backup D1 theo quy trình Cloudflare của xưởng. Giữ credentials hiện có, không cần đổi PIN/secrets cho các fixes này.
3. Tại `cloud/worker`, chạy `npx wrangler d1 migrations apply lalab-db --remote` để áp dụng 0006, sau đó `npx wrangler deploy`. Cần tài khoản Cloudflare đã đăng nhập và được quyền database/Worker này. Đây là thao tác production do chủ tiệm/operator thực hiện hoặc ủy quyền riêng.
4. Khởi động Desktop mới, kiểm tra `/api/health` Cloud báo `configurationReady: true`, rồi đồng bộ lại các hóa đơn cần nghiệm thu. SyncAll hiện chỉ lấy recent/unpaid; hóa đơn cũ đã trả ngoài phạm vi cần sync từng bill qua workflow xuất lại/SyncBill, không quét filesystem để sửa projection.
5. Đăng nhập Staff trên điện thoại, preview tem của order đã gắn bill: không có tiền/QR thanh toán. Đăng nhập Admin: bill preview còn đúng tiền.
6. Với bill fixture có included 75.000 và excluded 25.000, kiểm tra Desktop/JPEG/LAN/Cloud cùng hiển thị dòng included và subtotal 75.000; thêm adjustment -5.000 thì total 70.000.
7. In một tem thử trên đúng printer/stock. Thử mất mạng sau gửi job bằng môi trường thử: không nhận thêm tem tự động; khi có cảnh báo không rõ, kiểm tra máy in trước khi tạo command mới. Không dùng hóa đơn khách thật để thử reset hoặc sửa payment.

Không bật remote-print với Desktop cũ sau khi Worker mới đã yêu cầu claim token. Desktop mới gặp Worker cũ sẽ không spool khi claim chưa được xác nhận. Migrate→Worker→Desktop rollout và re-projection là điều kiện để xác nhận Cloud production đã cập nhật.
