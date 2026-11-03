# ADR-0004: RBAC với system role, group và grant theo thư mục

- Trạng thái: Accepted
- Ngày: 2026-11-03

## Context
Hệ thống cần đăng nhập và phân quyền như hệ thống thật, quản lý trong admin panel. Pure RBAC (chỉ role toàn cục) cho phép mọi Editor sửa mọi file, không thực tế.

## Options
1. Pure RBAC: role toàn cục.
2. ACL theo từng file.
3. **RBAC kết hợp scope**: role toàn hệ thống + role cấp trên thư mục / file cho user hoặc group, kế thừa xuống cây (giống Google Drive, SharePoint).

## Decision
Chọn option 3. Built-in role: Admin, Auditor (system); Owner, Editor, Viewer (thư mục / file). Admin tạo thêm role và grant trong admin panel. Stage 1 chỉ có allow grant.

## Consequences
- Kiểm tra quyền phải gom grant của user + group và đi ngược lên thư mục cha → cần index và cache hợp lý.
- Data Server không liên quan tới RBAC (chỉ verify token).
- Deny rule và "ngừng kế thừa" để stage 2.
