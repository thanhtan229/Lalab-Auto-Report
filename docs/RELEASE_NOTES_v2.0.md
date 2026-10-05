# Lalab Auto Report v2.0 — Release Notes & Rollback Plan

Ngày phát hành: 2026-10-05
Phiên bản: **v2.0** (ProductVersion `2.0.0`, AssemblyVersion `2.0.0.0`)
Target Commit: `54385f770817abd4d87521f234b84a1f1ca6182f`
Artifact: `LalabAutoReport.exe` (SHA256: `66F7D9D9F4EA2F2AA627624D15D9914566C302064CED76363438B6E533F95D4F`)

---

## 1. Điểm nổi bật & Cải tiến chính

Bản phát hành V2.0 là bản nâng cấp toàn diện về độ ổn định, tính toàn vẹn dữ liệu, bảo mật và khả năng vận hành đồng bộ:

- **Bảo vệ tính toàn vẹn hóa đơn (Locked Bill Protection):**
  - Hóa đơn đã khóa (CustomerBill và GuestBill) và các bản xuất JPEG/tem được bảo toàn tuyệt đối, không bị biến đổi khi scan lại thư mục ảnh.
  - Mọi thao tác điều chỉnh số liệu trên hóa đơn đã khóa bắt buộc phải thực hiện qua quy trình Mở khóa (Reopen) có lý do rõ ràng được ghi nhận vào nhật ký kiểm toán.
  - Quản lý tách hóa đơn (Split Bill) nguyên tử trong giao dịch; thất bại không để lại hóa đơn hoặc thành viên dở dang.

- **Quản lý đa kho ảnh (Multi-Root Storage):**
  - Tách bạch hoàn toàn các đơn hàng từ nhiều thư mục gốc khác nhau (Root 1, Root 2).
  - Quá trình quét giới hạn (scoped rescan) độc lập; sự cố gián đoạn trên một kho không làm ảnh hưởng đến dữ liệu của kho còn lại.

- **Nâng cấp Giao thức Đồng bộ Vận hành Đám mây V2 (Cloud Protocol V2):**
  - Đồng bộ hai chiều an toàn dựa trên Event ID và Server Revision cho từng trường vận hành (ghi chú, trạng thái giao hàng).
  - Thao tác ngoại tuyến hiển thị rõ trạng thái chờ đồng bộ (pending); cơ chế retry không tạo trùng lặp thao tác.
  - Con trỏ đồng bộ (cursor) chỉ tiến cùng giao dịch ghi nhận event; cơ chế đối soát dữ liệu lịch sử bảo vệ toàn vẹn dữ liệu.

- **Bảo mật và Phân quyền Vận hành Di động (Mobile LAN/Cloud PWA):**
  - Phân quyền nghiêm ngặt giữa Admin và Nhân viên (Staff): Nhân viên không xem số tiền, không thấy nút thu tiền/thanh toán và không thể can thiệp dữ liệu tài chính.
  - Hợp nhất hợp đồng dữ liệu đơn hàng và hóa đơn trên toàn bộ giao diện Desktop, Mobile LAN và Cloud PWA.

- **Tự động Cập nhật An toàn (Atomic Auto-Update & Recovery):**
  - Cơ chế thay thế file thực thi nguyên tử, tự động sao lưu bản cũ sang `.previous-*` và lưu bản download để phục hồi khi cần.
  - Kiểm tra tính hợp lệ của định dạng thực thi PE và kiểm tra mã băm SHA256 trước khi áp dụng cập nhật.

---

## 2. Giới hạn đã biết (Known Limitations)

- **Gate 1 — Máy in nhiệt / in tem (Thermal Printer Hardware Acceptance):**
  - Chưa nghiệm thu trên phần cứng máy in nhiệt thực tế tại xưởng (`NOT TESTED — USER DEFERRED` do chưa có thiết bị máy in tại thời điểm bàn giao).
  - Chức năng xuất ảnh tem (shipping label image), xem trước tem và in giả lập trên máy in Windows đã được xác nhận hoạt động bình thường về mặt phần mềm.
  - Tuyệt đối không tuyên bố nghiệm thu phần cứng máy in nhiệt là PASS.

- **Gate 3 — Thiết bị lưu trữ NAS vật lý qua mạng SMB:**
  - Chưa kiểm thử trên thiết bị NAS vật lý chuyên dụng vì topology hiện tại của xưởng chỉ dùng ổ đĩa cục bộ / ổ đĩa phân vùng độc lập. Đã nghiệm thu multi-root hoàn tất trên các thư mục phân tách cục bộ.

---

## 3. Kế hoạch Hoàn tác (Rollback Plan)

Trong trường hợp phát sinh sự cố nghiêm trọng sau khi nâng cấp:

1. **Phiên bản ổn định trước đó:**
   - Tag: `v1.0` (commit `bc54840de5648db3c9a66cb927ed96f4d707218b`).
   - Tên tệp: `LalabAutoReport.UI.exe` (SHA256: `0943def48b8f0cb7ae171ae4814135128e7f6271640c37d2f80f15fa176fffec`).
   - Có thể tải lại từ GitHub Releases tại mục `v1.0`.

2. **Cách khôi phục Desktop Binary:**
   - Khi updater vừa thực hiện nâng cấp, tệp thực thi cũ được giữ nguyên tại cùng thư mục ứng dụng với tên dạng `LalabAutoReport.exe.previous-<guid>`.
   - Để quay lại: Đóng ứng dụng, xóa `LalabAutoReport.exe` mới và đổi tên tệp `.previous-*` trở lại thành `LalabAutoReport.exe`.

3. **Bảo toàn Cấu hình và Cơ sở Dữ liệu:**
   - Ứng dụng tự động tạo bản sao lưu cơ sở dữ liệu (`lalab_backup_*.db`) trong thư mục `backups/` trước khi áp dụng bất kỳ migration nào.
   - Các migration cơ sở dữ liệu SQLite (migration 21, 22) có tính tương thích thuận, dữ liệu cũ hoàn toàn được bảo toàn nguyên vẹn.

4. **Vận hành Đám mây (Cloudflare / D1):**
   - Không thực hiện hoàn tác D1 database một cách phá hủy; các event trong D1 là bất biến và có phiên bản.
   - Nếu cần dừng đồng bộ đám mây, chỉ cần tắt tính năng Đồng bộ Cloud trong phần **Cài đặt → Đám mây** trên ứng dụng Desktop.
