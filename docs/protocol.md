# Protocol (v0.1 – draft)

> Đây là contract chung: đổi gì phải báo cả team.

## Transport

- **Raw TCP**, một connection dài cho mỗi cặp client – server.
- Port mặc định: Metadata `7000`, Data Server `7001`.

## Frame format

```
+----------------+-------------+-------------------+
| length (4 B)   | type (1 B)  | payload (length B)|
| big-endian u32 | message id  |                   |
+----------------+-------------+-------------------+
```

- `length` = độ dài payload, tối đa 16 MB.
- Control message: payload là **JSON** (UTF-8). Có thể đổi sang MessagePack sau mà không đổi framing.
- Chunk data (`WriteChunk`, `ReadChunk` response): payload = header JSON có độ dài 4 byte phía trước, sau đó là raw bytes của chunk.
- Mọi request có `requestId` (GUID) để log theo dõi một thao tác qua nhiều laptop.

## Handshake

Message đầu tiên của mọi connection là `Hello`:

```json
{ "requestId": "…", "protocolVersion": 1, "nodeType": "client" }
```

Sai `protocolVersion` → server trả lỗi `VersionMismatch` và đóng connection.

## Messages

| Message | Chiều | Mục đích | Stage |
| --- | --- | --- | --- |
| `Hello` | Bất kỳ → bất kỳ | Kiểm tra protocol version | 1 |
| `Login`, `Logout` | Client → Metadata | Bắt đầu / kết thúc session | 1 |
| `ListDir`, `Stat`, `ListVersions` | Client → Metadata | Duyệt thư mục, thông tin file, lịch sử version | 1 |
| `CreateFolder`, `Rename`, `Move`, `DeleteItem` | Client → Metadata | Thay đổi namespace | 1 |
| `CreateUpload`, `CommitUpload`, `AbortUpload` | Client → Metadata | Lên kế hoạch upload, làm file hiển thị, huỷ | 1 |
| `Open` | Client → Metadata | Chunk location + read token cho một version | 1 |
| `WriteChunk`, `ReadChunk` | Client → Data Server | Ghi / đọc một chunk, kèm token | 1 |
| `DeleteChunk` | Metadata → Data Server | Garbage collection | 1 |
| `Register`, `Heartbeat`, `ChunkReport` | Data Server → Metadata | Tham gia, báo còn sống, liệt kê chunk đang giữ | 1 |
| `User*`, `Group*`, `Role*`, `Grant*`, `ListAudit`, `ClusterStatus` | Admin panel → Metadata | Chức năng admin | 1 |
| `ForwardChunk` | Data Server → Data Server | Replication pipeline | 2 |
| `CopyChunk` | Metadata → Data Server | Repair bản sao bị mất | 2 |

### Ví dụ: `CreateUpload`

Request:
```json
{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf",
  "size": 10485760, "baseVersion": 3 }
```
Response:
```json
{ "uploadId": "…", "chunkSize": 4194304,
  "chunks": [
    { "index": 0, "chunkId": "c-01H…", "servers": ["192.168.10.13:7001"], "token": "…" },
    { "index": 1, "chunkId": "c-01H…", "servers": ["192.168.10.15:7001"], "token": "…" }
  ] }
```

### Ví dụ: `CommitUpload`

```json
{ "requestId": "…", "session": "…", "uploadId": "…",
  "checksums": [ { "chunkId": "c-01H…", "sha256": "…" } ] }
```
Lỗi `Conflict` nếu version hiện tại của file khác `baseVersion`.

## Data access token

```
base64url(payload) "." base64url(HMAC-SHA256(TOKEN_KEY, payload))
payload = { "u": userId, "c": chunkId, "op": "r" | "w", "exp": unixSeconds }
```

- Ký bởi Metadata Server, verify bởi Data Server bằng `TOKEN_KEY` chung.
- Hết hạn sau 5 phút; Data Server chấp nhận lệch đồng hồ 2 phút.

## Error codes

| Code | Ý nghĩa |
| --- | --- |
| `Unauthorized` | Chưa login hoặc session hết hạn |
| `Forbidden` | Không có permission |
| `NotFound` | Path hoặc chunk không tồn tại |
| `Conflict` | Version đã thay đổi so với `baseVersion` |
| `TokenInvalid`, `TokenExpired` | Token sai chữ ký / hết hạn |
| `Unavailable` | Data Server cần thiết đang `Dead` |
| `VersionMismatch` | Sai protocol version |
| `NotLeader` | (Stage 2) Node không phải leader; kèm địa chỉ leader |
