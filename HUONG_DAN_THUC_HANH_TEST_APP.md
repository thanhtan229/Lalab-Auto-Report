# SỔ TAY HƯỚNG DẪN THỰC HÀNH KIỂM THỬ ỨNG DỤNG LALAB AUTO REPORT

> **Dành cho:** Tester, Developer, Kế toán & Quản lý xưởng in ảnh  
> **Phiên bản ứng dụng:** Lalab Auto Report V2 (Clean Architecture, WPF, SQLite WAL)  
> **Cập nhật tính năng:** Nâng cấp V2 (Effective Billing Folder, Album Pricing), Customer-Centric Billing, Xuất Hóa Đơn JPEG Vector 1080px, và Quick Bill / Tính Bill Khách Lẻ  
> **Ngày cập nhật:** 2026-09-29  

---

## MỤC LỤC

- [I. Mục Tiêu Kiểm Thử](#i-mục-tiêu-kiểm-thử)
- [II. Chuẩn Bị Môi Trường Test Nhanh (1 Phút)](#ii-chuẩn-bị-môi-trường-test-nhanh-1-phút)
  - [1. Khởi chạy ứng dụng](#1-khởi-chạy-ứng-dụng)
  - [2. Tự động tạo dữ liệu mẫu và nạp bảng giá (1 Lệnh duy nhất)](#2-tự-động-tạo-dữ-liệu-mẫu-và-nạp-bảng-giá-1-lệnh-duy-nhất)
  - [3. Cấu hình Thư mục Gốc trong App](#3-cấu-hình-thư-mục-gốc-trong-app)
  - [4. Bảng số liệu có sẵn trong App phục vụ kiểm thử](#4-bảng-số-liệu-có-sẵn-trong-app-phục-vụ-kiểm-thử)
- [III. Ma Trận 22 Bài Thực Hành Kiểm Thử Toàn Diện](#iii-ma-trận-22-bài-thực-hành-kiểm-thử-toàn-diện)
  - [Nhóm A: Nhận Diện Cơ Bản & Đơn Hàng Con (Happy Paths)](#nhóm-a-nhận-diện-cơ-bản--đơn-hàng-con-happy-paths)
    - [Bài 01: Ảnh in phẳng chuẩn (Flat Product Folder)](#bài-01-ảnh-in-phẳng-chuẩn-flat-product-folder)
    - [Bài 02: Tự động nhận diện Khách quen qua Biệt danh (Customer Alias)](#bài-02-tự-động-nhận-diện-khách-quen-qua-biệt-danh-customer-alias)
    - [Bài 03: Bảo toàn Provenance - Cùng một khách nhiều thư mục trong ngày](#bài-03-bảo-toàn-provenance---cùng-một-khách-nhiều-thư-mục-trong-ngày)
    - [Bài 04: Cấu trúc Đơn hàng con tường minh (Explicit Sub-orders)](#bài-04-cấu-trúc-đơn-hàng-con-tường-minh-explicit-sub-orders)
  - [Nhóm B: Thư Mục Tính Số Lượng Động V2 (Effective Billing Folder Resolution)](#nhóm-b-thư-mục-tính-số-lượng-động-v2-effective-billing-folder-resolution)
    - [Bài 05: Thư mục sửa ảnh nhiều cấp sâu (Deepest Leaf Retouch)](#bài-05-thư-mục-sửa-ảnh-nhiều-cấp-sâu-deepest-leaf-retouch)
    - [Bài 06: Thư mục in mơ hồ tranh chấp (Ambiguous Print Folder)](#bài-06-thư-mục-in-mơ-hồ-tranh-chấp-ambiguous-print-folder)
    - [Bài 07: Tiến trình thư mục động không bị dính (Anti-Stickiness)](#bài-07-tiến-trình-thư-mục-động-không-bị-dính-anti-stickiness)
    - [Bài 08: Lọc file rác, file hệ thống & Xử lý chữ HOA/thường](#bài-08-lọc-file-rác-file-hệ-thống--xử-lý-chữ-hoathường)
    - [Bài 09: Thư mục rỗng / Không có ảnh in (No Print Folder)](#bài-09-thư-mục-rỗng--không-có-ảnh-in-no-print-folder)
  - [Nhóm C: Tính Giá Album Chuyên Sâu V2 (Album Pricing Matrix)](#nhóm-c-tính-giá-album-chuyên-sâu-v2-album-pricing-matrix)
    - [Bài 10: Album đúng số trang chuẩn (Included Sheets)](#bài-10-album-đúng-số-trang-chuẩn-included-sheets)
    - [Bài 11: Album vượt trang phát sinh (Extra Sheets Calculation)](#bài-11-album-vượt-trang-phát-sinh-extra-sheets-calculation)
    - [Bài 12: Album thiếu trang so với chuẩn (< Included Sheets)](#bài-12-album-thiếu-trang-so-với-chuẩn--included-sheets)
    - [Bài 13: Đơn hàng kết hợp nhiều cuốn Album khác kích thước](#bài-13-đơn-hàng-kết-hợp-nhiều-cuốn-album-khác-kích-thước)
  - [Nhóm D: Xử Lý Ngoại Lệ & Bổ Sung Danh Mục (Business Exceptions)](#nhóm-d-xử-lý-ngoại-lệ--bổ-sung-danh-mục-business-exceptions)
    - [Bài 14: Quy cách lạ chưa có trong bảng giá (Unresolved Specification)](#bài-14-quy-cách-lạ-chưa-có-trong-bảng-giá-unresolved-specification)
    - [Bài 15: Khách hàng mới toanh chưa có danh bạ (Unresolved Customer)](#bài-15-khách-hàng-mới-toanh-chưa-có-danh-bạ-unresolved-customer)
  - [Nhóm E: Khóa Đơn, Bất Biến & Báo Cáo (Lock Invariance & Reporting)](#nhóm-e-khóa-đơn-bất-biến--báo-cáo-lock-invariance--reporting)
    - [Bài 16: Khóa Đơn & Kiểm tra Tính Bất Biến (Lock & Tamper-Proofing)](#bài-16-khóa-đơn--kiểm-tra-tính-bất-biến-lock--tamper-proofing)
    - [Bài 17: Mở lại đơn đã khóa (Reopen Order Workflow)](#bài-17-mở-lại-đơn-đã-khóa-reopen-order-workflow)
    - [Bài 18: Báo cáo Doanh thu & Phát hiện Ngày Bỏ Sót (Missing Days Discovery)](#bài-18-báo-cáo-doanh-thu--phát-hiện-ngày-bỏ-sót-missing-days-discovery)
    - [Bài 19: Sao lưu & Phục hồi CSDL (Backup & Disaster Recovery)](#bài-19-sao-lưu--phục-hồi-csdl-backup--disaster-recovery)
  - [Nhóm F: Tính & Xuất Hóa Đơn Theo Khách Hàng (Customer-Centric Billing & JPEG Export)](#nhóm-f-tính--xuất-hóa-đơn-theo-khách-hàng-customer-centric-billing--jpeg-export)
    - [Bài 20: Gom đơn đa ngày/alias, Điều chỉnh chi phí & Xuất ảnh JPEG 1080px](#bài-20-gom-đơn-đa-ngàyalias-điều-chỉnh-chi-phí--xuất-ảnh-jpeg-1080px)
  - [Nhóm G: Quick Bill / Tính Bill Khách Lẻ (Guest Bill Workflow)](#nhóm-g-quick-bill--tính-bill-khách-lẻ-guest-bill-workflow)
    - [Bài 21: Tính Bill Khách Lẻ từ Đa Thư Mục Nguồn (Multi-Source Quick Bill)](#bài-21-tính-bill-khách-lẻ-từ-đa-thư-mục-nguồn-multi-source-quick-bill)
    - [Bài 22: Cảnh Báo Thư Mục Trùng Lặp & Chuyển Khách Lẻ Thành Khách Hàng](#bài-22-cảnh-báo-thư-mục-trùng-lặp--chuyển-khách-lẻ-thành-khách-hàng)
- [IV. Bảng Checklist Nghiệm Thu 22 Bài (Pass / Fail Acceptance Matrix)](#iv-bảng-checklist-nghiệm-thu-22-bài-pass--fail-acceptance-matrix)
- [V. Xử Lý Sự Cố Thường Gặp (Troubleshooting)](#v-xử-lý-sự-cố-thường-gặp-troubleshooting)

---

## I. MỤC TIÊU KIỂM THỬ

Tài liệu này là cẩm nang thực hành kiểm thử toàn diện (End-to-End Hands-on Testing Guide) cho ứng dụng **Lalab Auto Report**.

Mục tiêu cốt lõi:
1. **Bảo toàn dữ liệu gốc:** Hệ thống chỉ đọc metadata của thư mục, **tuyệt đối không ghi đè, đổi tên hay xóa bất kỳ tệp ảnh nào của xưởng**.
2. **Thư mục tính số lượng động V2 (Effective Billing Folder):** Tự động nhận diện thư mục ảnh hợp lệ sâu nhất; hỗ trợ tính bill trước in ngay tại Product Folder; tự động chuyển sang thư mục sửa mới khi quét lại mà không bị dính vào thư mục cũ.
3. **Tính giá Album V2:** Tính tiền theo gói cơ bản kèm số tờ chuẩn, tự động tính số tờ vượt và phụ phí, **không trừ bìa**.
4. **Bảo toàn nguồn gốc (Provenance):** Nhiều thư mục gửi trong ngày của cùng một khách hàng vẫn giữ nguyên các đơn hàng vật lý độc lập.
5. **Tính bất biến của Hóa đơn đã khóa (Lock Invariance):** Bất biến số tiền lịch sử; phát hiện và cảnh báo can thiệp ngoài ổ cứng sau khi khóa đơn.
6. **Customer-Centric Billing & Xuất Hóa Đơn JPEG:** Gom toàn bộ đơn chưa thanh toán của một khách hàng từ nhiều ngày/alias; điều chỉnh phí ship, phụ phí, chiết khấu; xuất ảnh JPEG vector 1080px chuẩn Tinix.
7. **Quick Bill / Khách lẻ (Guest Bill):** Tính và xuất bill nhanh chóng từ một hoặc nhiều thư mục rời rạc mà **không tạo hồ sơ khách hàng rác trong database**, phát hiện thư mục đã từng tính bill cũ, và hỗ trợ nâng cấp thành khách hàng chính thức khi cần.

---

## II. CHUẨN BỊ MÔI TRƯỜNG TEST NHANH (1 PHÚT)

### 1. Khởi chạy ứng dụng
Nhấp đúp vào file `CHAY_APP.bat` (hoặc `DEV_START.bat`) trong thư mục dự án:
`d:\___TOOLS\__TINIX\LalabReport\CHAY_APP.bat`

### 2. Tự động tạo dữ liệu mẫu và nạp bảng giá (1 Lệnh duy nhất)
Mở cửa sổ **PowerShell** tại thư mục dự án và chạy:
```powershell
powershell -ExecutionPolicy Bypass -File .\tao_thu_muc_test_mau.ps1
```

> [!NOTE]
> Script này sẽ tự động:
> 1. Tạo thư mục `TEST_DATA_LALAB` với đầy đủ ảnh mẫu cho 3 ngày làm việc: `2026-09-25` (kỳ trước), `2026-09-29` (ngày test chính), `2026-09-30` (ngày bỏ sót) và cụm thư mục `KHACH_LE` (cho Quick Bill).
> 2. Nạp sẵn toàn bộ Bảng giá quy cách, Dòng sản phẩm, Biến thể và Khách hàng mẫu vào Database SQLite cục bộ của ứng dụng.

---

### 3. Cấu hình Thư mục Gốc trong App
1. Trên menu bên trái của ứng dụng, chọn **⚙️ Cài đặt hệ thống**.
2. Tại ô **Thư mục gốc chứa ảnh (Root Folder)**:
   - Bấm nút **📂 Chọn thư mục...**
   - Trỏ tới: `d:\___TOOLS\__TINIX\LalabReport\TEST_DATA_LALAB`
3. Kiểm tra mục **Định dạng file ảnh hỗ trợ**:
   - `.jpg, .jpeg, .png, .tif, .tiff, .bmp, .webp, .heic`
4. Bấm **💾 Lưu Cài Đặt**. Thông báo hiển thị: *"Đã lưu cài đặt thành công!"*.

---

### 4. Bảng số liệu có sẵn trong App phục vụ kiểm thử

Sau khi chạy script trên, các dữ liệu sau đã có sẵn trong ứng dụng:

#### A. Bảng giá quy cách sản phẩm
| ID | Tên quy cách chuẩn | Loại sản phẩm | Phương thức tính | Thông số giá có sẵn | Biệt danh (Aliases) nhận diện |
|:---:|:---|:---|:---|:---|:---|
| **1** | `13x18 in` | Ảnh in (`PhotoPrint`) | Theo tệp (`FileCount`) | **5,000 đ** / tấm | `13x18`, `13x18 in`, `13-18` |
| **2** | `20x30` | Ảnh in (`PhotoPrint`) | Theo tệp (`FileCount`) | **15,000 đ** / tấm | `20x30`, `20x30 in` |
| **3** | `40x60 TG` | Ảnh in (`PhotoPrint`) | Theo tệp (`FileCount`) | **80,000 đ** / tấm | `40x60 TG`, `40x60` |
| **4** | `Album 20x20` | Album (`Album`) | Gói + Tờ phụ trội (`AlbumBasePlusExtra`) | **Gói: 400,000 đ** (kèm 10 tờ)<br>**Phát sinh: 20,000 đ** / tờ | `Album 20x20`, `Alb 20x20`, `A20x20`, `Album20x20` |
| **5** | `15x21 in` | Ảnh in (`PhotoPrint`) | Theo tệp (`FileCount`) | **10,000 đ** / tấm | `15x21`, `15x21 in` |
| **6** | `Album 25x25` | Album (`Album`) | Gói + Tờ phụ trội (`AlbumBasePlusExtra`) | **Gói: 500,000 đ** (kèm 10 tờ)<br>**Phát sinh: 25,000 đ** / tờ | `Album 25x25`, `Alb 25x25`, `A25x25`, `Album25x25` |

#### B. Danh bạ khách hàng chuẩn & Biệt danh (Aliases)
| ID | Tên khách hàng chuẩn | Số điện thoại | Biệt danh thư mục (Aliases) | Ghi chú |
|:---:|:---|:---|:---|:---|
| **1** | **Văn An** | `0901234567` | `Văn An`, `Anh An`, `A.An` | Khách quen có nhiều đợt gửi file |
| **2** | **Quang Studio** | `0912345678` | `Quang Studio`, `Studio Quang` | Dùng test các case sửa ảnh & thư mục mơ hồ |
| **3** | **Kim Studio** | `0923456789` | `Kim Studio` | Dùng test ma trận tính giá Album V2 |
| **4** | **Minh Studio** | `0934567890` | `Minh Studio` | Dùng test đơn hàng con tường minh (Explicit orders) |

---

## III. MA TRẬN 22 BÀI THỰC HÀNH KIỂM THỬ TOÀN DIỆN

---

### NHÓM A: NHẬN DIỆN CƠ BẢN & ĐƠN HÀNG CON (HAPPY PATHS)

#### Bài 01: Ảnh in phẳng chuẩn (Flat Product Folder)
* **Mục tiêu:** Xác minh cấu trúc đơn giản nhất: thư mục quy cách nằm ngay dưới khách hàng, chứa file in trực tiếp.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Quang Studio\13x18 in\` (10 file `.jpg`)
* **Thao tác:**
  1. Mở tab **📋 Quét & Đơn hàng**.
  2. Chọn ngày: `2026-09-29`.
  3. Bấm nút **🔍 Quét Ngày**.
* **Kỳ vọng:**
  - Thấy đơn hàng của **Quang Studio**.
  - Dòng sản phẩm `13x18 in` có **Số lượng in = 10**.
  - Đơn giá = `5,000 đ`, Thành tiền = `50,000 đ`.
  - Thư mục tính số lượng hiển thị badge: `Tự động`.
  - Trạng thái hiển thị: **Sẵn sàng (Ready)**.

---

#### Bài 02: Tự động nhận diện Khách quen qua Biệt danh (Customer Alias)
* **Mục tiêu:** Thợ đặt tên thư mục bằng biệt danh viết tắt, app tự động nhận diện đúng khách hàng chính mà không cần can thiệp thủ công.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Anh An\13x18 in\` (20 file `.jpg`)
* **Thao tác:** Xem kết quả quét ngày `2026-09-29`.
* **Kỳ vọng:**
  - Tên thư mục gốc hiển thị: `Anh An`.
  - Tên khách hàng quy chuẩn (Canonical Name): **Văn An** (nhận diện qua alias `Anh An`).
  - Số lượng in = **20**, Đơn giá = `5,000 đ`, Thành tiền = `100,000 đ`.

---

#### Bài 03: Bảo toàn Provenance - Cùng một khách nhiều thư mục trong ngày
* **Mục tiêu:** Khách gửi 3 đợt trong ngày tạo thành 3 thư mục vật lý khác nhau. Ứng dụng phải giữ nguyên **3 đơn hàng độc lập** trên Dashboard, nhưng **tổng hợp chung** trên Báo cáo.
* **Đường dẫn thư mục:**
  1. `TEST_DATA_LALAB\2026-09-29\Văn An\13x18 in\` (15 file)
  2. `TEST_DATA_LALAB\2026-09-29\Anh An\13x18 in\` (20 file)
  3. `TEST_DATA_LALAB\2026-09-29\A.An - Don Gap\20x30\` (6 file)
* **Thao tác:**
  1. Đếm số thẻ đơn hàng của khách Văn An trên Dashboard ngày `2026-09-29`.
  2. Vào tab **📊 Báo cáo doanh thu** -> Báo cáo ngày `2026-09-29`.
* **Kỳ vọng:**
  - **Trên Dashboard:** Xuất hiện đủ **3 đơn hàng riêng biệt** (không bị gộp thành 1, không bị ghi đè dữ liệu).
  - **Trên Báo cáo:** Khách hàng **Văn An** được tổng hợp tổng sản lượng và doanh thu:
    - Đơn 1 (`Văn An`): 15 ảnh x 5.000 = `75,000 đ`
    - Đơn 2 (`Anh An`): 20 ảnh x 5.000 = `100,000 đ`
    - Đơn 3 (`A.An - Don Gap`): 6 ảnh x 15.000 = `90,000 đ`
    - **Tổng cộng khách Văn An:** 41 ảnh, `265,000 đ`.

---

#### Bài 04: Cấu trúc Đơn hàng con tường minh (Explicit Sub-orders)
* **Mục tiêu:** Kiểm tra cấu trúc phân cấp V2: `Khách hàng -> Đơn hàng con -> Sản phẩm`.
* **Đường dẫn thư mục:**
  - `TEST_DATA_LALAB\2026-09-29\Minh Studio\Don 01\13x18 in\` (8 file)
  - `TEST_DATA_LALAB\2026-09-29\Minh Studio\Don 02\20x30\` (6 file)
* **Kỳ vọng:**
  - Dashboard hiển thị rõ ràng 2 đơn hàng có tên **Don 01** và **Don 02** đều thuộc khách hàng **Minh Studio**.
  - `Don 01`: 8 ảnh 13x18 = `40,000 đ`.
  - `Don 02`: 6 ảnh 20x30 = `90,000 đ`.

---

### NHÓM B: THƯ MỤC TÍNH SỐ LƯỢNG ĐỘNG V2 (EFFECTIVE BILLING FOLDER RESOLUTION)

#### Bài 05: Thư mục sửa ảnh nhiều cấp sâu (Deepest Leaf Retouch)
* **Mục tiêu:** Thợ sửa ảnh qua nhiều vòng tạo ra thư mục lồng nhau: `sua-lan-1/sua-lan-2/in-final/`. App phải tự tìm thư mục lá sâu nhất chứa ảnh để tính tiền, không đếm các file thô ở cấp cha.
* **Đường dẫn thư mục:**
  `TEST_DATA_LALAB\2026-09-29\Quang Studio\20x30\sua-lan-1\sua-lan-2\in-final\` (12 file ảnh)  
  *(Tại `sua-lan-1\` có để sẵn 2 file thô `raw_01.jpg`, `raw_02.jpg`)*
* **Kỳ vọng:**
  - Hệ thống tự động chọn thư mục `.../in-final` làm thư mục tính số lượng.
  - Số lượng in = **12** (chỉ lấy 12 file trong `in-final`, **tuyệt đối không cộng thêm 2 file thô ở cấp cha**).
  - Thành tiền = 12 x 15.000 = `180,000 đ`.

---

#### Bài 06: Thư mục in mơ hồ tranh chấp (Ambiguous Print Folder)
* **Mục tiêu:** Khi thợ xuất ra 2 phương án in ở 2 thư mục lá ngang hàng (`ban_mau_am` và `ban_mau_lanh`), hệ thống không được tự ý đoán mò mà phải gắn cờ cảnh báo yêu cầu người dùng chọn.
* **Đường dẫn thư mục:**
  `TEST_DATA_LALAB\2026-09-29\Quang Studio\40x60 TG\` có 2 thư mục lá:
  - `ban_mau_am\` (5 file)
  - `ban_mau_lanh\` (5 file)
* **Thao tác:**
  1. Quan sát dòng `40x60 TG` trên Dashboard:
     - Biểu tượng cảnh báo màu cam: `AmbiguousPrintFolder` (Thư mục in mơ hồ).
     - Trạng thái đơn: **Cần xử lý (NeedsReview)**.
  2. Bấm vào nút **📂 Chọn thư mục in**.
  3. Trong hộp thoại, chọn nhánh `ban_mau_am`.
* **Kỳ vọng:**
  - Trạng thái chuyển sang **Sẵn sàng (Ready)**, badge chuyển sang **Thủ công**.
  - Số lượng in được chốt là **5**.
  - Thành tiền = 5 x 80.000 = `400,000 đ`.

---

#### Bài 07: Tiến trình thư mục động không bị dính (Anti-Stickiness)
* **Mục tiêu:** Chứng minh cơ chế V2: Khi thợ tiếp tục sửa ảnh và tạo thư mục con sâu hơn, lượt quét lại (Rescan) phải tự động đi theo nhánh mới, không bị "dính" vào thư mục cũ.
* **Các bước thực hiện:**
  1. Đơn `Quang Studio\13x18 in` ban đầu đang có 10 ảnh ở thư mục gốc (Thư mục in: `13x18 in`, 10 ảnh = 50.000 đ).
  2. Mở File Explorer, vào `TEST_DATA_LALAB\2026-09-29\Quang Studio\13x18 in\`.
  3. Tạo thư mục con mới: `sua-lai` và copy vào đó **15 file ảnh**.
  4. Quay lại ứng dụng, bấm **🔍 Quét Ngày** (hoặc Quét lại đơn).
* **Kỳ vọng:**
  - Hệ thống **tự động chuyển thư mục tính số lượng** sang `13x18 in\sua-lai`.
  - Số lượng in tự động cập nhật từ 10 thành **15** ảnh.
  - Thành tiền tự động nhảy từ 50.000 đ lên **75,000 đ**.

---

#### Bài 08: Lọc file rác, file hệ thống & Xử lý chữ HOA/thường
* **Mục tiêu:** Thư mục in có chứa lẫn file văn bản, file hệ thống, file Photoshop và đuôi tệp chữ hoa (`.JPG`, `.PNG`). Hệ thống chỉ đếm file ảnh hợp lệ.
* **Đường dẫn thư mục:**
  `TEST_DATA_LALAB\2026-09-29\Quang Studio\15x21 in\` gồm 7 file:
  - Hợp lệ: `photo_01.JPG`, `photo_02.jpeg`, `photo_03.PNG` (3 file)
  - Không hợp lệ: `notes.txt`, `Thumbs.db`, `design.psd`, `backup.tmp` (4 file)
* **Kỳ vọng:**
  - Số lượng in nhận diện chính xác là **3** file.
  - Bỏ qua hoàn toàn 4 file rác và văn bản.
  - Thành tiền = 3 x 10.000 = `30,000 đ`.

---

#### Bài 09: Thư mục rỗng / Không có ảnh in (No Print Folder)
* **Mục tiêu:** Thợ tạo thư mục quy cách nhưng bên trong chỉ có file text ghi chú hoặc rỗng, chưa bỏ ảnh in vào.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Quang Studio\50x75 in\` (chỉ có 1 file `readme.txt`)
* **Kỳ vọng:**
  - Dòng sản phẩm bị gắn cờ: **NoPrintFolder** (Không tìm thấy ảnh in).
  - Số lượng in = 0, trạng thái báo đỏ / vàng.
  - Ứng dụng **chặn không cho phép khóa đơn hàng này**.

---

### NHÓM C: TÍNH GIÁ ALBUM CHUYÊN SÂU V2 (ALBUM PRICING MATRIX)

> [!IMPORTANT]
> **Quy tắc tính giá Album V2:**
> - $1 \text{ thư mục album} = 1 \text{ cuốn album vật lý}$ (`BillQuantity = 1`).
> - $\text{Số tờ in } (N)$ = Tổng số file ảnh trong thư mục in cuối cùng (**không trừ file bìa**).
> - $\text{Số tờ phát sinh} = \max(N - \text{IncludedSheets}, 0)$.
> - $\text{Thành tiền} = \text{BasePrice} + (\text{Số tờ phát sinh} \times \text{ExtraSheetPrice})$.

#### Bài 10: Album đúng số trang chuẩn (Included Sheets)
* **Mục tiêu:** Cuốn album có đúng 10 tờ in, bằng đúng số trang chuẩn trong gói.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Kim Studio\Album 20x20\retouch\` (chứa đúng 10 file `.jpg`)
* **Số liệu có sẵn:** Gói cơ bản: `400,000 đ` (10 tờ), Tờ phát sinh: `20,000 đ`/tờ.
* **Kỳ vọng:**
  - Số lượng cuốn: **1**.
  - Số tờ in: **10** tờ.
  - Phát sinh: **0** tờ.
  - Thành tiền: **400,000 đ**.

---

#### Bài 11: Album vượt trang phát sinh (Extra Sheets Calculation)
* **Mục tiêu:** Khách làm album dày hơn, in 14 tờ (vượt 4 tờ so với gói 10 tờ chuẩn).
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Kim Studio\Album 20x20 - Bo 2\in\` (chứa 14 file `.jpg`)
* **Công thức tính:**
  $$\text{ExtraSheets} = 14 - 10 = 4 \text{ tờ}$$
  $$\text{Thành tiền} = 400,000 + (4 \times 20,000) = 480,000 \text{ đ}$$
* **Kỳ vọng:**
  - Số tờ in hiển thị: **14** tờ.
  - Số tờ phát sinh: **+4** tờ.
  - Thành tiền: **480,000 đ**.

---

#### Bài 12: Album thiếu trang so với chuẩn (< Included Sheets)
* **Mục tiêu:** Khách làm album mỏng, chỉ có 8 tờ in (ít hơn gói 10 tờ).
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Kim Studio\Album 20x20 - Bo 3\final\` (chứa 8 file `.jpg`)
* **Quy tắc nghiệp vụ:** Tính theo giá sàn trọn gói cơ bản, kèm cảnh báo nhắc nhở nhẹ.
* **Kỳ vọng:**
  - Số tờ in: **8** tờ.
  - Số tờ phát sinh: **0** tờ.
  - Thành tiền: **400,000 đ** (giá sàn trọn gói, không bị trừ âm tiền).
  - Bill xuất gởi khách: hiển thị `Album 20x20 — 8 tờ` với thành tiền `400,000 đ`, **không ghi chú dòng gói chuẩn 10 tờ** (tránh làm khách thắc mắc).

---

#### Bài 13: Đơn hàng kết hợp nhiều cuốn Album khác kích thước
* **Mục tiêu:** Trong cùng 1 đơn hàng có cả Album 20x20 và Album 25x25.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Minh Studio\Don 03 - Tron Goi\` gồm:
  - `Album 20x20\` (10 file in) -> Giá gói 10 tờ: `400,000 đ`
  - `Album 25x25\` (13 file in) -> Gói 10 tờ: `500,000 đ` + (3 tờ vượt x 25.000 đ) = `575,000 đ`
* **Kỳ vọng:**
  - Đơn hiển thị rõ ràng 2 dòng album riêng biệt:
    - Dòng 1 (Album 20x20): `400,000 đ`
    - Dòng 2 (Album 25x25): `575,000 đ`
  - Tổng tiền đơn `Don 03 - Tron Goi` = **975,000 đ**.

---

### NHÓM D: XỬ LÝ NGOẠI LỆ & BỔ SUNG DANH MỤC (BUSINESS EXCEPTIONS)

#### Bài 14: Quy cách lạ chưa có trong bảng giá (Unresolved Specification)
* **Mục tiêu:** Thợ đặt tên thư mục sản phẩm mới mà xưởng chưa nhập giá vào bảng giá.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Quang Studio\Tranh Mica 50x75\` (4 file ảnh)
* **Thao tác & Kỳ vọng:**
  1. Trên Dashboard: Dòng sản phẩm `Tranh Mica 50x75` bị gắn cờ màu vàng: **UnresolvedSpecification** (Chưa liên kết bảng giá).
  2. Đơn hàng bị chặn khóa.
  3. **Khắc phục:**
     - Sang tab **🏷️ Bảng giá quy cách**.
     - Bấm **➕ Thêm quy cách mới**: Tên: `Tranh Mica 50x75`, Đơn giá: `120,000 đ`. Bấm Lưu.
     - Quay lại tab **📋 Quét & Đơn hàng**, bấm nút **🔄 Quét lại quy cách** tại dòng đó.
  4. Trạng thái chuyển sang hợp lệ, thành tiền tính đúng: $4 \times 120,000 = \mathbf{480,000 \text{ đ}}$.

---

#### Bài 15: Khách hàng mới toanh chưa có danh bạ (Unresolved Customer)
* **Mục tiêu:** Khách hàng mới lần đầu gửi in ảnh, tên thư mục chưa có trong hệ thống.
* **Đường dẫn thư mục:** `TEST_DATA_LALAB\2026-09-29\Studio Sen Vang\13x18 in\` (12 file ảnh)
* **Thao tác & Kỳ vọng:**
  1. Trên Dashboard: Đơn hàng hiển thị cờ **UnresolvedCustomer** (Khách hàng chưa giải quyết).
  2. Ứng dụng **tuyệt đối không tự ý gộp** khách mới này vào khách cũ.
  3. **Khắc phục:**
     - Sang tab **👥 Khách hàng & Hóa Đơn**.
     - Bấm **➕ Thêm khách hàng**: Nhập `Studio Sen Vàng`, SĐT: `0988123456`, Alias: `Studio Sen Vang`.
     - Quay lại Dashboard bấm **🔍 Quét Ngày**.
  4. Đơn hàng nhận diện chính xác `Studio Sen Vàng`, thành tiền: $12 \times 5,000 = \mathbf{60,000 \text{ đ}}$.

---

### NHÓM E: KHÓA ĐƠN, BẤT BIẾN & BÁO CÁO (LOCK INVARIANCE & REPORTING)

#### Bài 16: Khóa Đơn & Kiểm tra Tính Bất Biến (Lock & Tamper-Proofing)
* **Mục tiêu:** Chứng minh: **Hóa đơn đã khóa là lịch sử bất biến**. Nếu can thiệp thêm/bớt file ngoài ổ cứng sau khi đã khóa, số tiền lịch sử vẫn được bảo toàn nguyên vẹn.
* **Các bước thực hiện:**
  1. **Khóa đơn:** Tại Dashboard ngày `2026-09-29`, tìm đơn của `Anh An` (20 file `13x18 in`, thành tiền `100,000 đ`). Bấm nút **🔒 Khóa Đơn**. Thẻ chuyển sang trạng thái xanh **Đã Khóa (Locked)**.
  2. **Can thiệp ngoài đĩa:** Mở thư mục `TEST_DATA_LALAB\2026-09-29\Anh An\13x18 in\`, tạo thêm 5 file ảnh mới (tổng thành 25 file).
  3. **Quét lại để kiểm tra:** Quay lại ứng dụng, bấm **🔍 Quét Ngày**.
* **Kỳ vọng:**
  - Đơn hàng xuất hiện cờ cảnh báo: ⚠️ **FilesystemChangedAfterLock (Dữ liệu đĩa đã thay đổi sau khi khóa)**.
  - **SỐ TIỀN VÀ SỐ LƯỢNG ĐÃ KHÓA TUYỆT ĐỐI BẤT BIẾN**: Số lượng vẫn là **20**, Thành tiền vẫn là **100,000 đ**!

---

#### Bài 17: Mở lại đơn đã khóa (Reopen Order Workflow)
* **Mục tiêu:** Chỉnh sửa đơn đã khóa thông qua quy trình mở khóa có lý do rõ ràng.
* **Các bước thực hiện:**
  1. Tại đơn `Anh An` vừa bị sửa file ở Bài 16, bấm nút **🔓 Mở Lại Đơn**.
  2. Hộp thoại yêu cầu nhập lý do: Nhập *"Khách gửi in bổ sung 5 ảnh"*. Bấm Xác nhận.
  3. Bấm **Quét lại đơn**: Số lượng được cập nhật thành **25** ảnh, Thành tiền thành **125,000 đ**.
  4. Bấm **🔒 Khóa Đơn** lại -> Đơn được khóa mới an toàn.

---

#### Bài 18: Báo cáo Doanh thu & Phát hiện Ngày Bỏ Sót (Missing Days Discovery)
* **Mục tiêu:** Kiểm tra Báo cáo đọc 100% từ SQLite WAL (tốc độ mở tức thì, không quét đĩa) và khả năng tự động phát hiện các ngày thư mục trên đĩa chưa được quét.
* **Dữ liệu chuẩn bị:** Script đã tạo sẵn ngày `TEST_DATA_LALAB\2026-09-30\`, ngày này chưa từng được quét vào database.
* **Các bước thực hiện:**
  1. Vào tab **📊 Báo cáo doanh thu** -> Chọn **Báo cáo Tháng** -> Tháng: `09/2026`.
* **Kỳ vọng:**
  - Tốc độ mở tức thì (dưới 0.1 giây), 0% CPU quét đĩa.
  - Phía trên xuất hiện thanh cảnh báo màu vàng:
    > ⚠️ *Phát hiện ngày có trên ổ cứng nhưng chưa quét vào hệ thống: **2026-09-30**.*
  - Bấm nút **⚡ Quét các ngày còn thiếu** -> Hệ thống tự động quét bù ngày `2026-09-30`. Doanh thu tháng tự động cộng thêm số liệu ngày 30.

---

#### Bài 19: Sao lưu & Phục hồi CSDL (Backup & Disaster Recovery)
* **Mục tiêu:** Đảm bảo dữ liệu đơn hàng và doanh thu luôn được sao lưu an toàn.
* **Thao tác:**
  1. Vào tab **⚙️ Cài đặt hệ thống**.
  2. Bấm nút **⚡ Sao Lưu Ngay**.
  3. Kiểm tra danh sách bản sao lưu: Xuất hiện bản backup mới với ngày giờ hiện tại.
  4. Mở thư mục `%LocalAppData%\LalabAutoReport\backups\` để xác nhận file `.bak` đã được tạo.
  5. Bấm nút **Khôi phục** -> Hệ thống tự động sao lưu dự phòng trước khi khôi phục và báo thành công.

---

### NHÓM F: TÍNH & XUẤT HÓA ĐƠN THEO KHÁCH HÀNG (CUSTOMER-CENTRIC BILLING & JPEG EXPORT)

#### Bài 20: Gom đơn đa ngày/alias, Điều chỉnh chi phí & Xuất ảnh JPEG 1080px
* **Mục tiêu:** Kiểm tra quy trình lập hóa đơn tổng hợp cho 1 khách hàng: gom các đơn chưa bill từ nhiều ngày/alias, ghi đè số lượng/đơn giá không phá hủy, thêm phụ phí/ship/giảm giá, và xuất ảnh JPEG vector 1080px chuẩn Tinix.
* **Dữ liệu kiểm thử có sẵn cho khách "Văn An":**
  - Đơn ngày cũ `2026-09-25`: `Văn An\13x18 in` (10 ảnh x 5.000 đ = 50.000 đ)
  - Đơn 1 ngày `2026-09-29`: `Văn An\13x18 in` (15 ảnh x 5.000 đ = 75.000 đ)
  - Đơn 2 ngày `2026-09-29`: `Anh An\13x18 in` (20 ảnh x 5.000 đ = 100.000 đ)
  - Đơn 3 ngày `2026-09-29`: `A.An - Don Gap\20x30` (6 ảnh x 15.000 đ = 90.000 đ)
* **Các bước thực hiện:**
  1. Vào tab **👥 Khách hàng & Hóa đơn**.
  2. Chọn khách hàng: **Văn An**.
  3. Quan sát khung **Hóa đơn & Đơn hàng chưa tính bill**:
     - Hiển thị: 4 đơn chưa thanh toán, tổng ước tính = `315,000 đ`.
  4. Bấm nút **⚡ Tính Bill Khách Hàng**:
     - Cửa sổ **Duyệt Hóa Đơn Khách Hàng (CustomerBillReviewWindow)** mở ra.
     - Thấy cảnh báo màu vàng: ⚠️ *Có 1 đơn hàng từ kỳ trước (2026-09-25).*
     - Danh sách hiển thị đủ 4 đơn hàng con vật lý, kèm ngày và tên thư mục gốc.
  5. **Thực hiện điều chỉnh hóa đơn:**
     - Tại dòng 15 ảnh của đơn `Văn An`, nhấp đúp vào ô Số lượng thanh toán -> Đổi thành **18** ảnh, nhập lý do: *"Bù ảnh hỏng"*.
     - Tại mục **Điều chỉnh & Phụ phí**:
       - Phí vận chuyển (+): Nhập `30,000 đ`
       - Phụ phí gấp (+): Nhập `20,000 đ`
       - Giảm giá (-): Nhập `50,000 đ`
     - Tổng thanh toán tự động cập nhật:
       $$\text{GrandTotal} = \text{Tiền hàng (đã điều chỉnh)} + 30,000 + 20,000 - 50,000$$
  6. **Bấm '💾 Khóa & Xuất Bill (JPEG)':**
     - Hệ thống kiểm tra hợp lệ -> Khóa hóa đơn (Locked) -> Chụp snapshot SQLite -> Render ảnh JPEG.
     - File ảnh JPEG được lưu tại thư mục: `TEST_DATA_LALAB\_BILLS\BILL-20260929-0001_Van-An.jpg`.
  7. **Kiểm tra file JPEG xuất ra:**
     - Độ rộng chuẩn xác: **1080px**.
     - Typography chuẩn Tinix Design, hiển thị đầy đủ thông tin: Logo xưởng, Tên khách hàng `Văn An`, bảng từng đơn hàng, các khoản phụ phí/giảm giá, và tổng tiền thanh toán rõ ràng, **không bị cắt chữ**.
  8. **Kiểm tra tính năng Xuất lại (Re-export):**
     - Trong lịch sử hóa đơn của khách Văn An, bấm nút **🖼️ Xuất lại ảnh**.
     - File ảnh được xuất lại ngay lập tức từ dữ liệu SQLite snapshot mà **không quét lại ổ đĩa**.

---

### NHÓM G: QUICK BILL / TÍNH BILL KHÁCH LẺ (GUEST BILL WORKFLOW)

#### Bài 21: Tính Bill Khách Lẻ từ Đa Thư Mục Nguồn (Multi-Source Quick Bill)
* **Mục tiêu:** Khách vãng lai in gấp từ nhiều thư mục rời rạc (`Chi Lan 1` và `Chi Lan them`). Xuất bill ngay mà **tuyệt đối không tạo hồ sơ khách hàng trong database**, không làm ô nhiễm danh bạ.
* **Đường dẫn thư mục mẫu:**
  - `TEST_DATA_LALAB\KHACH_LE\Chi Lan 1\13x18 in` (10 file x 5.000 đ = 50.000 đ)
  - `TEST_DATA_LALAB\KHACH_LE\Chi Lan them\13x18 in` (8 file x 5.000 đ = 40.000 đ)
* **Các bước thực hiện:**
  1. Trên thanh công cụ Dashboard (hoặc tab Khách Hàng), bấm nút **⚡ Quick Bill**.
  2. Cửa sổ **Thiết Lập Quick Bill (Khách Lẻ)** xuất hiện.
  3. Bấm nút **➕ Thêm thư mục nguồn...**:
     - Chọn thư mục: `TEST_DATA_LALAB\KHACH_LE\Chi Lan 1`
  4. Bấm tiếp nút **➕ Thêm thư mục nguồn...**:
     - Chọn thư mục: `TEST_DATA_LALAB\KHACH_LE\Chi Lan them`
  5. Tên khách lẻ tự động điền: `Chi Lan 1`.
     - Sửa trực tiếp ô tên khách thành: **Chị Lan VIP Quận 7**.
  6. Bấm nút **⚡ Quét & Tính Bill Nháp**:
     - Cửa sổ Duyệt Hóa Đơn mở ra với nhãn nổi bật: **⚡ KHÁCH LẺ**.
     - Danh sách hiển thị đủ 2 đơn nguồn: `Chi Lan 1` (10 ảnh) và `Chi Lan them` (8 ảnh).
     - Tổng tiền hàng: 18 ảnh x 5.000 đ = **90,000 đ**.
  7. Bấm nút **💾 Khóa & Xuất Bill (JPEG)**:
     - Hệ thống khóa bill và xuất file ảnh: `_BILLS\BILL-20260929-0002_Chi-Lan-VIP-Quan-7.jpg`.
  8. **Kiểm chứng cô lập khách hàng (Customer Isolation):**
     - Đóng cửa sổ bill, vào tab **👥 Khách hàng & Hóa đơn**.
     - Kiểm tra danh sách khách hàng: **Hoàn toàn KHÔNG xuất hiện khách nào tên "Chi Lan 1" hay "Chị Lan VIP"**!
     - Bảng `customers` trong CSDL hoàn toàn sạch 100%.

---

#### Bài 22: Cảnh Báo Thư Mục Trùng Lặp & Chuyển Khách Lẻ Thành Khách Hàng
* **Mục tiêu:**
  1. Phát hiện và cảnh báo khi người dùng vô tình tính bill cho một thư mục đã từng được thanh toán trước đó.
  2. Nâng cấp một hóa đơn khách lẻ thành khách hàng chính thức khi khách muốn mở tài khoản thân thiết.
* **Các bước thực hiện:**

**Phần 1: Kiểm thử Phát hiện Trùng Lặp (Duplicate Folder Warning):**
1. Bấm lại nút **⚡ Quick Bill**.
2. Bấm **➕ Thêm thư mục nguồn...** và chọn lại thư mục `TEST_DATA_LALAB\KHACH_LE\Chi Lan 1` (vừa được xuất bill ở Bài 21).
3. **Kỳ vọng:**
   - Hộp thoại lập tức hiển thị cảnh báo màu đỏ:
     > ⚠️ *Cảnh báo trùng lặp: Thư mục này đã từng được tính trong hóa đơn: **BILL-20260929-0002** (Ngày: 2026-09-29, Tổng: 90,000 đ).*
   - Có nút **🔍 Mở bill cũ** -> Bấm vào để xem lại hóa đơn đã xuất trước đó.

**Phần 2: Kiểm thử Chuyển Khách Lẻ thành Khách Hàng (Convert to Customer):**
1. Bấm **⚡ Quick Bill**, chọn thư mục: `TEST_DATA_LALAB\KHACH_LE\Chi Mai Wedding` (chứa 4 ảnh quy cách `20x30`, 4 x 15.000 = `60,000 đ`).
2. Bấm **⚡ Quét & Tính Bill Nháp**.
3. Tại thanh tiêu đề trên cùng của cửa sổ Review, bấm nút **👤 Chuyển thành Khách Hàng**.
4. Hộp thoại xuất hiện: Nhập tên khách hàng chuẩn: **Studio Mai Wedding**.
5. Bấm **Xác nhận**:
   - Bill được chuyển đổi từ loại `GUEST` sang `CUSTOMER`.
   - Khách hàng `Studio Mai Wedding` được tạo mới trong danh bạ với đầy đủ liên kết tới các đơn hàng.
   - Kiểm tra tab Khách hàng: `Studio Mai Wedding` xuất hiện chính thức trong danh sách kèm hóa đơn vừa lập!

---

## IV. BẢNG CHECKLIST NGHIỆM THU 22 BÀI (PASS / FAIL ACCEPTANCE MATRIX)

| STT | Bài Kiểm Thử | Trọng Tâm Nghiệp Vụ | Kết Quả Mong Đợi | Kết Quả Thực Tế | Đánh Giá |
|:---:|:---|:---|:---|:---:|:---:|
| **1** | Ảnh in phẳng chuẩn | Happy path | Đếm 10 ảnh, 50.000 đ | `[ ]` | Đạt / Không |
| **2** | Nhận diện Khách qua Alias | Tên thợ gõ tắt | Tự map về khách `Văn An` | `[ ]` | Đạt / Không |
| **3** | Cùng khách nhiều thư mục | Bảo toàn Provenance | 3 đơn trên Dashboard, gộp 1 trên Báo cáo | `[ ]` | Đạt / Không |
| **4** | Đơn hàng con tường minh | Explicit Sub-orders V2 | Nhận diện `Don 01`, `Don 02` | `[ ]` | Đạt / Không |
| **5** | Thư mục sửa nhiều cấp | Deepest Leaf Folder | Chỉ lấy 12 ảnh ở `in-final`, bỏ qua file thô | `[ ]` | Đạt / Không |
| **6** | Thư mục in mơ hồ | Ambiguous Print Folder | Báo cờ cam, cho phép chọn nhánh in | `[ ]` | Đạt / Không |
| **7** | Tiến trình động Anti-stickiness | Dynamic Progression | Quét lại tự nhảy sang thư mục sửa mới | `[ ]` | Đạt / Không |
| **8** | Lọc file rác & Chữ HOA | Case-insensitive & Junk filter | Đếm 3 ảnh, bỏ qua `.txt`, `.tmp`, `.psd` | `[ ]` | Đạt / Không |
| **9** | Thư mục rỗng | No Print Folder | Cảnh báo lỗi, chặn khóa đơn | `[ ]` | Đạt / Không |
| **10** | Album đúng số trang chuẩn | Gói Album cơ bản V2 | 10 tờ = 400.000 đ | `[ ]` | Đạt / Không |
| **11** | Album vượt trang phát sinh | Phụ trội trang V2 | 14 tờ = 400k + (4x20k) = 480.000 đ | `[ ]` | Đạt / Không |
| **12** | Album thiếu trang | Giá sàn tối thiểu | 8 tờ = 400.000 đ (giá sàn) | `[ ]` | Đạt / Không |
| **13** | Đa Album trong 1 đơn | Tính tiền ma trận nhiều album | Album 20x20 (400k) + Album 25x25 (575k) = 975k | `[ ]` | Đạt / Không |
| **14** | Quy cách in lạ | Unresolved Specification | Cảnh báo thiếu giá, thêm giá xong quét lại OK | `[ ]` | Đạt / Không |
| **15** | Khách hàng mới | Unresolved Customer | Không tự ý gộp khách, hỗ trợ thêm danh bạ | `[ ]` | Đạt / Không |
| **16** | Khóa đơn & Bất biến | Lock & Invariance | Báo `FilesystemChangedAfterLock`, số tiền giữ nguyên | `[ ]` | Đạt / Không |
| **17** | Mở lại đơn đã khóa | Reopen Order Workflow | Mở khóa có lý do, cập nhật file mới | `[ ]` | Đạt / Không |
| **18** | Phát hiện ngày bỏ sót | Missing Days on Report | Báo ngày `2026-09-30`, cho phép quét bù 1 chạm | `[ ]` | Đạt / Không |
| **19** | Sao lưu & Phục hồi | Backup & Restore | Tạo bản sao lưu `.bak`, khôi phục an toàn | `[ ]` | Đạt / Không |
| **20** | Tính bill khách hàng & JPEG | Customer Billing & Export | Gom đơn đa ngày, điều chỉnh phí, xuất JPEG 1080px | `[ ]` | Đạt / Không |
| **21** | Quick Bill đa thư mục nguồn | Multi-Source Guest Bill | 2 thư mục gom 1 bill, CSDL khách hàng sạch 100% | `[ ]` | Đạt / Không |
| **22** | Trùng thư mục & Chuyển khách | Duplicate Check & Convert | Cảnh báo trùng bill cũ, chuyển khách lẻ thành khách quen | `[ ]` | Đạt / Không |

---

## V. XỬ LÝ SỰ CỐ THƯỜNG GẶP (TROUBLESHOOTING)

### 1. Bấm "Quét Ngày" nhưng thông báo: *"Không tìm thấy thư mục ngày: 'YYYY-MM-DD' trong thư mục gốc"*
* **Nguyên nhân:** Thư mục gốc chưa trỏ đúng vào thư mục `TEST_DATA_LALAB`, hoặc ngày đang chọn trên lịch không trùng với ngày có trong thư mục test.
* **Cách xử lý:** 
  1. Vào **⚙️ Cài đặt hệ thống** kiểm tra lại đường dẫn Thư mục gốc (phải là `.../TEST_DATA_LALAB`).
  2. Trên Dashboard, bấm nút mũi tên hoặc chọn đúng ngày `2026-09-29` trên ô chọn ngày.

### 2. File ảnh không được đếm hoặc số lượng bằng 0
* **Nguyên nhân:** Đuôi file ảnh thực tế trên máy tính (ví dụ `.HEIC`, `.jfif`) chưa được thêm vào danh sách đuôi ảnh hỗ trợ.
* **Cách xử lý:** Vào **⚙️ Cài đặt hệ thống**, thêm đuôi file vào danh sách hỗ trợ, ví dụ: `.jpg, .jpeg, .png, .tif, .tiff, .bmp, .webp, .heic, .jfif` rồi bấm Lưu.

### 3. Đơn hàng không khóa được, báo nút khóa bị mờ (Disabled)
* **Nguyên nhân:** Đơn hàng đang còn ít nhất một cảnh báo chưa xử lý: Khách hàng chưa map, Sản phẩm chưa có giá, Thư mục in bị mơ hồ, hoặc Thư mục rỗng.
* **Cách xử lý:** Xử lý hết các cờ màu vàng/cam như hướng dẫn trong Bài 06, 14, 15. Khi thẻ chuyển sang màu xanh **Sẵn sàng (Ready)**, nút Khóa đơn sẽ tự động kích hoạt.

### 4. Muốn làm sạch toàn bộ dữ liệu test để chạy lại từ đầu
* **Cách xử lý:**
  1. Tắt ứng dụng Lalab Auto Report.
  2. Mở PowerShell chạy lại lệnh:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\tao_thu_muc_test_mau.ps1
     ```
  3. Nếu muốn xóa sạch toàn bộ lịch sử database để nạp mới:
     - Xóa file: `%LocalAppData%\LalabAutoReport\lalab_autoreport.db`.
     - Chạy lại script `tao_thu_muc_test_mau.ps1` để tự động tạo lại DB và thư mục sạch sẽ.

---
*Chúc bạn có trải nghiệm kiểm thử hiệu quả và tin cậy cùng Lalab Auto Report!*
