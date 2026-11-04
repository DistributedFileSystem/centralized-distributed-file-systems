# Sơ đồ luồng màn hình tổng thể (Screen Flow) – Stage 1

Tài liệu này thể hiện kiến trúc chuyển hướng trang và luồng tương tác của người dùng trong ứng dụng Desktop Client (Avalonia UI).

---

## 1. Sơ đồ trạng thái điều hướng tổng quát

```mermaid
stateDiagram-v2
    [*] --> LoginState: Khởi động App (Chưa có Session)
    
    state LoginState {
        [*] --> EnteringCredentials
        EnteringCredentials --> Authenticating: Bấm "Đăng nhập"
        Authenticating --> EnteringCredentials: Lỗi (Sai pass / Bị khóa / Mất mạng)
    }

    LoginState --> AuthenticatedApp: Đăng nhập thành công (Nhận Session Token)
    
    state AuthenticatedApp {
        [*] --> FileBrowser
        
        state SidebarNavigation {
            FileBrowser: Trình duyệt tệp tin (File Explorer)
            TransfersQueue: Hàng đợi truyền tải (Upload / Download)
            AdminPanel: Quản trị hệ thống (Chỉ hiện khi có quyền Admin)
            ClusterStatus: Giám sát cụm Data Servers
        }

        FileBrowser --> TransfersQueue: Kéo thả file / Chọn Upload / Tải file
        FileBrowser --> VersionHistory: Chuột phải -> "Lịch sử phiên bản"
        FileBrowser --> ConflictDialog: Lưu đè file bị Conflict (baseVersion cũ)
        
        ConflictDialog --> FileBrowser: Chọn "Tải lại bản mới" (Reload)
        ConflictDialog --> TransfersQueue: Chọn "Lưu thành bản sao" (Save as copy)
        
        VersionHistory --> TransfersQueue: Tải một phiên bản quá khứ
        
        AdminPanel --> FileBrowser: Chuyển lại xem tệp tin
        ClusterStatus --> FileBrowser: Chuyển lại xem tệp tin
    }

    AuthenticatedApp --> LoginState: Bấm "Đăng xuất" / Session hết hạn (8 giờ)
```

---

## 2. Mô tả các luồng nghiệp vụ người dùng chính

### Luồng 1: Xác thực & Đăng nhập
1. Người dùng khởi động ứng dụng Desktop:
   - Hệ thống gửi gói tin bắt tay `Hello(protocolVersion=1)` đến Metadata Server.
   - Nếu sai phiên bản giao thức: Hiển thị lỗi `VersionMismatch` và đóng kết nối.
   - Nếu kết nối thành công: Hiển thị cửa sổ [Màn hình Đăng nhập (01-login.md)](01-login.md).
2. Người dùng nhập `Username` và `Password`, bấm **Đăng nhập**:
   - Client gọi `Login(username, password)`.
   - Nếu thất bại: Hiển thị thông báo màu đỏ (Mật khẩu không đúng, hoặc tài khoản đã bị Quản trị viên vô hiệu hóa).
   - Nếu thành công: Lưu trữ `sessionToken` vào bộ nhớ; kiểm tra quyền hạn của tài khoản. Nếu có quyền `Admin` hoặc `Auditor`, bật hiển thị tab **Quản trị (Admin Panel)** và **Trạng thái cụm (Cluster)** trên thanh menu bên. Chuyển sang [Màn hình Duyệt tệp (02-file-browser.md)](02-file-browser.md).

### Luồng 2: Duyệt thư mục & Quản lý tập tin
1. Cửa sổ hiển thị cây thư mục bên trái (`/`, `/projects`, `/users`...) và danh sách tệp tin bên phải.
2. Khi người dùng nhấp đúp vào một thư mục:
   - Client gọi `ListDir(path)` qua `IDfsClient`.
   - Cập nhật thanh đường dẫn (Breadcrumb) và vẽ lại bảng danh sách tập tin.
3. Thao tác trên tệp tin/thư mục qua thanh công cụ hoặc menu chuột phải:
   - *Tạo thư mục mới:* Mở popup nhập tên -> Gọi `CreateFolder(path)`.
   - *Đổi tên:* Mở popup sửa tên -> Gọi `Rename(oldPath, newPath)`.
   - *Di chuyển:* Kéo thả hoặc chọn mục tiêu -> Gọi `Move(oldPath, newParent)`.
   - *Xóa:* Mở popup xác nhận -> Gọi `DeleteItem(path)`.
   - *Phân quyền nhanh (Grant):* Mở dialog cấp quyền trên item được chọn.

### Luồng 3: Tải lên tập tin & Thanh tiến trình
1. Người dùng bấm **Tải lên (Upload)** hoặc kéo thả tệp tin từ máy tính vào thư mục hiện hành:
   - Client tính toán số lượng chunk (mỗi chunk 4 MB) và gọi `CreateUpload(path, size, baseVersion)`.
   - Nếu vi phạm quyền hạn: Metadata Server trả về `Forbidden` -> Client hiển thị thông báo lỗi và ghi nhật ký.
   - Nếu thành công: Metadata Server trả về danh sách Data Server được chỉ định và `dataToken` cho từng chunk.
2. Chuyển sang hoặc mở khay hiển thị [Quản lý truyền tải (03-transfers.md)](03-transfers.md):
   - Client đọc các khối nhị phân và gửi song song `WriteChunk` tới các Data Server.
   - Cập nhật liên tục % hoàn thành trên ProgressBar.
   - Khi hoàn tất mọi chunk: Client gọi `CommitUpload(uploadId, checksums)` để đưa tập tin vào hoạt động.
   - Cây thư mục tự động làm mới để hiển thị tập tin mới tạo.

### Luồng 4: Tải xuống tập tin (Download)
1. Người dùng chọn tệp tin và bấm **Tải xuống (Download)**:
   - Chọn thư mục lưu trữ trên máy tính cục bộ.
   - Client gọi `Open(path, version)` lấy danh sách chunk location và read token.
2. Hàng đợi [03-transfers.md](03-transfers.md) hiển thị tiến độ tải:
   - Client kết nối song song các Data Server tải từng khối bằng `ReadChunk`.
   - Kiểm tra mã băm SHA-256 từng chunk sau khi nhận xong.
   - Ghép các khối lại thành tệp tin hoàn chỉnh tại đích đến.

### Luồng 5: Phát hiện và xử lý Xung đột phiên bản (Optimistic Concurrency)
1. Khi hai người dùng cùng mở sửa một tệp tin (ví dụ cùng dựa trên `baseVersion = 3`):
   - Người dùng A commit trước -> Thành công, phiên bản file trên hệ thống tăng lên `4`.
   - Người dùng B commit sau với `baseVersion = 3` -> Metadata Server trả về mã lỗi `Conflict`.
2. Client của người dùng B tự động kích hoạt hiển thị [Hộp thoại Xử lý xung đột (05-conflict-dialog.md)](05-conflict-dialog.md):
   - Cung cấp tùy chọn 1: **Tải lại bản mới nhất (Reload)** -> Tải bản phiên bản 4 của Người dùng A về xem.
   - Cung cấp tùy chọn 2: **Lưu thành bản sao (Save as copy)** -> Tạo tệp mới tên `ten_file (copy 1).ext` và commit độc lập.

### Luồng 6: Quản trị hệ thống (Admin Workflow)
1. Người dùng có vai trò `Admin` bấm vào biểu tượng **Admin Panel** trên thanh Sidebar:
   - Chuyển sang màn hình [06-admin-panel.md](06-admin-panel.md).
   - Thực hiện tạo mới user, disable tài khoản, tạo group và gán quyền (Grants).

### Luồng 7: Giám sát cụm lưu trữ (Cluster Monitoring)
1. Bấm vào biểu tượng **Cluster Status** trên thanh Sidebar:
   - Chuyển sang màn hình [07-cluster-status.md](07-cluster-status.md).
   - Hiển thị danh sách 5 laptop Data Server, dung lượng, trạng thái Heartbeat.

---

## 3. Liên kết màn hình chi tiết
- [01. Màn hình Đăng nhập](01-login.md)
- [02. Trình duyệt tập tin & Cây thư mục](02-file-browser.md)
- [03. Quản lý truyền tải Tải lên / Tải xuống](03-transfers.md)
- [04. Hộp thoại Lịch sử phiên bản](04-version-history.md)
- [05. Hộp thoại Xử lý xung đột phiên bản](05-conflict-dialog.md)
- [06. Bảng điều khiển quản trị](06-admin-panel.md)
- [07. Màn hình Giám sát cụm lưu trữ](07-cluster-status.md)
