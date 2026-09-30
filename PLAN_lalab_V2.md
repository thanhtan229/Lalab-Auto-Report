# PLAN — Lalab Auto Report V2 Upgrade

> **Purpose:** Upgrade the already-implemented Lalab Auto Report without rewriting the application from scratch.
>
> **Primary goals of this upgrade:**
> 1. Support customers that contain multiple explicit order folders.
> 2. Support multiple product types, especially photo prints and albums.
> 3. Replace the old Source-vs-Print comparison model with a simpler rule: **only the final print folder is counted for every product**.
> 4. Preserve backward compatibility with the folder structure already supported by the current app.
>
> **Important:** This is a migration/improvement plan for an existing codebase. Codex must audit the current implementation before changing schemas, models, services, or UI. Preserve working behavior unless this plan explicitly supersedes it.

---

## 1. Product decisions locked by the owner

These are product requirements and must be treated as authoritative.

### 1.1 One physical image file equals one printed copy for photo products

For normal photo-print products:

```text
1 printable image file in the final print folder = 1 billed copy
```

No filename quantity multipliers are used.

### 1.2 Effective Billing Folder is the only quantity source (Supersedes Final Print Folder)

The previous concept of mandatory `Final Print Folder` is replaced with **`Effective Billing Folder`** (UI display: *Thư mục tính số lượng*):

```text
Effective Billing Folder = deepest valid image folder currently existing under the Product Job.
```

- **Billing Prior to Retouch / File Intake:**
  If valid images exist directly inside the Product Folder and no descendant folder contains images:
  ```text
  Effective Billing Folder = Product Folder itself
  ```
  This allows the print shop to generate bills immediately upon receiving files.

- **Dynamic Folder Progression (Anti-Stickiness):**
  If deeper folders appear later (e.g., `sua/`, `sua lai/`):
  Rescan **automatically resolves to the newly added deeper folder** without permanently sticking to the older auto-resolved path.

- **Resolution Modes (`AutoResolved` vs `ManuallySelected`):**
  - `AutoResolved`: Single clear deepest leaf candidate. Re-evaluated from the filesystem on every rescan.
  - `ManuallySelected`: Multiple competing leaf candidates. Retained on rescan *only* if the chosen folder still exists, is still a leaf, and still contains images. If invalid or if deeper folders are added beneath it, re-evaluated.

- **Draft vs Locked Bills:**
  - `Draft`: Rescan recomputes `Effective Billing Folder`, updates quantities, line totals, and bill subtotal.
  - `Locked`: Immutable historical snapshot. Rescan never mutates a locked bill; flags `filesystem_changed_after_lock` if filesystem differs.

### 1.3 Effective Billing Folder may have any arbitrary name

Examples:

```text
(Product Folder itself)
retouch/
lan 2/
ok/
final/
abc/
sua lai/
print-new/
```

The app does not rely on hardcoded folder names such as `retouch`, `final`, `print`, or `edited`. Structural depth and presence of supported images determine the candidate.

### 1.4 Multiple customer-folder aliases may represent one real customer

Examples:

```text
Văn An
Anh An
A. An
A.An
```

may map to one canonical Customer ID.

Never silently fuzzy-merge an unknown customer folder.
Known aliases may auto-resolve; uncertain matches are suggestions only.

### 1.5 Multiple customer folders on the same day remain separate orders

If these physical folders all map to the same canonical customer:

```text
2026-09-29/
  Văn An/
  Anh An/
  A.An/
```

they must remain separate order records, then aggregate to the same customer total.

### 1.6 A customer folder may also contain multiple explicit order folders

New supported hierarchy:

```text
Date/
  Customer Folder/
    Order Folder/
      Product Folder/
        files / retouch generations / final print folder
```

Example:

```text
2026-09-29/
  Anh An/
    Don 01/
      13x18 in/
      Album 20x20/
    Don 02/
      13x18 in/
```

`Don 01` and `Don 02` are separate orders.

### 1.7 Legacy hierarchy must remain supported

Existing structure:

```text
Date/
  Customer Folder/
    Product Folder/
```

must still work.

Internally, create an **Implicit Order** for the customer folder so the domain model remains consistent:

```text
Customer -> Order -> ProductJob
```

Do not maintain two separate downstream billing/reporting pipelines.

### 1.8 One album folder equals one physical album

For album products:

```text
1 recognized album product folder = 1 album
```

If the same customer has two Album 20x20 folders, they are two separate Album ProductJobs and may be aggregated as quantity 2 in the final bill/report.

### 1.9 Album sheet count equals final print-folder file count

The earlier assumption `file count - 1 cover file` is removed.

The locked rule is:

```text
albumSheetCount = number of printable image files in the final print folder
```

The cover is normally stored elsewhere and is not subtracted.

### 1.10 Album pricing is base price plus extra sheets

Example price configuration:

```text
Product: Album 20x20
Included sheets: 10
Base price: 400,000
Extra sheet price: 20,000
```

Formula:

```text
sheetCount = printableFileCount(finalPrintFolder)
extraSheets = max(sheetCount - includedSheets, 0)
lineTotal = basePrice + extraSheets * extraSheetPrice
```

Examples:

```text
10 sheets -> 400,000
11 sheets -> 420,000
13 sheets -> 460,000
8 sheets  -> 400,000 (base/minimum price)
```

If an album has fewer sheets than the included count, bill the base price but show a non-blocking informational warning.

---

## 2. Target domain model

The upgraded canonical hierarchy must be:

```text
Date
  -> CustomerFolder
      -> Customer
      -> Order
          -> ProductJob
              -> Product
              -> FinalPrintFolder
              -> BillingResult
```

### 2.1 Customer

Represents the real customer identity.

Core fields:

```text
CustomerId
CanonicalName
CreatedAt
UpdatedAt
```

### 2.2 CustomerAlias

Maps folder-name variants to a canonical customer.

```text
AliasId
CustomerId
AliasOriginal
AliasNormalized
CreatedAt
```

### 2.3 CustomerFolder

Represents the physical folder discovered under a date.

This is provenance, not customer identity.

```text
CustomerFolderId
DateId / WorkDate
CustomerId
PhysicalPath
FolderName
```

### 2.4 Order

Represents one logical order.

An Order may be:

```text
EXPLICIT  -> physical order subfolder exists
IMPLICIT  -> legacy Customer -> Product layout
```

Suggested fields:

```text
OrderId
CustomerFolderId
CustomerId
OrderKind           // Explicit | Implicit
OrderName
PhysicalPath
WorkDate
Status
CreatedAt
UpdatedAt
```

### 2.5 Product

A canonical product definition from the price/product registry.

Examples:

```text
13x18 in
40x60 TG
Album 20x20
Album 25x25
```

A Product definition owns the billing method and price configuration.

### 2.6 ProductAlias

Maps physical product-folder names to a Product.

Examples:

```text
Album 20x20
Alb 20x20
A20x20
Album20x20
```

may all map to the same Product ID.

Unknown or ambiguous folder names must not be silently assigned when billing could be affected.

### 2.7 ProductJob

Represents one physical product folder within an order.

Do not merge ProductJobs merely because they use the same canonical product.

Example:

```text
Order 01/
  Album 20x20 - Be/
  Album 20x20 - Gia dinh/
```

Both may map to Product `ALBUM_20X20`, but remain two ProductJobs.

Reports/bills may aggregate them later.

Suggested fields:

```text
ProductJobId
OrderId
ProductId
PhysicalPath
FolderName
FinalPrintFolderPath
PrintableFileCount
BillingQuantity
BillingMetadataJson
ScanStatus
CreatedAt
UpdatedAt
```

---

## 3. Target folder grammar

The scanner must support both forms.

### 3.1 Legacy form

```text
Date/
  Customer/
    Product/
      ...
```

Normalize to:

```text
CustomerFolder
  -> ImplicitOrder
      -> ProductJob
```

### 3.2 Explicit-order form

```text
Date/
  Customer/
    Order/
      Product/
        ...
```

Normalize to:

```text
CustomerFolder
  -> ExplicitOrder
      -> ProductJob
```

### 3.3 Key parsing boundary

Before a recognized Product folder, folders may represent Customer or Order structure.

After a recognized Product folder, all descendant folders belong to the product's production/retouch chain and must never be interpreted as Orders.

```text
BEFORE PRODUCT
  Customer / Order semantics

PRODUCT FOLDER
  boundary

AFTER PRODUCT
  production workflow only
```

This rule prevents an arbitrary retouch folder from being mistaken for an order.

---

## 4. Order detection algorithm

Implement deterministic structure detection. Do not rely on folder names such as `Don 01`, `Order`, `album job`, etc.

For each CustomerFolder:

### Case A — direct children resolve to Products

Example:

```text
Anh An/
  13x18 in/
  40x60 TG/
```

Result:

```text
Create one Implicit Order for Anh An/
Attach both ProductJobs to that order.
```

### Case B — direct child is not a Product, but its children resolve to Products

Example:

```text
Anh An/
  Sinh nhat be/
    13x18 in/
    Album 20x20/
  Anh gia dinh/
    13x18 in/
```

Result:

```text
Sinh nhat be  -> Explicit Order
Anh gia dinh  -> Explicit Order
```

### Case C — mixed direct Products and explicit Order folders

This may occur in real data:

```text
Anh An/
  13x18 in/
  Don 02/
    Album 20x20/
```

Support it safely:

```text
13x18 in -> Implicit/default order
Don 02   -> Explicit order
```

Do not discard either branch.

### Case D — no known Product can be resolved

Do not guess silently.

Create a scan issue such as:

```text
UNRESOLVED_STRUCTURE
```

and let the user map the unknown folder as:

```text
Product alias
Order folder
Ignored folder
```

Persist confirmed mappings where appropriate.

---

## 5. Product recognition model

The current "Print Specification" concept should be migrated/generalized into a Product Registry.

### 5.1 Product categories

Minimum V2 categories:

```text
PHOTO_PRINT
ALBUM
```

Keep the model extensible for future products:

```text
FRAME
CANVAS
PHOTOBOOK
LAMINATION
WOOD_MOUNT
OTHER
```

Do not implement unneeded categories now; only avoid schema choices that make them impossible later.

### 5.2 Billing methods

Minimum V2 billing methods:

```text
FILE_COUNT
ALBUM_BASE_PLUS_EXTRA
MANUAL
```

`MANUAL` is a safe fallback for future/custom products and exceptional cases.

Do not scatter product-specific `if album` conditions across UI/scanner/report code. Billing behavior must be dispatched through a billing strategy/service based on `BillingMethod`.

---

## 6. Final print folder detection

The final print folder is the only folder whose printable files determine product billing metadata.

### 6.1 Detection rules

For each ProductJob:

1. Enumerate descendant folders that contain printable image files.
2. Determine image-containing leaf folders: folders with printable images that do not have an image-containing descendant.
3. If exactly one leaf candidate exists, use it as FinalPrintFolder.
4. If multiple leaf candidates exist, do not guess silently. Mark the ProductJob as requiring print-folder selection.
5. Persist the user's selected final print folder for that ProductJob/path so rescans remain stable.

### 6.2 Product folder itself may be final

If printable files are directly inside the Product folder and there is no image-containing descendant, the Product folder itself is the FinalPrintFolder.

Example:

```text
13x18 in/
  A.jpg
  B.jpg
```

FinalPrintFolder = `13x18 in/`.

### 6.3 Intermediate folders do not contribute to billing

Example:

```text
13x18 in/
  A.jpg
  B.jpg
  retouch1/
    A.jpg
    B.jpg
    retouch2/
      A.jpg
      B.jpg
```

FinalPrintFolder = `retouch2/`.

Billing count = 2.

Ignore the outer and intermediate image files for quantity purposes.

### 6.4 Remove old Source cross-check behavior

Delete/deprecate UI and business rules for:

```text
SourceQuantity
Source vs Print mismatch
Use Source Quantity
Use Print Quantity because of mismatch
Mismatch-required bill resolution
```

Historical persisted values may remain for migration/audit compatibility, but they must not drive new V2 scans or billing.

---

## 7. Printable file rules

Create one centralized `PrintableFilePolicy` used by every billing method.

Configurable supported extensions should include at minimum the formats already supported by the current app.

Typical defaults:

```text
.jpg
.jpeg
.png
.tif
.tiff
```

If the current app already supports other production formats, preserve that support unless proven unsafe.

Do not count:

```text
Thumbs.db
.DS_Store
.tmp
.txt
non-print metadata files
```

Do not recursively count files below the selected FinalPrintFolder unless the product explicitly defines a recursive policy. V2 default is direct printable files in FinalPrintFolder only.

---

## 8. Billing engine

Billing must be separated from scanning.

Scanner output:

```text
ProductJob
ProductId
FinalPrintFolder
PrintableFileCount
```

Billing engine input:

```text
Product definition
ProductJob scan result
Price configuration
```

Billing engine output:

```text
BillingQuantity
BillingBreakdown
LineTotal
Warnings
```

### 8.1 FILE_COUNT strategy

For normal photo products:

```text
quantity = printableFileCount
lineTotal = quantity * unitPrice
```

Example:

```text
13x18 in
Final print files = 52
Unit price = 5,000
Total = 260,000
```

### 8.2 ALBUM_BASE_PLUS_EXTRA strategy

For albums:

```text
albumCount = 1 per ProductJob
sheetCount = printableFileCount
extraSheets = max(sheetCount - includedSheets, 0)
lineTotal = basePrice + extraSheets * extraSheetPrice
```

Example:

```text
Album 20x20
Included sheets = 10
Base price = 400,000
Extra sheet = 20,000
Final print files = 13

sheetCount = 13
extraSheets = 3
lineTotal = 460,000
```

### 8.3 Album with fewer than included sheets

Example:

```text
Final print files = 8
Included sheets = 10
```

Result:

```text
sheetCount = 8
extraSheets = 0
lineTotal = basePrice = 400,000
warning = BELOW_INCLUDED_SHEETS
```

This warning is informational and must not block bill locking.

### 8.4 Empty album

```text
printableFileCount = 0
```

This is a blocking or high-severity validation issue because there is no printable album content.

Do not silently bill a normal album from an empty folder without explicit user review.

### 8.5 MANUAL strategy

Allow an explicitly configured manual product to accept a user-entered amount/quantity with an audit note.

This is not the default for photo prints or albums.

---

## 9. Product and price configuration

Upgrade the existing price/specification UI to support a Product Registry.

### 9.1 Photo product configuration

Fields:

```text
Product name
Category = PHOTO_PRINT
Billing method = FILE_COUNT
Unit price
Aliases
Enabled
```

### 9.2 Album product configuration

Fields:

```text
Product name            e.g. Album 20x20
Category                ALBUM
Billing method          ALBUM_BASE_PLUS_EXTRA
Included sheets         10
Base price              400000
Extra sheet price       20000
Aliases
Enabled
```

Internal field naming should use `sheet`/`sheetCount`, not `page`, to match the owner's operational rule.

The display label may remain friendly and configurable, but calculations must use sheet count = final printable file count.

### 9.3 Product aliases

Known exact/normalized aliases may auto-map.

Unknown or ambiguous product folders must be reviewed.

Never silently map `20x20` to Album 20x20 if the same term could also represent a normal print size.

---

## 10. Customer identity and order behavior

Keep the existing customer alias system, but adapt aggregation to the new Order layer.

### 10.1 Identity flow

```text
Physical Customer Folder
  -> normalize alias
  -> canonical Customer
```

### 10.2 Order provenance

Never lose the physical folder path/name after alias resolution.

Example:

```text
Canonical customer: Văn An

Orders on 2026-09-29:
- Physical customer folder: Văn An
- Physical customer folder: Anh An
- Anh An / Don 01
- Anh An / Don 02
- A.An / Album cuoi
```

All may aggregate under `Văn An`, but remain independently inspectable.

### 10.3 Order naming

Explicit order:

```text
OrderName = physical order folder name
```

Implicit order:

UI-friendly default:

```text
"Đơn mặc định"
```

or derive from the customer-folder provenance without exposing an ugly generated ID.

The exact display string may follow the existing UI style.

---

## 11. Billing aggregation

Aggregation must occur in layers without destroying underlying jobs.

### 11.1 ProductJob level

Preserve each physical product folder as one ProductJob.

### 11.2 Order level

Aggregate equivalent products for display/billing where appropriate.

Example:

```text
Order 01/
  Album 20x20 - Be/
  Album 20x20 - Gia dinh/
```

Detailed records:

```text
Album Job A: 1 album, 10 sheets, 400,000
Album Job B: 1 album, 13 sheets, 460,000
```

Order summary may show:

```text
Album 20x20: 2 albums
Total: 860,000
```

Do not derive the total as `2 * one shared price` because each album can have a different sheet count.

### 11.3 Customer daily total

Sum all Orders belonging to the canonical customer for that work date.

### 11.4 Monthly total

Use persisted billing/order data in SQLite.

Do not rescan the whole month merely to open a monthly report.

---

## 12. Bill locking and snapshots

Preserve the existing locked-bill/snapshot principle.

Once a bill is locked:

```text
filesystem changes must not silently rewrite historical billing values
```

A rescan may show that current disk state differs from the locked snapshot, but updating the historical bill must require an explicit reopen/recalculate action.

### 12.1 Snapshot data to store for each ProductJob

At lock time persist enough information to explain the calculation later:

For FILE_COUNT:

```text
ProductId
ProductNameSnapshot
FinalPrintFolderPath
PrintableFileCount
UnitPriceSnapshot
LineTotal
```

For ALBUM_BASE_PLUS_EXTRA:

```text
ProductId
ProductNameSnapshot
FinalPrintFolderPath
AlbumCount = 1
SheetCount
IncludedSheetsSnapshot
BasePriceSnapshot
ExtraSheetPriceSnapshot
ExtraSheetCount
LineTotal
```

Price-list changes in the future must not rewrite locked historical bills.

---

## 13. Database migration strategy

Codex must inspect the existing schema before writing migrations.

Do not drop/recreate the database as the default upgrade path.

### 13.1 Recommended migration objectives

Add or generalize entities equivalent to:

```text
Orders
Products
ProductAliases
ProductJobs
Billing configuration fields
Album billing metadata
```

Existing entities such as Customers, CustomerAliases, ScanSnapshots, PrintSpecifications, Bills, or BillLines should be migrated/extended rather than duplicated blindly.

### 13.2 PrintSpecification -> Product migration

If the current codebase has a `PrintSpecification` table/model, prefer a controlled migration:

```text
existing print spec
  -> Product
     Category = PHOTO_PRINT
     BillingMethod = FILE_COUNT
     UnitPrice = existing price
```

Preserve IDs where practical or create a mapping migration so existing bill history remains valid.

### 13.3 Existing Order semantics

If the current app already uses one physical customer folder as an Order, preserve those records as Implicit Orders.

Introduce explicit child-order support without changing the identity of existing historical orders unnecessarily.

### 13.4 Old Source/Print fields

Do not destructively remove old fields in the first migration if historical records depend on them.

Preferred staged approach:

```text
Migration 1: stop using fields in new scans
Migration 2: mark deprecated in code
Future cleanup: remove only after compatibility is proven
```

Historical screens may continue to display old snapshot data if useful, but new V2 calculations must not use it.

### 13.5 Migration safety

Before applying a schema migration:

- back up the SQLite database automatically or provide an explicit backup step;
- make migrations idempotent through the framework's migration mechanism;
- test upgrade from a representative V1 database;
- test clean install using the new schema;
- test app startup against an already-upgraded database.

---

## 14. Scanner refactor plan

Do not rewrite the scanner in one pass. Separate responsibilities.

Recommended services/modules:

```text
DateFolderScanner
CustomerFolderResolver
CustomerAliasResolver
OrderStructureResolver
ProductResolver
ProductJobScanner
FinalPrintFolderResolver
PrintableFileCounter
ScanIssueCollector
```

### 14.1 Scanner output should be structural

The scanner should not calculate money.

It should return a structure similar to:

```text
WorkDate
CustomerFolder
CustomerId / unresolved customer issue
Orders[]
  ProductJobs[]
    ProductId / unresolved product issue
    FinalPrintFolder
    PrintableFileCount
    Issues[]
```

Billing is a subsequent stage.

### 14.2 Scoped Smart Scan remains preferred

Retain the existing performance policy:

```text
No continuous realtime watcher required.
No frequent background full scans.
```

Support scoped manual scan/rescan:

```text
ProductJob
Order
Customer folder
Date
Missing dates / custom date range
```

Monthly reports should read persisted data, not automatically scan every day again.

---

## 15. UI/UX changes

The upgrade should integrate with the existing interface rather than creating a parallel UI.

### 15.1 Dashboard hierarchy

Allow users to drill down:

```text
Date
  Customer
    Order
      ProductJob
```

Example:

```text
Văn An                         1,230,000
  Anh An / Don 01               840,000
    13x18 in        120 files
    Album 20x20     13 sheets
  Anh An / Don 02               150,000
  A.An / Album gia đình         240,000
```

### 15.2 ProductJob detail

For normal photos:

```text
Product: 13x18 in
Final print folder: ...
Printable files: 52
Unit price: ...
Total: ...
```

For albums:

```text
Product: Album 20x20
Final print folder: ...
Album quantity: 1
Sheets: 13
Included: 10
Extra: 3
Base: 400,000
Extra sheets: 60,000
Total: 460,000
```

### 15.3 Remove obsolete Source/Print comparison UX

Remove/hide for V2 scans:

```text
Source count column
Source-vs-Print mismatch status
Use Source button
Use Print because mismatch button
Custom mismatch resolution flow tied to Source vs Print
```

Keep only historical display if necessary for old locked records.

### 15.4 Print-folder ambiguity UI

If multiple leaf image folders exist:

```text
⚠ Chưa xác định thư mục in

Candidate A — 52 files
Candidate B — 51 files

[Chọn A]
[Chọn B]
```

Selection must be persisted.

### 15.5 Product mapping UI

For unknown folders:

```text
Folder: "20x20"

○ Ảnh 20x20
○ Album 20x20
○ Tạo sản phẩm mới
○ Bỏ qua
```

Do not auto-select an ambiguous option.

### 15.6 Price list UI

Product editor changes fields according to Billing Method.

`FILE_COUNT`:

```text
Unit price
```

`ALBUM_BASE_PLUS_EXTRA`:

```text
Included sheets
Base price
Extra sheet price
```

Hide irrelevant fields instead of showing zero-value clutter.

---

## 16. Validation and issue severity

Use explicit issue types rather than generic errors.

Recommended issues:

```text
UNRESOLVED_CUSTOMER
AMBIGUOUS_CUSTOMER_ALIAS
UNRESOLVED_STRUCTURE
UNRESOLVED_PRODUCT
AMBIGUOUS_PRODUCT_ALIAS
NO_PRINTABLE_FILES
MULTIPLE_FINAL_PRINT_FOLDER_CANDIDATES
MISSING_FINAL_PRINT_FOLDER
ALBUM_BELOW_INCLUDED_SHEETS
FILESYSTEM_ACCESS_ERROR
```

Suggested severity:

```text
Blocking:
- unresolved customer when bill requires customer identity
- unresolved product
- unresolved/multiple final print folder
- empty album/no printable output where billing cannot be trusted
- filesystem access error for required folder

Warning/non-blocking:
- album below included sheets
```

Bill lock should be blocked only by unresolved issues that can change billing correctness.

---

## 17. Performance requirements

The upgrade must not regress the original low-overhead design.

### 17.1 No constant full-tree scanning

Do not introduce frequent recursive background scans.

### 17.2 Count filenames, do not decode images

Quantity scanning should enumerate file metadata/path only.

Do not load image pixels, thumbnails, EXIF, or previews merely to count files.

### 17.3 Cache persisted results

Daily/monthly reporting uses SQLite snapshots.

### 17.4 Rescan narrowly

If only one order changed, provide a rescan path that does not require rescanning the full month/root.

---

## 18. Backward compatibility requirements

V2 is not done if it only supports the new hierarchy.

The following must all work:

```text
A. Date -> Customer -> Product
B. Date -> Customer -> Order -> Product
C. Date -> Customer -> direct Product + explicit Order folders mixed
D. Multiple physical customer folders mapping to the same Customer ID
E. Multiple ProductJobs mapping to the same Product
F. Historical locked V1 bills remain readable
```

Do not force the owner to reorganize existing folders to adopt V2.

---

## 19. Test strategy

Add tests before or alongside refactoring. Do not depend on manual UI testing alone.

### 19.1 Unit tests — order parsing

Test:

```text
Customer -> Product                     -> one Implicit Order
Customer -> Order -> Product            -> explicit Orders
Customer -> Product + Order -> Product  -> mixed support
Unknown structure                       -> issue, no silent guess
```

### 19.2 Unit tests — final print folder

Test:

```text
Product contains images, no child images        -> product folder selected
Linear retouch chain                            -> deepest image leaf selected
Multiple image leaf branches                    -> ambiguity issue
Intermediate image folders                      -> not counted
Remembered user selection                       -> reused on rescan
```

### 19.3 Unit tests — photo billing

```text
52 final files * unit price -> correct total
0 files -> expected validation behavior
```

### 19.4 Unit tests — album billing

Given:

```text
includedSheets = 10
basePrice = 400000
extraSheetPrice = 20000
```

Assert:

```text
0 files  -> blocking empty-album issue
8 files  -> 8 sheets, 0 extra, 400000 + informational warning
10 files -> 10 sheets, 0 extra, 400000
11 files -> 11 sheets, 1 extra, 420000
13 files -> 13 sheets, 3 extra, 460000
```

Explicitly test that **no cover is subtracted**.

### 19.5 Unit tests — multiple albums

Two Album 20x20 ProductJobs:

```text
Album A: 10 sheets -> 400000
Album B: 13 sheets -> 460000
```

Order aggregation:

```text
album count = 2
total = 860000
```

Do not calculate `2 * 400000`.

### 19.6 Customer alias regression tests

Verify multiple physical customer folders remain separate order provenance while customer totals aggregate correctly.

### 19.7 Migration tests

Test upgrade from a representative V1 database containing:

- customers and aliases;
- print specifications/prices;
- scanned orders;
- locked bills;
- old Source/Print fields;
- historical reports.

Verify historical bill totals do not change after schema migration.

### 19.8 End-to-end filesystem fixtures

Create deterministic temporary folder fixtures for all acceptance scenarios below.

Do not test against the owner's real production directories.

---

## 20. Acceptance scenarios

### Scenario A — legacy photo order

```text
2026-09-29/
  Văn An/
    13x18 in/
      old files...
      abc/
        50 final jpg files
```

Expected:

```text
Customer = Văn An
Implicit Order = 1
Product = 13x18
Bill quantity = 50
No Source comparison
```

### Scenario B — explicit multiple orders

```text
2026-09-29/
  Anh An/
    Don 01/
      13x18 in/
    Don 02/
      13x18 in/
```

Expected:

```text
Customer alias -> canonical Văn An
2 separate Orders
Each order billed independently
Customer daily total = sum of both
```

### Scenario C — one order with different products

```text
Don 01/
  13x18 in/
  40x60 TG/
  Album 20x20/
```

Expected:

```text
1 Order
3 ProductJobs
Each ProductJob uses its own billing strategy
```

### Scenario D — Album 20x20 with 10 sheets

Final print folder has exactly 10 printable files.

Expected:

```text
Album count = 1
Sheet count = 10
Extra sheets = 0
Total = 400000
```

### Scenario E — Album 20x20 with 13 sheets

Final print folder has exactly 13 printable files.

Expected:

```text
Album count = 1
Sheet count = 13
Extra sheets = 3
Total = 460000
```

### Scenario F — two Album 20x20 folders

```text
Don 01/
  Album 20x20 - A/
  Album 20x20 - B/
```

Both aliases map to Album 20x20.

Expected:

```text
2 ProductJobs
1 physical folder = 1 album
Each album sheet count computed separately
Summary may display 2 albums
Total is sum of each album's calculated total
```

### Scenario G — album file count does not subtract cover

Final print folder has 11 printable files.

Expected:

```text
Sheet count = 11
NOT 10
Total = 420000
```

### Scenario H — multiple retouch generations

```text
13x18 in/
  files
  edit1/
    files
    edit2/
      final files
```

Expected:

```text
Only edit2 direct printable files are counted.
No Source Quantity is generated.
```

### Scenario I — ambiguous final branch

```text
Album 20x20/
  editA/
    10 files
  editB/
    13 files
```

Expected:

```text
Do not choose automatically.
Require FinalPrintFolder selection.
Billing remains unresolved until selected.
```

### Scenario J — ambiguous product name

```text
20x20/
```

when both a photo product and an album product could match.

Expected:

```text
Require user mapping.
Persist confirmed alias.
```

### Scenario K — same real customer, multiple physical folders and explicit orders

```text
2026-09-29/
  Văn An/
    13x18 in/
  Anh An/
    Don 01/
      Album 20x20/
    Don 02/
      13x18 in/
```

Expected:

```text
All map to canonical customer Văn An.
Orders remain separately auditable.
Customer daily total aggregates all orders.
```

### Scenario L — locked V1 historical bill

After upgrade, an old locked bill is opened.

Expected:

```text
Historical amount unchanged.
Historical record readable.
No automatic recalculation under V2 formulas.
```

---

## 21. Implementation phases for Codex

Because the app is already implemented, make incremental, reviewable changes.

### Phase 0 — Repository audit and upgrade baseline

Before modifying production code:

1. Read existing project instructions and architecture docs.
2. Inspect current domain models, DB schema/migrations, scanner, billing service, UI routes/views, and tests.
3. Identify exactly where the current implementation encodes:
   - CustomerFolder == Order;
   - PrintSpecification assumptions;
   - Source/Print comparison;
   - billing quantity selection;
   - bill lock snapshots.
4. Run the current test/build suite and record baseline results.
5. Produce a short implementation-impact note before invasive schema changes.

Do not rewrite working modules merely because the plan uses different names.

### Phase 1 — Domain and schema migration

Implement/migrate:

```text
OrderKind: Implicit / Explicit
Product model generalized from print specification
ProductAlias
Product billing method/configuration
ProductJob linked to Order
Album pricing fields
```

Preserve historical IDs/relationships where possible.

Add migration tests.

### Phase 2 — Remove Source dependency from new scans

Refactor new scan pipeline so quantity derives only from FinalPrintFolder.

Remove old Source mismatch as a prerequisite for new bill locking.

Keep historical fields readable if required.

Regression-test locked old bills.

### Phase 3 — Order structure resolver

Add legacy and explicit-order parsing.

Implement:

```text
Customer -> Product
Customer -> Order -> Product
mixed layout
unresolved structure issues
```

Add filesystem fixture tests.

### Phase 4 — Product registry and alias resolver

Add/upgrade Product configuration and alias mapping.

Migrate existing photo specs to:

```text
Category = PHOTO_PRINT
BillingMethod = FILE_COUNT
```

Add safe ambiguity handling.

### Phase 5 — Billing strategy engine

Implement billing strategies:

```text
FILE_COUNT
ALBUM_BASE_PLUS_EXTRA
MANUAL
```

Add complete unit-test matrix for album pricing.

### Phase 6 — UI/UX upgrade

Update:

- Dashboard hierarchy Customer -> Order -> ProductJob;
- explicit/implicit order display;
- product mapping dialog;
- final print-folder ambiguity dialog;
- Product/Price editor;
- album billing breakdown;
- remove obsolete Source comparison from V2 workflows.

### Phase 7 — Reporting and aggregation

Ensure:

```text
ProductJob -> Order -> Customer daily -> Monthly
```

aggregations are correct for multiple album jobs with different sheet counts.

Monthly report must continue to use persisted data, not full rescans.

### Phase 8 — Migration hardening and regression

Run:

- unit tests;
- integration tests;
- V1 DB upgrade test;
- clean DB test;
- filesystem fixture suite;
- locked bill regression;
- build/package smoke test.

Fix all new regressions before marking upgrade complete.

### Phase 9 — Documentation sync

After implementation stabilizes, update project documentation to match reality:

```text
PLAN_lalab.md / project status as appropriate
agents.md
ux_ui.md
ARCHITECTURE.md if present
PROJECT_STATUS.md if present
```

Do not overwrite historical design notes without preserving useful upgrade context.

---

## 22. Recommended database concepts

Exact names should match the existing codebase conventions after audit.

Conceptual schema:

```text
Customers
CustomerAliases
CustomerFolders

Orders
- Id
- CustomerFolderId
- CustomerId
- Kind
- Name
- PhysicalPath
- WorkDate
- Status

Products
- Id
- Name
- Category
- BillingMethod
- UnitPrice nullable
- IncludedSheets nullable
- BasePrice nullable
- ExtraSheetPrice nullable
- Enabled

ProductAliases
- Id
- ProductId
- AliasOriginal
- AliasNormalized

ProductJobs
- Id
- OrderId
- ProductId
- PhysicalPath
- FolderName
- FinalPrintFolderPath
- PrintableFileCount
- ScanStatus

BillLines / BillingSnapshots
- ProductJobId
- ProductNameSnapshot
- BillingMethodSnapshot
- Quantity / AlbumCount
- SheetCount nullable
- IncludedSheetsSnapshot nullable
- ExtraSheetCount nullable
- UnitPriceSnapshot nullable
- BasePriceSnapshot nullable
- ExtraSheetPriceSnapshot nullable
- LineTotal

Bills
- existing ownership/status/lock fields
```

Prefer typed columns for important billing values rather than storing all financial data only in JSON.

Use JSON only for optional strategy-specific diagnostic metadata where appropriate.

---

## 23. Data integrity rules

1. Never use folder display names as permanent identity keys.
2. Persist physical paths/provenance separately from canonical Customer/Product identities.
3. Never silently resolve ambiguous customer or product identity when money can be affected.
4. Never merge separate physical ProductJobs during scanning.
5. Aggregate only at reporting/billing presentation layers.
6. Locked bills use price/calculation snapshots, not live price-table values.
7. A new scan must not mutate a locked historical bill automatically.
8. FinalPrintFolder selection must be auditable and stable across rescans.
9. Album `sheetCount` is exactly the printable file count in the selected final print folder; do not subtract a cover.
10. New V2 calculations must not depend on Source Quantity.

---

## 24. Out of scope for this upgrade

Do not expand this upgrade unnecessarily with:

- realtime filesystem watcher;
- OCR/image content analysis;
- cloud sync;
- AI-based customer identification;
- automatic album-cover recognition;
- automatic page pairing from image content;
- filename quantity multipliers;
- full inventory/accounting/CRM features;
- destructive reorganization of the user's photo folders.

These may be considered later after V2 is stable.

---

## 25. Rollout and rollback

Before first production use of V2:

1. Back up the production SQLite DB.
2. Run migration on a copied DB first.
3. Scan a small representative date containing:
   - legacy photo order;
   - explicit multiple orders;
   - album 20x20 with exactly 10 sheets;
   - album with extra sheets;
   - customer alias;
   - multiple retouch generations.
4. Compare calculated totals manually.
5. Only then migrate the live database.

If migration or billing validation fails, restore the V1 database backup and previous application build.

Do not make database migration irreversible without a tested recovery path.

---

## 26. Definition of Done for V2 upgrade

The upgrade is complete only when all of the following are true:

- Existing legacy `Date -> Customer -> Product` folders still work.
- New `Date -> Customer -> Order -> Product` folders work.
- Mixed legacy/explicit order structures are handled safely.
- Customer aliases still aggregate to one canonical customer without losing order provenance.
- Multiple orders for the same customer remain separate and aggregate correctly.
- Product registry supports at least PHOTO_PRINT and ALBUM.
- Existing photo products are migrated to FILE_COUNT billing.
- Album products support Included Sheets + Base Price + Extra Sheet Price.
- Album sheet count equals final print-folder printable file count.
- No album cover subtraction occurs.
- One album product folder equals one album.
- Multiple album folders are calculated independently before aggregation.
- Only the final print folder contributes quantity for all new scans.
- Source Quantity and Source-vs-Print mismatch are removed from new billing workflows.
- Ambiguous final print folders require explicit selection.
- Ambiguous product aliases require explicit mapping.
- Locked V1 historical bills remain unchanged/readable.
- Locked V2 bills snapshot prices and calculation inputs.
- Daily/monthly reports use persisted data and aggregate correctly.
- No frequent background scanning is introduced.
- Automated tests cover the acceptance scenarios in this plan.
- Build/package succeeds with no new critical regression.

---

## 27. Codex implementation instruction

Use this plan as an upgrade contract, not as permission to rewrite the application.

Recommended initial instruction:

```text
Read PLAN_lalab_V2.md and the existing project documentation. This is an upgrade to an already-working Lalab Auto Report application. First audit the current repository, database schema/migrations, scanner, billing logic, customer alias logic, bill-lock behavior, UI, and test suite. Run the current tests/build and establish a baseline. Then implement the upgrade incrementally by the phases in PLAN_lalab_V2.md, preserving backward compatibility and historical locked bills. Do not rewrite working architecture without a demonstrated need. After each phase, run the relevant tests and report changed files, migrations, tests, unresolved risks, and the next phase. Do not mark the upgrade complete until all Definition of Done items and acceptance scenarios pass.
```

---

## 28. Final target behavior summary

```text
Filesystem
   ↓
Date
   ↓
Customer folder
   ↓
Customer alias resolution
   ↓
Implicit or Explicit Order
   ↓
Product folder
   ↓
Product alias resolution
   ↓
Final print folder
   ↓
Printable file count
   ↓
Billing strategy
   ├── FILE_COUNT
   └── ALBUM_BASE_PLUS_EXTRA
   ↓
ProductJob total
   ↓
Order total
   ↓
Canonical customer total
   ↓
Daily / Monthly reports
```

Core simplification for V2:

```text
Only the final print folder matters for quantity.
```

Core extensibility for V2:

```text
Customer -> Order -> ProductJob -> Billing Strategy
```

This provides a clean base for the current photo-print workflow, multiple customer orders, albums with extra-sheet pricing, and future product types without forcing the existing workshop folder workflow to be reorganized.
