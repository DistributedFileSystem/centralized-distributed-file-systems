# Hộp thoại Lịch sử phiên bản (Version History Dialog)

> Thuộc giai đoạn **Stage 1 (MVP)** – Yêu cầu: **FR-06**, quyết định kiến trúc: **ADR-0002**, liên kết với nhiệm vụ **[M3-06]**.

---

## 1. Mục tiêu màn hình
Cung cấp cửa sổ độc lập cho phép người dùng xem toàn bộ lịch sử các lần cập nhật của một tệp tin (tối đa 5 phiên bản theo cấu hình NFR-03). Cho phép tải lại các phiên bản quá khứ mà không ảnh hưởng đến phiên bản mới nhất đang hoạt động.

---

## 2. Wireframe trực quan (ASCII Mockup)

```
+-----------------------------------------------------------------------------------+
| Lịch sử phiên bản: /projects/dfs/report_final.pdf                        [-] [x]  |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  Tệp tin: report_final.pdf (Phiên bản hiện tại: v3 - 14.2 MB)                     |
|  Hệ thống lưu giữ tối đa 5 phiên bản gần nhất theo cơ chế Write-Once.             |
|                                                                                   |
|  +-----------------------------------------------------------------------------+  |
|  | Phiên bản | Kích thước | Số chunk | Người tạo   | Thời gian tạo    | Hành động  |  |
|  +-----------+------------+----------+-------------+------------------+------------+  |
|  | v3 (Mới)  | 14.2 MB    | 4 chunks | an.nguyen   | 04/11/2026 16:05 | [ Đang xem]|  |
|  | v2        | 12.0 MB    | 3 chunks | tan.phan    | 04/11/2026 14:15 | [ Tải về ] |  |
|  | v1 (Gốc)  |  9.8 MB    | 3 chunks | an.nguyen   | 03/11/2026 20:30 | [ Tải về ] |  |
|  +-----------------------------------------------------------------------------+  |
|                                                                                   |
|  Thông tin chi tiết phiên bản đang chọn (v2):                                     |
|  - Mã định danh nội bộ: item-id: 01H8X9... | version: 2                           |
|  - Danh sách mã khối (Chunk IDs):                                                 |
|    * Chunk 0: c-01H8XA... (4 MB) -> SHA-256: e3b0c44298fc1c149afbf4c8996fb924... |
|    * Chunk 1: c-01H8XB... (4 MB) -> SHA-256: 8f434346648f6b96df89dda901c5176b... |
|    * Chunk 2: c-01H8XC... (4 MB) -> SHA-256: ca978112ca1bbdcafac231b39a23dc4d... |
|                                                                                   |
|  [!] Lưu ý: Tải phiên bản cũ sẽ không ghi đè phiên bản v3 hiện tại trên server.   |
|                                                                                   |
|                                                            +-------------------+  |
|                                                            |    [  ĐÓNG  ]     |  |
|                                                            +-------------------+  |
+-----------------------------------------------------------------------------------+
```

---

## 3. Các thành phần giao diện & Luồng tương tác

1. **Bảng danh sách phiên bản (Version DataGrid):**
   - Liệt kê các phiên bản theo thứ tự thời gian giảm dần (phiên bản mới nhất ở trên cùng).
   - Đánh dấu rõ nhãn `(Mới nhất)` tại phiên bản hiện hành.
   - Các cột hiển thị: Số phiên bản, Dung lượng, Số lượng khối chunk, Người tải lên (`Created By`), Thời điểm hoàn tất (`Created At`).
2. **Nút "Tải về" (Download Historical Version):**
   - Khi bấm vào nút "Tải về" ở dòng phiên bản `v2`:
     - Mở hộp thoại chọn vị trí lưu tệp trên máy người dùng (mặc định gợi ý tên file kèm hậu tố phiên bản, ví dụ: `report_final_v2.pdf`).
     - Gọi `Open(path, version=2)` để Metadata Server cấp vé tải (`readToken`) của các chunk thuộc phiên bản 2.
     - Đưa tác vụ tải vào [Quản lý truyền tải (03-transfers.md)](03-transfers.md) để kéo các khối về máy.
3. **Vùng thông tin kiểm chứng (Verification Detail Pane):**
   - Hiển thị danh sách các Chunk ID và mã băm SHA-256 tương ứng để chứng minh tính bất biến (Immutability) của dữ liệu: dù có cập nhật phiên bản mới, dữ liệu của phiên bản cũ vẫn còn nguyên vẹn trên các Data Server.

---

## 4. Xử lý giới hạn phiên bản (Garbage Collection Indicator)
- Theo yêu cầu phi chức năng NFR-03, hệ thống chỉ lưu tối đa 5 phiên bản.
- Khi người dùng upload phiên bản thứ 6 (`v6`), phiên bản cũ nhất (`v1`) sẽ không còn xuất hiện trong danh sách và các chunk mồ côi (orphan chunks) chỉ thuộc riêng `v1` sẽ được đánh dấu để Metadata Server gửi lệnh `DeleteChunk` dọn dẹp.

---

## 5. Liên kết tài liệu
- Sơ đồ luồng: [screen-flow.md](screen-flow.md)
- Trình duyệt tệp tin: [02-file-browser.md](02-file-browser.md)
- Quản lý truyền tải: [03-transfers.md](03-transfers.md)
- Quyết định kiến trúc Write-Once: [docs/adr/0002-write-once-file-versions.md](../adr/0002-write-once-file-versions.md)
