# Lalab Auto Report — Cloudflare Read Replica & Mobile PWA

Hệ thống **Cloud Read Replica** cho phép xem toàn bộ dữ liệu xưởng (Đơn hàng, Hóa đơn, Ảnh bill JPEG, Báo cáo doanh thu ngày/tháng, Công nợ) trên điện thoại **24/7** bất cứ lúc nào, ngay cả khi máy tính ở xưởng đã tắt hoàn toàn.

---

## 1. Kiến trúc hệ thống

```text
       LALAB DESKTOP (Xưởng)
      [SQLite Single Source of Truth]
                 │
                 │ (Đồng bộ ngầm 1 chiều HTTPS, không làm chậm máy)
                 ▼
      [Cloudflare Worker Gateway]
       ├── D1 Database (Bản sao số liệu SQLite)
       ├── R2 Bucket (Lưu ảnh hóa đơn JPEG)
       └── Static Assets (Mobile PWA)
                 ▲
                 │ (Truy cập bảo mật bằng mã PIN)
                 │
      [Điện Thoại Di Động 24/7]
      (https://lalab.tinix.io.vn)
```

---

## 2. Hướng dẫn Triển khai Cloudflare (Làm 1 lần duy nhất)

### Bước 2.1: Mở terminal tại thư mục worker
```bash
cd cloud/worker
```

### Bước 2.2: Đăng nhập Cloudflare (nếu chưa đăng nhập)
```bash
npx wrangler login
```

### Bước 2.3: Tạo D1 Database
```bash
npx wrangler d1 create lalab-db
```
*Lưu ý:* Sau khi chạy, terminal sẽ hiển thị `database_id` (ví dụ: `xxxx-xxxx-xxxx-xxxx`). Copy ID này và dán vào file [wrangler.jsonc](file:///d:/___TOOLS/__TINIX/Lalab%20Auto%20Report%20Antigravity/cloud/worker/wrangler.jsonc) tại trường:
```jsonc
"database_id": "xxxx-xxxx-xxxx-xxxx"
```

### Bước 2.4: Tạo R2 Bucket để lưu ảnh bill
```bash
npx wrangler r2 bucket create lalab-media
```

### Bước 2.5: Cấu hình Secret Keys
Thiết lập mã bảo mật đồng bộ giữa Desktop và Worker:
```bash
npx wrangler secret put SYNC_SECRET
# Nhập mật khẩu bảo mật đồng bộ (ví dụ: LalabSync@2026!SecretKey)
```

Thiết lập JWT secret ngẫu nhiên (ít nhất 32 byte), PIN Admin bắt buộc và PIN Staff tùy chọn. Worker không có secret/PIN mặc định; thiếu cấu hình trả 503. Hai PIN phải khác nhau:
```bash
npx wrangler secret put JWT_SECRET
```
```bash
npx wrangler secret put ADMIN_PIN
# Nhập PIN Admin riêng của xưởng

npx wrangler secret put STAFF_PIN
# Nhập PIN Staff khác PIN Admin
```

### Bước 2.6: Chạy migration database trên Cloud
```bash
npx wrangler d1 migrations apply lalab-db --remote
```

### Bước 2.7: Deploy Worker & Mobile PWA
```bash
npx wrangler deploy
```

Sau khi deploy thành công, Cloudflare sẽ cấp URL:
`https://lalab-cloud-worker.<subdomain>.workers.dev` (hoặc custom domain `https://lalab.tinix.io.vn` nếu bạn đã gán domain trên Cloudflare Dashboard).

---

## 3. Cấu hình trên Desktop App (Lalab Auto Report)

1. Mở ứng dụng **Lalab Auto Report** trên máy tính xưởng.
2. Vào menu **Cài Đặt (Settings)** ➔ Kéo xuống phần **Đồng Bộ Cloudflare (Xem Trên Điện Thoại 24/7)**.
3. Tích chọn: **Kích hoạt đồng bộ Cloudflare Read Replica (Xem 24/7)**.
4. **URL Cloudflare Worker API**: Nhập URL bạn vừa deploy ở bước 2 (ví dụ: `https://lalab-cloud-worker.yourname.workers.dev` hoặc `https://lalab.tinix.io.vn`).
5. **Mã Bảo Mật Đồng Bộ (Sync Secret)**: Nhập chuỗi ký tự bạn đã đặt ở bước `npx wrangler secret put SYNC_SECRET`.
6. Bấm nút **[LƯU CÀI ĐẶT]**.
7. Bấm nút **[🔄 ĐỒNG BỘ TOÀN BỘ NGAY]** để đồng bộ toàn bộ đơn hàng, bill và báo cáo hiện tại lên Cloud.

---

## 4. Tự động đồng bộ (Zero-Touch)

Sau khi cài đặt, ứng dụng Desktop sẽ **tự động đồng bộ** trong các tình huống:
- **Khi mở ứng dụng:** Tự động chạy đồng bộ ngầm sau 3 giây.
- **Khi quét thư mục ngày (Scan Date):** Tự động đẩy đơn hàng của ngày vừa quét lên Cloud.
- **Khi quét ngày thiếu (Scan Missing Days):** Tự động đẩy đơn hàng cập nhật lên Cloud.
- **Khi xuất hóa đơn (Xuất Bill / Re-export):** Tự động đẩy thông tin hóa đơn và upload ảnh JPEG lên Cloud.
- **Khi chuyển trạng thái thanh toán (Đã TT / Chưa TT):** Tự động cập nhật tức thì trạng thái hóa đơn lên Cloud.

> 💡 **Đặc tính an toàn:** Quá trình đồng bộ diễn ra hoàn toàn chạy ngầm (asynchronous background). Nếu máy tính xưởng mất mạng hoặc Cloudflare bảo trì, ứng dụng Desktop vẫn hoạt động trơn tru 100% offline, không bao giờ bị đơ hay báo lỗi làm phiền thao tác tại xưởng.

---

## 5. Truy cập trên điện thoại di động

1. Mở trình duyệt Safari (iOS) hoặc Chrome (Android) trên điện thoại.
2. Truy cập địa chỉ web của bạn (ví dụ: `https://lalab.tinix.io.vn`).
3. Nhập mã PIN (`123456` cho Quản lý hoặc `000000` cho Nhân viên).
4. **Cài đặt dạng PWA (Icon ngoài màn hình chính):**
   - **iPhone (Safari):** Bấm nút Share (hình vuông có mũi tên lên) ➔ Chọn **"Thêm vào MH chính" (Add to Home Screen)**.
   - **Android (Chrome):** Bấm biểu tượng 3 chấm ở góc trên ➔ Chọn **"Thêm vào Màn hình chính" (Install app / Add to Home screen)**.
5. Mở ứng dụng từ màn hình chính như một ứng dụng Native, xem dữ liệu sắc nét, vuốt mượt mà 24/24 dù máy tính xưởng đã tắt!

## Secret rotation và QR

Đổi JWT_SECRET bằng `wrangler secret put JWT_SECRET` sẽ vô hiệu toàn bộ phiên Cloud cũ; người dùng đăng nhập lại bằng PIN Cloud. Đổi SYNC_SECRET phải cập nhật cùng giá trị trong Cài đặt Desktop trước lần đồng bộ kế tiếp. Không đưa secrets vào source, log hoặc QR. QR LAN chứa token LAN; QR Cloud chỉ dẫn tới màn hình đăng nhập Cloud, không dùng lại token LAN. Kiểm tra `/api/health` có `configurationReady: true` sau khi cấu hình.
