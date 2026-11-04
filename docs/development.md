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

1. **Contract first**: `docs/protocol.md`, `docs/rbac.md` và `Dfs.Shared` được review và merge **trước** khi làm feature. Thay đổi sau đó phải báo cả team.
2. **CI publish image**: mỗi lần merge vào `develop`, CI build và đẩy image `dfs-metadata` và `dfs-data` lên GitHub Container Registry.
3. **Chạy phần người khác bằng image, phần mình bằng IDE**: Compose profile khởi động mọi thứ trừ service của mình. Ví dụ người làm Data Server chạy `docker compose --profile metadata up` rồi chạy Data Server trong IDE với `METADATA_ENDPOINTS=localhost:7000`.
4. **Fake trong `Dfs.Fakes`**: fake Metadata Server (thư mục và chunk location cố định), fake Data Server (chunk trong RAM), fake client SDK cho UI.
5. **`Hello` mang protocol version**, nên build cũ báo lỗi rõ ràng thay vì lỗi lạ.

## Log

- Mọi node log ra console và file, mỗi dòng gắn `NODE_ID` và `requestId`, để theo dõi một lần upload qua nhiều laptop.

## CI

- Mỗi pull request: build, unit test, integration test với cluster Compose.
- Mỗi lần merge vào `develop`: publish server image.
- Linux runner là đủ; Avalonia build được trên Linux.
- Hiện tại (giai đoạn docs) CI chỉ kiểm tra Markdown link; workflow build .NET sẽ thêm ở milestone M1.
