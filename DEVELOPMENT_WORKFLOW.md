# Development Workflow Rules

## Mục tiêu

Trong quá trình development, ưu tiên vòng lặp phát triển nhanh, ổn định và dễ kiểm thử.

Không được mặc định đóng gói ứng dụng thành `.exe` sau mỗi lần coding.

Mục tiêu của mỗi iteration là:

`Understand → Code → Validate → Build when needed → Restart → Health Check → Ready for User Test`

Ứng dụng phải được đưa về trạng thái có thể test ngay sau khi hoàn thành thay đổi.

---

## 1. Không package `.exe` sau mỗi lần coding

Trong quá trình development:

- Không tự động tạo installer.

- Không tự động package `.exe`.

- Không chạy pipeline release nếu không cần thiết.

- Không thực hiện các bước đóng gói tốn thời gian chỉ để kiểm tra một thay đổi development thông thường.

Chỉ package `.exe` khi:

- User yêu cầu rõ ràng.

- Cần kiểm thử behavior riêng của packaged application.

- Cần kiểm tra installer/distribution.

- Đang chuẩn bị release.

- Có thay đổi liên quan trực tiếp đến packaging, installer, auto-update hoặc production executable.

Nếu không thuộc các trường hợp trên, hãy chạy ứng dụng trực tiếp từ source hoặc development build.

---

## 2. Phân biệt Build và Package

Không được hiểu `build` và `package .exe` là cùng một việc.

### Build

Build có thể được sử dụng trong quá trình development để phát hiện:

- compile errors;

- TypeScript/type errors;

- bundling errors;

- import/module errors;

- dependency incompatibilities;

- production-only build errors;

- frontend/backend integration issues;

- configuration errors.

Build không nhất thiết phải tạo `.exe`.

### Package

Package là quá trình tạo executable/installer/distributable artifact như:

- `.exe`;

- installer;

- portable build;

- release archive;

- production distribution bundle.

Package chỉ thực hiện khi thực sự cần.

---

## 3. Khi nào cần Build

Không bắt buộc full build sau mọi thay đổi nhỏ.

Agent phải tự đánh giá phạm vi thay đổi.

### Có thể không cần full build khi

Thay đổi chỉ gồm các chỉnh sửa development nhỏ như:

- CSS;

- spacing;

- text;

- icon;

- layout nhỏ;

- UI presentation;

- logic frontend rất cục bộ;

- thay đổi không ảnh hưởng module graph hoặc compilation.

Trong trường hợp này ưu tiên:

`Code → relevant validation → restart/reload → test`

### Nên Build khi

Thay đổi có liên quan đến:

- TypeScript/interface/type;

- dependency;

- package configuration;

- bundler;

- build configuration;

- environment variables;

- module/import structure;

- shared library;

- API contract;

- backend;

- database;

- application bootstrap;

- routing architecture;

- Electron/Tauri/native layer;

- production runtime;

- hoặc bất kỳ thay đổi nào có khả năng chỉ lỗi khi compile/build.

Trong trường hợp đó:

`Code → Validate → Build → Restart → Health Check`

Không package `.exe` chỉ vì đã chạy build.

---

## 4. Validation sau mỗi thay đổi

Sau khi coding xong, chạy các validation phù hợp với phạm vi thay đổi.

Có thể bao gồm:

- targeted tests;

- unit tests;

- typecheck;

- lint;

- compile;

- build;

- integration test;

- smoke test.

Không cần chạy toàn bộ test suite nếu thay đổi rất nhỏ và targeted validation đã đủ.

Ngược lại, với thay đổi core hoặc có phạm vi rộng, phải tăng mức validation tương ứng.

Ưu tiên validation có giá trị thực tế, không chạy command nặng một cách máy móc.

---

## 5. Tự động Restart sau khi Coding

Sau khi thay đổi code và validation cần thiết hoàn tất, phải đưa ứng dụng về trạng thái sẵn sàng để user test.

### Frontend

Sau mỗi iteration có thay đổi frontend:

- đảm bảo frontend đang chạy code mới nhất;

- restart frontend nếu runtime hiện tại không tự reload đáng tin cậy;

- không để user test nhầm process hoặc bundle cũ.

Nếu development server hỗ trợ hot reload đáng tin cậy và không cần restart, có thể giữ process hiện tại.

Tuy nhiên nếu có nghi ngờ trạng thái runtime cũ, hãy restart.

### Backend

Chỉ restart backend nếu thay đổi có ảnh hưởng tới backend hoặc backend runtime cần reload.

Ví dụ:

- API;

- service;

- server code;

- database integration;

- backend config;

- environment variables;

- shared code được backend sử dụng;

- dependency backend;

- process startup;

- backend runtime state.

Nếu thay đổi chỉ liên quan frontend và backend không bị ảnh hưởng, không restart backend không cần thiết.

---

## 6. Không để nhiều instance chạy song song

Trước khi restart một service, phải xác định và dừng đúng instance cũ nếu cần.

Không được để:

- nhiều frontend instance;

- nhiều backend instance;

- nhiều app instance;

- process cũ giữ port;

- zombie process;

- test nhầm server cũ.

Không kill process dựa trên giả định nguy hiểm nếu có khả năng ảnh hưởng ứng dụng khác.

Ưu tiên xác định process dựa trên:

- PID đã lưu;

- port;

- working directory;

- executable;

- command line;

- project-specific process metadata.

Sau khi stop, xác nhận port/process cũ đã được giải phóng trước khi start lại nếu cần.

---

## 7. Script chạy Development

Ưu tiên tạo một hoặc một số script development đơn giản để user có thể chạy trực tiếp trên Windows.

Có thể sử dụng:

- `.bat`;

- `.cmd`;

- `.ps1`;

- `.vbs`;

hoặc script phù hợp với stack hiện tại.

Nếu repo chưa có script phù hợp, ưu tiên tạo một entry point rõ ràng, ví dụ:

`RESTART.bat`

hoặc:

`DEV_START.bat`

`DEV_STOP.bat`

`DEV_RESTART.bat`

Không tạo nhiều script dư thừa nếu một script duy nhất có thể xử lý tốt.

---

## 8. Yêu cầu đối với `RESTART.bat`

Nếu project phù hợp, ưu tiên duy trì một script `RESTART.bat` có khả năng:

1. xác định các process development hiện tại;

2. dừng process cũ một cách an toàn;

3. giải phóng port nếu cần;

4. start frontend;

5. start backend nếu backend cần chạy;

6. tránh tạo duplicate instance;

7. ghi log startup cần thiết;

8. kiểm tra service đã start thành công;

9. trả về trạng thái thành công/thất bại rõ ràng.

Script phải có thể được chạy nhiều lần mà không làm hệ thống rơi vào trạng thái có nhiều instance trùng nhau.

Ưu tiên tính idempotent.

---

## 9. Health Check sau Restart

Không được coi việc chạy command start thành công là bằng chứng ứng dụng đã sẵn sàng.

Sau restart phải kiểm tra runtime thực tế.

### Frontend

Kiểm tra ít nhất một trong các điều kiện:

- dev server đang listen;

- URL frontend trả response;

- application window khởi động thành công;

- UI bundle load được;

- không có startup error nghiêm trọng.

### Backend

Nếu backend được restart, kiểm tra:

- process tồn tại;

- port đang listen;

- health endpoint trả kết quả đúng nếu có;

- API cơ bản phản hồi;

- không có crash loop;

- không có startup exception nghiêm trọng.

Nếu project có endpoint như:

`/health`

`/api/health`

`/ready`

hãy ưu tiên sử dụng endpoint đó.

---

## 10. Nếu Restart hoặc Startup thất bại

Không dừng ở việc báo rằng command thất bại.

Hãy:

1. đọc log;

2. xác định lỗi;

3. xác định lỗi có liên quan đến thay đổi vừa thực hiện hay không;

4. sửa lỗi nếu nằm trong scope hợp lý;

5. chạy lại validation;

6. restart lại;

7. health check lại.

Không được tuyên bố app đã sẵn sàng test nếu health check chưa đạt.

Nếu lỗi là blocker nằm ngoài scope hoặc không thể xử lý an toàn, báo cáo rõ:

- command nào thất bại;

- lỗi chính;

- service nào chưa hoạt động;

- phần nào vẫn hoạt động;

- blocker còn lại.

---

## 11. Không mở nhiều cửa sổ không cần thiết

Khi restart development environment:

- tránh mở nhiều terminal window;

- tránh mở nhiều browser tab;

- tránh spawn process mới mỗi iteration mà không cleanup process cũ;

- tránh gây nhiễu desktop của user.

Nếu cần chạy process nền, ưu tiên cách chạy ổn định và có thể quản lý PID/log.

---

## 12. Không thay đổi workflow hiện có nếu không cần

Trước khi tạo script mới, hãy kiểm tra repo đã có:

- start script;

- dev script;

- restart script;

- build script;

- process manager;

- health check;

- PID management;

- launcher.

Nếu workflow hiện tại đã tốt, ưu tiên mở rộng hoặc sửa workflow đó thay vì tạo một hệ thống song song.

Không phá vỡ command mà user đang sử dụng nếu không có lý do cần thiết.

---

## 13. Ưu tiên tốc độ vòng lặp Development

Mục tiêu là user có thể test thay đổi nhanh nhất có thể nhưng vẫn đủ an toàn.

Không chạy tác vụ nặng không cần thiết sau mỗi chỉnh sửa nhỏ.

Ví dụ không nên mặc định:

`Code → full clean → full build → full test suite → package exe → installer → restart`

cho một thay đổi UI nhỏ.

Thay vào đó:

`Code → targeted validation → restart/reload → health check`

Với thay đổi lớn:

`Code → tests/typecheck → build → restart frontend/backend → health check`

Package `.exe` vẫn là bước riêng.

---

## 14. Development mode là mặc định

Trừ khi task nói rõ đang làm release hoặc production package, hãy mặc định đang ở:

`Development / Test Mode`

Điều này có nghĩa:

- ưu tiên source runtime;

- ưu tiên development build;

- không package `.exe`;

- giữ vòng lặp nhanh;

- tự động đưa app về trạng thái test được.

---

## 15. Sau mỗi task coding

Trước khi kết thúc một task coding, agent phải cố gắng đảm bảo:

- code đã được thay đổi đúng yêu cầu;

- validation phù hợp đã chạy;

- build đã chạy nếu cần;

- frontend đang chạy phiên bản mới nhất;

- backend đã restart nếu thay đổi yêu cầu;

- không còn duplicate instance rõ ràng;

- health check đạt;

- app sẵn sàng cho user test.

Báo cáo cuối task nên ngắn gọn và bao gồm:

- thay đổi chính;

- validation đã chạy;

- có build hay không;

- frontend đã restart/reload hay chưa;

- backend có restart hay không;

- health/status hiện tại;

- blocker nếu còn.

---

## 16. Quy tắc đóng gói cuối cùng

Không package `.exe` trừ khi:

- user nói `build exe`;

- user nói `package`;

- user nói `release`;

- user yêu cầu test executable;

- task trực tiếp liên quan packaging;

- hoặc có lý do kỹ thuật bắt buộc.

Nếu không có các điều kiện trên:

**không tạo** `**.exe**`**.**

Build để kiểm tra code là được phép và được khuyến khích khi phù hợp.

---

## Default Development Loop

Mặc định sử dụng workflow:

`Inspect current runtime`

→ `Implement change`

→ `Run targeted validation`

→ `Run build only when justified`

→ `Restart/reload frontend`

→ `Restart backend only when affected`

→ `Health check`

→ `Fix startup/runtime issues if introduced`

→ `Leave application ready for user testing`

Không package `.exe` trong workflow này.
