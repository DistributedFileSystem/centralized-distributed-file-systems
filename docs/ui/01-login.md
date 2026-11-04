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
|        Máy chủ Metadata:                                              |
|        +-----------------------------------------------------+        |
|        | 192.168.10.11:7000                                  |        |
|        +-----------------------------------------------------+        |
|                                                                       |
|        [ ] Ghi nhớ tên đăng nhập trên máy này                         |
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
| `TxtPassword` | `TextBox` (`PasswordChar='*'`) | Nút `RevealPassword` xem mật khẩu | Mật khẩu xác thực (băm PBKDF2 ở server) |
| `TxtEndpoint` | `TextBox` | Mặc định đọc từ config/env: `192.168.10.11:7000` | Địa chỉ IP LAN và port của Metadata Server |
| `ChkRemember` | `CheckBox` | Mặc định: `False` | Lưu username vào cấu hình cục bộ máy |
| `BtnLogin` | `Button` (Primary Style) | Text: "ĐĂNG NHẬP", phím tắt `Enter` | Gửi yêu cầu xác thực |
| `LoadingIndicator` | `ProgressBar` (Indeterminate) | Ban đầu `IsVisible=False` | Hiển thị hiệu ứng xoay khi đang xử lý |

---

## 4. Các trạng thái giao diện (UI States)

1. **Trạng thái Mặc định (Default State):**
   - Các ô nhập liệu trống hoặc điền sẵn username đã nhớ.
   - Nút `Đăng nhập` sẵn sàng kích hoạt.
   - Thanh lỗi và tiến trình đều ẩn.

2. **Trạng thái Đang xác thực (Authenticating State):**
   - Vô hiệu hóa ô nhập `Username`, `Password`, `Endpoint` và nút `Đăng nhập` (`IsEnabled=False`).
   - Hiển thị thanh tiến trình `ProgressBar` đang chạy.
   - Con trỏ chuột chuyển sang dạng `Wait/Busy`.

3. **Trạng thái Lỗi xác thực (Authentication Failure State):**
   - Kích hoạt thanh `ErrorBanner` màu đỏ với nội dung cụ thể:
     - *Sai thông tin:* "Tên đăng nhập hoặc mật khẩu không chính xác."
     - *Tài khoản bị khóa:* "Tài khoản của bạn đã bị Quản trị viên vô hiệu hóa."
     - *Mất kết nối:* "Không thể kết nối tới Metadata Server tại địa chỉ 192.168.10.11:7000. Vui lòng kiểm tra lại mạng LAN hoặc tường lửa."
     - *Lệch phiên bản giao thức:* "Phiên bản ứng dụng không khớp với máy chủ (Protocol Version Mismatch)."
   - Tự động focus lại vào ô nhập `Mật khẩu` và bôi đen văn bản để người dùng tiện gõ lại.

4. **Trạng thái Thành công (Success State):**
   - Ẩn hoàn toàn cửa sổ Login, chuyển quyền điều khiển và mở cửa sổ chính `MainWindow` với phiên làm việc đã xác lập.

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
