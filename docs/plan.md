# Kế hoạch dự án · 4 tuần (thứ Tư 04/11 – thứ Ba 01/12/2026)

> Kế hoạch đã được team lead duyệt ngày 04/11/2026. Đây là nguồn để tạo **milestones** và **issues** trên GitHub; đổi kế hoạch thì sửa file này trong một pull request.

## 1. Team và role

| Role | Thành viên | GitHub | Phụ trách |
| --- | --- | --- | --- |
| **Lead + core** (owner) | Tân | `tankhangkm12` | Contract cho team · metadata store core (op log, snapshot, Raft) · khởi tạo project, **CI**, Docker, deploy · approve và merge mọi pull request · release, báo cáo, slide |
| **Backend: security** | Nhất | `duynhat1504` | Authentication, authorization, RBAC, data token, audit log, TLS · client failover |
| **Backend: data path** | Cường | `khongpc-ptit` | Framing code, metadata skeleton, client SDK, Data Server, upload/download, version, replication, repair |
| **GUI core** | Nguyên | `khanhnguyn2709` | GUI design (bản nhanh ở tuần 1, tài liệu cuối ở tuần 4) · app shell, file browser, upload/download, admin panel, version history, role editor, audit log viewer |
| **Test + GUI edge** | Quân | `dvquan0516` | Test **độc lập** dạng black-box từ contract và GUI design · conflict dialog và các màn hình giám sát (cluster, leader, replication, repair) |

## 2. Contract cho team (Tân, hạn trưa thứ Năm 05/11)

| Contract | Chốt điều gì | Ai code theo |
| --- | --- | --- |
| `docs/protocol.md` v1 | Frame format, mọi message stage 1 và field, error code | Cường, Quân |
| `docs/rbac.md` v1 | Permission, built-in role, kế thừa grant, format data token | Nhất, Quân |
| `Dfs.Shared` message types | C# record cho mọi message + error code, không chứa logic | Tất cả |
| Interface nội bộ | `IMetadataStore` (submit command) + danh sách command · `IAuthorizer` (user này có được làm việc này trên path này không?) · `IDfsClient` (SDK mà GUI gọi) | Nhất, Cường, Nguyên |

- Cường, Nhất, Quân đọc và comment pull request contract trong nửa ngày; Tân sửa theo góp ý rồi merge.
- Sau khi merge, **chỉ Tân đổi contract**, trong một pull request có báo Quân.
- Dev code theo interface; phần thật chưa có thì dùng `Dfs.Fakes`.

## 3. Cách làm việc

Một repo, một solution (`Dfs.sln`), mỗi phần là một project. Chi tiết Gitflow, review và CI: [CONTRIBUTING.md](../CONTRIBUTING.md).

| Thư mục | Chủ yếu |
| --- | --- |
| `docs/protocol.md`, `docs/rbac.md`, contract trong `Dfs.Shared` | Tân |
| `src/Dfs.MetadataServer` – store core / Raft | Tân |
| `src/Dfs.MetadataServer` – auth / RBAC | Nhất |
| `src/Dfs.MetadataServer` – namespace / upload / version | Cường |
| `src/Dfs.DataServer`, `src/Dfs.Client.Core`, framing code | Cường |
| `src/Dfs.Client.Desktop`, `docs/ui/` | Nguyên (core), Quân (edge) |
| `tests/Dfs.AcceptanceTests` | Chỉ Quân |
| `deploy/`, `.github/workflows/` | Tân |

## 4. Timeline

| Tuần | Ngày | Milestone | Tag | Demo |
| --- | --- | --- | --- | --- |
| 1 | 04 – 10/11 | M1, M2, M3 (stage 1) | `v1.0.0` | Demo script stage 1 (`docs/test-plan.md`) |
| 2 | 11 – 17/11 | S1 Raft | `v1.1.0` | Kill leader → leader mới, không mất dữ liệu |
| 3 | 18 – 24/11 | S2 Replication | `v1.2.0` | Tắt một laptop data → download vẫn chạy |
| 4 | 25/11 – 01/12 | S3 Self-healing + final | `v2.0.0` | Repair về 3 bản sao + full demo stage 2 |

## 5. Milestones và issues

Labels: `area:core`, `area:security`, `area:data`, `area:gui`, `area:test`, `area:devops`, `area:docs`; `type:feature`, `type:test`, `type:design`, `type:docs`, `type:bug`; `stage:1`, `stage:2`. Mỗi issue: một label `area:*`, một `type:*`, một `stage:*`.

### M1 – Contract, skeleton, GUI design nhanh · hạn 05/11/2026

| ID | Issue | Assignee | Area / type | Acceptance criteria | Blocked by |
| --- | --- | --- | --- | --- | --- |
| M1-01 | Solution skeleton + CI (build + unit test) | `tankhangkm12` | devops / feature | `Dfs.sln` + mọi project trong `docs/architecture.md`; `global.json` pin .NET 10; CI build + unit test trên mọi PR; **merge trong ngày 04/11** | – |
| M1-02 | Contract cho team: `protocol.md` v1, `rbac.md` v1, message types trong `Dfs.Shared`, `IMetadataStore` / `IAuthorizer` / `IDfsClient` | `tankhangkm12` | core / docs | Mọi message stage 1 có field + ví dụ; error code đủ; interface có XML doc; Cường, Nhất, Quân đã comment; merge trước trưa 05/11 | M1-01 |
| M1-03 | Command + op log boundary sau `IMetadataStore` | `tankhangkm12` | core / feature | Ghi op log + fsync rồi apply SQLite; command deterministic; unit test apply cùng command ra cùng kết quả | M1-02 |
| M1-04 | `Dfs.Fakes`: fake `IDfsClient`, fake Metadata Server, fake Data Server | `duynhat1504` | security / feature | Fake client trả cây thư mục mẫu; fake server trả chunk location cố định; fake Data Server lưu chunk trong RAM | M1-02 |
| M1-05 | User store + password hashing (PBKDF2-SHA256) | `duynhat1504` | security / feature | Hash có salt; verify đúng / sai; unit test | M1-01 |
| M1-06 | Framing codec + `Hello` | `khongpc-ptit` | data / feature | Encode/decode frame theo `protocol.md`; `Hello` trả `VersionMismatch` khi sai version; unit test round-trip | M1-02 |
| M1-07 | Metadata Server skeleton + SQLite schema | `khongpc-ptit` | data / feature | Console app nghe `LISTEN_PORT`, xử lý `Hello`; tạo schema theo `docs/data-model.md`; đọc config từ env | M1-01 |
| M1-08 | GUI design nhanh | `khanhnguyn2709` | gui / design | Screen flow + mockup đơn giản cho mọi màn hình stage 1 (login, file browser, upload/download, version history, conflict dialog, admin panel, cluster); màu, font, component; trong `docs/ui/`; Tân duyệt trước trưa 05/11 | – |
| M1-09 | App shell + navigation trên fake `IDfsClient` | `khanhnguyn2709` | gui / feature | Cửa sổ chính + navigation theo design; chạy được trên Windows và Linux | M1-04, M1-08 |
| M1-10 | Acceptance test project + raw-TCP test client | `dvquan0516` | test / test | `tests/Dfs.AcceptanceTests` chỉ tham chiếu `Dfs.Shared`; client gửi/nhận frame qua TCP; chạy được với `dotnet test` | M1-01 |
| M1-11 | Test: framing, `Hello`, version mismatch | `dvquan0516` | test / test | Test viết từ `protocol.md`; merge dạng pending, bật khi M1-06 xong | M1-02, M1-10 |
| M1-12 | GUI test checklist từ design | `dvquan0516` | test / test | Checklist cho từng màn hình stage 1 trong `docs/ui/` | M1-08 |

### M2 – Core features · hạn 08/11/2026

| ID | Issue | Assignee | Area / type | Acceptance criteria | Blocked by |
| --- | --- | --- | --- | --- | --- |
| M2-01 | Dockerfile + `docker-compose.yml` (1 metadata + 5 data) | `tankhangkm12` | devops / feature | Image cho Metadata và Data Server; mỗi service một volume + port; xong sáng 06/11 | M1-07 |
| M2-02 | CI: khởi động Compose + chạy acceptance test (required check) | `tankhangkm12` | devops / feature | Job chạy trên mọi PR; thêm vào ruleset làm required check | M2-01, M1-10 |
| M2-03 | Login + session | `duynhat1504` | security / feature | Session token hạn 8 giờ; user bị disable không login được; admin đầu tiên tạo từ `ADMIN_USER` / `ADMIN_PASSWORD` | M1-05, M1-03 |
| M2-04 | Data token: ký + verify (HMAC) | `duynhat1504` | security / feature | Sign ở Metadata, verify trong `Dfs.Shared`; `TokenInvalid` / `TokenExpired`; lệch đồng hồ 2 phút | M1-02 |
| M2-05 | RBAC sau `IAuthorizer`: role, group, grant, kế thừa | `duynhat1504` | security / feature | Built-in role theo `rbac.md`; grant cho user hoặc group; kế thừa xuống thư mục con; người tạo là Owner | M2-03 |
| M2-06 | Kiểm tra permission trên namespace operation | `duynhat1504` | security / feature | Mọi request namespace qua `IAuthorizer`; bị từ chối trả `Forbidden` | M2-05, M2-07 |
| M2-07 | Namespace operation: ListDir, Stat, CreateFolder, Rename, Move, DeleteItem | `khongpc-ptit` | data / feature | Mỗi thao tác là command qua `IMetadataStore`; trùng tên bị từ chối | M1-03, M1-07 |
| M2-08 | SDK `IDfsClient`: connect, login, namespace | `khongpc-ptit` | data / feature | Kết nối theo `METADATA_ENDPOINTS`; giữ session; xử lý `Unauthorized` | M1-06 |
| M2-09 | Data Server: Register, Heartbeat, Write/ReadChunk + heartbeat monitor | `khongpc-ptit` | data / feature | Register bằng `ADVERTISE_HOST:PORT`; heartbeat 3 giây, mất 3 lần → `Dead`; ghi file tạm rồi rename; SHA-256; kiểm tra data token | M2-04 |
| M2-10 | CreateUpload / CommitUpload / Open + chuyển chunk song song | `khongpc-ptit` | data / feature | Chọn Data Server theo chỗ trống; commit kiểm tra checksum; SDK upload/download song song, báo tiến độ | M2-07, M2-09 |
| M2-11 | Màn hình login + cây thư mục | `khanhnguyn2709` | gui / feature | Theo design; báo lỗi rõ; tạo / đổi tên / xoá / di chuyển | M1-09 |
| M2-12 | Upload / download có progress | `khanhnguyn2709` | gui / feature | Chọn file / kéo thả; progress bar, huỷ được; báo `Unavailable` | M1-09 |
| M2-13 | Admin panel: user, group, grant | `khanhnguyn2709` | gui / feature | Tạo / disable user, quản lý group, cấp grant trên thư mục | M1-09 |
| M2-14 | Test: auth + RBAC | `dvquan0516` | test / test | Sai password, user bị disable, session hết hạn, kế thừa quyền, guest `Forbidden`, token sai / hết hạn | M1-10 |
| M2-15 | Test: namespace + upload → download | `dvquan0516` | test / test | Mọi namespace operation; hash file tải về khớp file gốc | M1-10 |

### M3 – Version, durability, release · hạn 10/11/2026

| ID | Issue | Assignee | Area / type | Acceptance criteria | Blocked by |
| --- | --- | --- | --- | --- | --- |
| M3-01 | Snapshot + replay op log khi khởi động | `tankhangkm12` | core / feature | Snapshot kèm `lastSeq`; xoá SQLite rồi restart → dựng lại đúng | M1-03 |
| M3-02 | Deploy 5 laptop: `node.yml` (build tại máy), env, firewall, smoke test | `tankhangkm12` | devops / feature | `docker compose -f deploy/node.yml --env-file laptopN.env up --build`; script mở port Windows + Linux; smoke test kết nối mọi node | M2-01 |
| M3-03 | `release/1.0.0` → tag `v1.0.0`, tổng duyệt demo, báo cáo | `tankhangkm12` | docs / docs | Demo script stage 1 chạy trọn trên 5 laptop; báo cáo stage 1 | M3-12 |
| M3-04 | Audit log | `duynhat1504` | security / feature | Ghi mọi thao tác thay đổi và mọi lần bị từ chối; `ListAudit` | M2-05 |
| M3-05 | Version + `baseVersion` conflict, giữ N version | `khongpc-ptit` | data / feature | `ListVersions`, `Open` theo version; `Conflict` khi version khác `baseVersion` | M2-10 |
| M3-06 | Màn hình version history | `khanhnguyn2709` | gui / feature | Danh sách version, tải version cũ | M2-12 |
| M3-07 | Test màn hình của Quân bằng GUI checklist | `khanhnguyn2709` | test / test | Conflict dialog và cluster screen qua checklist; lỗi thành issue Bug | M3-08, M3-09 |
| M3-08 | Conflict dialog | `dvquan0516` | gui / feature | Reload hoặc save as copy | M1-09 |
| M3-09 | Cluster screen | `dvquan0516` | gui / feature | Data Server, dung lượng, số chunk, trạng thái | M1-09 |
| M3-10 | Test: version + conflict, restart, SQLite dựng lại | `dvquan0516` | test / test | T-01, T-02, T-08 trong `docs/test-plan.md` | M1-10 |
| M3-11 | Chạy GUI checklist trên app thật | `dvquan0516` | test / test | Mọi màn hình của Nguyên; lỗi thành issue Bug | M1-12 |
| M3-12 | Failure test T-01..T-09 + release test trên 5 laptop | `dvquan0516` | test / test | Kết quả ghi vào `docs/test-plan.md` | M3-02 |

### S1 – Raft · hạn 17/11/2026

| ID | Issue | Assignee | Area / type | Acceptance criteria | Blocked by |
| --- | --- | --- | --- | --- | --- |
| S1-01 | Cập nhật contract cho stage 2 (`NotLeader`, `ForwardChunk`, `CopyChunk`) | `tankhangkm12` | core / docs | `protocol.md` có field + error code mới; Quân đã được báo | – |
| S1-02 | ADR-0005 + Raft adapter sau `IMetadataStore` | `tankhangkm12` | core / feature | Thử thư viện 2 ngày đầu, ghi ADR-0005; command commit khi 2/3 node có | S1-01 |
| S1-03 | Snapshot SQLite cho log compaction | `tankhangkm12` | core / feature | Log cũ được cắt sau snapshot; node mới bắt kịp từ snapshot | S1-02 |
| S1-04 | Compose + CI test cho 3 metadata node; tag `v1.1.0` | `tankhangkm12` | devops / feature | Compose 3 metadata + 5 data; CI chạy test failover | S1-02 |
| S1-05 | `NotLeader` redirect + retry theo endpoint list | `duynhat1504` | security / feature | Client theo `NotLeader` tới leader mới; retry qua 3 endpoint | S1-01 |
| S1-06 | Custom role editor (backend) | `duynhat1504` | security / feature | Tạo / sửa role và tick permission | M2-05 |
| S1-07 | Data Server heartbeat theo leader hiện tại | `khongpc-ptit` | data / feature | Tìm leader mới khi heartbeat lỗi; register lại nếu cần | S1-01 |
| S1-08 | Garbage collection + ChunkReport / DeleteChunk | `khongpc-ptit` | data / feature | Xoá chunk mồ côi và chunk của version cũ; ChunkReport sau restart | M3-05 |
| S1-09 | Cập nhật GUI design cho màn hình stage 2 | `khanhnguyn2709` | gui / design | Cluster view, replication status, repair progress, role editor, audit log viewer | – |
| S1-10 | Màn hình role editor | `khanhnguyn2709` | gui / feature | Theo design; gọi API của S1-06 | S1-06, S1-09 |
| S1-11 | Cluster view: 3 metadata node + leader | `dvquan0516` | gui / feature | Hiện node nào là leader, cập nhật khi đổi leader | S1-09 |
| S1-12 | Test: failover | `dvquan0516` | test / test | Kill leader → leader mới; không mất dữ liệu đã commit; leader cũ quay lại làm follower | S1-01 |

### S2 – Replication · hạn 24/11/2026

| ID | Issue | Assignee | Area / type | Acceptance criteria | Blocked by |
| --- | --- | --- | --- | --- | --- |
| S2-01 | Test failover + replication nhiều node trong CI; tag `v1.2.0` | `tankhangkm12` | devops / feature | CI chạy kịch bản tắt Data Server | S1-04 |
| S2-02 | Read fallback khi lỗi hoặc checksum sai | `duynhat1504` | security / feature | Đọc bản sao kế tiếp; báo bản hỏng cho leader | S2-04 |
| S2-03 | Xử lý pipeline đứt | `duynhat1504` | security / feature | Data Server chết giữa chừng → xin server khác từ leader, ghi lại chunk | S2-04 |
| S2-04 | Replication factor 3 + pipeline `ForwardChunk` | `khongpc-ptit` | data / feature | Factor trong config; client ghi một lần, Data Server chuyển tiếp | S1-01 |
| S2-05 | Đặt 3 bản sao trên 3 laptop khác nhau | `khongpc-ptit` | data / feature | Placement không bao giờ đặt 2 bản sao trên cùng laptop | S2-04 |
| S2-06 | Audit log viewer | `khanhnguyn2709` | gui / feature | Lọc theo user, thời gian, allowed / denied | M3-04, S1-09 |
| S2-07 | UX polish các màn hình chính | `khanhnguyn2709` | gui / feature | Sửa các điểm trong GUI checklist | M3-11 |
| S2-08 | Replication status theo file | `dvquan0516` | gui / feature | Số bản sao mỗi chunk, cảnh báo khi thiếu | S1-09 |
| S2-09 | Test: replication | `dvquan0516` | test / test | 3 bản sao trên 3 laptop; laptop chết giữa upload / download; bản sao hỏng | S1-01 |

### S3 – Self-healing + final · hạn 01/12/2026

| ID | Issue | Assignee | Area / type | Acceptance criteria | Blocked by |
| --- | --- | --- | --- | --- | --- |
| S3-01 | TLS certificate trong deploy và CI | `tankhangkm12` | devops / feature | Chứng chỉ tự ký cho LAN; CI chạy với TLS | S3-03 |
| S3-02 | `release/2.0.0` → tag `v2.0.0`, báo cáo cuối + slide | `tankhangkm12` | docs / docs | Demo stage 2 chạy trọn trên 5 laptop | S3-10 |
| S3-03 | TLS trên mọi kết nối | `duynhat1504` | security / feature | `SslStream` cho client ↔ metadata và client ↔ data | – |
| S3-04 | Dọn bản sao thừa | `duynhat1504` | security / feature | Laptop cũ quay lại → ChunkReport → xoá bản thừa | S1-08 |
| S3-05 | Repair manager + `CopyChunk` | `khongpc-ptit` | data / feature | Ưu tiên chunk còn 1 bản; giới hạn copy song song; location mới commit qua Raft | S2-04 |
| S3-06 | Tài liệu GUI design cuối | `khanhnguyn2709` | gui / design | Mọi màn hình stage 1 + 2; **hạn 01/12** | S1-09 |
| S3-07 | UI polish cuối cho demo | `khanhnguyn2709` | gui / feature | Không còn lỗi mở trong GUI checklist | S2-07 |
| S3-08 | Repair progress view | `dvquan0516` | gui / feature | Số chunk thiếu bản sao giảm dần về 0 | S3-05 |
| S3-09 | Test: repair, dọn bản thừa, TLS bắt buộc | `dvquan0516` | test / test | Repair về 3 bản sao; bản thừa bị xoá; kết nối không TLS bị từ chối | S1-01 |
| S3-10 | Full test suite stage 2 trên 5 laptop + test report cuối | `dvquan0516` | test / test | Kết quả trong `docs/test-plan.md` | S3-09 |

**Stage 1 polish** đã được đưa vào stage 2: audit log (M3-04), role editor (S1-06), garbage collection + ChunkReport/DeleteChunk (S1-08).

## 6. Rủi ro

| Rủi ro | Cách xử lý |
| --- | --- |
| Tân gánh critical path ngày 1–2 (skeleton + CI, contract, op log) | Skeleton + CI xong 04/11; contract trưa 05/11; Docker dời sang 06/11; trong lúc đó mọi người làm việc không cần contract |
| Contract sai ảnh hưởng cả team | Nửa ngày comment trước khi merge; sau đó chỉ Tân sửa và báo Quân |
| Tân tự merge bằng bypass bỏ qua CI và test | Quy tắc team: chỉ bypass khi CI xanh và Quân đã comment "Test passed" |
| Không có CD, mỗi laptop tự build image | Build sẵn trên mỗi laptop một ngày trước demo |
| Stage 1 trong 1 tuần | Đã gộp issue nhỏ, dời phần phụ sang stage 2; nếu M2 trễ, admin panel chỉ còn grant |
| Raft là phần rủi ro nhất | Thử thư viện 2 ngày đầu S1, ghi ADR-0005; task S1 của người khác không chờ Raft |
| Quân dồn việc ở M3 | Ưu tiên test; cluster screen dời sang S1 nếu cần |
