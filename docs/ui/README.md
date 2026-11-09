# Tài liệu thiết kế giao diện (GUI Design) – Stage 1

> Thuộc nhiệm vụ **[M1-08] GUI design nhanh** (Assignee: `@khanhnguyn2709`).  
> Tài liệu này chuẩn hóa ngôn ngữ thị giác, hệ thống thành phần (Design Tokens & Components) và mockups chi tiết cho toàn bộ các màn hình ứng dụng Desktop Client (Avalonia UI) trong giai đoạn MVP (Stage 1).

---

## 1. Mục tiêu và Nguyên tắc thiết kế

1. **Đa nền tảng nhất quán (Cross-platform):** Giao diện chạy mượt mà, không lỗi font hoặc vỡ layout trên cả **Windows 10/11** và **Linux (Ubuntu/Debian)**.
2. **Trực quan & Hiện đại:** Bố cục rõ ràng, thao tác chuột/phím quen thuộc như các trình quản lý tệp tin phổ biến (File Explorer, OneDrive, Google Drive).
3. **Phản hồi thời gian thực:** Mọi thao tác I/O mạng phân tán (kết nối Metadata Server, truyền chunk tới Data Server) đều có thanh trạng thái tiến độ rõ ràng, không làm đơ giao diện (non-blocking UI).
4. **Bảo mật & Phân quyền minh bạch:** Giao diện tự động thích ứng theo vai trò người dùng (ẩn/khóa các tính năng quản trị nếu không có quyền), cảnh báo lỗi quyền hạn (`Forbidden`) dễ hiểu.

---

## 2. Hệ thống thiết kế (Design Tokens)

Ứng dụng Desktop sử dụng phong cách giao diện bán phẳng (Semi-flat) hiện đại với hai chế độ màu: **Light Theme** (mặc định) và hỗ trợ **Dark Theme**.

### 2.1. Bảng màu chủ đạo (Color Palette)

| Mã màu | HEX (Light) | HEX (Dark) | Mục đích sử dụng |
|---|---|---|---|
| **Primary (Brand)** | `#0066CC` | `#3399FF` | Màu nhấn chính, nút bấm hành động (Primary Button), tiêu đề đang chọn |
| **Primary Hover** | `#0052A3` | `#66B2FF` | Trạng thái hover của nút bấm chính |
| **Secondary** | `#5C6B73` | `#8D99AE` | Các nút phụ, đường viền phân tách, văn bản thứ cấp |
| **Background** | `#F8F9FA` | `#1A1D20` | Màu nền tổng thể của cửa sổ ứng dụng |
| **Surface (Card)** | `#FFFFFF` | `#24282C` | Nền các bảng danh sách, thẻ nội dung, hộp thoại dialog |
| **Text Primary** | `#212529` | `#E9ECEF` | Màu văn bản chính, độ tương phản cao |
| **Text Secondary** | `#6C757D` | `#ADB5BD` | Nhãn phụ, thời gian, kích thước, breadcrumb |
| **Border / Divider** | `#DEE2E6` | `#343A40` | Đường kẻ phân chia bảng, đường viền ô nhập liệu |
| **Success** | `#28A745` | `#2EC4B6` | Trạng thái hoàn thành truyền tải, node Data Server Healthy |
| **Warning** | `#FFC107` | `#FFD166` | Cảnh báo xung đột phiên bản, sắp hết hạn session |
| **Danger / Error** | `#DC3545` | `#EF476F` | Báo lỗi đăng nhập, `Forbidden`, node Data Server Dead |

### 2.2. Quy chuẩn Typography

- **Phông chữ mặc định:** `Segoe UI` (trên Windows) và `Noto Sans`, `Ubuntu`, `DejaVu Sans` (trên Linux). Khai báo chuỗi font dự phòng trong Avalonia:
  `FontFamily="Segoe UI, Noto Sans, Ubuntu, -apple-system, sans-serif"`
- **Thang kích thước chữ:**
  - **Heading 1 (Window Title / Section Title):** 18–20 pt (SemiBold)
  - **Heading 2 (Card Title / Dialog Header):** 15–16 pt (SemiBold)
  - **Body (Regular text, DataGrid cell):** 13–14 pt (Regular)
  - **Small / Metadata (Timestamp, size, hints):** 11–12 pt (Regular)
  - **Code / Monospace (Path, SHA-256, Node ID):** 12 pt (`Consolas, Cascadia Mono, monospace`)

### 2.3. Quy chuẩn Thành phần giao diện (UI Components)

- **Button:**
  - *Primary Button:* Nền xanh `#0066CC`, chữ trắng, bo góc 4px, chiều cao 32px.
  - *Secondary / Outline Button:* Nền trong suốt, viền xám `#DEE2E6`, chữ `#212529`.
  - *Danger Button:* Nền đỏ `#DC3545`, chữ trắng (dùng cho hành động Xóa, Khóa user).
- **TextBox:** Chiều cao 32px, viền 1px bo góc 4px. Khi focus: viền chuyển xanh `#0066CC`.
- **DataGrid:** Dòng xen kẽ (zebra striping: trắng và xám cực nhạt `#F8F9FA`), chiều cao dòng 36px, hỗ trợ sắp xếp khi bấm vào tiêu đề cột.
- **TreeView:** Thụt lề 16px mỗi cấp, icon thư mục đóng/mở trực quan.
- **ProgressBar:** Chiều cao 6–8px, bo góc 3px, nền xám nhạt, thanh chạy màu xanh `#0066CC` (khi lỗi chuyển đỏ).
- **Dialog:** Modal window hiển thị chính giữa cửa sổ cha, nền mờ đen trong suốt (Opacity 0.4).

---

## 3. Danh mục các màn hình thiết kế (Stage 1)

Chi tiết cấu trúc, sơ đồ luồng và wireframe cụ thể của từng màn hình được chia thành các tài liệu độc lập:

1. [**Sơ đồ luồng màn hình tổng thể (Screen Flow)**](screen-flow.md)
2. [**01. Màn hình Đăng nhập (Login View)**](01-login.md)
3. [**02. Trình duyệt tập tin & Cây thư mục (File Explorer View)**](02-file-browser.md)
4. [**03. Quản lý truyền tải Tải lên / Tải xuống (Transfer Manager View)**](03-transfers.md)
5. [**04. Hộp thoại Lịch sử phiên bản (Version History Dialog)**](04-version-history.md)
6. [**05. Hộp thoại Xử lý xung đột phiên bản (Conflict Resolution Dialog)**](05-conflict-dialog.md)
7. [**06. Bảng điều khiển quản trị (Admin Panel View: Users, Groups, Grants)**](06-admin-panel.md)
8. [**07. Màn hình Giám sát cụm lưu trữ (Cluster Status View)**](07-cluster-status.md)

---

## 4. Tài liệu tham chiếu
- Yêu cầu chức năng: [docs/requirements.md](../requirements.md)
- Kiến trúc tổng thể: [docs/architecture.md](../architecture.md)
- Đặc tả giao thức và mã lỗi: [docs/protocol.md](../protocol.md)
- Cơ chế xác thực và phân quyền: [docs/rbac.md](../rbac.md)
- Kế hoạch triển khai: [docs/plan.md](../plan.md)
- Kế hoạch kiểm thử: [docs/test-plan.md](../test-plan.md)
