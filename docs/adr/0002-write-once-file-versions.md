# ADR-0002: File write-once với version và conflict detection

- Trạng thái: Accepted
- Ngày: 2026-11-03

## Context
Sửa chunk tại chỗ (in-place edit) trên dữ liệu phân tán gây ra: partial failure giữa các bản sao, hai người ghi xen kẽ vào cùng chunk, checksum phải tính lại liên tục.

## Options
1. In-place edit + lock.
2. **Write-once**: mỗi lần lưu tạo version mới với chunk mới; con trỏ file chuyển sang version mới trong một bước.

## Decision
Chọn **write-once**. Mỗi `CommitUpload` mang `baseVersion`; nếu version hiện tại khác → `Conflict` (**optimistic concurrency**). Giữ N version gần nhất (config), GC xoá chunk của version cũ.

## Consequences
- Chunk không bao giờ thay đổi: checksum cố định, người đọc không bao giờ thấy file ghi dở.
- Rename / move / delete / đổi quyền chỉ đụng metadata.
- Sửa 1 byte của file lớn phải upload lại cả file. Stage 2 có thể đặt tên chunk theo content hash để dùng lại chunk không đổi.
