# ADR-0003: SQLite + op log làm metadata store

- Trạng thái: Accepted
- Ngày: 2026-11-03

## Context
Metadata (cây thư mục, version, chunk map, user, grant) cần bền, truy vấn được, và stage 2 phải replicate được bằng Raft.

## Options
1. Database server riêng (PostgreSQL): mạnh nhưng thêm một thành phần phải vận hành và replicate.
2. **SQLite + op log**: nhúng trong process, không cần cài thêm; op log là chuỗi command có thứ tự, đúng thứ Raft replicate.

## Decision
Chọn **SQLite + op log**. Mọi thay đổi là command; ghi vào op log trước, apply vào SQLite sau. Snapshot định kỳ. Mỗi Metadata node có file SQLite riêng.

## Consequences
- Stage 2 chỉ thay op log bằng Raft log, không đổi schema.
- Command phải deterministic (ID, timestamp, hash quyết định trước khi ghi log).
- Phải test khôi phục: xoá SQLite → dựng lại từ snapshot + log.
