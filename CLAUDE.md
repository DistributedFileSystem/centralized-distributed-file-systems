# CLAUDE.md – context cho AI làm việc trên repo này

Đồ án cuối kỳ: **Đề tài 7, phần B** – distributed file system với **centralized metadata** (giống GFS/HDFS). Team 5 sinh viên. Phải chạy được trên **5 laptop trong một LAN** (yêu cầu của giảng viên) và cũng chạy được trên 1 máy bằng Docker Compose.

Giao tiếp với người dùng bằng **tiếng Anh**; docs trong repo viết **tiếng Việt, giữ nguyên thuật ngữ tiếng Anh**.

## Trạng thái hiện tại (2026-11-04)

- Repo có docs + quy trình, **chưa có code**. Branch: `main`, `develop`.
- Default branch = `develop`. Ruleset bảo vệ `main` và `develop` đã bật (PR + 1 approval + required check `markdown-links`); đang kiểm tra bằng test PR `feature/0-test-branch-rules` → `develop`.
- Labels, milestones, 39 issues và GitHub Project **chưa được tạo**: chạy `scripts/setup_github.py` (cần `gh auth refresh -s project`).
- Session cloud trước không push được (Claude GitHub App chưa cài trên org `DistributedFileSystem`), nên nội dung được đưa qua git bundle.

## Việc tiếp theo (theo thứ tự)

1. Đảm bảo `main` và `develop` đã lên GitHub; default branch = `develop`; bật branch protection (xem `CONTRIBUTING.md`).
2. `python scripts/setup_github.py --dry-run` rồi chạy thật → labels, milestones M1–M6 + S1–S4, 39 issues, Project "DFS - Task board". Sau đó chỉnh cột Status trên board: Backlog, Ready, In progress, In review, Done.
3. Milestone **M1**: solution skeleton (`Dfs.sln`, các project trong `docs/architecture.md`, `global.json` pin .NET 10), CI build .NET, Dockerfile + `deploy/docker-compose.yml`, chốt `docs/protocol.md` v1, `Dfs.Shared` framing + `Hello`.
4. Điền tên thành viên và người điều phối vào README và slide team.

## Quyết định đã chốt (đừng đổi nếu người dùng không yêu cầu)

| Chủ đề | Quyết định | Lý do / tài liệu |
| --- | --- | --- |
| Data path | **Direct**: client lấy chunk location + **HMAC signed token** từ Metadata Server, rồi đọc/ghi chunk trực tiếp với Data Server | Tránh bottleneck; ADR-0001. Cần xác nhận với giảng viên vì sơ đồ đề vẽ client chỉ nói với server |
| Transport | **Raw TCP**, frame: length 4 B big-endian + type 1 B + payload (JSON); `Hello(protocolVersion)` đầu mỗi connection | `docs/protocol.md` |
| Auth | User login (PBKDF2-SHA256), session token phía server, **RBAC**: system role + group + grant theo thư mục/file, kế thừa xuống cây; admin panel | ADR-0004, `docs/rbac.md` |
| File | **Write-once versions**, `baseVersion` → `Conflict` (optimistic concurrency), giữ N version, GC chunk | ADR-0002 |
| Metadata store | **SQLite + op log + snapshot**; mọi thay đổi là command deterministic qua một store boundary | ADR-0003; để stage 2 chỉ thay op log bằng Raft |
| Stack | **C#, .NET 10 LTS**, **Avalonia** (Windows + Linux), servers là console app chạy trong **Docker** | Team dùng Windows + Linux |
| Stage 1 | 1 Metadata Server, 5 Data Server (mỗi laptop một), **1 bản sao/chunk**, chunk location lưu dạng list | MVP để nộp và bảo vệ |
| Stage 2 | 3 Metadata node **Raft** (DotNext.Net.Cluster, chốt ở ADR-0005), 3 bản sao qua pipeline `ForwardChunk`, read fallback, repair, TLS tuỳ chọn | Chỉ bắt đầu sau tag `v1.0.0` |
| Deployment | Cùng image; env: `NODE_ROLE`, `NODE_ID`, `LISTEN_PORT`, `ADVERTISE_HOST` (IP LAN, không dùng IP Docker), `METADATA_ENDPOINTS` (list), `DATA_DIR`, `TOKEN_KEY` | `docs/deployment.md` |
| Team process | **Gitflow** (`feature/<issue>-<tên>` → `develop` bằng PR, `release/*` → `main` + tag). **Không chia role cố định**: 1 người điều phối, tự nhận task, ai cũng review được (1 approve) | `CONTRIBUTING.md`, README |

## Design rules (bắt buộc khi viết code)

Xem `docs/architecture.md` mục "Design rules": command cho mọi thay đổi metadata, một đường ghi duy nhất, command deterministic, op log là source of truth, endpoint và chunk location luôn là list, đọc metadata từ leader, Data Server không biết user, node quảng bá `ADVERTISE_HOST`.

## Tài liệu

- Repo: `README.md`, `CONTRIBUTING.md`, `docs/` (requirements, architecture, protocol, rbac, data-model, flows, deployment, development, test-plan, adr/), `scripts/` (backlog.json + setup_github.py).
- Plan doc (Claude Docs): https://claude.ai/artifact/GBrdVhj1nsX9iWXrpyj36v
- Slide kickoff (Claude Slides, 18 slide, nền sáng, stage 2 chi tiết): https://claude.ai/artifact/AhGVSV2vtYevkHuy56Mcj7

## Lưu ý cho AI

- Lập kế hoạch và chờ người dùng duyệt trước khi sửa file, chạy lệnh có thay đổi, push, tạo issue.
- Coding conventions do docs của project quyết định; không tự thêm convention.
- Đổi `protocol.md`, `rbac.md`, `data-model.md` phải đổi trong cùng PR với code và báo team.
