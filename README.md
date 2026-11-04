# Centralized Distributed File Systems

Đồ án cuối kỳ – **Đề tài 7, phần B**: hệ thống truy xuất tài nguyên (thư mục và tập tin) theo mô hình **centralized metadata**: một **Metadata Server** lưu thông tin meta của file, nội dung file được lưu trên nhiều **Data Server**.

> Thuật ngữ chính xác: *distributed file system with centralized metadata* (kiến trúc master–worker, giống GFS / HDFS).

## Tổng quan

| | Stage 1: MVP | Stage 2: High Availability (HA) |
| --- | --- | --- |
| Metadata | 1 server, op log + snapshot, SQLite | 3 node, Raft log, mỗi node một SQLite |
| Data Server | 5 (mỗi laptop một) | Như stage 1 |
| Số bản sao mỗi chunk | 1 (location lưu dạng list) | 3, tự động repair |
| Đường đi dữ liệu | Client ↔ Data Server trực tiếp, kèm signed token | Như stage 1 |
| Chịu được | Restart Metadata Server không mất dữ liệu; phát hiện chunk bị hỏng | Mất 1 Metadata node; mất 1 Data Server |

## Công nghệ

- **.NET 10 (LTS)**, C#
- **Raw TCP** với message framing tự định nghĩa
- **SQLite** cho metadata
- **Avalonia** cho desktop client (Windows + Linux)
- **Docker / Docker Compose** để chạy server
- Stage 2: **Raft** (thư viện DotNext)

## Tài liệu

| Tài liệu | Nội dung |
| --- | --- |
| [docs/plan.md](docs/plan.md) | Kế hoạch 4 tuần: role, contract, milestone, issue của từng người |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Quy trình Gitflow, branch, pull request, review, test, CI |
| [docs/requirements.md](docs/requirements.md) | Yêu cầu chức năng và phi chức năng |
| [docs/architecture.md](docs/architecture.md) | Kiến trúc stage 1 và stage 2, design rules |
| [docs/protocol.md](docs/protocol.md) | Frame format, message, error code, token |
| [docs/rbac.md](docs/rbac.md) | Login, RBAC, group, grant, admin panel |
| [docs/data-model.md](docs/data-model.md) | SQLite schema, op log, chunk file |
| [docs/flows.md](docs/flows.md) | Sequence diagram: login, upload, download, conflict |
| [docs/deployment.md](docs/deployment.md) | Chạy trên 1 máy hoặc 5 laptop, LAN checklist |
| [docs/development.md](docs/development.md) | Môi trường dev, phát triển độc lập từng phần |
| [docs/test-plan.md](docs/test-plan.md) | Kế hoạch test và demo script |
| [docs/adr/](docs/adr/) | Architecture Decision Records |

## Chạy nhanh (sẽ có ở milestone M1)

```bash
# 1 máy: 1 Metadata Server + 5 Data Server
docker compose -f deploy/docker-compose.yml up --build

# 5 laptop: mỗi laptop một file env riêng
docker compose -f deploy/node.yml --env-file deploy/env/laptop1.env up --build
```

## Team

| Role | Thành viên | GitHub |
| --- | --- | --- |
| Lead + core (owner) | Tân | `tankhangkm12` |
| Backend: security | Nhất | `duynhat1504` |
| Backend: data path | Cường | `khongpc-ptit` |
| GUI core | Nguyên | `khanhnguyn2709` |
| Test + GUI edge | Quân | `dvquan0516` |

## Timeline

| Tuần | Ngày | Milestone | Tag |
| --- | --- | --- | --- |
| 1 | 04 – 10/11/2026 | M1, M2, M3 (stage 1) | `v1.0.0` |
| 2 | 11 – 17/11 | S1 Raft | `v1.1.0` |
| 3 | 18 – 24/11 | S2 Replication | `v1.2.0` |
| 4 | 25/11 – 01/12 | S3 Self-healing + final | `v2.0.0` |

Task chi tiết của từng người: [docs/plan.md](docs/plan.md). Theo dõi trên **GitHub Project** của organization.
