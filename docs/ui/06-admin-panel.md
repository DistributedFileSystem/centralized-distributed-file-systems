# Bảng điều khiển quản trị (Admin Panel View)

> Thuộc giai đoạn **Stage 1 (MVP)** – Yêu cầu: **FR-08, FR-09**, quyết định kiến trúc: **ADR-0004**, liên kết với nhiệm vụ **[M2-13]**.

---

## 1. Mục tiêu màn hình
Cung cấp phân hệ quản trị tập trung dành riêng cho người dùng có vai trò `Admin`. Hỗ trợ quản lý vòng đời tài khoản người dùng, cấu hình nhóm thành viên và thiết lập chính sách phân quyền truy cập theo cấu trúc phân cấp thư mục (RBAC với tính năng kế thừa quyền từ cha xuống con).

---

## 2. Wireframe trực quan (ASCII Mockup)

```
+---------------------------------------------------------------------------------------------------------+
| Centralized Distributed File System (DFS) - Bảng điều khiển quản trị                          [-] [o] [x]|
+---------------------------------------------------------------------------------------------------------+
| [Logo DFS]  | BẢNG ĐIỀU KHIỂN QUẢN TRỊ HỆ THỐNG      | Quyền hạn: System Admin      | [an.nguyen v] [Thoát]|
+---------------------------------------------------------------------------------------------------------+
| TAB ĐIỀU HƯỚNG:  [ (1) NGƯỜI DÙNG (USERS) ]   [ (2) NHÓM (GROUPS) ]   [ (3) PHÂN QUYỀN (GRANTS) ]        |
+---------------------------------------------------------------------------------------------------------+
|                                                                                                         |
|  === TAB 1: QUẢN LÝ NGƯỜI DÙNG (USERS) ===                                                              |
|                                                                                                         |
|  [+] Tạo người dùng mới  |  [T] Đặt lại mật khẩu  |  [!] Khóa tài khoản  | Tìm kiếm: [               ]      |
|  +---------------------------------------------------------------------------------------------------+  |
|  | Username      | Trạng thái    | Nhóm thành viên       | Ngày khởi tạo    | Thao tác                |  |
|  +---------------+---------------+-----------------------+------------------+-------------------------+  |
|  | admin         | [v] Kích hoạt | Admin, Team A         | 03/11/2026 19:00 | [Đổi pass]              |  |
|  | an.nguyen     | [v] Kích hoạt | Team A                | 04/11/2026 08:30 | [Đổi pass] [Khóa]       |  |
|  | tan.phan      | [v] Kích hoạt | Team A                | 04/11/2026 08:30 | [Đổi pass] [Khóa]       |  |
|  | guest_user    | [x] ĐÃ KHÓA   | Guests                | 04/11/2026 09:15 | [Đổi pass] [Mở khóa]    |  |
|  | temp_tester   | [v] Kích hoạt | Guests                | 04/11/2026 10:00 | [Đổi pass] [Khóa]       |  |
|  +---------------------------------------------------------------------------------------------------+  |
|                                                                                                         |
|---------------------------------------------------------------------------------------------------------|
|                                                                                                         |
|  === TAB 2: QUẢN LÝ NHÓM (GROUPS) ===                                                                   |
|                                                                                                         |
|  DANH SÁCH NHÓM:                             THÀNH VIÊN TRONG NHÓM 'Team A':                            |
|  [+] Tạo nhóm mới                            [+] Thêm thành viên vào nhóm                               |
|  +---------------------------+               +-------------------------------------------------------+  |
|  | Tên nhóm         | Số user|               | Username      | Họ và tên            | Hành động      |  |
|  +------------------+--------+               +---------------+----------------------+----------------+  |
|  | Team A (Đang xem)| 3      |               | an.nguyen     | Nguyễn Văn An        | [Xóa khỏi nhóm]|  |
|  | Guests           | 2      |               | tan.phan      | Phan Nhật Tân        | [Xóa khỏi nhóm]|  |
|  | Auditors         | 1      |               | cuong.khong   | Khổng Phi Cường      | [Xóa khỏi nhóm]|  |
|  +---------------------------+               +-------------------------------------------------------+  |
|                                                                                                         |
|---------------------------------------------------------------------------------------------------------|
|                                                                                                         |
|  === TAB 3: PHÂN QUYỀN TRUY CẬP (GRANTS) ===                                                            |
|                                                                                                         |
|  1. CHỌN MỤC TIÊU CẤP QUYỀN:                 2. DANH SÁCH QUYỀN ĐANG ÁP DỤNG TRÊN '/projects':          |
|  Cây thư mục:                                +-------------------------------------------------------+  |
|  v [Root] (/)                                | Đối tượng     | Loại   | Vai trò | Nguồn gốc | Hành động |  |
|    v projects/  <-- [Đang chọn]              +---------------+--------+---------+-----------+-----------+  |
|      > dfs/                                  | an.nguyen     | User   | Owner   | Trực tiếp | [Xóa]     |  |
|      > ai_lab/                               | Team A        | Group  | Editor  | Trực tiếp | [Xóa]     |  |
|    > shared/                                 | Guests        | Group  | Viewer  | Trực tiếp | [Xóa]     |  |
|    > users/                                  +-------------------------------------------------------+  |
|                                                                                                         |
|  3. CẤP QUYỀN MỚI TRÊN MỤC '/projects':                                                                 |
|  Gán cho: (o) User  ( ) Group      Tên: [ tan.phan          v]    Vai trò: [ Editor               v]    |
|  [+] XÁC NHẬN CẤP QUYỀN                                                                                 |
|                                                                                                         |
|  [i] Ghi chú: Thư mục con '/projects/dfs' sẽ tự động kế thừa toàn bộ quyền Editor của Team A ở trên.    |
+---------------------------------------------------------------------------------------------------------+
```

---

## 3. Chi tiết chức năng từng Tab quản trị

### Tab 1: Quản lý Người dùng (Users Management)
- **Tạo người dùng mới:** Mở popup nhập `Username`, `Mật khẩu ban đầu` và chọn `Nhóm gia nhập`. Mật khẩu được mã hóa PBKDF2-SHA256 kèm salt phía máy chủ.
- **Khóa tài khoản (Disable):** Khi một tài khoản bị khóa, trường `disabled=1` trong SQLite. Nếu tài khoản đó đang đăng nhập hoặc cố tình đăng nhập lại, server lập tức từ chối với mã lỗi `Unauthorized / AccountDisabled`.
- **Đặt lại mật khẩu (Reset Password):** Quản trị viên nhập mật khẩu mới thay cho thành viên khi bị quên.

### Tab 2: Quản lý Nhóm (Groups Management)
- Tạo các nhóm làm việc (ví dụ `Team A`, `Guests`).
- Phân chia thành viên linh hoạt: Một người dùng có thể thuộc nhiều nhóm khác nhau (quan hệ nhiều-nhiều `GROUP_MEMBERS`).
- Khi cấp quyền cho nhóm, toàn bộ thành viên trong nhóm lập tức thừa hưởng quyền tương ứng.

### Tab 3: Phân quyền truy cập (Grants & Inheritance)
- **Mô hình phân quyền:** Kết hợp giữa System Grants (quyền toàn cục) và Item Grants (quyền gắn trên thư mục/tập tin).
- **Nguyên lý kế thừa (Inheritance):**
  - Quyền cấp trên `/projects` tự động áp dụng xuống tất cả các thư mục con như `/projects/dfs/`, `/projects/dfs/docs/`...
  - Giao diện thể hiện rõ cột **Nguồn gốc** (`Trực tiếp` hoặc `Kế thừa từ <đường dẫn cha>`). Các quyền kế thừa sẽ ở trạng thái mờ (chỉ đọc) và chỉ có thể sửa/xóa tại đúng thư mục cha đã khai báo nó.
- **Vai trò tích hợp sẵn (Built-in Roles):**
  - `Admin`: Toàn quyền hệ thống.
  - `Auditor`: Chỉ xem trạng thái cụm và nhật ký audit log.
  - `Owner`: Toàn quyền trên thư mục/file do mình tạo ra (bao gồm quyền chia sẻ và cấp quyền cho người khác).
  - `Editor`: Xem, đọc, tải lên phiên bản mới, tạo thư mục con, đổi tên, xóa.
  - `Viewer`: Chỉ duyệt danh sách và đọc/tải nội dung file.

---

## 4. Kịch bản kiểm thử tương ứng (Demo Script Stage 1)
Màn hình này phục vụ trực tiếp cho **Bước 2** trong kịch bản Demo nghiệm thu Stage 1 (`docs/test-plan.md`):
> *"Admin tạo tài khoản cho từng thành viên, group Team A và Guests; cấp Team A quyền Editor và Guests quyền Viewer trên `/projects`."*

---

## 5. Liên kết tài liệu
- Sơ đồ luồng: [screen-flow.md](screen-flow.md)
- Trình duyệt tệp tin: [02-file-browser.md](02-file-browser.md)
- Chi tiết mô hình RBAC: [docs/rbac.md](../rbac.md)
- Lược đồ dữ liệu phân quyền: [docs/data-model.md](../data-model.md)
