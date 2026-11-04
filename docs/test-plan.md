# Test plan

> File này trở thành **test report** ở cuối mỗi stage (thêm cột kết quả).

## Cách test

- Test do **Quân** viết **độc lập** với dev: từ contract (`docs/protocol.md`, `docs/rbac.md`, `docs/flows.md`) và acceptance criteria của issue, không dựa vào code của dev.
- Acceptance test nằm trong `tests/Dfs.AcceptanceTests`, chỉ tham chiếu `Dfs.Shared`, nói chuyện với hệ thống qua TCP như client thật (black-box).
- Test viết song song với code trên branch `test/*`, merge ở dạng pending, bật khi feature sẵn sàng; feature chỉ merge khi test pass.
- Màn hình GUI do Quân làm được Nguyên test bằng GUI checklist.

## Phạm vi

| Loại | Nội dung |
| --- | --- |
| Unit test | Framing, token sign / verify, RBAC check, command apply, checksum |
| Integration test | Cluster Docker Compose: login, upload, download, conflict, restart metadata |
| Failure test | Kịch bản bên dưới, chạy trên 5 laptop |

## Failure scenarios (stage 1)

| ID | Kịch bản | Kết quả mong đợi |
| --- | --- | --- |
| T-01 | Restart Metadata Server | Thư mục, user, grant, version còn nguyên |
| T-02 | Xoá file SQLite rồi restart | SQLite dựng lại từ snapshot + op log |
| T-03 | Sửa tay một chunk file | Client báo checksum sai |
| T-04 | Tắt Data Server laptop 5 | File có chunk ở đó báo `Unavailable`; file khác vẫn đọc được |
| T-05 | Token hết hạn | Data Server trả `TokenExpired` |
| T-06 | Token bị sửa | Data Server trả `TokenInvalid` |
| T-07 | Guest upload vào `/projects` | `Forbidden`, có dòng trong audit log |
| T-08 | Hai user cùng lưu một file | Người thứ hai nhận `Conflict` |
| T-09 | Client crash giữa upload | File không hiển thị; chunk bị GC xoá sau timeout |

## Demo script – Stage 1

1. Khởi động cluster: Metadata trên laptop 1, Data Server trên mọi laptop. Admin panel thấy 5 Data Server healthy.
2. Admin tạo tài khoản cho từng thành viên, group `Team A` và `Guests`; cấp `Team A` quyền Editor và `Guests` quyền Viewer trên `/projects`.
3. User ở laptop 2 upload file lớn (ví dụ 50 MB). Màn hình cluster thấy chunk rải trên cả 5 laptop.
4. User `Team A` ở laptop 3 tải về; hash khớp, chunk đến từ nhiều laptop cùng lúc.
5. Guest ở laptop 4 mở được file nhưng không upload được: `Forbidden`. Audit log có dòng bị từ chối.
6. Hai user cùng sửa một file. Người lưu sau nhận conflict; lịch sử version hiện cả hai version.
7. Restart Metadata Server: thư mục, user, grant, version vẫn còn.
8. Tắt Data Server laptop 5: file có chunk ở đó báo unavailable. Đây là giới hạn stage 1 mà stage 2 giải quyết.

## Demo script – Stage 2

1. Khởi động 3 Metadata node và 5 Data Server. Admin panel hiện node nào là leader.
2. Kill leader khi đang dùng: leader mới được bầu, client kết nối lại, không mất file đã commit.
3. Tắt một laptop data: download tiếp tục từ bản sao khác, repair đưa mọi chunk về 3 bản sao.
4. Bật lại leader cũ: nó tham gia lại làm follower và bắt kịp log.
