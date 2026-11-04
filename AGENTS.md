# AGENTS.md – context cho mọi AI agent làm việc trên repo này

> File này dùng chung cho mọi AI agent (Claude, Codex, Cursor, Gemini, Copilot, …). `CLAUDE.md` chỉ import file này (`@AGENTS.md`), nên mọi chỉnh sửa context làm ở `AGENTS.md`.

Đồ án cuối kỳ: **Đề tài 7, phần B** – distributed file system với **centralized metadata** (giống GFS/HDFS). Team 5 sinh viên. Phải chạy được trên **5 laptop trong một LAN** (yêu cầu của giảng viên) và cũng chạy được trên 1 máy bằng Docker Compose.

Giao tiếp với người dùng bằng **tiếng Anh**; docs trong repo viết **tiếng Việt, giữ nguyên thuật ngữ tiếng Anh**.

## Trạng thái hiện tại (2026-11-04)

- Repo có docs + quy trình, **chưa có code**. Branch: `main`, `develop` (default).
- Ruleset "Protect main and develop": PR + 1 approval + required check `markdown-links`, chặn force push và xoá branch; đã kiểm tra bằng PR #1.
- **Kế hoạch 4 tuần đã được duyệt**: [docs/plan.md](docs/plan.md) (role, contract, timeline, 70 issue có assignee, acceptance criteria và phụ thuộc).
- Labels, milestones, issues và GitHub Project **chưa được tạo**; tạo từ `docs/plan.md`.
- Session cloud ghi được lên repo qua **GitHub MCP connector** (tạo branch, commit, PR); `gh` CLI trong session cloud bị chặn, nên việc cần `gh` (label, milestone, project, ruleset) chạy trên máy người dùng.

## Việc tiếp theo (theo thứ tự)

1. Thêm **Repository admin** vào bypass list của ruleset (chế độ For pull requests only) để Tân merge pull request của chính mình.
2. Tạo labels, milestones (M1–M3, S1–S3, có hạn) và 70 issues theo `docs/plan.md`, assign đúng người, điền "Blocked by #".
3. Tạo GitHub Project "DFS - Task board", cột Status: Backlog, Ready, In progress, Testing, In review, Done; issue M1 vào Ready.
4. Bắt đầu **M1** (hạn 05/11): M1-01 solution skeleton + CI phải merge trong ngày 04/11.

## Quyết định đã chốt (đừng đổi nếu người dùng không yêu cầu)

| Chủ đề | Quyết định | Lý do / tài liệu |
| --- | --- | --- |
| Data path | **Direct**: client lấy chunk location + **HMAC signed token** từ Metadata Server, rồi đọc/ghi chunk trực tiếp với Data Server | Tránh bottleneck; ADR-0001. Cần xác nhận với giảng viên vì sơ đồ đề vẽ client chỉ nói với server |
| Transport | **Raw TCP**, frame: length 4 B big-endian + type 1 B + payload (JSON); `Hello(protocolVersion)` đầu mỗi connection | `docs/protocol.md` |
| Auth | User login (PBKDF2-SHA256), session token phía server, **RBAC**: system role + group + grant theo thư mục/file, kế thừa xuống cây; admin panel | ADR-0004, `docs/rbac.md` |
| File | **Write-once versions**, `baseVersion` → `Conflict` (optimistic concurrency), giữ N version, GC chunk | ADR-0002 |
| Metadata store | **SQLite + op log + snapshot**; mọi thay đổi là command deterministic qua một store boundary (`IMetadataStore`) | ADR-0003; để stage 2 chỉ thay op log bằng Raft |
| Stack | **C#, .NET 10 LTS**, **Avalonia** (Windows + Linux), servers là console app chạy trong **Docker** | Team dùng Windows + Linux |
| Stage 1 | 1 Metadata Server, 5 Data Server (mỗi laptop một), **1 bản sao/chunk**, chunk location lưu dạng list | MVP, tuần 1, tag `v1.0.0` |
| Stage 2 | 3 Metadata node **Raft** (DotNext.Net.Cluster, chốt ở ADR-0005), 3 bản sao qua pipeline `ForwardChunk`, read fallback, repair, TLS | Tuần 2–4: S1 Raft, S2 Replication, S3 Self-healing |
| Deployment | Cùng Dockerfile; env: `NODE_ROLE`, `NODE_ID`, `LISTEN_PORT`, `ADVERTISE_HOST` (IP LAN), `METADATA_ENDPOINTS` (list), `DATA_DIR`, `TOKEN_KEY`; **không có CD**, image build tại máy bằng `--build` | `docs/deployment.md` |
| Team | Tân lead + core (contract, store core, Raft, CI); Nhất security; Cường data path; Nguyên GUI core; Quân test độc lập + GUI edge | `docs/plan.md` |
| Process | **Gitflow** với `feature/*`, `test/*`, `design/*`, `release/*`, `hotfix/*`; Quân test trước, Tân approve; Tân tự merge PR của mình bằng admin bypass chỉ khi CI xanh và Quân đã "Test passed"; chỉ Tân sửa contract | `CONTRIBUTING.md` |
| CI | Mọi PR: build, unit test, acceptance test trên cluster Compose, `markdown-links` | `CONTRIBUTING.md` |

## Design rules (bắt buộc khi viết code)

Xem `docs/architecture.md` mục "Design rules": command cho mọi thay đổi metadata, một đường ghi duy nhất, command deterministic, op log là source of truth, endpoint và chunk location luôn là list, đọc metadata từ leader, Data Server không biết user, node quảng bá `ADVERTISE_HOST`.

## Tài liệu

- Repo: `README.md`, `CONTRIBUTING.md`, `docs/` (plan, requirements, architecture, protocol, rbac, data-model, flows, deployment, development, test-plan, adr/).
- Plan doc (Claude Docs): https://claude.ai/artifact/GBrdVhj1nsX9iWXrpyj36v
- Slide kickoff (Claude Slides): https://claude.ai/artifact/AhGVSV2vtYevkHuy56Mcj7

## Lưu ý cho AI

- Lập kế hoạch và chờ người dùng duyệt trước khi sửa file, chạy lệnh có thay đổi, push, tạo issue.
- Coding conventions do docs của project quyết định; không tự thêm convention.
- Chỉ Tân sửa contract (`protocol.md`, `rbac.md`, message types, interface); đổi `data-model.md` trong cùng PR với code.
- Không sửa `tests/Dfs.AcceptanceTests` khi làm feature của dev.
