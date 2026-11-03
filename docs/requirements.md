# Requirements

## Bối cảnh

Đề tài 7: Xây dựng ứng dụng truy xuất tài nguyên là các thư mục và tập tin.
Phần B: **Server là nơi lưu trữ các thông tin meta file. Nội dung được lưu trữ ở các data server.**
Yêu cầu của giảng viên: hệ thống chạy trên **5 laptop** kết nối với nhau thành một hệ thống.

Use case thực tế để trình bày: kho file nội bộ cho một lab hoặc công ty nhỏ, chịu được khi một máy hỏng.

## Functional requirements (Stage 1)

| ID | Yêu cầu |
| --- | --- |
| FR-01 | User đăng nhập bằng username + password; đăng xuất |
| FR-02 | Xem cây thư mục, thông tin file (size, owner, version, thời gian) |
| FR-03 | Tạo, đổi tên, di chuyển, xoá thư mục và file |
| FR-04 | Upload file bất kỳ kích thước (giới hạn trong config); file được chia thành chunk |
| FR-05 | Download file; client tải các chunk song song trực tiếp từ Data Server |
| FR-06 | Lưu lại file tạo **version** mới; xem lịch sử version, tải version cũ |
| FR-07 | Phát hiện **conflict** khi hai người cùng lưu một file |
| FR-08 | Phân quyền **RBAC**: system role, group, grant theo thư mục / file, kế thừa xuống thư mục con |
| FR-09 | Admin panel: quản lý user, group, role, grant, xem cluster và audit log |
| FR-10 | Data Server tự đăng ký và gửi heartbeat; admin thấy trạng thái từng Data Server |
| FR-11 | Mỗi chunk có checksum SHA-256; client phát hiện chunk bị hỏng |
| FR-12 | Metadata Server restart không mất dữ liệu (op log + snapshot) |
| FR-13 | Chạy được trên 1 máy (Docker Compose) và trên 5 laptop trong LAN |

## Functional requirements (Stage 2)

| ID | Yêu cầu |
| --- | --- |
| FR-21 | 3 Metadata node dùng **Raft**; mất 1 node hệ thống vẫn chạy |
| FR-22 | Client và Data Server tự tìm leader mới (`NotLeader` redirect) |
| FR-23 | Mỗi chunk có **3 bản sao**, ghi qua pipeline |
| FR-24 | Đọc tự chuyển sang bản sao khác khi Data Server chết hoặc checksum sai |
| FR-25 | **Re-replication**: tự khôi phục đủ 3 bản sao khi mất Data Server |
| FR-26 | TLS trên mọi kết nối (tuỳ chọn) |

## Non-functional requirements

| ID | Mục tiêu | Giá trị đề xuất |
| --- | --- | --- |
| NFR-01 | Chunk size | 4 MB (config) |
| NFR-02 | File size tối đa | 2 GB (config) |
| NFR-03 | Số version giữ lại | 5 (config) |
| NFR-04 | Heartbeat | 3 giây; mất 3 lần liên tiếp → Data Server bị đánh dấu `Dead` |
| NFR-05 | Data token hết hạn | 5 phút; cho phép lệch đồng hồ 2 phút |
| NFR-06 | Session hết hạn | 8 giờ |
| NFR-07 | Platform | Windows + Linux |
| NFR-08 | Ngôn ngữ / runtime | C#, .NET 10 |

## Out of scope

- Web UI, mobile app
- Sửa file trực tiếp trên chunk (in-place edit)
- Mã hoá dữ liệu khi lưu (encryption at rest)
- Stage 1: replication, HA, TLS
