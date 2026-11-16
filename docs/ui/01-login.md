# Màn hình Đăng nhập (Login View)

> Thuộc giai đoạn **Stage 1 (MVP)** – Yêu cầu: **FR-01**, liên kết với nhiệm vụ **[M2-11]**.

---

## 1. Mục tiêu màn hình
Cung cấp giao diện xác thực người dùng an toàn, đơn giản và trực quan trước khi truy cập vào hệ thống tập tin phân tán. Hỗ trợ bắt lỗi đăng nhập, thông báo tài khoản bị khóa hoặc sự cố mạng LAN.

---

## 2. Wireframe trực quan (ASCII Mockup)

```
+-----------------------------------------------------------------------+
|  Centralized Distributed File System (DFS) - v1.0.0          [-] [o] [x]|
+-----------------------------------------------------------------------+
|                                                                       |
|                                                                       |
|                      +-------------------------+                      |
|                      |   [Logo / Icon DFS]     |                      |
|                      |  ĐĂNG NHẬP HỆ THỐNG     |                      |
|                      +-------------------------+                      |
|                                                                       |
|        +-----------------------------------------------------+        |
|        | [!] Sai tên đăng nhập hoặc mật khẩu không đúng!     |  <-- Error Banner (chỉ hiện khi lỗi)
|        +-----------------------------------------------------+        |
|                                                                       |
|        Tên đăng nhập:                                                 |
|        +-----------------------------------------------------+        |
|        | an.nguyen                                           |        |
|        +-----------------------------------------------------+        |
|                                                                       |
|        Mật khẩu:                                                      |
|        +-----------------------------------------------------+        |
|        | ****************                                    | [Show] |
|        +-----------------------------------------------------+        |
|                                                                       |
|        Giao diện khởi động:                                           |
|        +-----------------------------------------------------+        |
|        | [ 🔄 Tự động (Theo quyền tài khoản)               v] |        |
|        +-----------------------------------------------------+        |
|                                                                       |
|        Máy chủ Metadata (IP:Port):                                    |
|        +-----------------------------------------------------+        |
|        | 127.0.0.1:7000                                      |        |
|        +-----------------------------------------------------+        |
|                                                                       |
|        [x] Ghi nhớ tên đăng nhập trên máy này                         |
|                                                                       |
|        +-----------------------------------------------------+        |
|        |                  [ ĐĂNG NHẬP ]                      |        |
|        +-----------------------------------------------------+        |
|                                                                       |
|                      Đang kết nối tới máy chủ... (nếu loading)        |
|                      [==============             ]                    |
|                                                                       |
|                                                                       |
+-----------------------------------------------------------------------+
| Sẵn sàng kết nối | Giao thức: TCP v1 | Thiết kế: Avalonia UI .NET 10 |
+-----------------------------------------------------------------------+
```

---

## 3. Các thành phần giao diện (UI Elements)

| Tên thành phần | Loại điều khiển | Thuộc tính / Giá trị mặc định | Mô tả chức năng |
|---|---|---|---|
| `LogoHeader` | `Image` + `TextBlock` | Text: "ĐĂNG NHẬP HỆ THỐNG", 18pt Bold | Nhận diện thương hiệu đồ án |
| `ErrorBanner` | `Border` (Nền đỏ `#F8D7DA`) | Ban đầu `IsVisible=False` | Hiển thị thông báo khi có lỗi phát sinh |
| `TxtUsername` | `TextBox` | Placeholder: "Nhập username..." | Tên tài khoản người dùng |
| `TxtPassword` | `TextBox` (`PasswordChar='•'`) | Nút toggle `👁️ Hiện / 🙈 Ẩn` | Mật khẩu xác thực (băm PBKDF2 ở server) |
| `CboTargetMode` | `ComboBox` | Mặc định: "🔄 Tự động (Theo quyền tài khoản)" | Tùy chọn giao diện đích: Tự động, User Client, Admin Panel |
| `TxtEndpoint` | `TextBox` | Mặc định: `127.0.0.1:7000` (LAN: `192.168.10.11:7000`) | Địa chỉ IP và cổng của Metadata Server |
| `ChkRemember` | `CheckBox` | Mặc định: `True` | Lưu username và endpoint vào `login_state.json` trên máy |
| `BtnLogin` | `Button` (Primary Style) | Text: "ĐĂNG NHẬP", phím tắt `Enter` | Gửi yêu cầu xác thực tới máy chủ Metadata |
| `LoadingIndicator` | `ProgressBar` (Indeterminate) | Ban đầu `IsVisible=False` | Hiển thị hiệu ứng khi gửi yêu cầu `Login` |

> **Cơ chế lưu trạng thái Remember Me:**  
> Khi tùy chọn `ChkRemember` được bật, ứng dụng tự động lưu trữ cấu hình vào file `login_state.json` (đường dẫn Windows: `%LOCALAPPDATA%\CentralizedDfs\login_state.json`, Linux: `~/.local/share/CentralizedDfs/login_state.json`). Lần mở tiếp theo, ứng dụng tự nạp lại `Username` và `Endpoint`, giúp tiết kiệm thời gian thao tác. Không lưu mật khẩu thô để đảm bảo an toàn.

---

## 4. Các trạng thái giao diện (UI States)

1. **Trạng thái Mặc định (Default State):**
   - Các ô nhập liệu trống hoặc điền sẵn username đã lưu từ `login_state.json`.
   - Nút `Đăng nhập` sẵn sàng kích hoạt.
   - Thanh lỗi và tiến trình đều ẩn. Giao diện sạch sẽ, không chứa các nút mock test thừa.

2. **Trạng thái Đang xác thực (Authenticating State):**
   - Vô hiệu hóa ô nhập `Username`, `Password`, `Endpoint`, `CboTargetMode` và nút `Đăng nhập` (`IsEnabled=False`).
   - Hiển thị thanh tiến trình `ProgressBar` đang chạy.
   - Con trỏ chuột chuyển sang dạng `Wait/Busy`.

3. **Trạng thái Lỗi xác thực (Authentication Failure State):**
   - Kích hoạt thanh `ErrorBanner` màu đỏ với nội dung cụ thể theo `ErrorCode`:
     - *Sai thông tin hoặc tài khoản bị khóa (`ErrorCode.Unauthorized` / `Forbidden`):* "Tên đăng nhập hoặc mật khẩu không chính xác." hoặc "Tài khoản của bạn đã bị Quản trị viên vô hiệu hóa."
     - *Mất kết nối (`ErrorCode.Unavailable`):* "Máy chủ Metadata tại 127.0.0.1:7000 hiện không khả dụng. Vui lòng kiểm tra lại dịch vụ."
     - *Lệch phiên bản giao thức (`ErrorCode.VersionMismatch`):* "Phiên bản ứng dụng không khớp với máy chủ (Protocol Version Mismatch)."
   - Tự động focus lại vào ô nhập `Mật khẩu` để người dùng tiện gõ lại.

4. **Trạng thái Thành công (Success State):**
   - Xác thực thành công: Nhận về `LoginResponse` chứa `SessionToken`, danh sách `SystemRoles` và `Permissions`.
   - Tùy theo `CboTargetMode` và quyền hạn:
     - `Admin Panel`: Chuyển tới giao diện Quản trị viên (`AdminShellView`).
     - `User Client` hoặc `Tự động` (với tài khoản thường): Chuyển tới giao diện Trình duyệt tệp tin (`UserShellView` / `MainWindow`).

---

## 5. Quy tắc xác thực dữ liệu đầu vào (Input Validation)
- `Username`: Không được để trống, độ dài từ 3 đến 50 ký tự, không chứa ký tự đặc biệt ngoài dấu chấm (`.`), gạch dưới (`_`) và gạch ngang (`-`).
- `Password`: Không được để trống.
- `Endpoint`: Phải đúng định dạng `IP:Port` (ví dụ `192.168.1.100:7000` hoặc `localhost:7000`).

---

## 6. Liên kết tài liệu
- Sơ đồ luồng: [screen-flow.md](screen-flow.md)
- Trình duyệt tệp tin: [02-file-browser.md](02-file-browser.md)
- Cơ chế xác thực backend: [docs/rbac.md](../rbac.md)
