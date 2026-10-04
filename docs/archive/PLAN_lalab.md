# PLAN — Lalab Auto Report

> Implementation plan for Codex
>
> Product name: **Lalab Auto Report**
>
> Primary platform: **Windows desktop**
>
> Primary purpose: Automatically scan photo-print job folders, identify customers and print specifications, resolve customer aliases, calculate bills, and generate daily/monthly reports without repeatedly browsing folders manually.
>
> ⚠️ **NOTE: V2 UPGRADE SPECIFICATION IN EFFECT**
> This document describes the original V1 baseline. The system has been upgraded to **V2** based on the latest business requirements:
> - **Multi-Order Hierarchy:** Supports `Date -> Customer -> Order -> Product` (Explicit) alongside legacy `Date -> Customer -> Product` (Implicit).
> - **Final Print Folder Sole Source:** Bill quantity is taken directly from the final print folder (`BillQuantity = PrintCount`). Source vs. Print comparison and mismatch warnings are eliminated.
> - **Product Architecture:** Organised into `Product Family -> Product Variant -> Product Specific Alias`. Family aliases (`ab`, `alb`, `album`) are shared across all sizes.
> - **Size Normalizer:** Orientation-independent canonical size normalization (`30x20` = `20x30` -> `min x max`).
> - **Album Pricing:** Base sheets + extra sheets, sheetCount = file count (no cover deduction).
> See `ARCHITECTURE.md` and `PLAN_lalab_V2.md` for full details.

---

## 1. Product goal

Build a fast, local-first Windows desktop application for a photo-print workshop.

The app must turn an existing folder structure into structured order/report data while requiring as little change as possible to the workshop's current workflow.

Existing operational pattern:

```text
Date/
  Customer Folder/
    Print Specification/
      source image files
      retouch folder/
        retouched files
        another retouch folder/
          final files to print
```

Example:

```text
2026-09-28/
  Văn An/
    13x18 in/
      IMG001.jpg
      IMG002.jpg
      IMG003.jpg
      sua-lan-1/
        IMG001.jpg
        IMG002.jpg
        IMG003.jpg
        sua-lan-2/
          IMG001.jpg
          IMG002.jpg
          IMG003.jpg
```

The app must not depend on the retouch folder having a fixed name.

---

## 2. Locked business rules

These rules are product requirements, not implementation suggestions.

### 2.1 Folder hierarchy

Expected primary hierarchy:

```text
Root / Date / Customer Folder / Print Specification / Files and subfolders
```

The first release should allow the root folder to be selected in Settings.

### 2.2 One file equals one printed copy

For bill quantity purposes:

```text
1 image file = 1 printed copy
```

There is no quantity multiplier encoded in filenames in V1.

### 2.3 Source folder

The **Print Specification folder itself** is the source folder.

Only supported image files directly inside the Print Specification folder are counted as Source Quantity.

Do not recursively include images from child folders in Source Quantity.

Example:

```text
13x18 in/
  A.jpg         <- source
  B.jpg         <- source
  C.jpg         <- source
  retouch/
    A.jpg       <- not source
    B.jpg       <- not source
```

Source Quantity = 3.

### 2.4 Print folder

The **final/deepest active image folder in the retouch chain** is the candidate Print Folder.

The Print Folder name can be anything. Do not rely on names such as `retouch`, `final`, `print`, `edited`, etc.

Examples of valid arbitrary names:

```text
retouch/
lan 2/
ok/
abc/
sua lai/
final-new/
```

### 2.5 Bill quantity

Default bill quantity comes from the **Print Folder**, not Source.

```text
Print Quantity = default Bill Quantity
Source Quantity = cross-check quantity
```

If:

```text
Print Quantity == Source Quantity
```

then bill quantity can be accepted automatically.

If:

```text
Print Quantity != Source Quantity
```

show a mismatch and require explicit user resolution before bill locking.

Resolution options:

```text
Use Print Quantity
Use Source Quantity
Enter Custom Quantity
```

Persist both the selected quantity and the reason/source of the selection.

### 2.6 Multiple customer folders for the same real customer

Folder names are not customer identity.

Examples:

```text
Văn An
Anh An
A. An
A.An
```

may represent one canonical customer.

The app must maintain:

```text
Customer ID
Canonical customer name
Customer folder aliases
```

Alias mapping example:

```text
CUST-0001: Văn An
Aliases:
- Văn An
- Anh An
- A. An
- A.An
```

### 2.7 Never silently fuzzy-merge customers

Fuzzy matching may suggest a customer but must not silently merge unknown folder names.

Rules:

```text
Known exact/normalized alias -> auto-map
High-confidence fuzzy match -> suggestion only
Ambiguous/no match -> user must map or create new customer
```

Once confirmed, the new alias is persisted for future scans.

### 2.8 Multiple folders of the same customer on the same date

Keep each physical customer folder as a separate **Order**.

Example:

```text
2026-09-28/
  Văn An/
  Anh An/
  A.An/
```

Even after all three map to the same canonical customer, retain:

```text
Order A = folder Văn An
Order B = folder Anh An
Order C = folder A.An
```

Reports may aggregate them under the canonical customer, but order-level provenance must remain visible.

### 2.9 Locked bills are snapshots

After bill locking, later filesystem changes must not silently change historical billed values.

Rescanning a locked order may detect differences but must not overwrite the locked bill automatically.

---

## 3. Recommended technical architecture

### 3.1 Recommended stack

Use a native/local Windows architecture optimized for filesystem work:

- **C# / .NET current LTS at implementation time**
- **WPF** for desktop UI
- MVVM architecture
- **SQLite** for local persistence
- EF Core or Dapper; prefer the simpler option that keeps migrations explicit and testable
- Structured logging to local files
- No cloud dependency in V1

Rationale:

- Fast direct Windows filesystem access
- Low idle resource usage
- Easy packaging for a workshop PC
- Mature desktop controls
- Strong support for SQLite and background tasks
- No browser/runtime server required

Do not add AI/LLM dependencies for core scanning or billing logic.

### 3.2 Architectural modules

```text
Lalab Auto Report
|
+-- UI
|   +-- Dashboard
|   +-- Scan Workspace
|   +-- Order Detail
|   +-- Customer/Alias Manager
|   +-- Price List
|   +-- Reports
|   +-- Settings
|
+-- Application Services
|   +-- ScanService
|   +-- FolderStructureParser
|   +-- PrintFolderResolver
|   +-- CustomerResolver
|   +-- BillingService
|   +-- ReportService
|   +-- LockingService
|
+-- Domain
|   +-- Customer
|   +-- CustomerAlias
|   +-- Order
|   +-- OrderItem
|   +-- ScanSnapshot
|   +-- Bill
|   +-- BillLine
|   +-- PrintSpecification
|
+-- Infrastructure
    +-- FileSystemAdapter
    +-- SQLiteRepository
    +-- SettingsRepository
    +-- Logger
```

Keep filesystem scanning independent from UI so it can be unit/integration tested.

---

## 4. Smart Scan strategy

### 4.1 V1 scan policy

Do **not** implement continuous realtime scanning in V1.

Do **not** scan every 5/15/30 minutes in V1.

Default idle behavior:

```text
No filesystem scanning.
```

Scanning is initiated intentionally by the user.

Supported scopes:

```text
Scan Order
Scan Customer Folder
Scan Date
Scan Date Range
Scan Missing Days
Scan & Verify Before Lock
```

### 4.2 Why manual Smart Scan is the default

Goals:

- Minimal CPU/disk impact
- Predictable numbers
- Avoid scanning during Photoshop export/copy operations
- Prevent UI values changing unexpectedly while a bill is being reviewed
- Make billing state explicit

### 4.3 Optional startup scan

May be implemented as an opt-in setting after core V1 is stable:

```text
[ ] Scan today's folder when app starts
[ ] Include yesterday
```

Default: OFF.

Do not add periodic polling until there is a demonstrated operational need.

### 4.4 Monthly reporting must not rescan the month by default

Monthly reports read SQLite snapshots/bills.

```text
Filesystem -> Smart Scan -> SQLite -> Reports
```

Opening a monthly report should not trigger scanning of all date folders.

### 4.5 Missing-day scan

For a selected month, calculate which date folders exist but have no scan record.

UI example:

```text
September 2026
01/09  Locked
02/09  Locked
03/09  Scanned
04/09  Never scanned
...
17/09  Never scanned
```

Provide:

```text
[Scan Missing Days]
```

Only missing/unscanned dates should be processed.

### 4.6 Final verification before locking

Before locking a bill/order, always run a fresh scan only for the relevant order(s):

```text
Scan
-> Resolve print folder
-> Count Source
-> Count Print
-> Resolve mismatch
-> Calculate bill
-> Lock snapshot
```

This is the authoritative final verification step.

---

## 5. Scanner behavior

### 5.1 Supported file extensions

Make extensions configurable.

Initial defaults should include common photo formats, case-insensitive:

```text
.jpg
.jpeg
.png
.tif
.tiff
.bmp
.webp
.heic
```

RAW formats should be configurable rather than assumed to be printable output.

Do not decode image pixels during normal counting.

Counting should operate on directory entries and extensions only.

### 5.2 Ignore rules

Support configurable ignored files/folders, especially:

```text
hidden/system files
Thumbs.db
.DS_Store
temporary files
```

Do not hardcode retouch folder names as ignored because arbitrary child folders may be valid processing stages.

### 5.3 Source count algorithm

For each Print Specification folder:

1. Enumerate files directly inside the folder.
2. Filter supported image extensions.
3. Count them.
4. Do not recurse for Source Quantity.

### 5.4 Print Folder candidate algorithm

For each Print Specification folder:

1. Traverse descendant folders.
2. Identify folders containing supported image files.
3. Build their image-bearing parent/child relationships.
4. Identify image-bearing **leaf folders**: folders containing images that have no deeper descendant image-bearing folder.
5. If there is exactly one valid image-bearing leaf folder, select it as Print Folder candidate.
6. If no child image folder exists, treat the Print Specification folder itself as the print candidate only if product policy explicitly allows direct-source printing; otherwise mark `NO_PRINT_FOLDER` for review.
7. If more than one leaf candidate exists, never guess silently. Mark `AMBIGUOUS_PRINT_FOLDER` and ask the user to choose.

### 5.5 Persist print-folder selection

For ambiguous structures, once the user selects a Print Folder, persist the selection against the OrderItem/scan context.

On rescan:

- reuse it if the folder still exists;
- if it no longer exists, invalidate it and request review.

Do not globally assume that a folder name selected once is always the final folder for every future job.

### 5.6 Example: linear retouch chain

```text
13x18 in/
  source files
  edit1/
    files
    edit2/
      files
      final-x/
        files
```

Leaf candidate:

```text
final-x/
```

### 5.7 Example: ambiguous branches

```text
13x18 in/
  source files
  retouch-a/
    files
  retouch-b/
    files
```

Result:

```text
AMBIGUOUS_PRINT_FOLDER
```

UI must display both candidates and counts.

### 5.8 Do not recursively double-count print files

Print Quantity is the supported image count directly inside the selected Print Folder.

If the selected folder itself has subfolders, they are not automatically added to Print Quantity unless a later product rule explicitly enables it.

---

## 6. Customer identity and alias resolution

### 6.1 Data model

```text
Customer
- Id
- CanonicalName
- Phone? optional
- Note? optional
- CreatedAt
- UpdatedAt

CustomerAlias
- Id
- CustomerId
- AliasText
- NormalizedAlias
- CreatedAt
```

### 6.2 Normalization

For matching only, derive a normalized representation such as:

- trim whitespace
- collapse repeated spaces
- case-insensitive comparison
- normalize punctuation spacing
- Unicode normalization

Be conservative about removing Vietnamese diacritics. It may be useful as a suggestion signal but should not turn an uncertain match into an automatic identity match.

### 6.3 Resolution flow

```text
Folder name
   |
   +-- Exact canonical/alias match -> map automatically
   |
   +-- Normalized exact alias match -> map automatically
   |
   +-- Possible fuzzy candidates -> show suggestions
   |
   +-- No safe match -> create/select customer
```

### 6.4 Alias review UI

When unresolved:

```text
Unrecognized customer folder: "A An"

Suggested customers:
[ ] Văn An
    Known aliases: Văn An, Anh An, A. An

[ ] Create new customer
```

After confirmation, save the alias permanently.

### 6.5 Collision handling

One normalized alias must not silently belong to multiple customers.

If a collision exists:

```text
AMBIGUOUS_CUSTOMER_ALIAS
```

require user selection for the current order.

---

## 7. Order model

### 7.1 Definition

One physical customer folder under one date folder is one Order.

Example:

```text
2026-09-28/Văn An/
```

is a separate order from:

```text
2026-09-28/Anh An/
```

regardless of whether both map to the same Customer ID.

### 7.2 Required provenance

Store:

```text
Order.Id
Order.Date
Order.CustomerId
Order.OriginalFolderName
Order.RelativePath
Order.AbsolutePath or root-relative canonical path
Order.Status
Order.LastScanAt
```

Never lose the original folder name/path after alias resolution.

---

## 8. Print specification model

A Print Specification corresponds to a direct child folder under an Order.

Examples:

```text
13x18 in
40x60 TG
20x30
```

Maintain a canonical PrintSpecification table with aliases if useful.

Recommended fields:

```text
PrintSpecification
- Id
- CanonicalName
- UnitPrice
- IsActive

PrintSpecificationAlias
- Id
- PrintSpecificationId
- AliasText
- NormalizedAlias
```

Unknown print-spec folders must not be ignored silently.

Show them as:

```text
UNKNOWN_PRINT_SPEC
```

and require mapping or creation before final billing.

---

## 9. Scan snapshot model

Each scan should produce immutable-ish observational data.

Recommended fields:

```text
ScanSnapshot
- Id
- OrderId
- ScanStartedAt
- ScanCompletedAt
- ScanScope
- AppVersion

OrderItemScan
- Id
- ScanSnapshotId
- OrderId
- PrintSpecificationId? nullable until resolved
- SpecificationFolderName
- SpecificationFolderPath
- SourceCount
- PrintCount? nullable
- SelectedPrintFolderPath? nullable
- PrintFolderResolutionStatus
- QuantityMismatch
- ScanStatus
- ErrorMessage? nullable
```

Do not erase prior snapshots when rescanning. Keep enough history to audit changes.

A retention/cleanup policy may be added later if database size becomes meaningful.

---

## 10. Billing model and mismatch resolution

### 10.1 Default rule

```text
BillQuantity = PrintCount
```

when a valid Print Folder is resolved.

### 10.2 If Source == Print

```text
Source = 100
Print  = 100
Bill   = 100
Resolution = AUTO_MATCH
```

### 10.3 If Source != Print

Example:

```text
Source = 120
Print  = 118
```

Require one of:

```text
USE_PRINT  -> BillQuantity = 118
USE_SOURCE -> BillQuantity = 120
CUSTOM     -> BillQuantity = user input
```

Persist:

```text
BillQuantity
QuantityResolutionMode
QuantityResolutionNote? optional
ResolvedBy
ResolvedAt
```

### 10.4 Custom quantity validation

Custom quantity:

- integer
- >= 0
- explicit user action
- optional/required note can be configured later

### 10.5 Never mutate locked bill from scan data

A rescan after locking may generate:

```text
FILESYSTEM_CHANGED_AFTER_LOCK
```

but must not change the locked bill without an explicit reopen workflow.

---

## 11. Bill locking workflow

Recommended state model:

```text
NEW
-> SCANNED
-> NEEDS_REVIEW      if unresolved customer/spec/print-folder/mismatch
-> VERIFIED
-> BILLED
-> LOCKED
```

Not all states need to be visually exposed exactly as enums, but domain rules should be explicit.

### Lock action

Button:

```text
[Scan & Verify Before Lock]
```

Flow:

1. Fresh scan selected order(s).
2. Resolve unknown customer aliases.
3. Resolve unknown print specifications.
4. Resolve ambiguous Print Folder.
5. Compare Source vs Print.
6. Resolve all quantity mismatches.
7. Calculate prices.
8. Display final bill preview.
9. Lock only after all blocking issues are resolved.
10. Store bill snapshot.

---

## 12. Bill aggregation rules

### 12.1 Order-level

Each physical customer folder remains a separate order/bill component.

### 12.2 Customer daily total

Aggregate all orders belonging to the same canonical customer for a selected date.

Example:

```text
Văn An

Order: Văn An/       125,000
Order: Anh An/        80,000
Order: A.An/         210,000
----------------------------
Daily total          415,000
```

### 12.3 Monthly total

Aggregate locked or eligible bill records from SQLite, not raw filesystem counts.

Reports should support:

```text
Today
Selected date
Date range
Current month
Selected month
By customer
By print specification
```

---

## 13. UI / UX plan

Prioritize speed and legibility over decorative UI.

### 13.1 Main Dashboard

Show:

```text
LALAB AUTO REPORT

Selected date: 28/09/2026
Last scan: 16:42

[Scan Date] [Scan Missing Days]

Customer / Folder     Status        Source   Print   Bill
----------------------------------------------------------
Văn An                OK             120     120    120
Studio Minh           Review         205     203    ---
Chị Hương             OK              64      64     64
```

Important: if one canonical customer has multiple physical orders, the UI must allow expansion rather than flattening provenance away.

Example:

```text
Văn An                      Total 415,000
  > Văn An/                 125,000
  > Anh An/                  80,000
  > A.An/                   210,000
```

### 13.2 Order Detail

Show every print specification:

```text
13x18 in
Source: 120
Print: 118
Print Folder: .../sua-lan-2/
Status: Mismatch -2

Bill quantity:
( ) 118 Use Print
( ) 120 Use Source
( ) Custom [     ]
```

Actions:

```text
Open Source Folder
Open Print Folder
Rescan Specification
Rescan Order
```

### 13.3 Customer Alias Manager

Functions:

```text
Create customer
Rename canonical display name
Add alias
Remove alias
Merge aliases carefully
View orders associated with customer
```

Do not implement destructive customer merge casually; if added, require preview and confirmation.

### 13.4 Price List

```text
Specification   Unit price
13x18 in        ...
20x30           ...
40x60 TG        ...
```

Support aliases and active/inactive specs.

### 13.5 Reports

Daily report:

- orders
- customer totals
- print specification quantities
- revenue
- mismatch/review status

Monthly report:

- total revenue
- total printed quantity
- totals by customer
- totals by specification
- unresolved/unlocked dates
- missing scan days

### 13.6 Scan status UX

Scan must be cancellable where practical.

Show:

```text
Scanning 28/09/2026...
Customer 12 / 43
```

Avoid blocking the entire UI thread.

---

## 14. Performance requirements

### 14.1 Idle performance

When no scan is running:

- effectively zero disk scanning
- no realtime watchers in V1
- no periodic polling in V1
- low idle CPU

### 14.2 Scan performance

Normal scan must:

- enumerate directory entries only
- never load image pixels
- never generate thumbnails by default
- avoid reading EXIF unless explicitly required by a future feature

### 14.3 Scoped rescans

Support rescanning progressively smaller scopes:

```text
Specification
Order
Customer folder
Date
Date range
```

Do not rescan a whole month to update one job.

### 14.4 Database reporting

Reports should be SQL/database operations and should not walk filesystem trees unless the user explicitly requests a verification scan.

---

## 15. Error and edge-case handling

Codex must implement explicit handling for at least these cases.

### Filesystem

- root folder unavailable
- drive disconnected
- permission denied
- path too long / invalid path handling
- folder renamed during scan
- file deleted during scan
- file copied while scan is running
- inaccessible subfolder
- empty folders
- print specification with no source images
- source images but no print folder
- multiple candidate Print Folders
- print folder disappears after manual selection

### Identity

- unknown customer
- ambiguous alias
- one alias accidentally colliding with another customer
- unknown print specification
- print specification alias collision

### Billing

- Source/Print mismatch
- Print = 0 with Source > 0
- Source = 0 with Print > 0
- custom quantity
- zero-price or missing-price specification
- attempt to lock unresolved bill
- attempt to rescan locked historical bill
- filesystem changed after lock

All recoverable errors should become visible review states, not app crashes.

---

## 16. Data integrity principles

1. Filesystem is observational input, not the historical billing database.
2. SQLite is the source of truth for persisted app decisions.
3. Locked bills are immutable by ordinary rescans.
4. User decisions must be auditable.
5. Never silently discard an unknown folder/spec/customer.
6. Never silently merge uncertain customers.
7. Never silently choose between multiple Print Folder candidates.
8. Preserve physical folder provenance even after aggregation.

---

## 17. Suggested database schema

Names can change during implementation, but semantics must remain.

```text
customers
- id PK
- canonical_name
- phone nullable
- note nullable
- created_at
- updated_at

customer_aliases
- id PK
- customer_id FK
- alias_text
- normalized_alias
- created_at
- UNIQUE(normalized_alias) unless ambiguity model deliberately changes

print_specifications
- id PK
- canonical_name
- unit_price
- is_active
- created_at
- updated_at

print_specification_aliases
- id PK
- print_specification_id FK
- alias_text
- normalized_alias

orders
- id PK
- work_date
- customer_id nullable until resolved
- original_folder_name
- relative_path
- status
- last_scan_at nullable
- created_at
- updated_at

scan_snapshots
- id PK
- order_id FK
- started_at
- completed_at nullable
- scan_scope
- status
- error_message nullable

order_item_scans
- id PK
- scan_snapshot_id FK
- order_id FK
- print_specification_id nullable
- specification_folder_name
- specification_relative_path
- source_count
- print_count nullable
- selected_print_folder_relative_path nullable
- print_folder_status
- mismatch_count nullable
- scan_status
- error_message nullable

bills
- id PK
- order_id FK
- customer_id FK
- status
- subtotal
- locked_at nullable
- created_at
- updated_at

bill_lines
- id PK
- bill_id FK
- print_specification_id FK
- source_count
- print_count nullable
- bill_quantity
- quantity_resolution_mode
- quantity_resolution_note nullable
- unit_price
- line_total
- source_scan_snapshot_id

app_settings
- key PK
- value
```

Consider storing money as integer minor units or integer VND, not floating point.

---

## 18. File path strategy

Prefer storing paths relative to configured Root Folder wherever possible.

Example:

```text
Root = D:\LALAB\PRINTS
Stored relative path = 2026-09-28\Văn An\13x18 in
```

Benefits:

- moving the entire root is easier
- DB is less coupled to drive letters
- paths are easier to compare

Keep Root Folder in Settings.

Normalize path comparison using Windows semantics.

---

## 19. Recommended implementation phases

### Phase 0 — Project bootstrap

Deliverables:

- solution/project skeleton
- WPF shell
- SQLite setup + migrations
- dependency injection
- structured logging
- settings storage
- configurable Root Folder
- test projects

Acceptance:

- app starts
- database auto-initializes safely
- user can select/save Root Folder
- restart preserves settings

### Phase 1 — Core filesystem scanner

Implement:

- date folder selection
- customer-folder discovery
- print-spec folder discovery
- supported extension filter
- direct Source counting
- descendant image-folder traversal
- leaf Print Folder detection
- ambiguous print-folder state
- order/spec rescan

Acceptance fixtures:

1. no retouch folder
2. one retouch level
3. multiple linear retouch levels
4. two competing leaf folders
5. empty source
6. empty final folder
7. unsupported files mixed with images

No billing yet.

### Phase 2 — Customer and print-spec resolution

Implement:

- canonical Customer model
- customer aliases
- exact/normalized matching
- fuzzy suggestions only
- unresolved-customer UI
- canonical PrintSpecification model
- spec aliases
- unknown-spec UI

Acceptance:

- `Văn An`, `Anh An`, `A.An` can map to one Customer
- each physical folder remains a separate Order
- unknown name never silently merges

### Phase 3 — Scan snapshots and Smart Scan

Implement:

- save scan snapshots
- scan order/date/date range
- last scan timestamps
- `Scan Missing Days`
- no background polling
- reports read cached DB records

Acceptance:

- opening monthly report does not walk filesystem
- rescanning one order does not rescan unrelated orders

### Phase 4 — Billing engine

Implement:

- price list
- Bill Quantity defaults to Print Quantity
- Source/Print comparison
- mismatch resolution modes
- custom quantity
- bill line calculations
- daily customer aggregation across multiple orders

Acceptance:

```text
Source=100 Print=100 -> Bill=100 auto
Source=100 Print=98 -> cannot finalize until resolved
Use Print -> 98
Use Source -> 100
Custom -> supplied integer
```

### Phase 5 — Verification and locking

Implement:

- Scan & Verify Before Lock
- blocking review states
- locked bill snapshot
- post-lock filesystem-change detection on explicit rescan
- reopen flow with explicit confirmation if required

Acceptance:

- locked bill cannot change from ordinary scan
- unresolved mismatch cannot lock
- historical values remain intact if files later change

### Phase 6 — Dashboard and reports

Implement:

- daily dashboard
- expandable canonical customer -> physical orders
- daily report
- date-range report
- monthly report
- totals by customer
- totals by specification
- unresolved/missing-day status

Acceptance:

- monthly report operates from DB without full month rescan
- one customer with three physical folders appears as three orders plus aggregate total

### Phase 7 — Hardening

Implement/test:

- permission failures
- disconnected drive
- concurrent filesystem modifications
- long paths
- cancellation
- transaction safety
- DB backup/restore strategy
- migration safety
- performance tests on realistic folder counts

### Phase 8 — Packaging

Deliver:

- Windows installer or self-contained package
- version display
- DB location documented
- backup instructions
- upgrade/migration flow

---

## 20. V1 scope

V1 SHOULD include:

- configurable root folder
- manual Smart Scan
- scan order/date/date range/missing days
- Source count
- automatic leaf Print Folder resolution
- ambiguous Print Folder resolution
- Customer database + aliases
- Print Specification + pricing
- separate physical orders for same canonical customer
- mismatch resolution
- bill calculation
- final verification scan
- bill lock/snapshot
- daily/monthly reports
- SQLite persistence
- open folder actions
- robust error states

V1 SHOULD NOT include unless implementation is already trivial and isolated:

- realtime filesystem watcher
- background periodic polling
- cloud sync
- multi-PC synchronization
- AI customer identification
- image-content comparison
- image thumbnails everywhere
- automatic fuzzy customer merging
- automatic bill mutation after lock

---

## 21. Possible V1.5 / V2 features

Only after V1 proves stable in real use:

### V1.5

- optional startup scan Today / Today+Yesterday
- CSV/Excel export
- printable invoice/report layout
- database backup UI
- richer audit history
- configurable scan presets

### V2

- optional lightweight filesystem watcher to mark orders `DIRTY`, not directly alter locked bills
- file-level Source vs Print comparison using canonical filenames
- detect missing/unexpected files even when counts match
- production workflow statuses
- `New -> Retouch -> Ready -> Printed -> Delivered -> Paid`
- revenue analytics
- customer pricing overrides
- multi-machine/network-share support if required

---

## 22. Deep Check future design

Do not block V1 on this.

Future comparison can detect:

```text
SOURCE
A.jpg
B.jpg
C.jpg

PRINT
A-edit.jpg
B-edit.jpg
D.jpg
```

Even though counts are both 3, Deep Check could report:

```text
Missing: C
Unexpected: D
```

This requires a configurable canonical filename strategy and must be designed from observed real filename patterns rather than guessed now.

---

## 23. Testing strategy

### Unit tests

Required for:

- filename extension filtering
- source counting
- leaf-folder resolution
- ambiguous-folder resolution
- alias normalization
- customer resolution
- print-spec resolution
- billing quantity rules
- money calculation
- state transitions
- locking rules

### Integration tests

Create temporary directory fixtures matching real structures.

Test at least:

```text
Date/Customer/Spec/source files
Date/Customer/Spec/source + final
Date/Customer/Spec/source + retouch1/final
Date/Customer/Spec/source + branchA + branchB
same canonical customer via multiple folder aliases
unknown customer
unknown specification
mismatch counts
locked bill then filesystem modification
```

### Performance test

Generate synthetic directory structures with large file counts.

Measure:

- scan elapsed time
- peak memory
- UI responsiveness
- report query time

The normal scanner must not load file contents.

### Regression tests

Every bug involving billing quantity, alias mapping, print-folder selection, or locking must gain a regression test before being considered fixed.

---

## 24. Acceptance scenarios

### Scenario A — normal job

```text
13x18 in/
  100 source images
  abc/
    100 print images
```

Expected:

```text
Source=100
Print=100
Bill=100
Status=OK
```

### Scenario B — mismatch

```text
Source=100
Print=98
```

Expected:

```text
Status=REVIEW_REQUIRED
Cannot lock until user selects Print, Source, or Custom.
```

### Scenario C — multiple retouch generations

```text
Spec/
  source
  a/
    files
    b/
      files
      xyz/
        final files
```

Expected:

```text
Print Folder = xyz
```

### Scenario D — ambiguous final folders

```text
Spec/
  source
  edit-a/
    files
  edit-b/
    files
```

Expected:

```text
No automatic choice.
User selects final Print Folder.
```

### Scenario E — aliases, same day

```text
2026-09-28/
  Văn An/
  Anh An/
  A.An/
```

All map to CUST-0001.

Expected:

```text
3 Orders remain distinct.
Customer daily report aggregates all 3.
```

### Scenario F — month report

All September days previously scanned/locked.

Expected:

```text
Opening September report does not rescan filesystem.
```

### Scenario G — locked bill changed on disk

Lock bill at quantity 100, then remove two print files.

Expected:

```text
Historical locked bill stays 100.
Explicit rescan may show filesystem changed.
No automatic mutation.
```

---

## 25. Logging and audit

Record important events:

```text
scan started/completed/failed
customer alias resolved manually
automatic alias match
print folder selected manually
mismatch resolution
custom bill quantity
bill locked
bill reopened
root folder changed
migration failure
filesystem access errors
```

Do not log image contents.

Do not require logging every file path unless needed for diagnostics; prefer summary-level logging to avoid oversized logs.

---

## 26. Backup and recovery

SQLite contains decisions that cannot be reconstructed perfectly from folders, especially:

- customer aliases
- pricing
- mismatch resolutions
- locked bills

Therefore implement a simple backup strategy before production rollout.

Minimum acceptable V1 behavior:

- database stored in a documented app-data location
- safe DB shutdown
- manual `Backup Database` action or documented copy procedure

Prefer adding timestamped backup before schema migrations.

---

## 27. Security/privacy

The app is local-first.

V1 should not upload customer names, paths, images, or billing data externally.

Do not introduce telemetry by default.

If crash reporting is ever added later, make it opt-in and strip sensitive filesystem/customer data.

---

## 28. Coding constraints for Codex

1. Keep scanner, domain rules, and UI separated.
2. Do not embed business logic in ViewModels/code-behind when it can live in services/domain objects.
3. Use asynchronous filesystem operations/task scheduling where appropriate so UI remains responsive.
4. Do not parallelize scanning aggressively enough to saturate the disk.
5. Make scan cancellation safe.
6. Use DB transactions for multi-record bill locking.
7. Store VND amounts as integer values, not floating point.
8. Make schema migrations explicit.
9. Never silently recover from a data-integrity conflict by guessing.
10. Prefer deterministic rules over heuristics for anything affecting billing.
11. Fuzzy matching is advisory only.
12. Add tests alongside each core rule.

---

## 29. Codex execution protocol

Codex should not implement the entire system as one uncontrolled large patch.

Work phase-by-phase.

For each phase:

1. Inspect current repository/workspace before editing.
2. State assumptions only when they materially affect implementation.
3. Implement the smallest complete vertical slice for the phase.
4. Add/update tests.
5. Run relevant tests, build, lint/analyzers where available.
6. Report:
   - files changed
   - behavior implemented
   - tests executed and results
   - known limitations
   - remaining blockers
7. Do not advance past a failing foundational test without diagnosing it.
8. Preserve existing working behavior.

Recommended checkpoints:

```text
Checkpoint 1: Project bootstrap
Checkpoint 2: Scanner proven with fixtures
Checkpoint 3: Alias/spec resolution proven
Checkpoint 4: Smart Scan + snapshots proven
Checkpoint 5: Billing/mismatch proven
Checkpoint 6: Locking proven
Checkpoint 7: Reports proven
Checkpoint 8: Production hardening/package
```

---

## 30. Initial implementation instruction for Codex

Use this plan as the authoritative product/implementation specification for **Lalab Auto Report**.

Start with **Phase 0 and Phase 1 only** unless the repository already contains completed equivalents.

Before coding:

- audit the workspace;
- identify the current stack and existing reusable components;
- do not replace an existing viable architecture merely to match the recommended stack;
- if this is a greenfield repository, use the recommended Windows local-first stack above;
- record any necessary deviation from this plan with rationale.

For Phase 1, create realistic temporary filesystem fixtures and prove the scanner rules through automated tests before building billing UI.

The first milestone is complete only when the app can reliably scan a selected date and return, for every physical customer/order folder and print-spec folder:

```text
original customer folder name
specification folder name
Source Quantity
resolved Print Folder or explicit unresolved status
Print Quantity
Source/Print mismatch status
```

No bill should be calculated until these observations are reliable.

---

## 31. Definition of V1 Done

Lalab Auto Report V1 is done when the user can:

1. Configure the print-job root folder.
2. Select a day and scan it manually.
3. See each physical customer folder as a separate order.
4. Map differing folder names to a canonical customer through aliases.
5. See each print specification and its Source count.
6. Have the app resolve the deepest valid Print Folder where unambiguous.
7. Manually choose a Print Folder where ambiguous.
8. See Print count and Source-vs-Print mismatch.
9. Use Print count as bill default.
10. Resolve mismatch using Print, Source, or Custom quantity.
11. Apply configured print-spec prices.
12. Run a fresh Scan & Verify immediately before locking.
13. Lock a bill snapshot that does not change when files later change.
14. Keep multiple same-customer folders as separate orders while aggregating their totals under one customer.
15. View daily and monthly reports from SQLite without repeatedly rescanning historical folders.
16. Scan only missing days when preparing a monthly report.
17. Recover gracefully from common filesystem and data-resolution errors.
18. Run with negligible idle disk/CPU overhead when no scan is active.

---

## 32. Product principle summary

```text
Filesystem tells us what exists now.
SQLite remembers what the user decided.
Print Folder proposes the bill quantity.
Source Folder verifies it.
Mismatch requires a human decision.
Aliases unify customer identity without destroying order provenance.
Manual Smart Scan protects performance and predictability.
Locked bills preserve history.
```

This set of principles should guide implementation decisions whenever an edge case is not explicitly described above.
