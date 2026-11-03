# Scripts

## setup_github.py

Tạo **labels**, **milestones** (M1–M6, S1–S4), **39 issues** (kèm acceptance criteria và "Blocked by #") và **GitHub Project** "DFS - Task board" từ `backlog.json`.

```bash
gh auth login                 # tài khoản owner của organization
gh auth refresh -s project    # cần scope project để tạo board
python scripts/setup_github.py --dry-run   # xem trước
python scripts/setup_github.py
```

Sửa backlog: chỉnh `backlog.json` rồi chạy lại; issue đã có (cùng title) được bỏ qua.
