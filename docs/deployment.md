# Deployment

Cùng một Dockerfile cho cả hai mode; chỉ khác file env. Không có CD: mỗi máy tự build image từ source bằng `--build` (build sẵn một ngày trước demo). Avalonia client luôn chạy native trên laptop, không chạy trong Docker.

| Mode | Ở đâu | Cách chạy | Dùng cho |
| --- | --- | --- | --- |
| Single machine | 1 laptop | `docker compose -f deploy/docker-compose.yml up --build`: 1 Metadata Server + 5 Data Server, mỗi cái một volume và port | Phát triển, demo nhanh |
| LAN cluster | 5 laptop | Mỗi laptop: `docker compose -f deploy/node.yml --env-file deploy/env/laptopN.env up --build` | Integration test, demo cuối |

## Bố trí laptop

Mỗi laptop vừa là storage node vừa là user: mỗi thành viên đăng nhập trên laptop của mình bằng tài khoản riêng.

| Laptop | Stage 1 | Stage 2 |
| --- | --- | --- |
| 1 | Metadata Server + Data Server + client | Metadata node + Data Server + client |
| 2 | Data Server + client | Metadata node + Data Server + client |
| 3 | Data Server + client | Metadata node + Data Server + client |
| 4 | Data Server + client | Data Server + client |
| 5 | Data Server + client | Data Server + client |

## Cấu hình node (environment variables)

| Biến | Ý nghĩa | Ví dụ |
| --- | --- | --- |
| `NODE_ROLE` | `metadata` hoặc `data` | `data` |
| `NODE_ID` | Tên duy nhất của node | `ds-3` |
| `LISTEN_PORT` | Port node lắng nghe | `7001` |
| `ADVERTISE_HOST` | Địa chỉ laptop khác dùng để kết nối tới node (IP LAN của laptop) | `192.168.10.13` |
| `METADATA_ENDPOINTS` | Địa chỉ metadata: stage 1 một, stage 2 ba (phân cách bằng dấu phẩy) | `192.168.10.11:7000` |
| `DATA_DIR` | Thư mục (Docker volume) cho SQLite hoặc chunk | `/data` |
| `TOKEN_KEY` | Secret HMAC chung, lấy từ file env, không commit | — |
| `ADMIN_USER`, `ADMIN_PASSWORD` | Admin đầu tiên, chỉ dùng lần khởi động đầu | — |

> **Lỗi hay gặp nhất:** Data Server đăng ký bằng IP nội bộ Docker (ví dụ `172.18.0.3`). Client ở laptop khác không kết nối được. Luôn đặt `ADVERTISE_HOST` = IP LAN của laptop.

## LAN checklist

- [ ] Dùng router / switch riêng để mạng trường không chặn kết nối giữa các laptop; cả 5 laptop cùng một subnet
- [ ] Gán IP cố định cho mỗi laptop (DHCP reservation trên router) để file env không phải sửa
- [ ] Mở port trên Windows Defender Firewall và `ufw` trên Linux (script trong `deploy/`)
- [ ] Trên Windows (Docker Desktop) publish port bằng `ports:`; không dựa vào host network mode
- [ ] Bật đồng bộ thời gian tự động (token cho phép lệch 2 phút)
- [ ] Trước mỗi buổi, chạy smoke test: từ mỗi laptop kết nối tới port của mọi node

## Mở port (tham khảo)

Windows (PowerShell, chạy as Administrator):
```powershell
New-NetFirewallRule -DisplayName "DFS" -Direction Inbound -Protocol TCP -LocalPort 7000,7001 -Action Allow
```

Linux:
```bash
sudo ufw allow 7000:7001/tcp
```
