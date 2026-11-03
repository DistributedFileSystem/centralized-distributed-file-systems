# Architecture Decision Records

Mỗi quyết định kiến trúc lớn là một file, đánh số tăng dần. Copy `0000-template.md` để viết ADR mới.

| ADR | Quyết định | Trạng thái |
| --- | --- | --- |
| [0001](0001-direct-data-access-with-signed-tokens.md) | Client truy cập Data Server trực tiếp bằng signed token | Accepted |
| [0002](0002-write-once-file-versions.md) | File write-once với version và conflict detection | Accepted |
| [0003](0003-sqlite-op-log-metadata-store.md) | SQLite + op log làm metadata store | Accepted |
| [0004](0004-rbac-model.md) | RBAC với system role, group và grant theo thư mục | Accepted |
| 0005 | Thư viện Raft cho stage 2 | Chưa viết |
