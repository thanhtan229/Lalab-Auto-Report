# Final production review đã xác minh — 2026-10-02

**Kết luận: NOT READY cho phát hành production toàn tuyến.** Code trong phạm vi tám finding đã sửa và đạt kiểm tra local; còn các gate nghiệm thu Cloud thật, thiết bị/dataset xưởng và phiên bản phát hành. Đây là báo cáo mới, không thay thế bằng chứng review trước.

## Kết quả các finding

| ID | Trạng thái | Bằng chứng hành vi |
|---|---|---|
| C1 | VERIFIED FIXED | Guard theo status DB cho Customer/Guest Locked/Exported; sửa nội dung phải reopen; re-export dùng snapshot; payment không sửa tiền/số lượng |
| C2 | VERIFIED FIXED | Identity root + relative path; fixture hai roots tách orders; rescan/print/media đúng root; known root thiếu không fallback |
| H1 | VERIFIED FIXED | Lỗi enumeration thành review, không resolve phần đọc được hoặc prune dữ liệu; fresh scoped verification trước lock |
| H2 | VERIFIED FIXED trong protocol V2 | Cursor ID, nhiều pages, event + checkpoint một transaction, durable outbox/opID, revision theo field, pull fail chặn push; legacy cursor yêu cầu đối soát |
| H3 | VERIFIED FIXED | Không fallback JWT/sync/PIN Cloud; cấu hình thiếu fail closed; QR Cloud không mang token LAN |
| H4 | VERIFIED FIXED | Staff payment 403 và financial/media bị chặn trên LAN/Cloud; workflow order hợp lệ vẫn được phép |
| H5 | VERIFIED FIXED | Split một transaction; failure injection rollback membership, totals và job links; retry không tạo bill thừa |
| H6 | VERIFIED FIXED | DTO chung cho bill; modal Admin hiển thị 75.000 đồng, Staff redacted; browser thực LAN/Cloud và contract tests |

Không còn lỗi OPEN được xác định trong tám finding này trên bản code đã kiểm tra. Không coi Desktop/Worker legacy là đáp ứng bảo đảm V2.

## Gate đã chạy

| Gate | Kết quả |
|---|---|
| Full Desktop Debug | PASS 534/534, không skip; `docs/production-evidence/test-results/production-debug-final.trx` |
| Full Desktop Release | PASS 534/534, không skip; `production-release-final.trx` |
| Worker | PASS 43/43; TypeScript noEmit; dry-run bundle; npm audit 0 vulnerabilities |
| Build | Debug sạch 0 warnings/errors; Release/publish win-x64 self-contained single-file thành công |
| D1 migration | Local 0001–0005; chạy lại không còn migration pending |
| Desktop migration | SQLite 21; fixture nâng cấp/khởi động lại, ledger legacy, FK/integrity tests |
| Bản EXE độc lập | PASS startup, duplicate handoff/IPC, restart, LAN token tồn tại qua restart, locked bill không đổi |
| Browser thực | PASS LAN/Cloud Admin/Staff, tên/ngày/dòng bill/tổng 75.000, missing JPEG/QR không gây lỗi |
| Export | JPEG thực đã render và xem: `docs/production-evidence/bill-export-fixture.jpg`; tests re-export snapshot không rescan |
| Backup/restore | Service tests và bản sao SQLite fixture: hashes locked-bill/adjustments/sync tables trước/sau bằng nhau; integrity OK, FK 0 |
| Updater | 6 tests thực thi script thay file; success, locked destination, changed download, unrelated PID, invalid executable, failed restart rollback; script production chạy trên EXE sao chép và profile fixture thành công |

Các lỗi phát hiện trong vòng nghiệm thu đã sửa: LAN PWA thiếu `IS_CLOUD_BUILD`; Dashboard/media/thumbnail còn fallback khi known root mất; công nợ Cloud dùng cache stale sau payment; updater xóa download/khởi động dù copy thất bại; startup không kiểm tra quyền mutex sau recovery. Đã chạy lại gate sau thay đổi cuối.

Updater hiện kiểm tra độ dài download/PE header, stage cạnh EXE, kiểm tra SHA256, thay file nguyên tử, giữ bản trước/download/log và rollback khi khởi động thất bại ngay. Hash kiểm tra tính toàn vẹn trong máy; **không phải chữ ký nhà phát hành**. Actual remote download + tiến trình cập nhật UI chưa nghiệm thu; không chạy tự cập nhật trên ứng dụng phục vụ người dùng.

## Runtime và dữ liệu

- Desktop local PID **93452**, thay PID 87564 sau batch cuối, khởi động sau build Debug cuối; owned LAN `/api/status` và PID listener 5050 xác minh OK.
- Worker local PID **102532**, thay PID 4692; listener 18787 được xác minh thuộc cây Wrangler project. Sau nghiệm thu đã bỏ `.dev.vars` giả: health `configuration_required`, `configurationReady=false` đúng fail closed. Không deploy remote.
- Các process fixture đã dừng. Server project khác trên 8787 được giữ nguyên. Không biết có task xưởng nào bị gián đoạn.
- Inventory chỉ đọc DB thật: schema 21, integrity OK, FK violations 0, 18 orders/36 customer bills; không thấy duplicate identity/known root mất/unassigned root. Cờ `requires_reconciliation=true` được giữ. Inventory không chứng minh lịch sử chưa từng mất provenance.
- Working tree có nhiều thay đổi tồn tại trước task. Không revert/commit/tag thay đổi đó. `git diff --check` còn whitespace trong tree có sẵn; không cleanup rộng để che baseline.

## Gate chưa hoàn thành

1. **Cloud production:** backup D1, secrets, migration/deploy V2, permission/offline/retry và đồng bộ Desktop–điện thoại trên endpoint thật. Chưa được thực hiện remote.
2. **Đối soát:** chủ tiệm duyệt từng payment/delivery/note cũ, backup rồi áp dụng. Không tự reset cursor hoặc sửa lịch sử.
3. **Xưởng:** thử máy in/tem 75×100, điện thoại vật lý, root/NAS đại diện và tình huống mất kết nối. Browser responsive override không thay kích thước thực tế (vẫn 1280×720), nên không ghi PASS cho viewport điện thoại 390×844.
4. **Release/updater:** chọn version mới lớn hơn bản đã phát hành, tag/asset phù hợp; thực hiện download/apply trên bản sao. Code/artifact hiện giữ version 1.0.0/assembly 1.0.0.0, chưa tạo tag hoặc GitHub release.
5. **UI native:** suite kiểm tra service/viewmodel/registry và IPC có PASS; thao tác Explorer/context menu, reopen/quick-bill bằng chuột trên Windows thực cần nghiệm thu tại máy xưởng. Profile fixture không đăng ký registry hoặc scan/cloud tự động.

Hướng dẫn từng bước: [PRODUCTION_ACCEPTANCE_GUIDE.md](docs/PRODUCTION_ACCEPTANCE_GUIDE.md). Release notes/rollback: [RELEASE_NOTES_HARDENING.md](docs/RELEASE_NOTES_HARDENING.md). Manifest xác định artifact bằng SHA256: `docs/production-evidence/release-artifact-manifest.json`.

**STATUS: PARTIAL — phases 0–9 COMPLETE, phase 10 local gates COMPLETE; production gates chờ người dùng/môi trường thật.** Không tuyên bố LGTM cho production khi các gate trên chưa có bằng chứng.
