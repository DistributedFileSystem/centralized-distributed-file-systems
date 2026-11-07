# Protocol (v1)

> Đây là contract chung: đổi gì phải báo cả team. Chỉ Tân sửa file này, trong PR riêng. Code tương ứng nằm ở `src/Dfs.Shared/Messages`.

## Transport

- **Raw TCP**, một connection dài cho mỗi cặp client – server.
- Port mặc định: Metadata `7000`, Data Server `7001`.
- `protocolVersion` hiện tại = **1**.

## Frame format

```
+----------------+-------------+-------------------+
| length (4 B)   | type (1 B)  | payload (length B)|
| big-endian u32 | message id  |                   |
+----------------+-------------+-------------------+
```

- `length` = độ dài payload (không gồm 5 byte header), tối đa 16 MB (`16 * 1024 * 1024`). Lớn hơn → đóng connection.
- Control message: payload là **JSON** (UTF-8, không BOM) dựng bằng System.Text.Json: tên field `camelCase`, enum là string, `null` được bỏ qua khi ghi. Có thể đổi sang MessagePack sau mà không đổi framing.
- Chunk data (`WriteChunk` request, `ReadChunk` response): payload = **4 byte big-endian `headerLength`**, rồi header JSON dài `headerLength` byte, rồi raw bytes của chunk (phần còn lại của payload).
- Mọi request có `requestId` (GUID, dạng chuỗi) để log theo dõi một thao tác qua nhiều laptop. Response lặp lại `requestId` của request.
- Casing của giá trị enum trong JSON: `kind`, `scope`, `subjectType`, `nodeType` dùng **chữ thường** (`"file"`, `"system"`, `"group"`, `"data"`); `status` dùng `"Alive"` / `"Dead"`; `code` của lỗi dùng PascalCase (`"Conflict"`). Tên role (`Admin`, `Editor`…) là string tự do, không phải enum.
- Quy ước kiểu: id là chuỗi; thời gian là ISO-8601 UTC (`2026-11-20T09:15:02Z`); size là số nguyên 64-bit (byte); `version` là số nguyên bắt đầu từ 1.

## Message id (byte `type`)

- Request: `0x01`–`0x7F`. Response = id của request **OR `0x80`** (ví dụ `Login` = `0x10`, `LoginResponse` = `0x90`).
- `0xFF` = `Error`, có thể trả thay cho bất kỳ response nào.
- Trong `Dfs.Shared`, enum `MessageType` có một member cho mỗi request (`Login = 0x10`) và một member cho mỗi response (`LoginResponse = 0x90`), cộng `Error = 0xFF`. Record của request tên `<Name>`, của response tên `<Name>Response`, của lỗi tên `ErrorResponse`.
- Message không có dữ liệu trả về dùng response rỗng (`{ "requestId": "…" }`).

| Id | Request | Chiều | Stage |
| --- | --- | --- | --- |
| `0x01` | `Hello` | Bất kỳ → bất kỳ | 1 |
| `0x10` | `Login` | Client → Metadata | 1 |
| `0x11` | `Logout` | Client → Metadata | 1 |
| `0x20` | `ListDir` | Client → Metadata | 1 |
| `0x21` | `Stat` | Client → Metadata | 1 |
| `0x22` | `ListVersions` | Client → Metadata | 1 |
| `0x23` | `CreateFolder` | Client → Metadata | 1 |
| `0x24` | `Rename` | Client → Metadata | 1 |
| `0x25` | `Move` | Client → Metadata | 1 |
| `0x26` | `DeleteItem` | Client → Metadata | 1 |
| `0x30` | `CreateUpload` | Client → Metadata | 1 |
| `0x31` | `CommitUpload` | Client → Metadata | 1 |
| `0x32` | `AbortUpload` | Client → Metadata | 1 |
| `0x33` | `Open` | Client → Metadata | 1 |
| `0x40` | `WriteChunk` | Client → Data Server | 1 |
| `0x41` | `ReadChunk` | Client → Data Server | 1 |
| `0x42` | `DeleteChunk` | Metadata → Data Server | 1 |
| `0x50` | `Register` | Data Server → Metadata | 1 |
| `0x51` | `Heartbeat` | Data Server → Metadata | 1 |
| `0x52` | `ChunkReport` | Data Server → Metadata | 1 |
| `0x60` | `CreateUser` | Admin → Metadata | 1 |
| `0x61` | `ListUsers` | Admin → Metadata | 1 |
| `0x62` | `SetUserDisabled` | Admin → Metadata | 1 |
| `0x63` | `ResetPassword` | Admin → Metadata | 1 |
| `0x64` | `CreateGroup` | Admin → Metadata | 1 |
| `0x65` | `ListGroups` | Admin → Metadata | 1 |
| `0x66` | `SetGroupMember` | Admin → Metadata | 1 |
| `0x67` | `CreateRole` | Admin → Metadata | 1 |
| `0x68` | `ListRoles` | Admin → Metadata | 1 |
| `0x69` | `UpdateRole` | Admin → Metadata | 1 |
| `0x6A` | `CreateGrant` | Admin / Owner → Metadata | 1 |
| `0x6B` | `ListGrants` | Admin / Owner → Metadata | 1 |
| `0x6C` | `RevokeGrant` | Admin / Owner → Metadata | 1 |
| `0x6D` | `ListAudit` | Admin → Metadata | 1 |
| `0x6E` | `ClusterStatus` | Admin → Metadata | 1 |
| `0x70` | `ForwardChunk` | Data Server → Data Server | 2 (chưa định nghĩa field) |
| `0x71` | `CopyChunk` | Metadata → Data Server | 2 (chưa định nghĩa field) |
| `0xFF` | `Error` | Server → Client | 1 |

## Handshake

Message đầu tiên của mọi connection là `Hello`. Server trả `HelloResponse` hoặc `Error(VersionMismatch)` rồi đóng connection. Message khác gửi trước `Hello` → `Error(BadRequest)` và đóng connection.

`Hello` — request: `{ "requestId": "…", "protocolVersion": 1, "nodeType": "client" }` (`nodeType`: `client` | `metadata` | `data`).
Response: `{ "requestId": "…", "protocolVersion": 1, "nodeType": "metadata", "nodeId": "m1" }`.

## Quy ước chung

- **Session**: mọi request tới Metadata Server, trừ `Hello`, `Login` và các message của Data Server (`Register`, `Heartbeat`, `ChunkReport`), có field `session`. Thiếu / hết hạn → `Unauthorized`. Message Data Server → Metadata chỉ mang `nodeId`; stage 1 tin cậy các node trong LAN (xem `rbac.md`, mục known limitation).
- **Path**: tuyệt đối, phân cách `/`, gốc là `/`, không có `.` / `..`, tên mỗi đoạn 1–255 ký tự và không chứa `/`. Path sai → `BadRequest`.
- **Namespace** phân biệt hoa thường; trùng tên trong cùng thư mục → `AlreadyExists`.
- **Item** (dùng trong response): `{ "id", "path", "name", "kind": "file"|"folder", "size", "ownerId", "currentVersion", "modifiedAt" }`. `size`/`currentVersion` = 0 với folder.
- **Permission cần** cho mỗi message ghi ở từng mục dưới đây; tên permission xem `rbac.md`.

## Messages (stage 1)

Mọi ví dụ dưới đây rút gọn `requestId` / `session` bằng `"…"` — field vẫn bắt buộc.

### Auth

**Login** — không cần session, không cần permission.
```json
{ "requestId": "…", "username": "an.nguyen", "password": "secret" }
```
Response (`0x90`):
```json
{ "requestId": "…", "session": "s-9f2c…", "expiresAt": "2026-11-20T17:15:02Z",
  "user": { "id": "u-01H…", "username": "an.nguyen" },
  "systemRoles": ["Admin"], "permissions": ["user.manage", "cluster.view"] }
```
Lỗi: `Unauthorized` (sai username/password hoặc user bị disable — không phân biệt để tránh lộ thông tin).

**Logout** — `{ "requestId": "…", "session": "…" }` → response rỗng. Session bị xoá.

### Namespace

**ListDir** (`item.list` trên thư mục) — `{ "requestId": "…", "session": "…", "path": "/projects" }`
```json
{ "requestId": "…", "items": [ { "id": "i-01H…", "path": "/projects/dfs", "name": "dfs", "kind": "folder",
  "size": 0, "ownerId": "u-01H…", "currentVersion": 0, "modifiedAt": "2026-11-20T09:15:02Z" } ] }
```
Chỉ trả item mà user có `item.list` (trên chính item hoặc thư mục cha). Path là file → `BadRequest`; không có → `NotFound`.

**Stat** (`item.list` trên item) — `{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf" }` → `{ "requestId": "…", "item": <Item> }`.

**ListVersions** (`file.read`) — `{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf" }`
```json
{ "requestId": "…", "versions": [
  { "version": 4, "size": 10485760, "createdBy": "u-01H…", "createdAt": "2026-11-20T09:15:02Z", "chunkCount": 3 } ] }
```
Sắp xếp giảm dần theo `version`, chỉ gồm các version còn được giữ (config N, mặc định 5).

**CreateFolder** (`item.create` trên thư mục cha) — `{ "requestId": "…", "session": "…", "path": "/projects/dfs/docs" }` → `{ "requestId": "…", "item": <Item> }`. Người tạo là Owner.

**Rename** (`item.rename`) — `{ "requestId": "…", "session": "…", "path": "/projects/a.txt", "newName": "b.txt" }` → `{ "requestId": "…", "item": <Item> }`.

**Move** (`item.rename` trên item, `item.create` trên thư mục đích) — `{ "requestId": "…", "session": "…", "path": "/projects/a.txt", "newParentPath": "/archive" }` → `{ "requestId": "…", "item": <Item> }`. Di chuyển thư mục vào chính nó hoặc con của nó → `BadRequest`.

**DeleteItem** (`item.delete`) — `{ "requestId": "…", "session": "…", "path": "/projects/a.txt" }` → response rỗng. Xoá thư mục xoá cả cây con. Chunk không còn version nào dùng sẽ bị garbage collection (`DeleteChunk`).

### Upload và Open

**CreateUpload** (`file.write` nếu file đã có, `item.create` trên thư mục cha nếu là file mới)
```json
{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf",
  "size": 10485760, "baseVersion": 3 }
```
`baseVersion` = 0 cho file mới. `size` > giới hạn config (mặc định 2 GB) → `BadRequest`. Response:
```json
{ "requestId": "…", "uploadId": "up-01H…", "chunkSize": 4194304,
  "chunks": [
    { "index": 0, "chunkId": "c-01H…", "servers": ["192.168.10.13:7001"], "token": "…" },
    { "index": 1, "chunkId": "c-01H…", "servers": ["192.168.10.15:7001"], "token": "…" },
    { "index": 2, "chunkId": "c-01H…", "servers": ["192.168.10.11:7001"], "token": "…" }
  ] }
```
Số chunk = `ceil(size / chunkSize)`. `servers` luôn là list (stage 1 một phần tử). `token` là write token (xem "Data access token"). Không đủ Data Server `Alive` → `Unavailable`. `baseVersion` khác version hiện tại → `Conflict` ngay lúc này (kiểm tra lại lúc commit).

**CommitUpload** (cùng permission như CreateUpload)
```json
{ "requestId": "…", "session": "…", "uploadId": "up-01H…",
  "checksums": [ { "chunkId": "c-01H…", "size": 4194304, "sha256": "9f86d0…" } ] }
```
Response: `{ "requestId": "…", "item": <Item>, "version": 4 }`. `checksums` phải đủ mọi chunk của upload, `sha256` là hex thường 64 ký tự. Lỗi `Conflict` nếu version hiện tại của file khác `baseVersion` (response lỗi có `details.currentVersion`). `uploadId` không tồn tại / đã commit / đã abort → `NotFound`.

**AbortUpload** — `{ "requestId": "…", "session": "…", "uploadId": "up-01H…" }` → response rỗng. Chỉ người tạo upload hoặc Admin. Chunk của upload bị garbage collection.

**Open** (`file.read`)
```json
{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf", "version": 4 }
```
`version` = 0 hoặc bỏ qua → version hiện tại. Response:
```json
{ "requestId": "…", "item": <Item>, "version": 4, "size": 10485760, "chunkSize": 4194304,
  "chunks": [ { "index": 0, "chunkId": "c-01H…", "size": 4194304, "sha256": "9f86d0…",
                "servers": ["192.168.10.13:7001"], "token": "…" } ] }
```
`token` là read token. Version không còn giữ → `NotFound`. Mọi Data Server giữ chunk đều `Dead` → `Unavailable`.

### Chunk (Client / Metadata → Data Server)

Payload dạng chunk framing (xem Frame format). Mỗi connection tới Data Server cũng bắt đầu bằng `Hello`.

**WriteChunk** — header JSON + raw bytes:
```json
{ "requestId": "…", "chunkId": "c-01H…", "token": "…", "size": 4194304 }
```
Response: `{ "requestId": "…", "chunkId": "c-01H…", "size": 4194304, "sha256": "9f86d0…" }`. Data Server verify token (`op = w`, khớp `chunkId`), ghi file tạm, tính SHA-256, rename. Số byte khác `size` → `BadRequest`. Ghi lại chunk đã tồn tại với cùng nội dung là idempotent.

**ReadChunk** — request (JSON thường): `{ "requestId": "…", "chunkId": "c-01H…", "token": "…" }`.
Response dạng chunk framing, header: `{ "requestId": "…", "chunkId": "c-01H…", "size": 4194304, "sha256": "9f86d0…" }` + raw bytes. Chunk không có → `NotFound`.

**DeleteChunk** (Metadata → Data Server, không dùng token của user) — `{ "requestId": "…", "chunkIds": ["c-01H…"] }` → `{ "requestId": "…", "deleted": 1 }`. Chunk không tồn tại được coi là đã xoá. Stage 1: Data Server tin Metadata Server trong LAN.

### Cluster (Data Server → Metadata)

**Register** — `{ "requestId": "…", "nodeId": "d1", "address": "192.168.10.13:7001", "capacityBytes": 107374182400, "freeBytes": 85899345920 }` → `{ "requestId": "…", "heartbeatIntervalSeconds": 3 }`. `address` = `ADVERTISE_HOST:PORT`, không dùng IP nội bộ Docker. Đăng ký lại cùng `nodeId` cập nhật địa chỉ.

**Heartbeat** — mỗi 3 giây: `{ "requestId": "…", "nodeId": "d1", "freeBytes": 85899345920, "chunkCount": 120 }` → response rỗng. Mất 3 heartbeat liên tiếp → `Dead`. `nodeId` chưa đăng ký → `NotFound` (Data Server phải `Register` lại).

**ChunkReport** — sau khi `Register` và khi restart: `{ "requestId": "…", "nodeId": "d1", "chunks": [ { "chunkId": "c-01H…", "size": 4194304, "sha256": "9f86d0…" } ] }` → `{ "requestId": "…", "unknownChunkIds": ["c-…"] }` (chunk Data Server giữ mà Metadata không biết; Data Server có thể xoá).

### Admin

Tất cả cần session; permission ghi ở từng message. Response liệt kê dùng `items`-style như dưới.

| Message | Permission | Request (field ngoài `requestId`, `session`) | Response |
| --- | --- | --- | --- |
| `CreateUser` | `user.manage` | `username`, `password` | `user` |
| `ListUsers` | `user.manage` | – | `users[]` |
| `SetUserDisabled` | `user.manage` | `userId`, `disabled` | rỗng |
| `ResetPassword` | `user.manage` | `userId`, `newPassword` | rỗng |
| `CreateGroup` | `group.manage` | `name` | `group` |
| `ListGroups` | `group.manage` | – | `groups[]` |
| `SetGroupMember` | `group.manage` | `groupId`, `userId`, `member` (true = thêm, false = bớt) | rỗng |
| `CreateRole` | `role.manage` | `name`, `scope` (`system`\|`item`), `permissions[]` | `role` |
| `ListRoles` | `role.manage` | – | `roles[]` |
| `UpdateRole` | `role.manage` | `roleId`, `permissions[]` | `role` |
| `CreateGrant` | `item.share` trên `path`; `role.manage` nếu `path` bỏ trống (system grant) | `roleId`, `subjectType` (`user`\|`group`), `subjectId`, `path` (bỏ trống = system grant) | `grant` |
| `ListGrants` | `item.share` trên `path`, hoặc `role.manage` nếu `path` rỗng | `path?`, `subjectId?` | `grants[]` |
| `RevokeGrant` | như `CreateGrant` | `grantId` | rỗng |
| `ListAudit` | `audit.view` | `from?`, `to?`, `userId?`, `limit` (mặc định 100, tối đa 1000) | `entries[]` |
| `ClusterStatus` | `cluster.view` | – | `dataServers[]` |

Response danh sách (mỗi cái kèm `requestId`): `ListUsers` → `{ "users": [<user>] }`, `ListGroups` → `{ "groups": [<group>] }`, `ListRoles` → `{ "roles": [<role>] }`, `ListGrants` → `{ "grants": [<grant>] }`, `ListAudit` → `{ "entries": [<entry>] }`, `ClusterStatus` → `{ "dataServers": [<dataServer>] }`. `UpdateRole` → `{ "role": <role> }` (cùng dạng `CreateRole`). `SetUserDisabled`, `ResetPassword`, `SetGroupMember`, `RevokeGrant` → `{ "requestId": "…" }`.

Hình dạng đối tượng:

- `user`: `{ "id", "username", "disabled", "groupIds": [] }` (không bao giờ trả hash / salt)
- `group`: `{ "id", "name", "memberIds": [] }`
- `role`: `{ "id", "name", "scope", "builtIn", "permissions": [] }` — role built-in không sửa được (`Forbidden`)
- `grant`: `{ "id", "roleId", "subjectType", "subjectId", "path", "itemId" }` — `itemId`/`path` là `null` với system grant
- `entry` (audit): `{ "seq", "time", "userId", "action", "target", "allowed" }`
- `dataServer`: `{ "nodeId", "address", "status": "Alive"|"Dead", "capacityBytes", "freeBytes", "chunkCount", "lastHeartbeat" }`

Ví dụ `CreateGrant`:
```json
{ "requestId": "…", "session": "…", "roleId": "r-editor", "subjectType": "group",
  "subjectId": "g-01H…", "path": "/projects/dfs" }
```
Response: `{ "requestId": "…", "grant": { "id": "gr-01H…", "roleId": "r-editor", "subjectType": "group", "subjectId": "g-01H…", "path": "/projects/dfs", "itemId": "i-01H…" } }`.

Ví dụ `ListAudit` response entry: `{ "seq": 17, "time": "2026-11-20T09:15:02Z", "userId": "u-01H…", "action": "CreateUpload", "target": "/projects/x.txt", "allowed": false }`.

## Error

Response lỗi (type `0xFF`) thay thế response bình thường:

```json
{ "requestId": "…", "code": "Conflict", "message": "File changed since version 3",
  "details": { "currentVersion": 4 } }
```

Record: `ErrorResponse(RequestId, Code, Message, Details)` với `Code` kiểu `ErrorCode`, `Details` là object tuỳ chọn (map string → giá trị JSON). Các field danh sách trong record dùng `ValueList<T>` (một `List<T>` so sánh bằng giá trị, serialize như JSON array). Khi code nhận `Error` nó ném `DfsException` mang `Code`, `Message`, `Details`. Field `null` được bỏ khỏi JSON khi ghi (ví dụ `path`/`itemId` của system grant không xuất hiện) và đọc lại thành `null`.

`message` cho người đọc, client xử lý theo `code`. `details` tuỳ chọn (`Conflict` → `currentVersion`; `NotLeader` → `leader`).

## Data access token

```
base64url(payload) "." base64url(HMAC-SHA256(TOKEN_KEY, payload))
payload = { "u": userId, "c": chunkId, "op": "r" | "w", "exp": unixSeconds }
```

- `payload` là JSON UTF-8 (đúng các byte đã ký; verify trên chuỗi base64url nhận được, không serialize lại). Không padding `=`.
- Ký bởi Metadata Server, verify bởi Data Server bằng `TOKEN_KEY` chung. So sánh chữ ký bằng so sánh constant-time.
- Hết hạn sau 5 phút (`exp` = lúc cấp + 300 giây); Data Server chấp nhận lệch đồng hồ 2 phút (`now > exp + 120` mới là `TokenExpired`).
- Token chỉ cho đúng một `chunkId` và một `op`; `op` sai, `chunkId` không khớp hoặc chữ ký sai → `TokenInvalid`.

## Error codes

| Code | Ý nghĩa |
| --- | --- |
| `Unauthorized` | Chưa login, sai username/password, hoặc session hết hạn |
| `Forbidden` | Không có permission |
| `NotFound` | Path, upload, version hoặc chunk không tồn tại |
| `AlreadyExists` | Tên đã tồn tại trong thư mục (`CreateFolder`, `Rename`, `Move`, `CreateUpload` file mới, `CreateUser`…) |
| `Conflict` | Version đã thay đổi so với `baseVersion` |
| `BadRequest` | Request sai định dạng, path không hợp lệ, field thiếu, message gửi trước `Hello`, size quá giới hạn |
| `TokenInvalid`, `TokenExpired` | Token sai chữ ký / hết hạn |
| `Unavailable` | Data Server cần thiết đang `Dead` hoặc không đủ Data Server `Alive` |
| `VersionMismatch` | Sai protocol version |
| `Internal` | Lỗi server không lường trước; client có thể retry |
| `NotLeader` | (Stage 2) Node không phải leader; `details.leader` là địa chỉ leader |
