# UX/UI SPEC — Lalab Auto Report

> Product: **Lalab Auto Report**
>
> Platform: Windows desktop
>
> Primary user: photo-print workshop operator
>
> Product plan: `PLAN_lalab.md`
>
> Agent rules: `AGENTS.md`

---

## 1. UX objective

Lalab Auto Report should make this workflow substantially faster:

```text
Find today's customer folders
-> inspect print specifications
-> identify final folders actually printed
-> compare source vs print quantities
-> resolve mismatches
-> calculate/lock bills
-> review daily or monthly totals
```

The UI must optimize for **fast verification**, not data-entry-heavy bookkeeping.

The operator should be able to understand the state of a day's work within seconds.

---

## 2. Product experience principles

### 2.1 Problems first

Rows requiring attention should be obvious.

Examples:

- unknown customer,
- unknown print specification,
- ambiguous print folder,
- Source/Print mismatch,
- missing price,
- scan error.

Do not make the user inspect every successful order to discover problems.

### 2.2 Quiet success

Successful items should use restrained visual treatment.

The UI should visually prioritize exceptions rather than filling the screen with green badges.

### 2.3 Never hide provenance

A canonical customer may contain multiple physical folders/orders.

The UI may aggregate totals but must always allow expansion back to the original folder names.

### 2.4 Distinguish observed vs billed data

Always distinguish:

```text
Source
Print
Bill
```

Do not use one generic `Quantity` field where the user could confuse them.

### 2.5 Explicit ambiguity

When the app cannot safely determine something, it should say so and offer a direct resolution action.

Do not silently guess customer identity, print folder, print specification or billed quantity.

### 2.6 Historical stability

Locked bills should look and behave differently from live/unlocked filesystem observations.

A historical bill should never appear to change automatically because files were later edited.

### 2.7 Low-friction scanning

The app does not continuously scan in V1.

Scanning must be easy to invoke at the scope the user currently needs.

---

## 3. Visual direction

Use a clean professional utility-app aesthetic suitable for long daily use.

Recommended characteristics:

- Windows-native desktop feel.
- Dense enough for operational data, but not spreadsheet-cluttered.
- Clear typography hierarchy.
- Moderate spacing.
- Strong alignment of numeric columns.
- Minimal decoration.
- System light/dark theme support is preferred if easy; light mode is sufficient for initial V1 if necessary.
- Avoid gradients, glass effects and oversized marketing-style cards.

Use color primarily for semantic states, not decoration.

Do not rely on color alone; pair color with icon/text/status.

---

## 4. Information architecture

Recommended primary navigation:

```text
Dashboard
Orders / Scan
Customers
Price List
Reports
Settings
```

For a compact V1, `Dashboard` and `Orders / Scan` may initially share one workspace if this reduces complexity without losing clarity.

### Persistent app chrome

Recommended shell:

```text
+---------------------------------------------------------------+
| Lalab Auto Report                           [Settings] [Help]  |
+---------------+-----------------------------------------------+
| Dashboard     |                                               |
| Orders        |                Main content                   |
| Customers     |                                               |
| Price List    |                                               |
| Reports       |                                               |
| Settings      |                                               |
+---------------+-----------------------------------------------+
```

The left navigation should remain compact.

---

## 5. Dashboard / Daily Workspace

This is the primary operational screen.

### 5.1 Header

Show:

```text
Selected date
Last successful scan time
Scan status
Primary Scan button
```

Example:

```text
28/09/2026                         Last scan: 16:42
[<] [Today] [>]                    [Scan Date]
```

Optional secondary actions:

```text
Scan Missing Days
Open Date Folder
```

Do not place auto-scan controls prominently because V1 is manual Smart Scan.

### 5.2 Summary strip

Show only useful operational totals, for example:

```text
Orders: 32
Customers: 21
Needs review: 3
Ready/Locked: 29
```

Revenue may be shown only when pricing/billing state makes the number meaningful.

### 5.3 Main list

Group by canonical customer while retaining each physical order.

Suggested columns:

```text
Customer / Order Folder
Status
Specs
Source
Print
Bill Qty
Amount
Last Scan
```

Canonical customer row example:

```text
▾ Văn An                         3 orders                 415,000
    Văn An/       Ready          2    25    25    25      125,000
    Anh An/       Review         1    20    18    --       --
    A.An/         Locked         3    42    42    42      210,000
```

Aggregation row must not replace the child orders.

### 5.4 Status priority

Recommended priority order:

```text
ERROR
NEEDS REVIEW
SCANNING
UNSCANNED / STALE
READY / VERIFIED
LOCKED
```

If one child order has a blocking issue, the canonical customer group should indicate that attention is required.

### 5.5 Filters

Useful filters:

```text
All
Needs Review
Ready
Locked
Unscanned
```

Search should match:

- canonical customer name,
- original folder name,
- alias,
- order path where practical.

---

## 6. Scan behavior and scan UX

### 6.1 Manual scopes

Expose the smallest relevant scan scope from context.

Global/date level:

```text
Scan Date
Scan Missing Days
Scan Date Range
```

Customer/order context:

```text
Rescan Order
```

Specification context:

```text
Rescan Specification
```

Before lock:

```text
Scan & Verify Before Lock
```

### 6.2 Progress

Scanning must not freeze the app.

Show:

```text
Scanning 28/09/2026...
Customer 12 of 43
```

Use a progress indicator even when exact file-level percentage is not available.

Offer Cancel when technically safe.

### 6.3 Scan completion

After scan, show a compact result summary:

```text
Scan complete
32 orders · 3 need review · 0 fatal errors
```

Do not use modal dialogs for every successful scan.

### 6.4 Scan failure

If the root/date folder is unavailable:

- show a clear top-level error,
- preserve previously saved data,
- do not replace old values with zero,
- offer Retry and Open Settings.

---

## 7. Order Detail screen

This screen resolves most day-to-day exceptions.

### 7.1 Header

Show:

```text
Canonical customer
Original folder name
Work date
Order status
Relative path
Last scan
```

Example:

```text
Văn An
Folder: Anh An/
28/09/2026
Status: Needs Review
```

Actions:

```text
Open Order Folder
Rescan Order
```

### 7.2 Specification table

Each print specification is one row/card.

Recommended fields:

```text
Specification
Source
Print
Difference
Bill Quantity
Unit Price
Line Total
Status
```

Example:

```text
13x18 in
Source      120
Print       118
Difference   -2
Bill          --
Status      Needs quantity decision
```

### 7.3 Expandable technical details

Keep path details available but secondary:

```text
Source folder:  ...\13x18 in\
Print folder:   ...\13x18 in\sua-lan-2\
```

Actions:

```text
Open Source Folder
Open Print Folder
Rescan Specification
Change Print Folder
```

Do not make long paths dominate the normal view.

---

## 8. Source/Print mismatch resolution

This is a key UX flow.

When counts differ, never auto-pick a billing quantity.

Example:

```text
13x18 in

Source: 120
Print:  118
Difference: -2

Choose bill quantity
(o) 118 — Use Print folder
( ) 120 — Use Source folder
( ) Custom: [      ]

Optional note: [____________________________]

[Apply]
```

Important behavior:

- `Use Print` is the recommended/default-focused choice because Print is the normal billing source, but it must still require explicit confirmation on mismatch.
- `Use Source` is an explicit override.
- `Custom` accepts integer >= 0.
- Save `QuantityResolutionMode` with the decision.
- Keep Source and Print visible after resolution.

Resolved display example:

```text
Source 120 | Print 118 | Bill 120 · Using Source
```

Do not hide the mismatch after it is resolved; mark it as resolved.

---

## 9. Ambiguous Print Folder resolution

If multiple image-bearing leaf folders exist, display candidates clearly.

Example:

```text
Cannot determine the final print folder automatically.

Choose the folder actually used for printing:

( ) retouch-a/      98 images
( ) retouch-b/     100 images

[Open Folder] available for each candidate

[Use Selected Folder]
```

Show relative path when names are identical or unclear.

After selection:

- persist it for that order/spec context;
- use it on rescan if it still exists;
- allow `Change Print Folder` later;
- if it disappears, return to review state.

Never choose the largest count or newest folder automatically unless a future explicit rule is approved.

---

## 10. Customer resolution UX

### 10.1 Known alias

If exact/normalized alias is known, resolve silently and show the canonical customer normally.

The original order folder name remains visible in Order Detail.

### 10.2 Unknown folder name

Example:

```text
Unrecognized customer folder: "A An"

Possible match
Văn An
Aliases: Văn An, Anh An, A. An

[Map to Văn An]
[Choose Another Customer]
[Create New Customer]
```

A fuzzy suggestion should be described as a possible match, not as a confirmed identity.

### 10.3 Alias collision

If an alias is ambiguous, require current-order selection and offer an option to repair aliases in Customer Manager.

Never auto-map a collision.

---

## 11. Customer Manager

Primary goals:

- maintain canonical names,
- maintain aliases,
- inspect customer history.

Recommended layout:

```text
Customers                     Customer Detail
-------------------------     ---------------------------
Search...                     Văn An
Văn An                        Aliases
Studio Minh                   - Văn An
Chị Hương                     - Anh An
                              - A. An
                              - A.An

                              [Add Alias]
                              [Rename Display Name]
                              [View Orders]
```

If destructive merge is implemented later, it must have a preview showing affected aliases/orders before confirmation.

---

## 12. Print Specification / Price List

Primary table:

```text
Specification      Unit price      Status
13x18 in            5,000           Active
20x30              15,000           Active
40x60 TG           80,000           Active
```

Support:

```text
Add specification
Edit price
Activate/deactivate
Manage aliases
```

Unknown spec resolution should allow:

```text
Map folder name to existing spec
or
Create new specification
```

Never silently ignore an unknown spec folder.

---

## 13. Bill preview and locking

### 13.1 Pre-lock action

Primary action:

```text
[Scan & Verify Before Lock]
```

Do not use a plain `Lock` button that bypasses verification.

### 13.2 Verification sequence

UI should guide the user through blockers in this order:

1. scan errors,
2. unresolved customer,
3. unknown specification,
4. ambiguous/missing print folder,
5. Source/Print mismatch,
6. missing price,
7. final bill preview.

Where possible, provide `Next issue` navigation.

### 13.3 Bill preview

Example:

```text
Văn An — 28/09/2026
Order folder: Anh An/

13x18 in    118 x 5,000    590,000
20x30         4 x 15,000    60,000
-----------------------------------
Total                       650,000

[Back to Review]        [Lock Bill]
```

### 13.4 Lock confirmation

Use a concise confirmation because lock changes historical behavior.

Explain:

```text
Locking stores this bill as a historical snapshot.
Later file changes will not update it automatically.
```

Buttons:

```text
Cancel
Lock Bill
```

### 13.5 Locked view

Clearly display:

```text
LOCKED
Locked at: ...
```

Normal scan actions must not imply that locked totals will update.

If a post-lock verification scan finds changes, show:

```text
Filesystem changed after this bill was locked.
Locked bill remains unchanged.
```

Provide explicit compare/reopen actions only if implemented.

---

## 14. Reports UX

Reports use persisted database data by default.

Opening a report must not silently trigger filesystem scanning.

### 14.1 Daily report

Show:

- total orders,
- canonical customers,
- quantities by specification,
- billed amount,
- unresolved orders,
- locked/unlocked status.

Allow expansion from customer aggregate to physical orders.

### 14.2 Monthly report

Recommended header:

```text
September 2026
```

Metrics:

```text
Total billed revenue
Total bill quantity
Orders
Customers
Unresolved orders
Days never scanned
```

Useful sections:

```text
By customer
By specification
By date
Needs attention
```

Primary corrective action:

```text
[Scan Missing Days]
```

This should scan only days without required scan records, not rescan the entire month.

### 14.3 Custom range

Allow selecting start/end dates and querying persisted data.

Scanning is a separate explicit action.

---

## 15. Settings UX

Recommended groups:

### General

```text
Root Folder          [D:\...] [Browse]
Database location    read-only or advanced
```

### Image counting

```text
Supported extensions
.jpg .jpeg .png .tif .tiff .bmp .webp .heic
```

Make editing safe; validate extension format.

### Scan behavior

V1:

```text
Manual Smart Scan
[ ] Scan today's folder when app starts   (optional feature)
[ ] Include yesterday                     (dependent option)
```

Do not expose periodic polling intervals if that feature is not implemented.

### Backup

When implemented:

```text
Open Data Folder
Backup Database
Restore / migration guidance
```

---

## 16. Empty states

Good empty states should tell the user what to do next.

Examples:

### No root folder

```text
Choose the root folder that contains your date folders.
[Choose Root Folder]
```

### Date not scanned

```text
No scan data for 28/09/2026.
[Scan Date]
```

### No orders on date

```text
No customer folders were found for this date.
[Open Date Folder] [Scan Again]
```

### No customers configured

```text
Customers will be created or mapped as folders are scanned.
```

Avoid empty grids with no explanation.

---

## 17. Error states

Use actionable language.

Bad:

```text
Error 0x80070005
```

Preferred:

```text
Cannot access this folder.
The folder may have been moved or Windows denied permission.

[Retry] [Open Parent Folder]
Details: Access denied (...)
```

Technical details can be expandable/copyable for diagnosis.

Never overwrite cached data with zero merely because a scan failed.

---

## 18. Status vocabulary

Use consistent user-facing wording.

Suggested mapping:

```text
UNSCANNED                Not scanned
SCANNING                 Scanning
SCANNED                  Scanned
NEEDS_REVIEW             Needs review
VERIFIED                 Ready
BILLED                    Billed
LOCKED                    Locked
ERROR                     Error
```

Sub-status/reasons can include:

```text
Unknown customer
Unknown specification
No print folder
Multiple print folders
Quantity mismatch
Missing price
Filesystem unavailable
Changed after lock
```

Avoid exposing raw enum names in UI.

---

## 19. Semantic color guidance

Exact colors may follow the Windows theme/design system; semantics should remain consistent.

```text
Neutral     normal/informational
Accent      primary action/current selection
Warning     needs review / mismatch / ambiguity
Error       scan/access failure or blocking problem
Success     verified/complete, used sparingly
Locked      distinct neutral/accent treatment, not necessarily success green
```

Always include icon/text in addition to color.

---

## 20. Icons

Use simple familiar icons consistently:

```text
Folder        open filesystem location
Refresh       rescan
Search        search/filter
Warning       review needed
Lock          locked bill
Check         verified
Calendar      date/range
User          customer
Receipt       bill/report
Settings      configuration
```

Avoid custom decorative iconography for core operations.

---

## 21. Keyboard and productivity

The operator may use the app repeatedly throughout the day, so keyboard efficiency is useful.

Recommended shortcuts where practical:

```text
Ctrl+F       focus search
F5           rescan current scope, with clear scope label
Enter        open selected order
Esc          close secondary dialog/panel
```

Do not assign destructive actions to easy accidental shortcuts.

Support standard Windows tab navigation.

---

## 22. Confirmation rules

Do not confirm routine safe actions unnecessarily.

No confirmation needed for:

- opening folders,
- filtering,
- ordinary scan/rescan,
- navigating reports.

Confirmation/review required for:

- locking a bill,
- reopening a locked bill,
- destructive alias/customer merge,
- database restore,
- deleting persistent records if such actions exist.

Mismatch resolution itself is explicit enough and does not need a second generic confirmation after `Apply`.

---

## 23. Responsive behavior inside desktop window

Minimum layout should remain usable on common 1080p Windows displays.

When window width becomes constrained:

1. preserve Customer/Order, Status and Bill columns first;
2. allow less-important columns such as Last Scan to hide or move into detail;
3. avoid horizontal scrolling for the most common workflow where reasonable.

Long folder names should truncate with tooltip/full-path detail rather than breaking the layout.

---

## 24. Accessibility and readability

- Use readable default font sizes for long sessions.
- Numeric values should be aligned consistently.
- Maintain sufficient contrast.
- Do not communicate status by color alone.
- Support keyboard focus indicators.
- Avoid tiny click targets.
- Format VND clearly and consistently.
- Format dates consistently according to the chosen app locale.

---

## 25. Recommended main workflow

The ideal daily flow should be:

```text
Open Lalab Auto Report
        ↓
Select Today / desired date
        ↓
Scan Date
        ↓
Review only highlighted problems
        ↓
Resolve unknown customer/spec/print folder/mismatch
        ↓
Scan & Verify Before Lock
        ↓
Review final bill
        ↓
Lock
        ↓
Daily/monthly reports update from database
```

The user should not need to manually browse every customer/spec folder during a normal successful day.

---

## 26. Key UX acceptance scenarios

Codex should verify these workflows manually or through UI/integration tests where practical.

### Scenario A — Straightforward order

```text
Source 100
Print 100
Known customer
Known spec
One print-folder leaf
```

Expected:

- scan resolves automatically,
- order is Ready,
- Bill Quantity = 100,
- lock flow requires no unnecessary intervention.

### Scenario B — Quantity mismatch

```text
Source 100
Print 98
```

Expected:

- order clearly marked Needs Review,
- user sees both values,
- user can choose Print, Source or Custom,
- selected mode remains visible/auditable,
- order cannot lock before resolution.

### Scenario C — Same customer, three folders

```text
Văn An/
Anh An/
A.An/
```

All map to the same canonical customer.

Expected:

- customer group shows three distinct child Orders,
- each child can be opened and billed independently,
- customer total aggregates them.

### Scenario D — Ambiguous print folders

Two leaf candidates exist.

Expected:

- no automatic selection,
- both candidates and counts displayed,
- user can open folders before choosing,
- selected choice persists for the order/spec.

### Scenario E — Monthly report

Expected:

- report opens from SQLite without scanning the month,
- missing scan days are visible,
- `Scan Missing Days` scans only missing days.

### Scenario F — Locked bill changed on disk

Expected:

- locked bill value remains unchanged,
- later explicit scan may warn that filesystem changed,
- no silent historical mutation occurs.

---

## 27. V1 UX scope guardrails

Do not spend V1 time on:

- realtime scan animation,
- image thumbnail galleries,
- AI customer recognition,
- analytics dashboards with decorative charts,
- complex customizable themes,
- drag-and-drop workflow builders,
- cloud collaboration,
- multi-user permissions.

First make the core loop exceptionally clear:

```text
Scan -> Review Exceptions -> Bill -> Lock -> Report
```

---

## 28. UI definition of done

A UI feature is complete when:

```text
[ ] The primary user action is obvious
[ ] Loading/scanning state is visible
[ ] Empty state is handled
[ ] Error state is handled
[ ] Review/blocking state is handled
[ ] Success state is clear but not noisy
[ ] Keyboard/focus behavior is reasonable
[ ] Long folder/customer names do not break layout
[ ] Source/Print/Bill meanings remain distinct
[ ] Original physical folder provenance is accessible
[ ] Locked vs live data cannot be confused
```

The final design should feel like a dependable workshop tool, not a generic admin dashboard.
