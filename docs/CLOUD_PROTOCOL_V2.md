# Đồng bộ vận hành V2

Server cấp revision bằng ID sự kiện khi transaction chấp nhận payment/delivery/note. Revision mới nhất thắng trên từng field; giờ Desktop/điện thoại không quyết định thứ tự. Offline là pending, chỉ confirmed sau phản hồi server. Retry cùng operation ID trả lại sự kiện gốc; dùng lại ID với nội dung khác bị 409.

`GET /api/sync/v2/pull?cursor=0&limit=500` dùng `X-Sync-Secret`, trả `protocolVersion:2`, `events`, `nextCursor` (ID cuối trang), `hasMore`. Cursor chỉ tiến sau transaction áp dụng thành công. Unknown/malformed event phải chặn và báo lỗi. Server time không dùng làm checkpoint.

`POST /api/sync/v2/operations` nhận `{operationId,entityType,entityId,value,deliveredBy?}`. Types: `bill_payment`/`order_delivered` (boolean), `order_note` (string). Desktop gửi giá trị cuối, không toggle. Mobile toggle cũ chuyển qua cùng transaction và dùng `X-Operation-Id`; PWA giữ ID chưa xác nhận trong localStorage để thử lại.

`/api/sync/batch` cập nhật projection, chỉ bootstrap operational fields khi INSERT entity mới. UPDATE không ghi đè payment/delivery/note, không cấp revision. Server events không bị purge.

| Worker | Desktop | Kết quả |
|---|---|---|
| Cũ | V2 | Chặn push khi không đàm phán được V2 |
| V2 | Cũ | Legacy pull còn đọc được; không đạt bảo đảm recovery V2. Thay đổi Desktop cũ không cập nhật operational fields qua projection |
| V2 | V2 | Cursor, transaction, durable outbox, revision server |

## Thứ tự triển khai (chưa thực hiện remote)

1. Backup/export D1, SQLite và cấu hình; lưu artifact Worker/Desktop cũ để rollback.
2. Cấu hình JWT_SECRET, SYNC_SECRET, ADMIN_PIN hợp lệ và STAFF_PIN riêng nếu dùng. Không đưa giá trị vào tài liệu/log.
3. Áp dụng migrations D1 0004–0005, deploy Worker hỗ trợ cả V2 và legacy; kiểm tra health/config và V2 trên fixture được phép.
4. Cài Desktop V2. SQLite migration lưu checkpoint/outbox riêng. LastCloudPullAt cũ cần reconciliation có người kiểm tra; không tự reset/replay lịch sử trên dữ liệu thật.
5. Kiểm tra pending/confirmed, retry offline, quyền Admin/Staff và trường hợp mobile sửa giữa pull/push trước khi chấp nhận release.

Không rollback Desktop về protocol cũ để tiếp tục ghi dữ liệu vận hành. Khi có lỗi, tắt Cloud sync và giữ SQLite/outbox; restore dữ liệu chỉ theo backup/phạm vi được xác nhận. D1 migration dùng ledger Wrangler, chạy lại không lặp ALTER TABLE. Không xóa DB để nâng cấp.

Dependencies test: Vitest 5.0.3/Vite 8.3.2 đã kiểm tra với Node 24, theo [migration guide chính thức](https://vitest.dev/guide/migration/). Test adapter dùng SQLite thật để thực thi WHERE/ORDER/LIMIT/transaction, không mock pagination.

## Đối soát cursor cũ — thao tác của chủ tiệm

1. Sau khi Worker V2 được triển khai và health báo cấu hình đầy đủ, mở **Cài đặt → Cloud → Xuất báo cáo đối soát**, lưu JSON vào thư mục riêng. Bước này chỉ lưu bằng chứng, không sửa payment/delivery/note.
2. Mở JSON. Mỗi `fields` có `payloadJson` (trạng thái server), `localValueJson` (giá trị Desktop), `revision`, `resolution`. Chỉ sửa `resolution`; giữ nguyên toàn bộ bằng chứng khác.
3. Với **từng field**, đổi `unreviewed` thành `server` để lấy trạng thái server, hoặc `desktop` để giữ giá trị Desktop và gửi nó như thao tác mới pending. Nếu `localValueJson` là null vì entity không còn local, kiểm tra ID/lịch sử trước khi chọn `ignore_missing`. Không chọn hàng loạt nếu chưa xem nội dung.
4. Chọn **Áp dụng báo cáo đã duyệt**, chọn JSON và xác nhận. App tạo backup; một transaction áp dụng toàn bộ lựa chọn và checkpoint. Nếu Desktop đã thay đổi từ lúc xuất, phải xuất lại báo cáo. Nếu lỗi, không có phần lựa chọn nào được commit. Chỉ các fields vận hành được thay đổi; snapshot tiền/số lượng của bill giữ nguyên.
5. Bấm **Đồng bộ ngay**. Các lựa chọn `desktop` vẫn là pending đến khi server ACK. Thay đổi mobile mới hơn cursor của báo cáo tiếp tục được kéo về; chọn `desktop` sẽ có revision mới khi server chấp nhận, đúng policy đã chốt.

SQLite migration 21 nhận biết LastCloudPullAt cũ, lưu checkpoint riêng và không tự replay. Endpoint/stream ID đổi sẽ chặn đồng bộ để đối soát. Báo cáo gốc và thời điểm duyệt được lưu trong DB; ledger cũ được archive khi đổi stream. Không chỉnh SQLite bằng tay để bỏ cờ đối soát.

## Release blocker hardening — 2026-10-03

SQLite migration 22 giữ ledger remote-print theo endpoint/stream/command. Worker migration 0006 lưu bill ProductSubtotal/AdjustmentsTotal và claim token. Desktop phải POST `/api/sync/print-commands/{id}/claim` với `claimToken` bền vững trước khi spool; Worker chỉ cho một PENDING→PROCESSING. POST `/status` yêu cầu cùng token và chỉ nhận COMPLETED/FAILED. Retry ACK cùng trạng thái được chấp nhận; không chuyển terminal về PENDING. PROCESSING không có automatic expiry/requeue.

Desktop ghi STARTED trước claim, COMPLETED sau spooler thành công và trước delivery/ACK. Recovery STARTED không in lại; cảnh báo kiểm tra máy in thủ công. ACK pending được retry độc lập với queue poll. Đây là at-most-once automatic spooling theo command, không phải cam kết exactly-once physical printing.

Reset/restore/purge database có Cloud credentials/history/binding bị chặn, kể cả đã tắt Cloud. Restore kiểm tra cả source và target; operational reset local giữ high-water IDs. Không xóa outbox/cursor để vượt guard. Chi tiết nghiệm thu và rollout đồng bộ hai phía: [RELEASE_BLOCKER_FIXES_20261003.md](RELEASE_BLOCKER_FIXES_20261003.md).
