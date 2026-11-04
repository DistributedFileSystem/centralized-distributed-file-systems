# Development environment

## Công cụ trên mọi laptop

| Công cụ | Lý do |
| --- | --- |
| .NET 10 SDK (LTS) | Cùng runtime cho mọi project |
| JetBrains Rider, hoặc VS Code + C# Dev Kit (Windows và Linux), hoặc Visual Studio (Windows) | IDE; Rider và VS Code preview được Avalonia |
| Avalonia templates (`dotnet new install Avalonia.Templates`) | Tạo và preview desktop app |
| Docker Desktop (Windows) / Docker Engine (Linux) | Chạy server ở cả hai mode |
| Git + GitHub CLI (`gh`) | Code, docs, issue, pull request |
| DB Browser for SQLite | Xem metadata khi debug |
| Wireshark | Xem frame khi TCP protocol lỗi |

## Phát triển độc lập

Mục tiêu: mỗi người chạy và test phần của mình mà không phải chờ người khác.

1. **Contract first**: Tân viết `docs/protocol.md`, `docs/rbac.md`, message types trong `Dfs.Shared` và các interface `IMetadataStore`, `IAuthorizer`, `IDfsClient`; merge **trước** khi làm feature. Chỉ Tân sửa contract sau đó, và phải báo Quân.
2. **Code theo interface**: mỗi người code phần của mình sau interface; phần chưa có thì dùng `Dfs.Fakes`.
3. **Chạy phần người khác bằng Compose, phần mình bằng IDE**: `docker compose up --build` với Compose profile khởi động mọi thứ trừ service của mình. Ví dụ người làm Data Server chạy `docker compose --profile metadata up --build` rồi chạy Data Server trong IDE với `METADATA_ENDPOINTS=localhost:7000`.
4. **Fake trong `Dfs.Fakes`**: fake Metadata Server (thư mục và chunk location cố định), fake Data Server (chunk trong RAM), fake client SDK cho UI.
5. **`Hello` mang protocol version**, nên build cũ báo lỗi rõ ràng thay vì lỗi lạ.

## Log

- Mọi node log ra console và file, mỗi dòng gắn `NODE_ID` và `requestId`, để theo dõi một lần upload qua nhiều laptop.

## CI

- Chỉ có CI, không có CD. Mỗi pull request: build, unit test, acceptance test của Quân trên cluster Compose, kiểm tra Markdown link.
- Linux runner là đủ; Avalonia build được trên Linux.
- Hiện tại (giai đoạn docs) CI chỉ kiểm tra Markdown link; workflow build .NET thêm ở M1-01, acceptance test thêm ở M2-02.
