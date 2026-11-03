# Flows

## Login

```mermaid
sequenceDiagram
  participant C as Client
  participant M as Metadata Server
  C->>M: Hello(protocolVersion)
  C->>M: Login(username, password)
  M->>M: Verify PBKDF2 hash, user không bị disable
  M-->>C: session token + roles
```

## Upload (file mới hoặc version mới)

```mermaid
sequenceDiagram
  participant C as Client
  participant M as Metadata Server
  participant D as Data Servers
  C->>M: CreateUpload(path, size, baseVersion)
  M->>M: RBAC check, chọn Data Server cho mỗi chunk, ký write token
  M-->>C: uploadId, chunkId, địa chỉ, token
  par song song cho từng chunk
    C->>D: WriteChunk(chunkId, bytes, token)
    D->>D: Verify token, ghi file tạm, tính SHA-256, rename
    D-->>C: OK + sha256
  end
  C->>M: CommitUpload(uploadId, checksums)
  M->>M: Kiểm tra version == baseVersion, ghi command vào op log
  M-->>C: OK, version mới hiển thị
```

Chunk của upload không bao giờ commit sẽ bị garbage collection xoá sau timeout.

## Download

```mermaid
sequenceDiagram
  participant C as Client
  participant M as Metadata Server
  participant D as Data Servers
  C->>M: Open(path, version)
  M->>M: RBAC check, ký read token
  M-->>C: danh sách chunk, địa chỉ, token
  par song song cho từng chunk
    C->>D: ReadChunk(chunkId, token)
    D-->>C: bytes
  end
  C->>C: Verify SHA-256, ghép file
```

## Save conflict

```mermaid
sequenceDiagram
  participant A as User A
  participant B as User B
  participant M as Metadata Server
  A->>M: CommitUpload(baseVersion=3)
  M-->>A: OK, version 4
  B->>M: CommitUpload(baseVersion=3)
  M-->>B: Conflict (current = 4)
  B->>B: Hỏi user: reload hoặc save as copy
```

## Permission denied

```mermaid
sequenceDiagram
  participant G as Guest
  participant M as Metadata Server
  G->>M: CreateUpload(/projects/x.txt)
  M->>M: Guests chỉ có Viewer trên /projects
  M->>M: Ghi audit log (denied)
  M-->>G: Forbidden
```

## Data Server tham gia cluster

```mermaid
sequenceDiagram
  participant D as Data Server
  participant M as Metadata Server
  D->>M: Register(nodeId, ADVERTISE_HOST:PORT, capacity)
  M-->>D: OK
  loop mỗi 3 giây
    D->>M: Heartbeat(freeBytes, chunkCount)
  end
  Note over M: Mất 3 heartbeat liên tiếp → Dead
```

Stage 2 sẽ bổ sung: leader failover, replication pipeline, re-replication.
