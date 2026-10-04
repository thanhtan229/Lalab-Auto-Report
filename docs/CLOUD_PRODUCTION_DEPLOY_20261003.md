# Cloud production deployment — 2026-10-03

**STATUS: COMPLETE — Worker production và migrations đã triển khai, HTTP verification đạt.**

Domain: https://lalab.tinix.io.vn. Code deployment version `681ef6eb-3938-49bc-a865-b53f433e61a0`; active version sau cấu hình secrets `982d60eb-a645-49d6-a452-21c3868b1178`, 100% traffic. Deployment xác minh lúc khoảng 08:33 UTC+7.

## Backup và migration

- Backup trước deploy: `artifacts/cloud-production-predeploy-20261003.sql`, SHA256 `87bad586efebe2d63d2fc290a12bcb57d5e51933053df6be49447f940655d30e`.
- Time Travel bookmark trước thay đổi: `0000001e-00000048-000050f9-18f9442aee9f2dfbb91e35f8ab948c73`.
- Previous Worker version: `c86f8c78-c523-4d39-8b15-f70abe8590e3`. Không rollback tự động; rollback operational protocol phải xét tương thích Desktop/D1 trước.
- D1 có schema 0003 nhưng ledger chỉ ghi 0001–0002. Đã load export vào SQLite và so sánh schema `cloud_orders`, `cloud_heartbeats`, `cloud_print_commands` và indexes với migrations thật 0001–0003: khớp. Chỉ sau đối chiếu mới thêm ledger entry 0003; không chạy lại các ALTER trùng.
- Đã áp dụng migrations **0004, 0005, 0006** qua Wrangler, tất cả thành công. Ledger cuối đủ 0001–0006.
- Hàng đợi print không có command trước/sau. Không gửi lệnh in thật, không sửa payment/delivery/note để smoke-test.

## Configuration

Production trước deploy chỉ có SYNC_SECRET. Health mới phát hiện thiếu JWT_SECRET/Admin PIN. Giữ nguyên SYNC_SECRET; tạo JWT_SECRET ngẫu nhiên mạnh và cấu hình ADMIN_PIN/STAFF_PIN bằng hai PIN hiện có của Desktop, đã kiểm tra distinct. Secrets chỉ truyền qua memory/stdin, không lưu giá trị vào evidence hoặc source.

## Validation

- Fresh Worker full suite **46/46 PASS**; `tsc --noEmit` PASS; actual Wrangler deploy PASS.
- Health HTTPS **200**, `status: ok`, `configurationReady: true`.
- Admin/Staff login thành công, đúng role; không lưu token vào report.
- Authenticated `/api/sync/v2/pull` **200**, protocol **2**, stream ID có mặt.
- Staff financial image **403**; unauthenticated bill **401**.
- Legacy ACK không claim token **400**. Claim ID không tồn tại **409**; không tạo command hay effect.
- PWA production có confirmation mới yêu cầu kiểm tra máy in trước retry.
- Export sau deploy load được, SQLite integrity `ok`. D1 API không cho `PRAGMA integrity_check` (SQLITE_AUTH), nên không tuyên bố chạy pragma trực tiếp trên D1; verification integrity là trên exported snapshot.
- So sánh mọi cột có trước migration: orders/bills/events/print commands/reports giữ nguyên, số lượng **18/20/8/0/3**. Tổng bill **9.615.000**, paid count **1** giữ nguyên. Chỉ `cloud_heartbeats.last_heartbeat_at` đổi bởi heartbeat Desktop bình thường. Existing sync metadata giữ nguyên; bổ sung event stream theo migration 0005.
- Desktop local PID **108676** vẫn đúng executable project, API 5050 ownership verified. Không sửa code/config local trong batch deploy, không cần restart local lần nữa. Worker local đã restart ở batch sửa trước; không có restart production ngoài actual Worker deployment và secret configuration được user cho phép.

Evidence: `docs/review-evidence-20261003/cloud-deployment/{predeploy,deployments,postdeploy-d1,http-verification,data-preservation}.json`. Export SQL chứa dữ liệu khách, giữ local trong artifacts; không đưa nội dung vào chat.

## Bước còn lại cho chủ tiệm

Deploy thành công không tự phê duyệt các quyết định nghiệp vụ cũ. Database Desktop hiện `requires_reconciliation=true`; giữ nguyên gate này, chưa replay hoặc override Cloud payments.

1. Desktop → **Cài đặt → Cloud → Xuất báo cáo đối soát**, lưu JSON.
2. Kiểm tra từng field local/server; đổi `resolution` từ `unreviewed` sang `server` hoặc `desktop` theo quyết định đúng. Giữ các thông tin còn lại nguyên vẹn. Hướng dẫn chi tiết tại `docs/CLOUD_PROTOCOL_V2.md`.
3. **Nhập báo cáo đã duyệt**, rồi **Đồng bộ ngay**. Nếu chưa chắc giá trị, không chọn hàng loạt. Lựa chọn Desktop vẫn pending đến khi server xác nhận revision.
4. Sync/re-project các bill cần dùng để bổ sung inclusion/subtotal snapshots mới. Recent/unpaid được SyncAll lấy; bill cũ đã trả ngoài scope cần SyncBill/export lại qua workflow tương ứng. Không suy inclusion của dữ liệu Cloud cũ bằng cách sửa totals.
5. Refresh/đăng nhập lại PWA bằng PIN Admin/Staff hiện có trong Desktop; nghiệm thu bill/label và một tem thử trên máy in thật. Physical printer và mất mạng ở spooler chưa được nghiệm thu trong deploy này.

Không dùng Desktop cũ gửi remote-print sau rollout claim protocol. Full production acceptance vẫn cần đối soát/re-projection và nghiệm thu thiết bị; Worker deploy riêng đã hoàn thành.

## Tiếp tục rollout — đối soát và re-projection đã hoàn tất

Sau yêu cầu tiếp tục, đã backup SQLite local qua online backup API và xuất báo cáo bằng `CreateReconciliationReportAsync` thật. Report ID `5c030ac91bfe4e9d9624f3320ac9c581`: **56/56 field local/server trùng nhau**, không entity thiếu; đối chiếu thêm order code/date/folder và bill number/customer/total đều khớp. Áp dụng `server` cho cả 56 fields theo lựa chọn user ưu tiên phiên bản server xác nhận; không giải quyết conflict bằng suy đoán vì không có conflict. Báo cáo gốc/quyết định được lưu trong DB.

`ApplyReviewedReconciliationAsync` kiểm tra lại live stream trước apply; checkpoint cursor **8**, `requires_reconciliation=0`. SyncAll thật thành công: **18 orders, 20 bills** trong scope; SyncBill lần lượt refresh **36 bills local**, gồm historical paid ngoài scope recent/unpaid. Không gọi printer, scanner hoặc sửa payment toggle.

Export D1 sau sync được so sánh với SQLite: **36/36 snapshot ProductSubtotal/AdjustmentsTotal/GrandTotal/IsPaid khớp**, tập included line IDs khớp từng bill, **0 mismatches**. Cloud có 18 orders/36 bills/8 events, local outbox **0 pending**, exported integrity `ok`. 16 bills lịch sử được bổ sung projection; không phát sinh operational event trong đối soát này.

Desktop đã restart để nạp trạng thái reconciliation mới: PID **108676 → 92560**, đúng executable/project, API 5050 ownership verified. Không có active workshop task được ghi nhận bị gián đoạn. Source app không đổi trong bước rollout này; dùng Debug/Release builds và test results đã xác minh ở batch fixes, không tuyên bố vừa chạy lại full suite. Helper runner nằm dưới ignored artifacts, chỉ gọi application/repository services thật.

Evidence: `reconciliation-verification.json`, `desktop-after-reconciliation.json` trong thư mục evidence deployment; backups/reports local tại `artifacts/cloud-reconciliation-20261003/`. Các bước 1–4 phía trên đã hoàn tất. Còn bước đăng nhập/refresh trên điện thoại và nghiệm thu máy in thật; không cần tự chỉnh JSON đối soát nữa.
