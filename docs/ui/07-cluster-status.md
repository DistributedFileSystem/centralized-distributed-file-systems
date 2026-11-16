# Màn hình Giám sát cụm lưu trữ (Cluster Status View)

> Thuộc giai đoạn **Stage 1 (MVP)** – Yêu cầu: **FR-10**, liên kết với nhiệm vụ **[M3-09]** (do Quân `@dvquan0516` code, Nguyên `@khanhnguyn2709` kiểm thử chéo theo **[M3-07]**).

---

## 1. Mục tiêu màn hình
Cung cấp cái nhìn trực quan thời gian thực về hạ tầng lưu trữ vật lý của hệ thống phân tán gồm **5 laptop trong mạng LAN**. Hiển thị tình trạng nhịp tim (Heartbeat), dung lượng lưu trữ khả dụng và phân bố khối dữ liệu (chunks) trên từng máy của các thành viên trong nhóm.

---

## 2. Wireframe trực quan (ASCII Mockup)

```
+---------------------------------------------------------------------------------------------------------+
| Centralized Distributed File System (DFS) - Trạng thái cụm lưu trữ                            [-] [o] [x]|
+---------------------------------------------------------------------------------------------------------+
| [Logo DFS]  | GIÁM SÁT HẠ TẦNG CỤM LƯU TRỮ (CLUSTER)  | Cập nhật mỗi: 3 giây [O]     | [an.nguyen v] [Thoát]|
+---------------------------------------------------------------------------------------------------------+
|                                                                                                         |
|  === TỔNG QUAN HỆ THỐNG (SYSTEM OVERVIEW) ===                                                           |
|                                                                                                         |
|  +------------------------+  +------------------------+  +------------------------+  +---------------+  |
|  | DATA SERVERS           |  | DUNG LƯỢNG LƯU TRỮ     |  | TỔNG SỐ KHỐI (CHUNKS)  |  | METADATA NODE |  |
|  | 4 / 5 Alive            |  | 380 GB trống / 500 GB  |  | 32,450 chunks          |  | m1 (Tân)      |  |
|  | [v] 4 Alive   [x] 1 Dead| | [====          ] 24%    |  | (1 bản sao / chunk)    |  | OpLog: seq 142|  |
|  +------------------------+  +------------------------+  +------------------------+  +---------------+  |
|                                                                                                         |
|---------------------------------------------------------------------------------------------------------|
|                                                                                                         |
|  === CHI TIẾT 5 DATA SERVERS (LAN / DOCKER COMPOSE) ===                                                  |
|                                                                                                         |
|  [O] Quét lại nhịp tim  |  Tự động làm mới: [v] Bật (3s)  |  Cảnh báo: Phát hiện 1 máy mất kết nối!     |
|                                                                                                         |
|  +---------------------------------------------------------------------------------------------------+  |
|  | Node ID | Laptop / Thành viên | Địa chỉ (IP:Port)      | Trạng thái  | Dung lượng trống | Số chunk | Nhịp tim |  |
|  +---------+---------------------+------------------------+-------------+------------------+----------+----------+  |
|  | ds-1    | Laptop 1 (Tân)      | 127.0.0.1:7001         | [v] ALIVE   | 78.5 GB / 100 GB | 6,420    | 1s trước |  |
|  | ds-2    | Laptop 2 (Nhất)     | 127.0.0.1:7002         | [v] ALIVE   | 72.0 GB / 100 GB | 6,850    | 2s trước |  |
|  | ds-3    | Laptop 3 (Cường)    | 127.0.0.1:7003         | [v] ALIVE   | 81.2 GB / 100 GB | 6,100    | 1s trước |  |
|  | ds-4    | Laptop 4 (Nguyên)   | 127.0.0.1:7004         | [v] ALIVE   | 76.4 GB / 100 GB | 6,580    | 1s trước |  |
|  | ds-5    | Laptop 5 (Quân)     | 127.0.0.1:7005         | [x] DEAD    | -- (Mất tín hiệu)| 6,500    | 25s trước|  |
|  +---------------------------------------------------------------------------------------------------+  |
|                                                                                                         |
|  [!] Ghi chú Stage 1: Khi Data Server ds-5 bị Dead, các tệp tin có chunk lưu trên máy này sẽ tạm thời    |
|      báo lỗi 'Unavailable' khi tải xuống. Tính năng tự phục hồi 3 bản sao sẽ có ở Stage 2.             |
|                                                                                                         |
+---------------------------------------------------------------------------------------------------------+
| Môi trường: Docker Compose (127.0.0.1:7000..7005) hoặc Mạng LAN (192.168.10.x:7001) | Chu kỳ: 3 giây   |
+---------------------------------------------------------------------------------------------------------+
```

---

## 3. Các chỉ số & Thành phần hiển thị chính

1. **Thẻ tóm tắt chỉ số (KPI Summary Cards):**
   - **Data Servers:** Đếm số lượng node đang hoạt động bình thường (`DataServerStatus.Alive`) trên tổng số 5 node.
   - **Dung lượng lưu trữ:** Tổng hợp dung lượng trống (`FreeBytes`) và tổng dung lượng (`TotalBytes`) của toàn cụm.
   - **Tổng số khối (Chunks):** Tổng số lượng chunk 4 MB đang được lưu trữ trên tất cả các Data Server (`ChunkCount`).
   - **Metadata Node:** Thông tin node điều phối (địa chỉ kết nối, số thứ tự command trong Op Log `lastSeq`).
2. **Bảng chi tiết các Data Server (DataGrid):**
   - **Node ID:** Tên định danh duy nhất của node trong cấu hình (`ds-1` đến `ds-5`).
   - **Laptop / Thành viên:** Ghi chú máy tính của ai trong nhóm để tiện theo dõi và kiểm tra vật lý.
   - **Địa chỉ quảng bá (`AdvertiseAddress`):** Cổng 7001–7005 trên `127.0.0.1` (Compose) hoặc port 7001 trên IP LAN của từng laptop (`192.168.10.x`).
   - **Trạng thái (Status Badge - Khớp enum `DataServerStatus`):**
     - `Alive` (Xanh lá): Nhận được nhịp tim đều đặn trong vòng dưới 9 giây (tối đa 3 chu kỳ heartbeat 3s).
     - `Dead` (Đỏ): Quá 3 chu kỳ nhịp tim liên tiếp (9 giây) không nhận được tin nhắn `Heartbeat`.
   - **Dung lượng trống:** Hiển thị số GB còn trống và thanh tiến trình dung lượng.
   - **Số lượng Chunk:** Số lượng khối nhị phân thực tế mà Data Server đó đang giữ trên ổ đĩa.
   - **Nhịp tim cuối (Last Heartbeat):** Thời gian trôi qua kể từ lần cuối cùng node gửi tín hiệu báo sống.

---

## 4. Kịch bản kiểm thử tương ứng trong Test Plan
Màn hình này phục vụ trực tiếp cho các kịch bản kiểm thử:
- **Kịch bản T-04 (Tắt Data Server laptop 5):** Khi tắt ứng dụng Data Server trên laptop 5, sau 9 giây màn hình tự động chuyển trạng thái dòng `ds-5` sang màu đỏ `[x] DEAD`.
- **Bước 1 & Bước 3 trong kịch bản Demo Stage 1 (`docs/test-plan.md`):**
  - Khởi động cluster -> Admin panel thấy 5 Data Server healthy.
  - Upload file 50 MB -> Thấy số lượng chunk tăng lên và rải đều trên cả 5 laptop.

---

## 5. Liên kết tài liệu
- Sơ đồ luồng: [screen-flow.md](screen-flow.md)
- Quản lý truyền tải: [03-transfers.md](03-transfers.md)
- Kế hoạch triển khai mạng LAN 5 laptop: [docs/deployment.md](../deployment.md)
- Quy trình kiểm thử: [docs/test-plan.md](../test-plan.md)
