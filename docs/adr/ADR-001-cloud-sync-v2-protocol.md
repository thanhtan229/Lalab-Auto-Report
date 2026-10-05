# ADR 001: Giao Thức Đồng Bộ Vận Hành Đám Mây V2 (Cloud Sync V2 Protocol)

> **Trạng thái:** Accepted (Chấp nhận chính thức tại Phase 8 & Phase 9)  
> **Ngày:** 2026-10-05  
> **Phạm vi:** Lalab Auto Report Desktop (.NET 8 WPF, SQLite) và Cloud Worker (Cloudflare Workers, D1, R2, PWA)  
> **Tham chiếu liên quan:** `ARCHITECTURE.md`, `docs/CLOUD_PROTOCOL_V2.md`, `PRODUCTION_FIX_PROGRESS.md`

---

## 1. Bối cảnh (Context)

Lalab Auto Report hoạt động theo mô hình local-first trên Windows Desktop với cơ sở dữ liệu SQLite cục bộ. Để phục vụ quản lý xưởng in từ xa trên thiết bị di động, hệ thống tích hợp Cloudflare Worker (D1/R2) và ứng dụng Mobile PWA.

Trong phiên bản V1, cơ chế đồng bộ phụ thuộc vào mốc thời gian `LastCloudPullAt` (timestamp-based pull). Cơ chế này bộc lộ các rủi ro cốt lõi trong môi trường sản xuất:
1. **Lệch đồng hồ (Clock Skew):** Chênh lệch giờ giữa máy chủ Cloud, máy Desktop và điện thoại di động có thể làm sai lệch thứ tự thao tác hoặc bỏ sót sự kiện.
2. **Trùng timestamp:** Khi có nhiều thao tác phát sinh trong cùng một timestamp hoặc vượt quá kích thước trang (500 events), phân trang theo thời gian có thể lặp hoặc nuốt sự kiện.
3. **Ghi đè projection:** Đồng bộ danh sách đơn hàng/hóa đơn hàng loạt (`/api/sync/batch`) có thể vô tình ghi đè trạng thái thanh toán hoặc giao hàng mới hơn từ mobile nếu không có cơ chế phân tách quyền ghi theo trường (field-level authority).
4. **Mất an toàn khi gặp sự cố mạng hoặc khởi động lại:** Thiếu hàng đợi ngoại tuyến bền vững (durable outbox) và checkpoint nguyên tử (atomic checkpoint) dẫn đến mất mát thao tác hoặc lặp hiệu ứng (duplicate mutation).

---

## 2. Quyết định Kiến trúc & Đặc tả Giao thức V2 (Decision & Semantics)

Hệ thống chấp nhận và chuẩn hóa Giao thức Đồng bộ Vận hành V2 (Cloud Sync V2 Protocol) với các quy tắc bất biến sau:

### 2.1 Con trỏ kéo V2 là ID sự kiện đơn điệu (Monotonic Server Event ID Cursor)
- Con trỏ kéo `cursor` trong `GET /api/sync/v2/pull?cursor={cursor}&limit=500` là một số nguyên dương 64-bit đơn điệu tăng dần tương ứng với khóa chính `id` của bảng sự kiện trên máy chủ (`cloud_events.id`).
- Con trỏ tuyệt đối không sử dụng mốc thời gian (wall-clock time) của máy trạm hay máy chủ.

### 2.2 nextCursor phản ánh ID sự kiện thực tế cuối cùng của trang
- Máy chủ trả về `nextCursor` bằng chính `id` của sự kiện cuối cùng thực sự nằm trong trang trả về (`events.LastOrDefault()?.Id ?? cursor`).
- Phía Desktop kiểm tra tính đơn điệu ngặt (`ev.Id > previous`). Checkpoint chỉ được cập nhật sau khi toàn bộ hiệu ứng của sự kiện đã được commit thành công vào SQLite cục bộ. Checkpoint trên đĩa luôn bằng ID sự kiện an toàn nhất đã commit, không bao giờ nhảy trước theo con trỏ nhận từ Worker nếu chưa áp dụng.

### 2.3 streamId bền vững xác định tính liên tục của luồng sự kiện (Stream Continuity)
- Cơ sở dữ liệu Cloudflare D1 lưu trữ một `stream_id` cố định (được khởi tạo và duy trì qua migration).
- Desktop liên kết (`BindStreamAsync`) với `stream_id` của endpoint hiện tại. Nếu `endpoint` hoặc `stream_id` bị thay đổi (ví dụ đổi worker, khôi phục D1 từ bản backup cũ, hoặc trỏ sang môi trường khác), Desktop kích hoạt trạng thái **fail-closed**: lập tức chặn đồng bộ và yêu cầu đối soát tường minh (`requires_reconciliation = 1`), nghiêm cấm tự ý reset cursor về 0 hay replay lịch sử đè lên dữ liệu thật.

### 2.4 ID sự kiện là Revision máy chủ có thẩm quyền tối cao (Authoritative Server Revision)
- Mỗi thao tác vận hành (`bill_payment`, `order_delivered`, `order_note`) khi được transaction máy chủ chấp nhận sẽ được cấp một ID sự kiện đơn điệu. ID sự kiện này đóng vai trò là số hiệu sửa đổi (`revision`) có thẩm quyền của trường đó.
- Quy tắc giải quyết xung đột: **Revision cao hơn luôn thắng trên từng trường**. Thời gian cục bộ của Desktop hay điện thoại không tham gia vào việc phân xử xung đột.

### 2.5 Revision được theo dõi độc lập trên từng trường vận hành (Per-Field Revisions)
- Desktop lưu trữ revision đã xác nhận độc lập cho từng trường trong bảng `cloud_field_revisions` theo `(entity_type, entity_id)`.
- Một sự kiện kéo về hoặc một phản hồi ACK chỉ được phép ghi đè giá trị hiển thị nếu revision của nó không nhỏ hơn revision hiện hành đã lưu trong cơ sở dữ liệu cục bộ.
- Ngay cả khi sự kiện cũ đến sau sự kiện mới (out-of-order delivery do mạng) hoặc đồng hồ Desktop bị chỉnh sai (ví dụ năm 2099), trạng thái vận hành đã xác nhận không bao giờ bị thụt lùi (no state regression).

### 2.6 operationId đảm bảo khả năng thử lại đột biến bất biến (Idempotent Mutation Retry)
- Mọi thao tác vận hành phát sinh cục bộ khi offline hoặc chưa gửi lên Cloud đều nhận một `operationId` duy nhất (UUID v4) và được lưu bền vững trong bảng `cloud_operational_outbox`.
- Khi gửi lên Cloud (`POST /api/sync/v2/operations`), nếu gặp lỗi mạng, tiến trình retry sẽ sử dụng lại chính xác `operationId` đó.
- Máy chủ sử dụng transaction và khóa duy nhất trên `operation_id`: nếu nhận lại một `operationId` đã xử lý thành công trước đó, máy chủ trả về sự kiện gốc tương ứng mà không tạo thêm revision mới hay nhân bản bản ghi. Nếu gửi lại cùng `operationId` nhưng khác nội dung payload, máy chủ từ chối với HTTP 409 Conflict.

### 2.7 Đồng bộ Projection không được sửa đổi các trường vận hành hoặc Revision
- API đồng bộ projection hàng loạt (`POST /api/sync/batch`) chỉ dùng để cập nhật thông tin hiển thị cơ bản (danh sách đơn hàng, bill, báo cáo tổng hợp).
- Projection chỉ khởi tạo (bootstrap) các trường vận hành (`is_paid`, `is_delivered`, `note`) khi thực hiện `INSERT` một thực thể hoàn toàn mới.
- Các lệnh `UPDATE` trong projection tuyệt đối không ghi đè lên các trường vận hành và không thể cấp phát revision. Thao tác vận hành chỉ được phép thay đổi thông qua endpoint V2 operations hoặc V2 pull events.

### 2.8 Kéo theo mốc thời gian cũ chỉ để tương thích, không an toàn cho V2 (Legacy Timestamp Deprecation)
- Endpoint kéo theo timestamp cũ (`GET /api/sync/pull?since=...`) chỉ được giữ lại để tương thích tạm thời cho các client cũ chưa nâng cấp.
- Endpoint cũ không đảm bảo con trỏ ID, không có per-field revision và không đáp ứng tiêu chuẩn phục hồi an toàn của V2.

### 2.9 Thứ tự triển khai bắt buộc (Deployment Ordering)
- **Bước 1: Worker V2 trước.** Cập nhật Cloudflare Worker và áp dụng các migration D1 liên quan (hỗ trợ song song V2 và legacy endpoint). Đảm bảo kiểm tra sức khỏe (`/api/health`) báo cấu hình đầy đủ.
- **Bước 2: Desktop V2 sau.** Nâng cấp ứng dụng Desktop với migration SQLite 21, bảng trạng thái đồng bộ và cơ chế đối soát cursor cũ.

### 2.10 Desktop V2 tự ngắt an toàn nếu thiếu giao thức V2 (Fail-Closed on Missing V2)
- Khi thực hiện kéo thay đổi (`PullRemoteChangesCoreAsync`), nếu máy chủ không hỗ trợ V2 (`protocolVersion != 2` hoặc `success != true`), Desktop V2 lập tức ném ngoại lệ và dừng toàn bộ luồng đồng bộ.
- Kéo dữ liệu thành công (`Pull Success`) là **điều kiện tiên quyết (precondition)** trước khi Desktop gửi bất kỳ dữ liệu projection nào lên đám mây. Nếu kéo thất bại, đẩy projection bị chặn hoàn toàn để ngăn chặn việc ghi đè trạng thái cũ lên dữ liệu đám mây mới hơn.

### 2.11 Tính bất biến của Snapshot tài chính (Financial Snapshot Immutability)
- Quá trình phục hồi hoặc đồng bộ từ Cloud tuyệt đối không bao giờ được thay đổi các trường dữ liệu tài chính lịch sử của các hóa đơn đã Khóa (`Locked`) hoặc đã Xuất (`Exported`): `product_subtotal`, `adjustments_total`, `grand_total`, cũng như chi tiết từng dòng `bill_lines`.
- Chỉ các trường vận hành được cấp phép (`is_paid`, `paid_at`, `is_delivered`, `delivered_at`, `delivered_by`, `note`) mới có thể được cập nhật theo revision.

### 2.12 Đối soát an toàn con trỏ cũ (Legacy Cursor Migration & Reconciliation)
- Khi nâng cấp từ phiên bản cũ có `LastCloudPullAt`, Desktop không giả định rằng các sự kiện trước thời điểm đó đã được áp dụng an toàn.
- Hệ thống thiết lập cờ `requires_reconciliation = 1`.
- Quá trình đối soát diễn ra theo quy trình:
  1. Xuất báo cáo đối soát chỉ đọc (`ExportReconciliationReportAsync`) kèm mã hash toàn vẹn của dữ liệu cục bộ.
  2. Người dùng xem xét và chỉ định tường minh lựa chọn giải quyết (`server`, `desktop`, hoặc `ignore_missing`) cho từng trường bị lệch.
  3. Khi áp dụng (`CompleteReconciliationAsync`), Desktop tự động tạo bản sao lưu cơ sở dữ liệu, xác thực mã hash không bị thay đổi, và thực thi toàn bộ lựa chọn trong đúng một transaction SQLite duy nhất.
  4. Tuyệt đối không tự động reset về 0 và tự phát lại toàn bộ lịch sử trên môi trường sản xuất.

---

## 3. Hệ quả (Consequences)

### Tích cực:
- Khắc phục triệt để mọi lỗi mất dữ liệu hoặc sai lệch trạng thái do lệch đồng hồ thiết bị.
- Đảm bảo tính nhất quán cuối cùng (eventual consistency) giữa Desktop và Cloud/PWA ngay cả khi mạng ngắt quãng kéo dài.
- Bảo vệ tuyệt đối tính bất biến của hóa đơn tài chính đã khóa.
- Người vận hành có toàn quyền kiểm soát và lưu vết đối soát rõ ràng khi nâng cấp hệ thống.

### Giới hạn & Tuân thủ:
- Thao tác ngoại tuyến trên Desktop hiển thị rõ ràng trạng thái "Chờ Cloud xác nhận" (Pending) cho đến khi nhận được phản hồi ACK từ máy chủ.
- Khi luồng sự kiện bị ngắt hoặc thay đổi endpoint, bắt buộc phải có sự can thiệp đối soát của người dùng trước khi tiếp tục đồng bộ.
