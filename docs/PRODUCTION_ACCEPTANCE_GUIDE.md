# Các bước cần chủ tiệm thực hiện

Phần triển khai local đã kiểm tra. Các bước dưới đây hoàn tất nghiệm thu production; chưa có thao tác deploy remote nào được thực hiện.

## 1. Backup và chọn release

1. Mở Desktop → **Cài đặt → Sao lưu**; tạo backup riêng, kiểm tra file có dung lượng và sao chép sang nơi khác. Giữ binary/cấu hình đang dùng.
2. Kiểm tra version đã phát hành trên GitHub và chọn version mới lớn hơn; cập nhật đồng nhất project/assembly/file/package/release tag, build lại và lập manifest SHA256 mới. Chưa đưa EXE 1.0.0 hiện tại lên auto-update như bản mới nếu đang dùng cùng version.
3. Chỉ thử updater trên bản sao executable và profile fixture; dùng `--isolated-data-directory "<thư mục fixture tuyệt đối>"`. Profile này không tự chạy Cloud/scan/registry/startup. Không trỏ tham số vào thư mục dữ liệu thật.

## 2. Cloud V2 — sau khi duyệt deploy

Các lệnh chạy từ `cloud/worker`; xác nhận tài khoản Cloudflare/database/domain thuộc xưởng trước các lệnh có `--remote`. Secrets nhập tại terminal, không gửi trong chat hoặc lưu vào repo.

1. Export D1 để backup: `npx wrangler d1 export lalab-db --remote --output <đường-dẫn-backup.sql>`; lưu artifact Worker cũ.
2. Đặt từng secret bằng `npx wrangler secret put JWT_SECRET`, `SYNC_SECRET`, `ADMIN_PIN`, `STAFF_PIN`. JWT/sync dùng giá trị ngẫu nhiên riêng đủ mạnh; PIN Admin/Staff khác nhau. Desktop cấu hình đúng SYNC_SECRET. Không dùng PIN fixture 246810/135790.
3. Áp dụng schema: `npx wrangler d1 migrations apply lalab-db --remote`; xác minh ledger có 0004 và 0005.
4. Deploy artifact đã duyệt: `npx wrangler deploy`; kiểm tra `/api/health` có cấu hình đủ. Kiểm tra V2 bằng secret header, không đặt secret trong URL. Giữ Desktop Cloud sync chưa chạy cho đến khi đối soát.
5. Đăng nhập Cloud trên điện thoại với Admin rồi Staff; mở một bill fixture 75.000 đồng. Admin thấy tiền; Staff không thấy tiền/nút thu tiền và request payment trực tiếp bị 403.

Tham khảo contract/thứ tự triển khai tại [CLOUD_PROTOCOL_V2.md](CLOUD_PROTOCOL_V2.md). Chưa chạy các lệnh remote chỉ vì thấy chúng trong hướng dẫn này.

## 3. Đối soát dữ liệu cũ

DB hiện tại đang có `requires_reconciliation=true`.

1. Sau khi endpoint V2 sẵn sàng, mở **Cài đặt → Cloud → Xuất báo cáo đối soát**, lưu JSON.
2. Xem từng field và so `payloadJson` (server) với `localValueJson` (Desktop). Chỉ sửa `resolution`: `server`, `desktop`, hoặc `ignore_missing` nếu đã kiểm tra entity không còn local. Không sửa các trường bằng chứng khác.
3. Chọn **Áp dụng báo cáo đã duyệt**, kiểm tra đường dẫn backup và xác nhận. Báo cáo stale/tampered hoặc field chưa duyệt phải bị chặn.
4. Bấm **Đồng bộ ngay**; lựa chọn `desktop` là pending đến ACK. Kiểm tra pending hết và trạng thái trên điện thoại khớp. Không chỉnh SQLite để bỏ cờ hoặc reset cursor.

## 4. Thử ở xưởng

Dùng một thư mục/dữ liệu test riêng, tránh bill khách thật.

1. Hai roots cùng relative path, số lượng khác: scan riêng, kiểm tra hai order IDs/nguồn và thao tác in đúng root. Ngắt một NAS/root: app báo review/lỗi, không đọc root còn lại thay thế.
2. Tạo bill Customer và Guest; thử override có lý do, lock, xuất JPEG, đóng/mở lại, thêm ảnh và scan. Bill lịch sử giữ tiền/số lượng; sửa chỉ sau reopen có lý do.
3. Dùng Explorer **QUICK BILL/ĐÃ IN** trên folder test; kiểm tra handoff đến đúng instance và marker đúng kho. Không chọn folder production để nghiệm thu.
4. In một JPEG bill và tem 75×100 thực: kiểm tra font Việt, số tiền/QR, kích thước/căn lề, chỉ in đúng một bản. Kiểm tra màn hình điện thoại thật, mở bill, note/delivery.
5. Offline Desktop: đổi note/delivery của order test, thấy pending; mobile đổi field cùng lúc; reconnect và sync. Revision server chấp nhận sau thắng; retry không toggle lại. Khởi động lại Desktop giữa pending và ACK rồi thử tiếp.
6. Chạy updater trên bản sao: download thành công, apply/restart và kiểm tra bill/token còn nguyên; thử thư mục/file đích bị khóa rồi xác minh bản cũ/download/log còn và retry được. Giữ `.previous-*` đến khi nghiệm thu xong.

## 5. Ghi kết quả và chốt

Ghi PASS/FAIL cho Cloud, đối soát, NAS, máy in/tem, điện thoại, Explorer và updater download/UI. Khi fail, lưu thông báo/log và bước tái hiện; giữ Cloud tắt nếu trạng thái chưa đối soát. Không cần gửi secrets hoặc dữ liệu khách hàng vào chat.

Chỉ chuyển kết luận review thành READY và phát hành khi các gate bắt buộc có PASS và version/artifact đã được duyệt. Các bằng chứng local hiện có: `../FINAL_REVIEW_VERIFIED.md` và `production-evidence/`.
