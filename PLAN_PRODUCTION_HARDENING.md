# Kế hoạch sửa lỗi trước production — Lalab Auto Report

Ngày lập: 2026-10-02 (UTC+7).

**Trạng thái: PLAN ONLY — chưa viết code.** Thực hiện tuần tự, mỗi lần giao coding agent một phase. Không triển khai cả kế hoạch trong một batch.

## 1. Mục tiêu và nguồn quyết định

Khắc phục các lỗi correctness, data integrity, security và recovery trong final review của cuộc trò chuyện; sau đó nghiệm thu lại bản release. Không refactor rộng, không thêm feature, không sửa business rules để làm test pass.

`FINAL_REVIEW.md` trên đĩa đã được tác vụ khác thay bằng báo cáo READY với 3 mục resolved. Code cũng có thay đổi sau review ban đầu. **Không coi bản READY đó là bằng chứng tất cả 8 finding đã hết, và không coi mọi finding cũ còn nguyên.** Phase 0 phải xác định trạng thái từng finding bằng code và reproducer hiện tại.

Áp dụng `agents.md`, `DEVELOPMENT_WORKFLOW.md`, `ARCHITECTURE.md`, PLAN V2 ở `docs/archive/` và `ux_ui.md`. Giữ quantity V2 theo Effective Billing Folder; không khôi phục source/print mismatch của V1. Bảo vệ cả CustomerBill và GuestBill, không chỉ legacy Bill.

Mã finding dùng trong kế hoạch là mã từ review trước trong cuộc trò chuyện:

| ID | Vấn đề | Phase chính |
|---|---|---|
| C1 | Bill Locked/Exported bị sửa qua rescan hoặc edit/export không reopen | 1; kiểm tra lại ở 3 và 10 |
| C2 | Hai root cùng relative path bị gộp Order | 2 |
| H1 | Enumeration lỗi nhưng vẫn auto-resolve từ phần đọc được | 3 |
| H2 | Cloud checkpoint bỏ event chưa tải/chưa áp dụng | 8–9 |
| H3 | Worker fallback secret/PIN đã biết trong source | 5 |
| H4 | Staff ghi payment trên Cloud/đọc financial trái hợp đồng LAN | 6 |
| H5 | Split commit nhiều bước, failure để order thuộc hai bills | 4 |
| H6 | Cloud bill response không khớp PWA viewBill | 7 |

Các cải tiến ngoài 8 finding, như persistent LAN HMAC key được thêm sau review, chỉ cần kiểm tra regression; không mở lại một phase triển khai mới nếu đã đạt.

## 2. Nguyên tắc thực hiện

1. Mỗi phase bắt đầu bằng đọc code hiện tại và các thay đổi có từ phase trước. Phần đã được sửa và có bằng chứng thì kiểm chứng, không viết lại.
2. Tạo baseline và lưu báo cáo ở file riêng. Không ghi đè kết luận của người/tác vụ khác, không tự sửa source dirty không liên quan.
3. Dùng fixture, DB tạm và HTTP doubles; không thử destructive/recovery trên DB khách hàng hay printer production.
4. Với schema thay đổi: migration mới, upgrade test từ schema trước, chạy lại migration không thay dữ liệu, foreign-key/integrity checks. Không xóa DB để upgrade.
5. Test behavior lỗi trước hoặc cùng fix. Test phải gọi service/repository thực; doubles không được che pagination, transaction hoặc permission semantics cần kiểm tra.
6. Chỉ hoàn thành phase khi targeted tests, broader relevant tests, build và review diff đạt. Nếu foundation fail: dừng phase sau, ghi nguyên nhân.
7. Không tự sửa lịch sử đã có thể bị sai. Phân tích/read-only reconciliation và backup trước; sửa data cũ phải có phạm vi và lựa chọn người dùng rõ ràng.
8. Sau phase đổi code/runtime, restart đúng server project theo AGENTS: xác minh executable/command line/path sở hữu PID; không kill theo tên chung hoặc chỉ port. Backend nằm trong WPF nên nếu không restart riêng được thì restart app đúng project. Kiểm tra process mới sau build và `/api/status` nếu mobile server bật, kèm log startup/version. Ghi task bị gián đoạn nếu biết. Không báo ready khi restart/health fail.
9. Không deploy cloud/đổi secrets remote/release chỉ để kiểm tra phase. Chuẩn bị đầy đủ artifact và validation trước bước publish. Nếu chưa có ủy quyền deploy trong phiên implementation, xin duyệt kết quả cụ thể ở bước cuối.

## 3. Quyết định nghiệp vụ

Các quy tắc đã rõ, không cần hỏi lại:

- Locked/Exported là snapshot lịch sử; cập nhật tiền/số lượng phải qua reopen có lý do.
- Payment/delivery/note là dữ liệu vận hành; thay đổi chúng không có nghĩa được sửa snapshot số tiền.
- Hai root khác nhau chứa cùng relative path vẫn là hai orders.
- Quan sát filesystem thiếu thì cần review/retry, không được đoán nhánh billing.
- Staff không được thu/hủy thu hoặc đọc dữ liệu tiền vốn dành cho Admin.

**Đã chốt với người dùng:** Desktop và điện thoại cùng sửa trạng thái thì **thay đổi mới nhất theo phiên bản server xác nhận thắng**. Server cấp phiên bản tăng dần khi chấp nhận thay đổi; không so giờ máy khách. Áp dụng trên từng field vận hành (payment, delivery, note), không dùng phiên bản mới để sửa snapshot tiền/số lượng lịch sử.

Một thao tác offline chưa được server xác nhận là pending. Khi gửi lại và được server chấp nhận sau một thao tác mobile, nó có thể trở thành phiên bản mới nhất dù thời điểm người dùng nhập sớm hơn. UI phải phân biệt pending với confirmed; retry cùng operation ID không được tạo phiên bản hoặc effect mới. Batch đồng bộ projection thông thường không được giả làm thao tác vận hành mới để ghi đè state server.

## 4. Thứ tự và phụ thuộc

```text
0 Baseline
→ 1 Bảo vệ lịch sử
→ 2 Identity theo root
→ 3 Scan lỗi + xác minh trước lock
→ 4 Split atomic
→ 5 Cloud secrets
→ 6 Phân quyền
→ 7 API/PWA contract
→ 8 Worker event cursor
→ 9 Desktop sync/recovery
→ 10 Release acceptance
```

Phase 5–7 không cần sửa billing/scanner. Phase 8–9 là một protocol upgrade chia hai bước có tương thích; không bật protocol mới khi mới hoàn thành một nửa. Dù có các phần độc lập, mặc định vẫn triển khai tuần tự để tránh đụng working tree.

## Phase 0 — Chốt baseline và xác minh phần đã sửa

**Mục tiêu:** Có danh sách OPEN / PARTIALLY FIXED / VERIFIED FIXED / NOT VERIFIED chính xác cho 8 finding, trước khi coding.

**Phạm vi:** Documentation, bằng chứng và test fixture ngoài source nếu cần; không fix app.

**Công việc:**

- Chụp HEAD, git status/diff, SDK/package versions và hash các critical files; nhận diện các changes có sẵn.
- Lưu bản review gốc từ cuộc trò chuyện và bản review READY hiện trên đĩa thành hai evidence riêng có thời gian; không trộn chúng.
- Đọc guard mới trong ScanService, CustomerBillingService, review UI và split, persistent MobileAuthSecret. Kiểm tra cả service/repository/UI path; không chỉ nhìn test count.
- Chạy Debug/Release desktop suite, Worker tests/typecheck; phân biệt assertion failure với host/MSBuild abort. Nếu môi trường vẫn có build lock, xác minh project ownership trước mọi can thiệp process.
- Chạy lại các reproducer có sẵn bằng fixture: historical rescan/direct rewrite, two roots collision, unreadable final branch, split failure, default JWT, Staff payment, event 501, event apply failure, bill DTO.
- Ghi result mỗi case và đường code đang gây lỗi vào `PRODUCTION_FIX_BASELINE.md`; lập checklist `PRODUCTION_FIX_PROGRESS.md`.

**Nghiệm thu:** Mỗi ID có evidence hiện tại và phase cần làm; không chỉ dựa trên `FINAL_REVIEW.md` hoặc 495 tests được báo trong tài liệu. Không tuyên bố runtime production ready.

**Phụ thuộc:** Không có.

## Phase 1 — Bảo vệ toàn bộ đường ghi bill lịch sử

**Mục tiêu:** Snapshot tiền/số lượng/provenance của Locked/Exported giữ nguyên khi scan, edit, refresh, re-export hoặc retry chưa reopen.

**Files chính:** `CustomerBillingService`, `ScanService`, `SqliteCustomerBillRepository`, `CustomerBillReviewViewModel`, review/history views.

**Công việc:**

- Giữ fix rescan mới nếu đúng; hoàn thiện guard trong đường refresh/sync/edit/lock và repository write. Kiểm tra status thực trong DB, không tin object stale của UI.
- Phân biệt phép ghi nội dung financial với metadata được phép: payment, export location/time và warning filesystem. Không dùng một guard chung làm hỏng record export hoặc toggle payment hợp lệ.
- View bill lịch sử read-only; re-export lấy snapshot, không rescan/recalculate hoặc gọi lock lại với object đã sửa.
- Reopen tường minh có lý do; UI refresh từ DB sau reopen và sau conflict. Không silently reopen bằng hành động xem bill.
- Warning so sánh **scan observation snapshot** (count, selected folder, job structure), không so current file count với BilledQuantity override. Số lượng sửa tay hợp lệ không tự gây warning giả.

**Tests bắt buộc:** Locked và Exported; customer và guest; rescan thêm/xóa ảnh/đổi nhánh; đổi bảng giá; quantity override; edit/save stale object; re-export; payment vẫn cập nhật được; reopen có lý do rồi sửa được.

**Nghiệm thu:** So trước/sau các fields snapshot trong DB phải giống nhau khi chưa reopen; đường UI và API/service trực tiếp đều không vượt guard. Có warning thật, không warning giả vì override.

**Không làm:** Sửa dữ liệu lịch sử đã bị mutate; redesign billing/export; thay đổi quy trình thu tiền.

**Phụ thuộc:** 0.

## Phase 2 — Identity filesystem theo root từ đầu đến cuối

**Mục tiêu:** Không gộp orders hoặc thao tác nhầm kho khi relative path trùng nhau.

**Files chính:** Scanner interfaces/services, order/root repositories, dashboard/billing/idle/thumbnail/print-status callers liên quan.

**Công việc:**

- Chốt identity `(RootFolderId, RelativePath)`; truyền root hoặc order ID đến scoped rescan thay vì tự đoán kho từ relative path.
- Loại fallback sang order của root khác; legacy row chưa có root chỉ được map theo quy tắc migration xác định và kiểm tra ambiguity.
- Rà soát các thao tác đọc filesystem dùng order path: bill refresh, spec rescan, thumbnail, open-folder, print markers và Explorer command. Absolute path phải được map về đúng root; không tự chọn root đầu tiên.
- Giữ đường dẫn tương đối và ID order ổn định; khi kho offline không chuyển sang kho khác cùng relative path.
- Lập read-only inventory dữ liệu có dấu hiệu root collision; chưa tự split/reconstruct lịch sử đã mất.

**Tests bắt buộc:** Hai active roots cùng ngày/khách/product path với count khác; scoped rescan một root; root offline; inactive/archive root; same-path legacy rows; printed/thumbnail thao tác đúng kho.

**Nghiệm thu:** Hai physical orders có hai ID và hai root IDs, bill provenance tách biệt. Rescan một order không đổi order bên kia. Upgrade không đổi identity đã hợp lệ.

**Không làm:** Reorganize thư mục xưởng; tự chữa historical collisions; thay cấu trúc product registry.

**Phụ thuộc:** 1 để tránh rescan test làm sửa lịch sử.

## Phase 3 — Scan thiếu dữ liệu phải được review; xác minh trước lock

**Mục tiêu:** Không tính tiền hoặc prune dữ liệu dựa trên filesystem observation không đầy đủ.

**Files chính:** `IFileSystemAdapter`, `PhysicalFileSystemAdapter`, parser/resolver/scanner, billing lock flow, review states.

**Công việc:**

- Truyền rõ enumeration success/partial/error; phân biệt empty directory với access denied, path disappeared và I/O failure.
- Scope bị lỗi không được auto-resolve từ nhánh còn đọc được; surface path/lỗi và scoped retry. Không biến lỗi đọc thành zero count hợp lệ.
- Date discovery thiếu dữ liệu không được prune orders như thể thư mục đã xóa. Bảo toàn last-known observation và snapshot history.
- Customer/guest bill lock cần fresh verification ở đúng root/scope; nếu filesystem đổi từ lúc review thì yêu cầu review lại trước lock. Giữ overrides có lý do, không tự sửa số lượng người dùng đã chọn.
- Không làm traversal trên WPF UI thread; giữ cancellation và scoped scan.

**Tests bắt buộc:** Không đọc được deepest leaf; một trong hai nhánh lỗi; lỗi enumerate date/customer; path biến mất giữa scan; empty folder thật; retry thành công; stale draft trước lock; overrides và Locked/Exported bất biến.

**Nghiệm thu:** Fixture 18 source/30 final không đọc được phải NeedsReview/error, không Ready 18. Không xóa DB orders vì lỗi đọc. Lock không dùng quan sát stale/incomplete. Unaffected orders vẫn dùng được.

**Không làm:** Thêm watcher/polling; thumbnail/image decoding vào count; quét lại toàn tháng để lock một order.

**Phụ thuộc:** 1–2.

## Phase 4 — Split bill atomic và retry an toàn

**Mục tiêu:** Split hoàn thành toàn bộ hoặc không thay đổi gì trong DB.

**Files chính:** Billing interface/service, customer bill repository, review UI, SplitBill tests.

**Công việc:**

- Giữ guard Locked/Exported đã thêm và kiểm tra lại status DB khi split.
- Một repository transaction bao gồm tạo bill mới, chuyển orders/lines/source folders, cập nhật bill cũ và job links. Không commit qua ba repository calls độc lập.
- Validate order membership hiện tại trong transaction; request stale hoặc repeat không tạo thêm bill.
- Giữ totals/adjustments/provenance đúng quy tắc hiện có; UI chỉ reload/show success sau commit.

**Tests bắt buộc:** Failure sau insert new bill, trong save original, trước job-link update; successful split; stale request/retry; Locked/Exported bị chặn; foreign-key check.

**Nghiệm thu:** Reproducer trigger failure trả lỗi nhưng không để new bill hoặc duplicate membership; reconnect/restart đọc cùng trạng thái trước split. Successful split giữ tổng và provenance đúng.

**Không làm:** Tách bill đã thanh toán mà không reopen; gộp/rebuild data cũ; đổi discount allocation ngoài requirement hiện tại.

**Phụ thuộc:** 1–3. Đây là gate hoàn tất nhóm local fixes trước cloud.

## Phase 5 — Cloud auth fail closed và cấu hình secrets đầy đủ

**Mục tiêu:** Worker thiếu secrets không chấp nhận default credentials hay token ký bằng source-known key.

**Files chính:** Worker auth/index/types, auth tests, deployment README/config validation.

**Công việc:**

- Bắt buộc JWT_SECRET/SYNC_SECRET, không fallback sang constant; bootstrap PIN rõ ràng, không có PIN production mặc định.
- Validate token role/expiry/payload và cấu hình cần thiết; thiếu cấu hình trả lỗi vận hành rõ ràng, không cấp quyền.
- Bổ sung hướng dẫn setup, rotation và invalidation phiên cũ; không log secret/token.
- Kiểm tra tương tác với LAN persistent HMAC và QR/login: hai token formats khác nhau không được gắn LAN token vào Cloud URL rồi kỳ vọng Worker chấp nhận. Dùng login Cloud hiện có; chưa thêm SSO.

**Tests bắt buộc:** Secret missing/blank; forged default-key token; valid configured token; expiry/role invalid; sync secret missing/wrong; remote URL/QR dẫn đúng luồng login.

**Nghiệm thu:** Default-key Admin token và default sync secret đều bị từ chối; deployment checklist bắt buộc JWT secret. Không thay secret remote trong phase này.

**Phụ thuộc:** 0; triển khai sau 4 theo thứ tự mặc định.

## Phase 6 — Phân quyền LAN và Cloud thống nhất

**Mục tiêu:** Staff không vượt quyền financial qua request trực tiếp hoặc ảnh bill.

**Files chính:** Kestrel endpoints, Worker routes, auth/permission tests, PWA display điều kiện quyền.

**Công việc:**

- Viết permission matrix ngắn cho Admin/Staff; áp dụng guard backend tương đương trên payment, financial detail/reports/debts và các media chứa tiền.
- Admin payment read/write; Staff dùng workflow order/production được cho phép. Guard không chỉ là hide button.
- Xác định bill image/label/QR có nội dung financial; redaction hoặc deny theo cùng quy tắc, không tạo đường đọc gián tiếp.

**Tests bắt buộc:** Không auth 401; Staff payment 403; Staff không nhận số tiền qua JSON/media; Admin thao tác hợp lệ; delivery/note/label không financial của Staff vẫn hoạt động nếu được phép.

**Nghiệm thu:** Matrix chạy trên cả LAN và Cloud; direct API và UI đồng nhất. Thay đổi Staff không làm mất quyền order workflow hợp lệ.

**Không làm:** Role system mới, tài khoản nhiều người, auth redesign.

**Phụ thuộc:** 5.

## Phase 7 — Contract bill detail dùng được trên cả LAN và Cloud

**Mục tiêu:** PWA hiển thị đúng tên khách, ngày, total và line items khi chuyển môi trường.

**Files chính:** Worker DTO mapping, PWA `viewBill`, cloud projection mapper; contract/UI tests.

**Công việc:**

- Chọn response contract theo API LAN/PWA đang dùng; map cloud storage projection tại API boundary hoặc adapter rõ ràng. Không đổi D1 storage schema chỉ để rename response nếu không cần.
- Đồng bộ nullable financial fields với phase 6; không render `undefined` hoặc gọi format trên field không tồn tại.
- Bổ sung response contract tests cùng dữ liệu ở LAN/Cloud, và browser/render acceptance bill detail.
- Kiểm tra tên/ngày/tổng, ảnh JPEG available/unavailable, empty lines/adjustments và quyền Staff.

**Nghiệm thu:** Bill 75.000 hiển thị đúng customer/date/total ở hai môi trường; Staff redaction đúng, không phá list debts/orders hoặc payment buttons.

**Không làm:** Redesign mobile UI, đổi monetary projection trong DB, thêm tính năng report mới.

**Phụ thuộc:** 6.

## Phase 8 — Worker event cursor và protocol tương thích

**Mục tiêu:** API pull có checkpoint ổn định, phân trang không mất event và nâng cấp an toàn với Desktop cũ.

**Files chính:** Worker db/index/types, D1 migration nếu thật sự cần, protocol tests/docs.

**Công việc:**

- Pull theo event ID tăng dần; trả `nextCursor` là ID cuối trong page, `hasMore` và limit hữu hạn. Server time chỉ dùng thông tin, không làm cursor.
- Event có identity/type/version rõ ràng; unknown type không bị silent skip. Không xóa event cần cho retry/recovery.
- Server xác nhận thao tác vận hành của cả Desktop và mobile theo cùng cơ chế revision tăng dần; lưu operation ID để retry idempotent. Tách ingest projection với mutation vận hành: push toàn bộ bản sao không được tự nâng revision hoặc ghi đè field đã có revision mới hơn. Có thể dùng event ID server làm nguồn revision nếu bảo đảm thứ tự commit và tính atomic; không cần thiết kế một sync engine mới.
- Thêm protocol version hoặc endpoint mới; giữ legacy route khi cần, nhưng không coi Desktop cũ là đã đạt safety của protocol mới.
- Chuẩn bị compatibility matrix/deployment order. Không bật hoặc deploy nửa protocol.
- Test bằng DB local/D1 emulator hoặc double thực thi đúng WHERE/ORDER/LIMIT; không dùng mock trả mọi row che bug.

**Tests bắt buộc:** 0, 1, 500, 501 và nhiều pages; events cùng timestamp; event mới xuất hiện trong lúc phân trang; malformed cursor; paging retry; client cũ/new route behavior.

**Nghiệm thu:** 501 events được nhận đủ 501, mỗi ID không bị mất do timestamp; response cursor không vượt page. Có kế hoạch compatibility kiểm chứng được cho phase 9.

**Không làm:** Remote DB reset, event retention purge, deploy Worker production.

**Phụ thuộc:** 5–7.

## Phase 9 — Desktop áp dụng event/checkpoint atomic, bảo vệ push và recovery

**Mục tiêu:** Event đã nhận không mất khi SQLite lỗi/crash; retry/restart không làm sai payment/delivery/note.

**Files chính:** CloudSyncService, dedicated sync-state/event repository, SQLite migration, sync UI/logging/tests.

**Công việc:**

- Kéo hết các pages; transaction cho event application + applied-event ID/checkpoint. Dùng update state trực tiếp với event ID, không replay toggle.
- Không advance qua failure/unknown event chưa có durable retry; giữ lỗi user-readable và retry có giới hạn. Checkpoint ghi riêng, không SaveSettings toàn object stale làm ghi đè setting vừa đổi.
- Serialize timer/manual/startup sync qua một gate; không chạy pull/push chồng nhau.
- Pull-before-push là điều kiện thành công, không chỉ thứ tự gọi: pull thất bại phải chặn push operational state stale. Trường hợp mobile ghi giữa pull và push cần version/precondition phía Worker để không overwrite change chưa Desktop áp dụng.
- Thực hiện policy đã chốt: revision server mới nhất thắng theo từng field; apply response/event chỉ khi không cũ hơn revision đã lưu. Local change offline có operation ID bền vững và trạng thái pending; khi reconnect gửi qua đường mutation được server xác nhận, không trộn vào snapshot push. Retry không cấp revision mới cho cùng operation ID.
- Upgrade từ LastCloudPullAt cũ không được giả định các event trước đó đã áp dụng đầy đủ. Có read-only reconciliation cho events có khả năng bị skip; không reset cursor/replay cả lịch sử lên data thật một cách tự động.
- Restart/failure không sửa snapshot bill; chỉ các field vận hành được phép.

**Tests bắt buộc:** SQLite write failure; crash boundary trước/sau event commit; duplicates; process restart; concurrent timer/manual sync; settings đổi khi pull đang chạy; 501 events; offline→online; pull fail→push blocked; mobile write trong cửa sổ pull/push; revision cũ đến sau revision mới; clocks Desktop/mobile lệch nhau; retry operation không cấp revision mới; unknown event; migration cursor cũ.

**Nghiệm thu:** Cùng event có effect một lần và retry được; checkpoint bằng ID cuối đã commit an toàn. Failed event không mất. Snapshot push hoặc event cũ không overwrite phiên bản mới; chỉ mutation mới được server xác nhận mới thay phiên bản đó. UI phân biệt pending/confirmed và báo thật khi sync không hoàn tất.

**Không làm:** Full cloud sync rewrite, auto repair payment lịch sử, push recovery không kiểm tra conflict.

**Phụ thuộc:** 8; policy xung đột đã được người dùng xác nhận.

Nếu compatibility và recovery không thể nằm trong một batch nhỏ, chia phase này thành **9A event/checkpoint** và **9B concurrency/push/reconciliation**, nghiệm thu riêng; không bỏ 9B để báo H2 hoàn tất.

## Phase 10 — Nghiệm thu toàn tuyến và quyết định release mới

**Mục tiêu:** Có kết luận readiness dựa trên bản code/artifact hiện tại, không dựa trên test count hoặc review cũ.

**Phạm vi:** Tests, build/package, isolated runtime acceptance, tài liệu kết quả; chỉ sửa defect trong phạm vi đã biết nếu phát hiện, rồi chạy lại gate tương ứng.

**Công việc:**

- Chạy full Debug/Release suites, Worker tests/typecheck/bundle và publish Windows đúng configuration phát hành.
- Nghiệm thu trên môi trường/data fixture tách khỏi DB người dùng: startup, shutdown, restart, single-instance/IPC, LAN login qua restart, context menu, customer/guest bill, overrides, history/re-export/reopen, multi-root, recovery, mobile/cloud contract và permission matrix.
- Chạy lại toàn bộ 8 reproducer; không chỉ chọn tests mới.
- Kiểm tra export output thực, printer acceptance nếu có thiết bị, cloud offline/reconnect và dataset/NAS đại diện nếu có.
- Kiểm tra release version/tag/assembly thống nhất; smoke standalone exe; updater download/apply/failure recovery trên bản sao executable/data fixture. Không chạy updater thay binary đang phục vụ người dùng.
- Backup/restore/upgrade từ DB fixture có lịch sử locked và payments; kiểm tra FK/integrity và totals trước/sau.
- Remote deploy/release chỉ sau local acceptance, artifact/release notes/rollback plan sẵn sàng và có authorization. Secret values không xuất hiện trong evidence.
- Viết `FINAL_REVIEW_VERIFIED.md` với READY / READY AFTER REQUIRED FIXES / NOT READY; cập nhật progress/status với evidence thật. Tránh ghi đè report cũ không lưu lại.

**Nghiệm thu:** Không còn OPEN blocker trong phạm vi phát hành; các flow thực tế có PASS/FAIL/NOT TESTED rõ ràng; build/version/runtime phù hợp. Nếu printer/cloud/updater acceptance cần thiết chưa chạy thì ghi limitation và phạm vi dùng được, không tự kết luận READY toàn bộ.

**Phụ thuộc:** Tất cả phase cần thiết đã verified; không chỉ phase đã viết code.

## 5. Deliverable chuẩn của mỗi phase

Coding agent báo ngắn:

```text
STATUS: COMPLETE | PARTIAL | BLOCKED
PHASE: <number + objective>
FINDINGS ADDRESSED: <IDs; còn gap gì>
CHANGED FILES: <list>
VALIDATION: targeted / relevant suite / build / acceptance evidence
MIGRATION: none | version + upgrade/idempotency result
RUNTIME: old/new owned PID, restart + health/version result
LIMITATIONS: current defects / NOT TESTED / pending decisions
NEXT PHASE: number; chỉ tiếp tục khi gate đạt
```

## 6. Prompt dùng cho từng lần giao coding agent

```text
Đọc PLAN_PRODUCTION_HARDENING.md và PRODUCTION_FIX_PROGRESS.md.
Thực hiện CHỈ Phase <N>.
Trước khi sửa, kiểm tra code và thay đổi có sẵn; bảo toàn fix đã verified.
Không refactor/sửa phase khác hoặc tự sửa production data.
Thực hiện behavior end-to-end trong phạm vi phase, migration nếu cần và tests lỗi quan trọng.
Chạy targeted tests, broader relevant suite, build, review diff.
Restart đúng local project runtime theo AGENTS và verify process/health đang chạy code mới.
Cập nhật progress bằng evidence; báo kết quả theo deliverable của kế hoạch.
Dừng khi phase đạt hoặc có blocker; không tự bắt đầu phase kế tiếp.
```

**Bước triển khai đầu tiên: Phase 0.** Kế hoạch này chưa cho phép coi các sửa đổi sau review là đủ production-ready và chưa thực hiện coding/deploy/restart.
