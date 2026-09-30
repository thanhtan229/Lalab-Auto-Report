import sqlite3
import os
import re
import sys
import datetime

sys.stdout.reconfigure(encoding='utf-8')

db_path = os.path.expandvars(r'%LOCALAPPDATA%\LalabAutoReport\lalab_autoreport.db')
print(f"Connecting to database: {db_path}")

os.makedirs(os.path.dirname(db_path), exist_ok=True)
conn = sqlite3.connect(db_path)
c = conn.cursor()

def normalize_text(text: str) -> str:
    t = text.strip().lower()
    t = re.sub(r'\s*([.,_\-/])\s*', r'\1', t)
    t = re.sub(r'\s+', ' ', t).strip()
    return t

# 1. Seed Specifications requested by user
specs = [
    {
        "canonical_name": "13x18 in",
        "unit_price": 5000,
        "category": "PhotoPrint",
        "billing_method": "FileCount",
        "included_sheets": None,
        "base_price": None,
        "extra_sheet_price": None,
        "aliases": ["13x18", "13x18 in", "13-18"]
    },
    {
        "canonical_name": "20x30",
        "unit_price": 15000,
        "category": "PhotoPrint",
        "billing_method": "FileCount",
        "included_sheets": None,
        "base_price": None,
        "extra_sheet_price": None,
        "aliases": ["20x30", "20x30 in"]
    },
    {
        "canonical_name": "40x60 TG",
        "unit_price": 80000,
        "category": "PhotoPrint",
        "billing_method": "FileCount",
        "included_sheets": None,
        "base_price": None,
        "extra_sheet_price": None,
        "aliases": ["40x60 TG", "40x60"]
    },
    {
        "canonical_name": "15x21 in",
        "unit_price": 10000,
        "category": "PhotoPrint",
        "billing_method": "FileCount",
        "included_sheets": None,
        "base_price": None,
        "extra_sheet_price": None,
        "aliases": ["15x21", "15x21 in"]
    },
    {
        "canonical_name": "Album 20x20",
        "unit_price": 0,
        "category": "Album",
        "billing_method": "AlbumBasePlusExtra",
        "included_sheets": 10,
        "base_price": 400000,
        "extra_sheet_price": 20000,
        "aliases": ["Album 20x20", "Alb 20x20", "A20x20", "Album20x20"]
    },
    {
        "canonical_name": "Album 25x25",
        "unit_price": 0,
        "category": "Album",
        "billing_method": "AlbumBasePlusExtra",
        "included_sheets": 10,
        "base_price": 500000,
        "extra_sheet_price": 25000,
        "aliases": ["Album 25x25", "Alb 25x25", "A25x25", "Album25x25"]
    }
]

now = datetime.datetime.now().isoformat()

for s in specs:
    c.execute("SELECT id FROM print_specifications WHERE canonical_name = ?", (s["canonical_name"],))
    row = c.fetchone()
    if row:
        spec_id = row[0]
        c.execute("""
            UPDATE print_specifications
            SET unit_price = ?, category = ?, billing_method = ?, included_sheets = ?, base_price = ?, extra_sheet_price = ?, is_active = 1, updated_at = ?
            WHERE id = ?
        """, (s["unit_price"], s["category"], s["billing_method"], s["included_sheets"], s["base_price"], s["extra_sheet_price"], now, spec_id))
        print(f" -> Cập nhật quy cách: {s['canonical_name']} (ID={spec_id})")
    else:
        c.execute("""
            INSERT INTO print_specifications (canonical_name, unit_price, category, billing_method, included_sheets, base_price, extra_sheet_price, is_active, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
        """, (s["canonical_name"], s["unit_price"], s["category"], s["billing_method"], s["included_sheets"], s["base_price"], s["extra_sheet_price"], now, now))
        spec_id = c.lastrowid
        print(f" -> Thêm mới quy cách: {s['canonical_name']} (ID={spec_id})")

    for alias in s["aliases"]:
        n = normalize_text(alias)
        c.execute("""
            INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
            VALUES (?, ?, ?)
        """, (spec_id, alias, n))
        c.execute("""
            UPDATE print_specification_aliases
            SET print_specification_id = ?, alias_text = ?
            WHERE normalized_alias = ?
        """, (spec_id, alias, n))

    # Also sync to product_variants and product_specific_aliases if V2 tables exist
    c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='product_variants'")
    if c.fetchone():
        c.execute("""
            UPDATE product_variants
            SET unit_price = ?, base_price = ?, extra_sheet_price = ?, included_sheets = ?, is_active = 1, updated_at = ?
            WHERE id = ?
        """, (s["unit_price"], s["base_price"], s["extra_sheet_price"], s["included_sheets"], now, spec_id))
        for alias in s["aliases"]:
            n = normalize_text(alias)
            c.execute("""
                INSERT OR IGNORE INTO product_specific_aliases (variant_id, alias_text, normalized_alias, created_at)
                VALUES (?, ?, ?, ?)
            """, (spec_id, alias, n, now))

# 2. Seed default customers for complete test workflow
customers = [
    {
        "canonical_name": "Văn An",
        "phone": "0901234567",
        "aliases": ["Văn An", "Anh An", "A.An", "A. An"]
    },
    {
        "canonical_name": "Quang Studio",
        "phone": "0912345678",
        "aliases": ["Quang Studio", "Studio Quang"]
    },
    {
        "canonical_name": "Kim Studio",
        "phone": "0923456789",
        "aliases": ["Kim Studio"]
    },
    {
        "canonical_name": "Minh Studio",
        "phone": "0934567890",
        "aliases": ["Minh Studio"]
    }
]

for cust in customers:
    c.execute("SELECT id FROM customers WHERE canonical_name = ?", (cust["canonical_name"],))
    row = c.fetchone()
    if row:
        cust_id = row[0]
        print(f" -> Khách hàng đã có: {cust['canonical_name']} (ID={cust_id})")
    else:
        c.execute("""
            INSERT INTO customers (canonical_name, phone, note, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?)
        """, (cust["canonical_name"], cust["phone"], "Khách hàng mẫu phục vụ kiểm thử", now, now))
        cust_id = c.lastrowid
        print(f" -> Thêm mới khách hàng: {cust['canonical_name']} (ID={cust_id})")

    for alias in cust["aliases"]:
        n = normalize_text(alias)
        c.execute("""
            INSERT OR IGNORE INTO customer_aliases (customer_id, alias_text, normalized_alias, created_at)
            VALUES (?, ?, ?, ?)
        """, (cust_id, alias, n, now))

conn.commit()

print("\n=== DANH SÁCH BẢNG GIÁ SAU KHI NẠP DỮ LIỆU ===")
c.execute("""
    SELECT id, canonical_name, category, billing_method, unit_price, base_price, extra_sheet_price, included_sheets
    FROM print_specifications ORDER BY id
""")
for row in c.fetchall():
    sid, name, cat, method, price, base, extra, inc = row
    if cat == "Album":
        print(f"[{sid}] {name:15} | Loại: {cat:10} | {method:18} | Gói: {base:7,d} đ ({inc} tờ) | Phát sinh: {extra:6,d} đ/tờ")
    else:
        print(f"[{sid}] {name:15} | Loại: {cat:10} | {method:18} | Đơn giá: {price:6,d} đ")

print("\n=== DANH SÁCH ALIASES QUY CÁCH ===")
c.execute("""
    SELECT a.alias_text, s.canonical_name
    FROM print_specification_aliases a
    JOIN print_specifications s ON a.print_specification_id = s.id
    ORDER BY s.id, a.id
""")
for row in c.fetchall():
    print(f"  • Alias '{row[0]}' --> '{row[1]}'")

print("\n=== DANH SÁCH KHÁCH HÀNG & ALIAS ===")
c.execute("SELECT id, canonical_name, phone FROM customers ORDER BY id")
for row in c.fetchall():
    cid, cname, phone = row
    c.execute("SELECT alias_text FROM customer_aliases WHERE customer_id = ?", (cid,))
    aliases = [r[0] for r in c.fetchall()]
    print(f"[{cid}] {cname} (SĐT: {phone}) - Aliases: {', '.join(aliases)}")

conn.close()
print("\n>>> NẠP DỮ LIỆU THÀNH CÔNG VÀO DATABASE CỦA APP! <<<")
