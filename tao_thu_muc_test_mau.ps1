<#
.SYNOPSIS
    Script tạo bộ dữ liệu thư mục mẫu hoàn chỉnh (Test Dataset) để thực hành kiểm thử ứng dụng Lalab Auto Report.
.DESCRIPTION
    Tạo ra một cấu trúc thư mục gồm đầy đủ các kịch bản:
    - Ảnh in chuẩn (Happy path)
    - Khách hàng Alias
    - Cùng 1 khách nhiều thư mục trong ngày (Provenance)
    - Đơn con tường minh (Explicit Orders)
    - Thư mục sửa ảnh nhiều cấp (Deepest Leaf Retouch)
    - Thư mục in mơ hồ (Ambiguous Print Folder)
    - Album chuẩn, thừa trang, thiếu trang, đa album
    - Lọc file rác / không hỗ trợ / đuôi hoa thường (.JPG, .png, .txt, .tmp)
    - Thư mục rỗng không có ảnh
    - Quy cách lạ chưa có giá
    - Khách hàng mới chưa đăng ký
    - Ngày bỏ sót (Missing Day) để test báo cáo
#>

param(
    [string]$TargetRoot = "d:\___TOOLS\__TINIX\Lalab Auto Report Antigravity\TEST_DATA_LALAB"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "    LALAB AUTO REPORT - KHỞI TẠO DỮ LIỆU KIỂM THỬ MẪU     " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Thư mục đích: $TargetRoot" -ForegroundColor Yellow

if (Test-Path $TargetRoot) {
    Write-Host "Đang dọn dẹp thư mục test cũ..." -ForegroundColor DarkGray
    Remove-Item -Path $TargetRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null

function Create-DummyFile {
    param(
        [string]$Path,
        [string]$Content = "Lalab Test Dummy File Content"
    )
    $parent = Split-Path -Path $Path -Parent
    if (!(Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

function Create-ImageBatch {
    param(
        [string]$Folder,
        [int]$Count,
        [string]$Prefix = "img",
        [string]$Ext = ".jpg"
    )
    for ($i = 1; $i -le $Count; $i++) {
        $fileName = "{0}_{1:D3}{2}" -f $Prefix, $i, $Ext
        $filePath = Join-Path $Folder $fileName
        Create-DummyFile -Path $filePath
    }
}

# ==============================================================================
# NGÀY CHÍNH ĐỂ TEST: 2026-09-29
# ==============================================================================
$day29 = Join-Path $TargetRoot "2026-09-29"

# 1. Quang Studio - Khách chuẩn, Happy path (10 ảnh 13x18 in)
Write-Host "1. Tạo: Quang Studio (Happy path - 13x18 in: 10 file)..." -ForegroundColor Green
Create-ImageBatch -Folder (Join-Path $day29 "Quang Studio\13x18 in") -Count 10 -Prefix "quang_print" -Ext ".jpg"

# 2. Quang Studio - Retouch nhiều cấp sâu (Deepest Leaf: 12 ảnh 20x30 trong in-final)
Write-Host "2. Tạo: Quang Studio (Deepest Leaf Retouch - 20x30: 12 file)..." -ForegroundColor Green
$deepDir = Join-Path $day29 "Quang Studio\20x30\sua-lan-1\sua-lan-2\in-final"
Create-ImageBatch -Folder $deepDir -Count 12 -Prefix "retouch_final" -Ext ".jpg"
# Đặt 2 file thô ở thư mục cha để kiểm tra xem hệ thống có bị đếm nhầm không
Create-DummyFile -Path (Join-Path $day29 "Quang Studio\20x30\sua-lan-1\raw_01.jpg")
Create-DummyFile -Path (Join-Path $day29 "Quang Studio\20x30\sua-lan-1\raw_02.jpg")

# 3. Quang Studio - Thư mục mơ hồ (Ambiguous Print Folder: 40x60 TG có 2 nhánh ban_mau_am và ban_mau_lanh)
Write-Host "3. Tạo: Quang Studio (Ambiguous Folder - 40x60 TG: 2 nhánh lá)..." -ForegroundColor Green
Create-ImageBatch -Folder (Join-Path $day29 "Quang Studio\40x60 TG\ban_mau_am") -Count 5 -Prefix "mau_am" -Ext ".jpg"
Create-ImageBatch -Folder (Join-Path $day29 "Quang Studio\40x60 TG\ban_mau_lanh") -Count 5 -Prefix "mau_lanh" -Ext ".jpg"

# 4. Quang Studio - Lọc file rác và phần mở rộng HOA/thường
Write-Host "4. Tạo: Quang Studio (Lọc file rác & extension hoa thường)..." -ForegroundColor Green
$junkFolder = Join-Path $day29 "Quang Studio\15x21 in"
Create-DummyFile -Path (Join-Path $junkFolder "photo_01.JPG")
Create-DummyFile -Path (Join-Path $junkFolder "photo_02.jpeg")
Create-DummyFile -Path (Join-Path $junkFolder "photo_03.PNG")
Create-DummyFile -Path (Join-Path $junkFolder "notes.txt")
Create-DummyFile -Path (Join-Path $junkFolder "Thumbs.db")
Create-DummyFile -Path (Join-Path $junkFolder "design.psd")
Create-DummyFile -Path (Join-Path $junkFolder "backup.tmp")

# 5. Quang Studio - Thư mục rỗng / Không có ảnh in
Write-Host "5. Tạo: Quang Studio (Thư mục rỗng / No Print Folder)..." -ForegroundColor Green
$emptyFolder = Join-Path $day29 "Quang Studio\50x75 in"
New-Item -ItemType Directory -Path $emptyFolder -Force | Out-Null
Create-DummyFile -Path (Join-Path $emptyFolder "readme.txt") -Content "Chưa chốt in"

# 6. Khách hàng Alias & Provenance: Văn An vs Anh An vs A.An (Cùng 1 khách, 3 thư mục vật lý)
Write-Host "6. Tạo: Nhóm khách Văn An (Alias & Bảo toàn Provenance - 3 đơn riêng)..." -ForegroundColor Green
# Đơn 1: Thư mục chính Văn An
Create-ImageBatch -Folder (Join-Path $day29 "Văn An\13x18 in") -Count 15 -Prefix "vanan_13x18" -Ext ".jpg"
# Đơn 2: Thư mục alias Anh An
Create-ImageBatch -Folder (Join-Path $day29 "Anh An\13x18 in") -Count 20 -Prefix "anhan_13x18" -Ext ".jpg"
# Đơn 3: Thư mục A.An - Don Gap
Create-ImageBatch -Folder (Join-Path $day29 "A.An - Don Gap\20x30") -Count 6 -Prefix "a_an_gap" -Ext ".jpg"

# 7. Minh Studio - Đơn hàng tường minh (Explicit Sub-orders: Don 01, Don 02, Don 03)
Write-Host "7. Tạo: Minh Studio (Đơn hàng con tường minh Don 01, Don 02, Don 03)..." -ForegroundColor Green
# Don 01: 8 ảnh 13x18 in
Create-ImageBatch -Folder (Join-Path $day29 "Minh Studio\Don 01\13x18 in") -Count 8 -Prefix "minh_d1" -Ext ".jpg"
# Don 02: 6 ảnh 20x30
Create-ImageBatch -Folder (Join-Path $day29 "Minh Studio\Don 02\20x30") -Count 6 -Prefix "minh_d2" -Ext ".jpg"
# Don 03: Đơn kết hợp Album 20x20 (10 tờ chuẩn) + Album 25x25 (13 tờ vượt trang)
Create-ImageBatch -Folder (Join-Path $day29 "Minh Studio\Don 03 - Tron Goi\Album 20x20") -Count 10 -Prefix "album20" -Ext ".jpg"
Create-ImageBatch -Folder (Join-Path $day29 "Minh Studio\Don 03 - Tron Goi\Album 25x25") -Count 13 -Prefix "album25" -Ext ".jpg"

# 8. Kim Studio - Chuyên kiểm thử Album V2 (Đủ trang, Thừa trang, Thiếu trang)
Write-Host "8. Tạo: Kim Studio (Kiểm thử Album V2: Đủ 10 tờ, Thừa 14 tờ, Thiếu 8 tờ)..." -ForegroundColor Green
# Album 1: Chuẩn 10 tờ (10 file in)
Create-ImageBatch -Folder (Join-Path $day29 "Kim Studio\Album 20x20\retouch") -Count 10 -Prefix "sheet_chuan" -Ext ".jpg"
# Album 2: Vượt trang 14 tờ (10 tờ chuẩn + 4 tờ phụ trội)
Create-ImageBatch -Folder (Join-Path $day29 "Kim Studio\Album 20x20 - Bo 2\in") -Count 14 -Prefix "sheet_vuot" -Ext ".jpg"
# Album 3: Thiếu trang 8 tờ (< 10 tờ chuẩn)
Create-ImageBatch -Folder (Join-Path $day29 "Kim Studio\Album 20x20 - Bo 3\final") -Count 8 -Prefix "sheet_thieu" -Ext ".jpg"

# 9. Đơn hàng có quy cách lạ chưa có trong bảng giá (Unresolved Specification)
Write-Host "9. Tạo: Quy cách lạ chưa có trong bảng giá (Tranh Mica 50x75)..." -ForegroundColor Green
Create-ImageBatch -Folder (Join-Path $day29 "Quang Studio\Tranh Mica 50x75") -Count 4 -Prefix "mica" -Ext ".jpg"

# 10. Khách hàng mới toanh chưa đăng ký danh bạ (Unresolved Customer)
Write-Host "10. Tạo: Khách hàng mới chưa có trong danh bạ (Studio Sen Vang)..." -ForegroundColor Green
Create-ImageBatch -Folder (Join-Path $day29 "Studio Sen Vang\13x18 in") -Count 12 -Prefix "senvang" -Ext ".jpg"

# ==============================================================================
# NGÀY THỨ HAI: 2026-09-30 (Dùng để kiểm thử tính năng phát hiện Ngày Bỏ Sót)
# ==============================================================================
Write-Host "11. Tạo ngày bỏ sót: 2026-09-30 (Dùng để test Missing Days trên Báo cáo)..." -ForegroundColor Green
$day30 = Join-Path $TargetRoot "2026-09-30"
Create-ImageBatch -Folder (Join-Path $day30 "Quang Studio\13x18 in") -Count 15 -Prefix "day30_quang" -Ext ".jpg"
Create-ImageBatch -Folder (Join-Path $day30 "Văn An\20x30") -Count 10 -Prefix "day30_vanan" -Ext ".jpg"

# ==============================================================================
# NGÀY CŨ KỲ TRƯỚC: 2026-09-25 (Dùng để test Gom đơn chưa bill từ kỳ trước)
# ==============================================================================
Write-Host "12. Tạo ngày cũ kỳ trước: 2026-09-25 (Dùng để test gom đơn kỳ trước của Văn An)..." -ForegroundColor Green
$day25 = Join-Path $TargetRoot "2026-09-25"
Create-ImageBatch -Folder (Join-Path $day25 "Văn An\13x18 in") -Count 10 -Prefix "day25_vanan" -Ext ".jpg"

# ==============================================================================
# THƯ MỤC KHÁCH LẺ (QUICK BILL): Dùng để test Quick Bill / Không tạo Customer
# ==============================================================================
Write-Host "13. Tạo các thư mục Khách Lẻ (Quick Bill - Đa thư mục nguồn va Chuyển đổi khách)..." -ForegroundColor Green
$guestRoot = Join-Path $TargetRoot "KHACH_LE"
# Đa thư mục nguồn: Chi Lan 1 và Chi Lan them
Create-ImageBatch -Folder (Join-Path $guestRoot "Chi Lan 1\13x18 in") -Count 10 -Prefix "chilan1" -Ext ".jpg"
Create-ImageBatch -Folder (Join-Path $guestRoot "Chi Lan them\13x18 in") -Count 8 -Prefix "chilanthem" -Ext ".jpg"
# Khách lẻ in Album vượt trang: 14 file (10 chuẩn + 4 phát sinh)
Create-ImageBatch -Folder (Join-Path $guestRoot "Khach Vang Lai VIP\Album 20x20") -Count 14 -Prefix "album_guest" -Ext ".jpg"
# Khách lẻ dùng để test Chuyển thành Khách Hàng (Convert to Customer)
Create-ImageBatch -Folder (Join-Path $guestRoot "Chi Mai Wedding\20x30") -Count 4 -Prefix "maiwedding" -Ext ".jpg"

# 14. Tự động nạp cấu hình Bảng giá & Khách hàng mẫu vào Database SQLite
Write-Host "14. Tự động nạp Bảng giá quy cách va Khách hàng mẫu vào SQLite Database..." -ForegroundColor Green
$pyScript = Join-Path $PSScriptRoot "seed_test_database.py"
if (Test-Path $pyScript) {
    python $pyScript
}

# 15. Đăng ký menu chuột phải QUICK BILL trong Windows Explorer
Write-Host "15. Đăng ký menu chuột phải QUICK BILL (Mặc định bật)..." -ForegroundColor Green
$ctxScript = Join-Path $PSScriptRoot "Install_QuickBill_ContextMenu.ps1"
if (Test-Path $ctxScript) {
    & $ctxScript
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ĐÃ TẠO XONG TOÀN BỘ CẤU TRÚC THƯ MỤC VÀ DỮ LIỆU BẢNG GIÁ! " -ForegroundColor Green
Write-Host " Đường dẫn thư mục test: $TargetRoot" -ForegroundColor Yellow
Write-Host " Hãy mở ứng dụng Lalab Auto Report và làm theo tài liệu:   " -ForegroundColor Cyan
Write-Host " HUONG_DAN_THUC_HANH_TEST_APP.md                           " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
