# Deployment

Cùng một Dockerfile cho cả hai mode; chỉ khác file env. Không có CD: mỗi máy tự build image từ source bằng `--build` (build sẵn một ngày trước demo). Avalonia client luôn chạy native trên laptop, không chạy trong Docker.

| Mode | Ở đâu | Cách chạy | Dùng cho |
| --- | --- | --- | --- |
| Single machine | 1 laptop | `docker compose -f deploy/docker-compose.yml up --build`: 1 Metadata Server + 5 Data Server, mỗi cái một volume và port | Phát triển, demo nhanh |
| LAN cluster | 5 laptop | Mỗi laptop: `docker compose -f deploy/node.yml --env-file deploy/env/laptopN.env up --build` | Integration test, demo cuối |

## Bố trí laptop

Mỗi laptop vừa là storage node vừa là user: mỗi thành viên đăng nhập trên laptop của mình bằng tài khoản riêng.

| Laptop | IP mẫu | Stage 1 | Stage 2 |
| --- | --- | --- | --- |
| 1 | `192.168.10.11` | Metadata Server + Data Server + client | Metadata node + Data Server + client |
| 2 | `192.168.10.12` | Data Server + client | Metadata node + Data Server + client |
| 3 | `192.168.10.13` | Data Server + client | Metadata node + Data Server + client |
| 4 | `192.168.10.14` | Data Server + client | Data Server + client |
| 5 | `192.168.10.15` | Data Server + client | Data Server + client |

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
| `KEEP_VERSIONS` | Số phiên bản file tối đa được giữ lại | `5` |
| `SNAPSHOT_INTERVAL_OPS` | Số command giữa các lần snapshot định kỳ (`0` = tắt tự động; shutdown vẫn snapshot nếu có dữ liệu) | `100` |
| `CAPACITY_BYTES` | Giới hạn dung lượng Data Server tính theo byte (`0` = tự động) | `0` |

> **Lỗi hay gặp nhất:** Data Server đăng ký bằng IP nội bộ Docker (ví dụ `172.18.0.3`). Client ở laptop khác không kết nối được. Luôn đặt `ADVERTISE_HOST` = IP LAN của laptop.

## Triển khai LAN cluster (5 laptop)

### 1. Chuẩn bị file env cho từng laptop

Sao chép file mẫu tương ứng (`deploy/env/laptop1.example.env` .. `deploy/env/laptop5.example.env`) sang `deploy/env/laptopN.env` (file `.env` thật đã được `.gitignore` bỏ qua, tuyệt đối không commit):

```bash
# Ví dụ trên Laptop 1 (Metadata + Data)
cp deploy/env/laptop1.example.env deploy/env/laptop1.env

# Ví dụ trên Laptop N (N = 2..5, chỉ chạy Data)
cp deploy/env/laptopN.example.env deploy/env/laptopN.env
```

Chỉnh sửa `deploy/env/laptopN.env`:
- Đặt `ADVERTISE_HOST` đúng IP LAN cố định của laptop đó.
- Đặt `TOKEN_KEY` giống nhau trên cả 5 laptop (và `ADMIN_PASSWORD` trên Laptop 1).

### 2. Mở port firewall

Windows (PowerShell, chạy **Run as Administrator**):

```powershell
.\deploy\scripts\open-firewall.ps1
# Xem trước thay đổi hoặc gỡ rule:
.\deploy\scripts\open-firewall.ps1 -WhatIf
.\deploy\scripts\open-firewall.ps1 -Remove
```

Linux (`ufw`):

```bash
sudo ./deploy/scripts/open-firewall.sh
# Xem trước lệnh hoặc gỡ rule:
./deploy/scripts/open-firewall.sh --dry-run
sudo ./deploy/scripts/open-firewall.sh --remove
```

### 3. Build tại máy và khởi động node

Mỗi laptop tự build Docker image từ source và khởi động theo profile đã khai báo trong `COMPOSE_PROFILES`:

```bash
docker compose -f deploy/node.yml --env-file deploy/env/laptopN.env up --build
```

### 4. Chạy smoke test kiểm tra kết nối LAN

Từ mỗi laptop, kiểm tra kết nối TCP tới toàn bộ 6 endpoint của Stage 1 (`192.168.10.11:7000` và `192.168.10.11..15:7001`):

Windows (PowerShell):

```powershell
.\deploy\scripts\smoke-test.ps1
```

Linux / macOS:

```bash
./deploy/scripts/smoke-test.sh
```

Có thể truyền danh sách endpoint tùy chỉnh với `--targets` (Linux) hoặc `-Targets` (Windows). Script trả về exit code `0` khi tất cả endpoint đều `PASS`, ngược lại trả về `1`.

## LAN checklist

- [ ] Dùng router / switch riêng để mạng trường không chặn kết nối giữa các laptop; cả 5 laptop cùng một subnet
- [ ] Gán IP cố định cho mỗi laptop (DHCP reservation trên router) để file env không phải sửa
- [ ] Mở port trên Windows Defender Firewall (`deploy/scripts/open-firewall.ps1`) và `ufw` trên Linux (`deploy/scripts/open-firewall.sh`)
- [ ] Trên Windows (Docker Desktop) publish port bằng `ports:`; không dựa vào host network mode
- [ ] Bật đồng bộ thời gian tự động (token cho phép lệch 2 phút)
- [ ] Trước mỗi buổi, chạy smoke test (`deploy/scripts/smoke-test.ps1` / `deploy/scripts/smoke-test.sh`): từ mỗi laptop kết nối tới port của mọi node
