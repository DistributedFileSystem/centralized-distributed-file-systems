# Dfs.Fakes

Project này cung cấp dữ liệu mẫu để phát triển GUI và client SDK khi server thật chưa sẵn sàng.

- `FakeDfsClient` implement `IDfsClient`: đăng nhập bằng `demo` / `demo`, duyệt cây `/projects/dfs/readme.txt`, xem thông tin và version, xem một Data Server mẫu. Các thao tác khác trả `NotSupportedException` rõ ràng.
- `FakeDataServer` lưu chunk trong RAM, bắt đầu với nội dung của `readme.txt`. `Store` và `Read` dùng trực tiếp trong unit test; TCP hỗ trợ `Hello`, `WriteChunk`, `ReadChunk`, `DeleteChunk`.
- `FakeMetadataServer` trả chunk location trỏ tới `FakeDataServer` đang chạy. TCP hỗ trợ `Hello`, `Login`, `Logout`, `ListDir`, `Stat`, `ListVersions`, `CreateUpload`, `CommitUpload`, `AbortUpload`, `Open`, `ClusterStatus`.

Ví dụ chạy hai fake TCP server trong một process:

```csharp
await using var data = new FakeDataServer();
data.Start();
await using var metadata = new FakeMetadataServer(data);
metadata.Start();

Console.WriteLine($"METADATA_ENDPOINTS={metadata.Address}");
Console.WriteLine($"Data Server: {data.Address}");
// Giữ process chạy trong khi client sử dụng các endpoint trên.
```

Hai server chỉ lắng nghe `127.0.0.1` trên port trống do hệ điều hành chọn. Data Server dùng token HMAC riêng cho dữ liệu fake; không dùng `TOKEN_KEY` production. Dữ liệu mất khi process kết thúc. Đây là fake phục vụ phát triển, không thay thế server thật hoặc acceptance test độc lập.
