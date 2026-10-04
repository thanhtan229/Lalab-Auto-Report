# Production hardening baseline — 2026-10-02

HEAD: `bc54840de5648db3c9a66cb927ed96f4d707218b` (master, pre-existing dirty tree preserved).

Evidence: `docs/production-evidence/` contains initial git status, source hashes, prior review copy and live reproducer output. Reproducers use temporary SQLite/filesystem fixtures, not customer data.

| Finding | Current evidence | Status |
|---|---|---|
| C1 | Rescan preserves exported quantity 15; direct locked rewrite persists 99 | PARTIALLY FIXED |
| C2 | Two roots return IDs 1,1; only one persisted order | OPEN |
| H1 | Access denied in deepest branch returns Resolved, quantity 18 | OPEN |
| H2 | 501 same-time events: first page 500, next 0; rejected DB apply advances checkpoint | OPEN |
| H3 | Unconfigured Worker accepts forged Admin JWT | OPEN |
| H4 | Staff payment mutation returns 200, two DB writes | OPEN |
| H5 | Injected original-bill update failure leaves order in two bills | OPEN |
| H6 | Worker fields differ from PWA bill rendering contract | OPEN |

Validation actually executed: Desktop Release 495/495; Worker 10/10 and TypeScript check passed. These suites do not yet cover the reproduced failures. Baseline is not production acceptance.

Baseline did not change runtime code; restart not applicable. No project application process was running at the initial ownership inspection.
