# Trình duyệt tập tin & Cây thư mục (File Explorer View)

> Thuộc giai đoạn **Stage 1 (MVP)** – Yêu cầu: **FR-02, FR-03**, liên kết với nhiệm vụ **[M2-11]**.

---

## 1. Mục tiêu màn hình
Đây là không gian làm việc chính (Main Workspace) của ứng dụng Desktop Client. Cho phép người dùng duyệt phân cấp thư mục, xem thông tin chi tiết các tệp tin, thực hiện các thao tác quản lý dữ liệu (Tạo, Đổi tên, Xóa, Di chuyển) và truy xuất nhanh các tính năng tải tệp, xem lịch sử phiên bản.

---

## 2. Wireframe trực quan (ASCII Mockup)

```
+---------------------------------------------------------------------------------------------------------+
| Centralized Distributed File System (DFS) - [an.nguyen]                                       [-] [o] [x]|
+---------------------------------------------------------------------------------------------------------+
| [Logo DFS]  | Tìm kiếm tệp tin... [ Q ]              | Máy chủ: 192.168.10.11:7000 | [an.nguyen v] [Thoát]|
+---------------------------------------------------------------------------------------------------------+
| [^] Lên cấp | [+] Thư mục mới | [^] Tải lên | [v] Tải xuống | [x] Xóa | [R] Đổi tên | [O] Làm mới (F5)   |
+---------------------------------------------------------------------------------------------------------+
| Đường dẫn: / > projects > dfs >                                                                         |
+---------------------+-----------------------------------------------------------------------------------+
| ĐIỀU HƯỚNG          | Tên                         | Kích thước | Phiên bản | Chủ sở hữu | Ngày sửa đổi      |
|                     +-----------------------------+------------+-----------+------------+-------------------+
| [*] Tệp tin của tôi | [D] source_code/            | --         | --        | an.nguyen  | 04/11/2026 14:20  |
| [ ] Hàng đợi truyền | [D] documentation/          | --         | --        | tan.phan   | 04/11/2026 15:10  |
| [ ] Bảng quản trị   | [F] report_final.pdf        | 14.2 MB    | v3        | an.nguyen  | 04/11/2026 16:05  |
| [ ] Trạng thái cụm  | [F] architecture.png        | 2.8 MB     | v1        | nhat.le    | 03/11/2026 09:30  |
|                     | [F] data_test_50mb.bin      | 50.0 MB    | v1        | cuong.khong| 04/11/2026 11:45  |
| CÂY THƯ MỤC         |                             |            |           |            |                   |
|                     |                             |            |           |            |                   |
| v [Root] (/)        |                             |            |           |            |                   |
|   v projects/       |                             |            |           |            |                   |
|     > dfs/  <-- sel |                             |            |           |            |                   |
|     > ai_lab/       |                             |            |           |            |                   |
|   > shared/         |                             |            |           |            |                   |
|   > users/          |                             |            |           |            |                   |
|     > an.nguyen/    |                             |            |           |            |                   |
|                     |                             |            |           |            |                   |
+---------------------+-----------------------------------------------------------------------------------+
| 5 mục trong thư mục | Đã chọn: report_final.pdf (14.2 MB, v3, 4 chunks) | Metadata Server: Healthy      |
+---------------------------------------------------------------------------------------------------------+
```

---

## 3. Menu ngữ cảnh khi nhấp chuột phải (Context Menu)

Khi người dùng chọn một mục trong bảng danh sách và nhấp chuột phải:

```
+-----------------------------------+
|  [v] Tải xuống (Download)         |  --> Mở File Save Dialog chọn nơi lưu
|  [H] Lịch sử phiên bản...         |  --> Mở dialog 04-version-history.md
|  -------------------------------- |
|  [R] Đổi tên (Rename)     [F2]    |  --> Mở hộp thoại nhập tên mới
|  [M] Di chuyển tới...     [Ctrl+X]|  --> Mở cây thư mục chọn đích đến
|  [x] Xóa (Delete)         [Del]   |  --> Mở hộp thoại xác nhận xóa
|  -------------------------------- |
|  [P] Phân quyền mục này... (Grant)|  --> Mở bảng cấp quyền (nếu là Owner)
|  [I] Thuộc tính chi tiết...       |  --> Hiển thị ID, SHA-256 các chunk
+-----------------------------------+
```

---

## 4. Các thành phần giao diện chính

1. **Thanh Header (App Header):**
   - Logo thương hiệu và tên hệ sinh thái DFS.
   - Hộp tìm kiếm nhanh (`SearchBox`): Lọc tên file theo từ khóa trong thư mục hiện tại.
   - Thẻ thông tin người dùng (`UserBadge`): Hiển thị avatar ký tự, username, vai trò (`Admin` / `Editor` / `Viewer`) và nút Đăng xuất (`Logout`).

2. **Thanh tác vụ (Action Toolbar):**
   - *Nút "Lên một cấp" (Up):* Điều hướng về thư mục cha (`parent_id`).
   - *Nút "Thư mục mới" (New Folder):* Tạo thư mục con trong thư mục đang xem.
   - *Nút "Tải lên" (Upload):* Mở hộp thoại chọn file trên máy tính hoặc cho phép kéo thả trực tiếp file vào vùng bảng.
   - *Nút "Tải xuống" (Download):* Tải tệp tin được chọn về máy.
   - *Nút "Xóa" (Delete):* Xóa vĩnh viễn tệp/thư mục (yêu cầu quyền `item.delete`).
   - *Nút "Làm mới" (Refresh - phím tắt F5):* Gọi lại `ListDir` để cập nhật trạng thái mới nhất từ server.

3. **Thanh đường dẫn (Breadcrumb Bar):**
   - Hiển thị cấu trúc cây dạng phân cấp: `/ > projects > dfs >`.
   - Mỗi nút trong đường dẫn có thể bấm được để nhảy nhanh về thư mục cấp trên.

4. **Ngăn điều hướng bên trái (Left Sidebar):**
   - Phía trên: Menu chuyển đổi các màn hình chức năng chính (Tệp tin, Truyền tải, Quản trị Admin, Cụm server).
   - Phía dưới: `TreeView` phân cấp hiển thị toàn bộ cây thư mục hệ thống. Khi nhấp chọn nhánh cây, bảng bên phải lập tức làm mới nội dung tương ứng.

5. **Bảng dữ liệu tệp tin (File DataGrid):**
   - Hỗ trợ chọn một dòng hoặc nhiều dòng (Multi-selection bằng Shift/Ctrl).
   - Biểu tượng tệp tin phân biệt theo phần mở rộng (`.pdf`, `.png`, `.zip`, `.txt`...).
   - Hiển thị kích thước tệp định dạng chuẩn người dùng (B, KB, MB, GB).
   - Hiển thị số hiệu phiên bản hiện hành (`v1`, `v2`, `v3`...).

---

## 5. Xử lý tương tác & Ràng buộc bảo mật (RBAC Integration)

- **Khi người dùng chỉ có quyền `Viewer` (như tài khoản thuộc group `Guests`):**
  - Các nút **"Tải lên"**, **"Thư mục mới"**, **"Xóa"**, **"Đổi tên"** trên thanh Toolbar và trong Menu chuột phải tự động chuyển sang trạng thái xám mờ (`IsEnabled=False`).
  - Nếu cố tình thao tác bằng phím tắt: Client chặn ngay lập tức hoặc hiển thị thông báo lỗi `Forbidden: Bạn không có quyền ghi trên thư mục này`.
- **Kéo thả tệp tin (Drag and Drop):**
  - Kéo file từ Desktop máy tính thả vào khoảng trống của DataGrid -> Tự động kích hoạt luồng tải lên vào thư mục hiện hành.

---

## 6. Liên kết tài liệu
- Sơ đồ luồng: [screen-flow.md](screen-flow.md)
- Quản lý truyền tải: [03-transfers.md](03-transfers.md)
- Lịch sử phiên bản: [04-version-history.md](04-version-history.md)
- Phân quyền chi tiết: [docs/rbac.md](../rbac.md)
