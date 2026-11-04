# Hộp thoại Xử lý xung đột phiên bản (Conflict Resolution Dialog)

> Thuộc giai đoạn **Stage 1 (MVP)** – Yêu cầu: **FR-07**, quyết định kiến trúc: **ADR-0002**, liên kết với nhiệm vụ **[M3-08]** (do Quân `@dvquan0516` code, Nguyên `@khanhnguyn2709` kiểm thử chéo theo **[M3-07]**).

---

## 1. Mục tiêu màn hình
Cung cấp hộp thoại cảnh báo phương thức kiểm soát đồng thời lạc quan (Optimistic Concurrency Control) khi phát hiện xung đột: hai người dùng cùng lấy một phiên bản tệp tin về chỉnh sửa, nhưng người thứ hai lưu đè sau khi người thứ nhất đã commit thành công phiên bản mới. Cho phép người dùng quyết định giải pháp bảo toàn dữ liệu, không làm mất mát thông tin.

---

## 2. Wireframe trực quan (ASCII Mockup)

```
+-----------------------------------------------------------------------------------+
|  [!] CẢNH BÁO: PHÁT HIỆN XUNG ĐỘT PHIÊN BẢN (SAVE CONFLICT)              [-] [x]  |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  +-----------------------------------------------------------------------------+  |
|  | [!] Tệp tin đã được thay đổi trên máy chủ bởi người dùng khác!              |  |
|  +-----------------------------------------------------------------------------+  |
|                                                                                   |
|  Đường dẫn: /projects/dfs/report_final.pdf                                        |
|                                                                                   |
|  Chi tiết xung đột:                                                               |
|  * Phiên bản gốc bạn bắt đầu sửa (baseVersion): v3 (lấy lúc 14:00)                |
|  * Phiên bản hiện tại trên máy chủ (Current)   : v4 (bởi 'tan.phan' lúc 14:15)    |
|  * Kích thước bản trên máy chủ                 : 12.0 MB                          |
|  * Kích thước bản chỉnh sửa của bạn            : 13.5 MB                          |
|                                                                                   |
|  Hệ thống không tự động ghi đè để bảo vệ dữ liệu. Vui lòng chọn hướng xử lý:      |
|                                                                                   |
|  (o) LỰA CHỌN 1: LƯU THÀNH BẢN SAO MỚI (Khuyên dùng)                             |
|      Lưu tệp tin của bạn thành một tệp tin mới riêng biệt:                        |
|      Tên tệp mới: [ report_final (copy 1).pdf                       ]             |
|      -> Cả hai bản sửa đổi đều được giữ nguyên trên hệ thống.                     |
|                                                                                   |
|  ( ) LỰA CHỌN 2: TẢI LẠI PHIÊN BẢN MỚI NHẤT TRÊN SERVER (RELOAD)                  |
|      Hủy bỏ dữ liệu bạn vừa tải lên và tải phiên bản v4 của 'tan.phan' về máy.    |
|      -> Các thay đổi cục bộ chưa lưu của bạn sẽ bị thay thế.                      |
|                                                                                   |
|                                                                                   |
|  +---------------------------+  +---------------------------+  +---------------+  |
|  | [ Lưu thành bản sao mới ] |  | [ Tải lại bản mới nhất ]  |  |   [ Hủy bỏ ]  |  |
|  +---------------------------+  +---------------------------+  +---------------+  |
+-----------------------------------------------------------------------------------+
```

---

## 3. Các thành phần giao diện & Luồng xử lý chi tiết

1. **Khung cảnh báo màu vàng cam (Warning Banner):**
   - Biểu tượng cảnh báo `[!]` nổi bật, thông báo rõ ràng mã lỗi `Conflict` nhận về từ lời gọi `CommitUpload(baseVersion=3)`.
2. **Bảng đối chiếu thông tin (Comparison Pane):**
   - Nêu rõ ai là người đã commit trước (`tan.phan`), vào thời gian nào, và kích thước tệp tin mới là bao nhiêu.
3. **Hai giải pháp giải quyết xung đột (Resolution Options):**
   - **Tùy chọn 1: Lưu thành bản sao mới (Save as Copy - Mặc định):**
     - Cho phép người dùng chỉnh sửa tên tệp mới tại ô nhập liệu (mặc định thêm hậu tố `(copy N)`).
     - Khi bấm **[Lưu thành bản sao mới]**: Client tạo một phiên upload mới với đường dẫn mới (ví dụ `/projects/dfs/report_final (copy 1).pdf`), gửi `baseVersion=0` (tệp mới) và commit thành công.
   - **Tùy chọn 2: Tải lại bản mới nhất (Reload Latest):**
     - Khi bấm **[Tải lại bản mới nhất]**: Client hủy bỏ tệp tạm, gọi `Open(/projects/dfs/report_final.pdf, version=4)` và tải phiên bản mới nhất về máy cục bộ của người dùng.
   - **Nút Hủy bỏ (Cancel):** Đóng hộp thoại, giữ tệp ở trạng thái chưa lưu.

---

## 4. Kịch bản kiểm thử tương ứng trong Test Plan
- Màn hình này phục vụ trực tiếp cho kịch bản kiểm thử thất bại **T-08 (Save Conflict)** và bước số 6 trong kịch bản **Demo Stage 1** (`docs/test-plan.md`):
  *"Hai user cùng lưu một file. Người lưu sau nhận conflict; lịch sử version hiện cả hai version hoặc tạo bản sao riêng."*
- Đây là màn hình do Quân (`dvquan0516`) triển khai, và bạn Nguyên (`khanhnguyn2709`) sẽ dựa vào bản thiết kế này để kiểm thử chéo và nghiệm thu trong task **M3-07**.

---

## 5. Liên kết tài liệu
- Sơ đồ luồng: [screen-flow.md](screen-flow.md)
- Quản lý truyền tải: [03-transfers.md](03-transfers.md)
- Kế hoạch kiểm thử & demo: [docs/test-plan.md](../test-plan.md)
- Quyết định kiến trúc xung đột: [docs/adr/0002-write-once-file-versions.md](../adr/0002-write-once-file-versions.md)
