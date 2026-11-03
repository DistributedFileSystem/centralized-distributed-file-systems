# ADR-0001: Client truy cập Data Server trực tiếp bằng signed token

- Trạng thái: Accepted
- Ngày: 2026-11-03

## Context
Sơ đồ đề tài vẽ Client chỉ nói chuyện với Server. Nếu mọi byte của file đi qua Metadata Server (proxy model), server đó thành **bandwidth bottleneck** và làm chậm download khi có nhiều client.

## Options
1. **Proxy model**: client → Metadata Server → Data Server. Đơn giản, một chỗ kiểm tra quyền; nhưng tốn băng thông gấp đôi và server là nút thắt.
2. **Direct model** (GFS / HDFS): Metadata Server trả location + token, client đọc / ghi chunk trực tiếp với Data Server.

## Decision
Chọn **direct model**. Metadata Server vẫn là nơi duy nhất quyết định quyền, thông qua **data token** ký bằng HMAC (user, chunkId, read/write, expiry). Data Server chỉ verify chữ ký.

## Consequences
- Download song song từ nhiều laptop, nhanh hơn; Metadata Server không phải chuyển dữ liệu.
- Client SDK phức tạp hơn; Data Server phải truy cập được từ mọi laptop (`ADVERTISE_HOST`).
- Upload bị bỏ dở để lại orphan chunk → cần garbage collection.
- Cần xác nhận với giảng viên rằng direct model được chấp nhận.
