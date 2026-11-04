# Data model (draft)

> Bản nháp, sẽ chốt ở milestone M1.

## SQLite schema (Metadata Server)

```mermaid
erDiagram
  USERS ||--o{ GROUP_MEMBERS : in
  GROUPS ||--o{ GROUP_MEMBERS : has
  ROLES ||--o{ ROLE_PERMISSIONS : has
  ROLES ||--o{ GRANTS : used_by
  ITEMS ||--o{ ITEMS : parent_of
  ITEMS ||--o{ FILE_VERSIONS : has
  FILE_VERSIONS ||--o{ VERSION_CHUNKS : made_of
  CHUNKS ||--o{ VERSION_CHUNKS : used_in
  CHUNKS ||--o{ CHUNK_LOCATIONS : stored_on
  DATA_SERVERS ||--o{ CHUNK_LOCATIONS : holds
  USERS {
    text id PK
    text username
    text password_hash
    text salt
    int disabled
  }
  GROUPS {
    text id PK
    text name
  }
  GROUP_MEMBERS {
    text group_id FK
    text user_id FK
  }
  ROLES {
    text id PK
    text name
    text scope
  }
  ROLE_PERMISSIONS {
    text role_id FK
    text permission
  }
  GRANTS {
    text id PK
    text role_id FK
    text subject_type
    text subject_id
    text item_id
  }
  ITEMS {
    text id PK
    text parent_id FK
    text name
    text kind
    text owner_id
    int current_version
  }
  FILE_VERSIONS {
    text item_id FK
    int version
    int size
    text created_by
    text created_at
  }
  VERSION_CHUNKS {
    text item_id FK
    int version
    int idx
    text chunk_id FK
  }
  CHUNKS {
    text id PK
    int size
    text sha256
  }
  CHUNK_LOCATIONS {
    text chunk_id FK
    text server_id FK
  }
  DATA_SERVERS {
    text id PK
    text address
    text status
    int free_bytes
    text last_heartbeat
  }
```

Thêm các bảng: `SESSIONS` (token, user_id, expires_at), `UPLOADS` (upload đang dở), `AUDIT_LOG` (time, user, action, target, allowed). `GRANTS.item_id` = NULL nghĩa là system grant.

## Op log

- File append-only `oplog.jsonl`, mỗi dòng một command:
  ```json
  {"seq":1042,"type":"CommitUpload","at":"2026-11-20T09:15:02Z","data":{…}}
  ```
- Ghi + `fsync` trước, apply vào SQLite sau.
- Snapshot: copy file SQLite (SQLite backup API) kèm `lastSeq`. Khởi động = nạp snapshot + replay các dòng có `seq > lastSeq`.

## Chunk file (Data Server)

- Đường dẫn: `DATA_DIR/chunks/<2 ký tự đầu của chunkId>/<chunkId>.chunk`
- Local index: `DATA_DIR/index.db` (SQLite nhỏ) lưu `chunkId, size, sha256`, dùng cho `ChunkReport` sau khi restart.
- Ghi vào file tạm rồi rename, để không bao giờ có chunk ghi dở.
