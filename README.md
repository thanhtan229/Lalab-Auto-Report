# Lalab Auto Report

Ứng dụng Windows Desktop (.NET 8 LTS, WPF, SQLite WAL) dành cho xưởng in ảnh chuyên nghiệp. Ứng dụng tự động hóa quy trình rà soát đơn hàng, đối soát số lượng file ảnh in, tính tiền và lập báo cáo tài chính/sản lượng từ cấu trúc thư mục thực tế của xưởng mà không can thiệp hay thay đổi file của khách hàng.

---

## 1. Nguyên Tắc Cốt Lõi (Core Principles)

1. **Tính chính xác của hóa đơn đặt lên hàng đầu**: Không bao giờ tự động suy đoán khi cấu trúc thư mục mơ hồ.
2. **Bảo toàn nguồn gốc đơn hàng (Provenance)**: Mỗi thư mục khách hàng vật lý dưới một ngày là **một Đơn hàng riêng biệt**. Nếu một khách hàng có nhiều thư mục con trong ngày (`Văn An`, `Anh An`, `A.An`), cả 3 vẫn là 3 Đơn hàng riêng biệt, chỉ được tổng hợp định danh trên báo cáo.
3. **1 File ảnh = 1 Bản in**: Trong phiên bản V1, mỗi file ảnh hỗ trợ trực tiếp được tính là số lượng 1.
4. **Không đọc giải mã pixel ảnh**: Quá trình quét chỉ đọc metadata thư mục và file hệ thống, không render thumbnail hay giải mã nội dung ảnh, đảm bảo tốc độ cực nhanh và tiêu hao tài nguyên CPU/RAM tối thiểu.
5. **Smart Scan thủ công - Không quét ngầm liên tục**: Ứng dụng ở trạng thái tĩnh hoàn toàn (0% CPU/Disk) khi không có thao tác quét của người dùng. Mở báo cáo tháng đọc trực tiếp từ SQLite chứ không quét lại toàn bộ cây thư mục ảnh.
6. **Lịch sử bất biến khi đã Khóa**: Hóa đơn sau khi khóa trở thành một bản snapshot lịch sử. Quét lại thư mục sau này nếu có thay đổi file trên ổ cứng sẽ cảnh báo `FilesystemChangedAfterLock` chứ không âm thầm làm sai lệch số tiền đã khóa.
7. **Tiền tệ VND là số nguyên**: Toàn bộ số tiền và đơn giá được lưu trữ và tính toán dưới dạng số nguyên (long), tuyệt đối không dùng số thực dấu phẩy động.

---

## 2. Quy Ước Cấu Trúc Thư Mục (Folder Structure)

```text
RootFolder/
└── 2026-09-28/                         <-- Thư mục ngày (YYYY-MM-DD)
    ├── Văn An/                         <-- Thư mục khách hàng = 1 Đơn hàng
    │   ├── 13x18 in/                   <-- Quy cách in (Specification)
    │   │   ├── photo01.jpg             <-- Source files (chỉ đếm trực tiếp)
    │   │   ├── photo02.jpg
    │   │   └── in/                     <-- Thư mục in (lá sâu nhất chứa ảnh)
    │   │       ├── photo01_retouch.jpg
    │   │       └── photo02_retouch.jpg
    │   └── 40x60 TG/
    │       └── in/
    └── Anh An/                         <-- Cùng khách Văn An, nhưng là Đơn hàng số 2
        └── 20x30/
```

- **SourceCount**: Số lượng file ảnh hỗ trợ nằm **trực tiếp** trong thư mục quy cách (không đệ quy).
- **PrintFolder**: Thư mục lá sâu nhất chứa ảnh in hợp lệ. Nếu phân nhánh tạo ra nhiều thư mục lá chứa ảnh (`edit-a`, `edit-b`), ứng dụng gắn cờ `AmbiguousPrintFolder` và yêu cầu người dùng chọn thư mục in chính thức.
- **Mismatch**: Nếu `SourceCount == PrintCount`, số lượng tính tiền được tự động khớp. Nếu khác nhau, người dùng phải xác nhận chọn một trong ba chế độ:
  - `USE_PRINT`: Tính theo số lượng thư mục in.
  - `USE_SOURCE`: Tính theo số lượng file gốc.
  - `CUSTOM`: Nhập số lượng chỉ định thủ công.

---

## 3. Kiến Trúc Ứng Dụng (.NET 8 Clean Architecture)

```text
src/
├── LalabAutoReport.Core/            # Domain models, Enums, Interfaces, và Business Services
│   ├── Domain/                     # Models (Order, Bill, Customer, PrintSpecification, v.v.)
│   ├── Interfaces/                 # Hợp đồng trừu tượng (Scanner, Billing, Locking, v.v.)
│   └── Services/                   # Logic nghiệp vụ độc lập, kiểm thử 100% tự động
├── LalabAutoReport.Infrastructure/  # Triển khai tầng hạ tầng
│   ├── Data/                       # SQLite WAL mode, Dapper, Migrations, BackupService
│   ├── FileSystem/                 # PhysicalFileSystemAdapter (chống crash truy cập, path dài)
│   └── Logging/                    # Serilog rolling file logger
└── LalabAutoReport.UI/              # Giao diện WPF MVVM (CommunityToolkit.Mvvm)
    ├── ViewModels/                 # Dashboard, Reports, Customers, PriceList, Settings
    └── Views/                      # XAML Controls, bảng kê, modal đối soát, xuất bản
tests/
└── LalabAutoReport.Tests/          # 52 Automated Unit, Integration & Hardening Tests
```

---

## 4. Hướng Dẫn Vận Hành & Tính Năng

### 4.1 Cài Đặt Ban Đầu (Settings)
1. Mở tab **Cài đặt**.
2. Chọn **Thư mục gốc chứa ảnh (Root Folder)** (ví dụ: `D:\WORK_2026\ORDERS`).
3. Khai báo danh sách đuôi ảnh hỗ trợ (mặc định: `.jpg, .jpeg, .png, .tif, .tiff, .psd, .bmp, .webp`).
4. Nhấn **Lưu Cài Đặt**.

### 4.2 Bảng Giá Quy Cách In (Price List)
1. Mở tab **Bảng giá**.
2. Nhập các quy cách in chuẩn của xưởng (ví dụ: `13x18 in`, `20x30 ép gỗ`, `Album 30x30`) và đơn giá tương ứng (VND).
3. Thêm các biệt danh (Aliases) thường gặp trong tên thư mục thợ đặt (ví dụ: `13x18`, `13x18in`, `13-18`).

### 4.3 Quản Lý Khách Hàng (Customers & Aliases)
1. Mở tab **Khách hàng**.
2. Thêm khách hàng chính thức và số điện thoại.
3. Khi quét thư mục nếu gặp tên mới, ứng dụng sẽ đề xuất gợi ý nhưng **không bao giờ tự ý gộp khách hàng**. Người dùng xác nhận lưu alias để tự động nhận diện vào các lần quét sau.

### 4.4 Quét & Kiểm Tra Đơn Hàng Hàng Ngày (Dashboard)
1. Chọn ngày cần làm việc (ví dụ: `2026-09-28`).
2. Nhấn **⚡ Quét Ngày**.
3. Xem danh sách các đơn hàng theo khách hàng.
4. Đối với các đơn hàng có cảnh báo:
   - **Lệch số lượng**: Nhấp để chọn `Dùng số in`, `Dùng số gốc`, hoặc `Nhập số lượng khác`.
   - **Thư mục in mơ hồ**: Nhấp chọn thư mục in chính xác trong danh sách thư mục lá được tìm thấy.
5. Sau khi kiểm tra xong:
   - Nhấn **Khóa đơn**: Hệ thống thực hiện một lượt **Quét & Xác Thực tươi (Scan & Verify Before Lock)** ngay trên ổ đĩa. Nếu không có lỗi, hóa đơn được chốt vĩnh viễn vào SQLite.

### 4.5 Báo Cáo Tài Chính & Sản Lượng (Reports)
1. Mở tab **Báo cáo**.
2. Hỗ trợ xem theo:
   - **Báo cáo Tháng**: Xem doanh thu, tổng số lượng in, biểu đồ bảng theo từng ngày và từng khách hàng.
   - **Báo cáo Ngày**: Chi tiết từng đơn và từng khách hàng trong ngày.
   - **Khoảng Ngày (Tùy chỉnh)**: Lọc theo kỳ thanh toán bất kỳ.
3. **Phát hiện ngày chưa quét (Missing Days)**:
   - Báo cáo tháng tự động kiểm tra danh sách thư mục ngày trên ổ cứng so với database.
   - Nếu có ngày chưa quét, nút **⚡ Quét các ngày còn thiếu** cho phép quét bù nhanh chóng mà không cần quét lại cả tháng.

### 4.6 Sao Lưu & Phục Hồi Cơ Sở Dữ Liệu (Backup & Restore)
- **Vị trí file dữ liệu**: `%LocalAppData%\LalabAutoReport\lalab_autoreport.db`.
- **Tự động sao lưu trước migration**: Mỗi khi hệ thống cập nhật cấu trúc database, một bản sao lưu timestamped được tự động tạo.
- **Sao lưu thủ công**: Vào tab **Cài đặt** -> bấm **⚡ Sao Lưu Ngay**. File sao lưu được lưu tại `%LocalAppData%\LalabAutoReport\backups\`. Hệ thống tự động duy trì 20 bản sao lưu gần nhất.
- **Khôi phục**: Bấm **📂 Khôi Phục Từ File...** hoặc nút **Khôi phục** ở danh sách bản sao lưu. Trước khi ghi đè, hệ thống luôn tự động tạo thêm một bản pre-restore backup để đảm bảo an toàn tuyệt đối.

---

## 5. Hướng Dẫn Biên Dịch & Đóng Gói (Build & Packaging)

### Yêu Cầu Môi Trường
- Windows 10/11 x64
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Chạy Kiểm Thử Tự Động (Automated Tests)
Toàn bộ 52 test cases (từ bộ nhận diện thư mục, giải quyết xung đột alias, tính giá, khóa đơn, báo cáo database-only, đến kiểm thử chịu lỗi và backup):

```powershell
dotnet test
```

### Đóng Gói Bản Thực Thi Độc Lập (Self-Contained Single File)
Chạy lệnh sau để xuất bản file `.exe` duy nhất, không yêu cầu máy người dùng phải cài trước .NET:

```powershell
dotnet publish src/LalabAutoReport.UI/LalabAutoReport.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

File thực thi sau khi hoàn tất:
```text
src/LalabAutoReport.UI/bin/Release/net8.0-windows/win-x64/publish/LalabAutoReport.UI.exe
```
Người dùng chỉ cần copy file `LalabAutoReport.UI.exe` vào máy tính là có thể sử dụng ngay lập tức.
