# Release candidate — production hardening

Ngày 2026-10-02. Chưa publish; version hiện tại 1.0.0 là version build, không phải quyết định phát hành mới. Artifact Windows và Worker nằm trong `artifacts/`; hashes ở `production-evidence/release-artifact-manifest.json`.

Bill đã khóa/xuất giữ nguyên lịch sử qua scan và re-export. Sửa snapshot phải reopen có lý do. Multi-root giữ riêng physical orders; scan lỗi không đoán quantity hoặc xóa quan sát cũ; lock xác minh lại scope. Split thất bại không để bill/membership dở dang.

Cloud yêu cầu secrets/PIN rõ ràng. Staff không ghi payment hoặc đọc tiền qua API/ảnh bill. LAN/Cloud dùng contract bill thống nhất. Công nợ Cloud lấy trạng thái payment hiện tại.

Sync V2 dùng event ID/revision server theo từng field. Thao tác offline hiển thị pending; retry cùng opID không tạo thao tác mới. Projection không ghi đè trạng thái vận hành. Cursor chỉ tiến cùng transaction áp dụng event. Upgrade legacy yêu cầu đối soát có người duyệt. D1 migrations 0004–0005 và SQLite migration 21 là bắt buộc.

Updater thay EXE nguyên tử và giữ `.previous-*`, `.failed-*`, download và `apply-update.log` để recovery. Không xóa các file này trước khi xác nhận bản mới hoạt động. PE/hash validation không thay thế chữ ký publisher. Script có thể báo failure nếu Windows chặn PowerShell; khi đó app/bản cũ và download được giữ.

## Triển khai/rollback

Backup SQLite bằng chức năng app, export D1 và lưu binary/config cũ trước upgrade. Deploy Worker V2 trước Desktop V2; không cho Desktop cũ tiếp tục ghi operational state qua projection. Không xóa DB để migration.

Khi sync lỗi: tắt Cloud sync, giữ SQLite/outbox/ledger và logs. Không reset cursor hoặc replay toàn lịch sử. Khôi phục EXE trước từ `.previous-*` chỉ khi đã dừng đúng process và kiểm tra tương thích DB. Không hạ Desktop về protocol legacy để tiếp tục ghi Cloud. Chỉ restore DB từ backup khi đã đối chiếu thao tác phát sinh sau backup; restore có thể mất thao tác mới.

Rollback Worker code dùng artifact cũ đã lưu; không tự DROP columns hoặc xóa D1 events. Nếu schema/protocol không tương thích, giữ Cloud sync tắt đến khi có bản V2 sửa. Các bước remote cần duyệt phạm vi và artifact cụ thể.

Validation: Desktop Debug/Release 534/534 mỗi configuration; Worker 43/43, tsc/bundle PASS, audit 0; standalone/IPC/token restart và updater copy fixture PASS. Gate production còn lại được ghi đầy đủ trong `../FINAL_REVIEW_VERIFIED.md`.
