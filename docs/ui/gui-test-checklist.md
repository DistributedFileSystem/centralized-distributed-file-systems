# Bảng Kiểm Thử Giao Diện Người Dùng (GUI Test Checklist)

> **Tài liệu tham chiếu:** GitHub Issue #15 (M1-12: GUI test checklist từ design)  
> **Dự án:** Centralized Distributed File System (DFS) - Desktop Client (`Dfs.Client.Desktop`)  
> **Công nghệ giao diện:** Avalonia UI (Cross-platform XAML), ReactiveUI / MVVM  
> **Tài liệu thiết kế nguồn:** `docs/ui/` (Branch `design/11-stage1-mockups` - `01-login.md` đến `07-cluster-status.md`)  
> **Mã nguồn thực tế tham chiếu:** Branch `feature/12-app-shell`, `feature/28-login-filebrowser`, `feature/29-transfers-progress`, `feature/31-admin-panel`  
> **Trạng thái tài liệu:** Đã chuẩn hóa toàn diện – Sẵn sàng phục vụ kiểm thử thủ công (Manual Testing)

---

## 1. Hướng Dẫn Thực Thi Kiểm Thử (Test Execution Guide)

### 1.1 Mục tiêu
Tài liệu này cung cấp checklist kiểm thử chức năng và giao diện người dùng cho ứng dụng DFS Desktop Client. Bộ kiểm thử giúp kiểm chứng độ khớp giữa bản vẽ thiết kế (UI/UX Mockups) và sản phẩm phần mềm thực tế, đảm bảo tính tiện dụng, độ tin cậy khi xử lý dữ liệu và khả năng thông báo lỗi chuẩn hóa theo giao thức DFS.

### 1.2 Môi trường kiểm thử
- **Hệ điều hành:** Windows 10/11 x64, macOS 13+, hoặc Ubuntu 22.04 LTS x64.
- **Nền tảng thực thi:** .NET SDK 9.0 trở lên.
- **Độ phân giải màn hình khuyến nghị:** Tối thiểu 1280 x 800 (khung nhìn tiêu chuẩn theo thiết kế), tỷ lệ hiển thị (DPI scaling) 100% hoặc 125%.
- **Lệnh khởi chạy ứng dụng:**
  ```powershell
  dotnet run --project src/Dfs.Client.Desktop/Dfs.Client.Desktop.csproj
  ```

### 1.3 Phân định Môi Trường Kiểm Thử & Cấu Hình Dữ Liệu
Để kiểm thử hiệu quả và tránh nhầm lẫn giữa mô phỏng và tích hợp thật, tester cần phân biệt rõ các môi trường backend:

1. **Môi trường Giả Lập In-Memory (`FakeDfsClient`):**
   - Đây là môi trường mặc định được khởi tạo trong `App.axaml.cs` (`new FakeDfsClient()`).
   - Phù hợp để kiểm thử nhanh giao diện (UI/UX), điều hướng, form nhập liệu, dialog, và luồng tương tác mô phỏng.
   - **Tài khoản hỗ trợ:** Hiện tại `FakeDfsClient` **chỉ hỗ trợ duy nhất tài khoản `demo` / `demo`** (mặc định mang vai trò Quản trị viên `Admin` với toàn quyền hệ thống). Mọi tài khoản khác khi đăng nhập trên `FakeDfsClient` đều bị từ chối với lỗi `Unauthorized`.
   - Các thao tác CRUD (tạo thư mục, đổi tên, xóa, chuyển) và phân quyền trên `FakeDfsClient` chỉ cập nhật trên `ObservableCollection` của giao diện; không lưu trữ thực tế xuống đĩa hay cơ sở dữ liệu ngầm.
   - Hoạt động truyền tải (Upload/Download) là tiến trình mô phỏng in-memory, không đọc/ghi tệp vật lý trên ổ cứng.
   - **Lưu ý về độ trễ và quan sát trạng thái tải:** Phương thức `LoginAsync` trên `FakeDfsClient` mặc định trả về đồng bộ rất nhanh (`Task.FromResult`, < 1ms), do đó không đủ độ trễ để mắt người kịp quan sát thanh tiến trình Loading Indicator.

2. **Môi trường Có Độ Trễ Mạng Được Kiểm Soát hoặc Cụm Phân Tán Thật (`Metadata Server` qua TCP):**
   - Ứng dụng kết nối tới cụm Metadata Server và các Storage Node thật qua mạng LAN/Localhost (mặc định `127.0.0.1:7000`) hoặc môi trường kiểm thử có điều tiết độ trễ (Network Throttling / Simulated Delay).
   - Bắt buộc phải sử dụng môi trường này để kiểm tra:
     - Trạng thái Loading Indicator và khóa form khi đăng nhập (`TC-LOGIN-05`): Cần môi trường có độ trễ mạng được kiểm soát hoặc thời gian xử lý đủ dài để quan sát rõ ràng. *(Lưu ý: Không khẳng định Metadata Server thật luôn có độ trễ đủ lớn, đặc biệt khi chạy local loopback, vì vậy môi trường có độ trễ kiểm soát là phương án đáng tin cậy nhất).*
     - Xác thực đa người dùng (`admin`, `alice`, `bob`, `locked_user`).
     - Các mã lỗi giao thức mạng như tài khoản bị vô hiệu hóa (`Forbidden`), máy chủ mất kết nối (`Unavailable`).
     - Kiểm soát truy cập RBAC động theo vai trò (User thường ẩn tab Admin, chặn truy cập thư mục cấm).
     - Lưu trữ thực tế thông tin người dùng, nhóm, phân quyền khi tạo mới.
     - Truyền nhận file vật lý thực tế giữa client và các Data Server.

| Tài khoản (Username) | Mật khẩu (Password) | Vai trò (Role) | Môi trường áp dụng | Mục đích kiểm thử |
|---|---|---|---|---|
| `demo` | `demo` | Quản trị viên (Admin) | `FakeDfsClient` (mặc định) & `Metadata Server` | Kiểm tra toàn bộ UI, luồng thao tác tệp, quản trị mẫu, xem cụm. |
| `admin` | `admin123` | Quản trị viên (Admin) | `Metadata Server` thật | Toàn quyền thao tác tệp, phân quyền RBAC, xem Admin Panel và Cluster Status. |
| `alice` | `alice123` | Người dùng thường (User) | `Metadata Server` thật | Thao tác thư mục `/home/alice`, kiểm tra ẩn tab Admin Panel. |
| `bob` | `bob123` | Người dùng thường (User) | `Metadata Server` thật | Thao tác thư mục `/home/bob`, kiểm tra phân quyền chia sẻ. |
| `locked_user` | `locked123` | Bị khóa (Disabled) | `Metadata Server` thật | Kiểm thử từ chối xác thực tài khoản bị vô hiệu hóa (`Forbidden`). |

- **Địa chỉ máy chủ mặc định:**
  - Thiết kế (`01-login.md`): `192.168.10.11:7000`
  - Thực tế mã nguồn (`LoginViewModel.cs`): `127.0.0.1:7000`

### 1.4 Quy ước các cột & Trạng thái kiểm thử
- **Test ID:** Mã định danh duy nhất của ca kiểm thử (`TC-<MODULE>-<STT>`).
- **Test Scenario:** Tên kịch bản và mục đích kiểm thử.
- **Preconditions:** Điều kiện tiên quyết trước khi thực hiện các bước (kèm môi trường yêu cầu).
- **Test Steps:** Trình tự các thao tác người dùng cần thực hiện trên giao diện.
- **Expected Result:** Kết quả kỳ vọng chi tiết theo đặc tả thiết kế và logic nghiệp vụ.
- **Actual Result:** Ghi nhận kết quả thực tế khi tester thao tác (để trống ban đầu).
- **Status:** Trạng thái kiểm thử, **mặc định là `Not Run` cho tất cả các ca kiểm thử chưa thực thi**.
  > **Quy định quan trọng:** Phân loại kỹ thuật (Nhóm 1, 2, 3, 4 ở Mục 9) chỉ phản ánh điều kiện và tính sẵn sàng của code, **hoàn toàn không đồng nghĩa với kết quả đã chạy**. Tester chỉ cập nhật cột Status sau khi thực sự tiến hành kiểm thử:
  - `Not Run`: Trạng thái ban đầu của mọi ca kiểm thử khi chưa chạy.
  - `Pass`: Chỉ ghi nhận khi tester đã thực sự chạy và hành vi thực tế khớp hoàn toàn với Expected Result.
  - `Fail`: Chỉ ghi nhận khi tester đã thực sự chạy nhưng hành vi thực tế không đạt Expected Result (bao gồm trường hợp phát hiện Implementation Gap hoặc lỗi logic/crash).
  - `Blocked`: Chỉ ghi nhận khi môi trường kiểm thử hiện tại của tester không đáp ứng điều kiện tiên quyết (ví dụ tester chỉ có bản build `FakeDfsClient` nhưng ca test đòi hỏi `Metadata Server thật`).
  - `N/A`: Áp dụng cho các màn hình chưa triển khai code (Milestone 3).
- **Notes:** Ghi chú kỹ thuật, phân loại khả năng kiểm thử, và ghi nhận khoảng cách (Implementation Gaps) giữa code và thiết kế.

---

## 2. Checklist Màn Hình Đăng Nhập (Login View)

*Căn cứ thiết kế:* `docs/ui/01-login.md`  
*Mã nguồn triển khai:* `LoginView.axaml`, `LoginViewModel.cs`

| Test ID | Test Scenario | Preconditions | Test Steps | Expected Result | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|---|
| `TC-LOGIN-01` | Kiểm tra bố cục và hiển thị giao diện Login ban đầu | Ứng dụng vừa khởi chạy, chưa có phiên đăng nhập nào.<br>*(Môi trường: FakeDfsClient)* | 1. Mở ứng dụng DFS Client.<br>2. Quan sát cửa sổ đăng nhập. | - Cửa sổ hiển thị tiêu đề và logo hệ thống Distributed File System.<br>- Có đầy đủ các trường nhập: Server Address, Username, Password.<br>- Trường Server Address có giá trị mặc định `127.0.0.1:7000` (theo thiết kế là `192.168.10.11:7000`).<br>- Nút "Đăng nhập" (Connect) hiển thị rõ ràng.<br>- Có nút điền tài khoản mẫu "⚡ Điền tài khoản demo". | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Code mặc định điền sẵn giá trị demo phục vụ phát triển nhanh. |
| `TC-LOGIN-02` | Kiểm tra tính năng ẩn/hiện mật khẩu (Password Masking) | Đang ở màn hình Login.<br>*(Môi trường: FakeDfsClient)* | 1. Nhập ký tự bất kỳ vào ô Password.<br>2. Quan sát hiển thị ký tự (mặc định là dấu chấm tròn `•`).<br>3. Bấm vào nút "👁️ Hiện" (Reveal).<br>4. Bấm lại vào nút "🙈 Ẩn". | - Khi chưa bật Reveal: Các ký tự mật khẩu được che giấu an toàn bằng dấu chấm tròn `•`.<br>- Khi bấm nút "👁️ Hiện": Mật khẩu hiển thị dưới dạng văn bản rõ (plain text) và nút đổi thành "🙈 Ẩn".<br>- Khi bấm lại: Mật khẩu lập tức trở về trạng thái bị che giấu và nút đổi thành "👁️ Hiện". | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Kiểm tra phản hồi tức thời của UI không bị giật lag. |
| `TC-LOGIN-03` | Kiểm tra tính năng tự động điền tài khoản demo | Đang ở màn hình Login.<br>*(Môi trường: FakeDfsClient)* | 1. Xóa các trường nhập liệu hiện tại.<br>2. Nhấn nút "⚡ Điền tài khoản demo".<br>3. Quan sát các ô nhập liệu. | - Trường Server Address giữ nguyên hoặc điền `127.0.0.1:7000`.<br>- Trường Username được tự động điền chính xác `demo`.<br>- Trường Password được tự động điền chính xác `demo`.<br>- Thông báo lỗi (nếu có trước đó) được xóa sạch. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>`FakeDfsClient` hiện tại chỉ hỗ trợ tài khoản `demo` / `demo` với quyền Admin. |
| `TC-LOGIN-04` | Kiểm tra kiểm thực dữ liệu đầu vào (Input Validation) | Đang ở màn hình Login.<br>*(Môi trường: FakeDfsClient)* | 1. Xóa trắng trường Username, nhấn "Đăng nhập".<br>2. Nhập Username, xóa trắng Password, nhấn "Đăng nhập". | - Khi thiếu Username: Hệ thống ngăn gửi request, hiển thị lỗi "Vui lòng nhập tên đăng nhập."<br>- Khi thiếu Password: Hệ thống ngăn gửi request, hiển thị lỗi "Vui lòng nhập mật khẩu."<br>- Không làm treo ứng dụng hay ném exception unhandled. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Kiểm tra logic `string.IsNullOrWhiteSpace(Username)` và `string.IsNullOrEmpty(Password)`. |
| `TC-LOGIN-05` | Kiểm tra trạng thái tải và khóa giao diện khi đang kết nối (Loading State) | Đang ở màn hình Login, đã nhập thông tin.<br>*(Yêu cầu môi trường có độ trễ mạng được kiểm soát hoặc thời gian phản hồi đủ dài)* | 1. Nhấn nút "Đăng nhập".<br>2. Quan sát giao diện trong thời gian chờ phản hồi kết nối. | - Trong quá trình kết nối (`IsBusy = true`): Nút "Đăng nhập" và các trường nhập bị vô hiệu hóa (`IsEnabled = false`).<br>- Hiển thị thanh tiến trình không xác định (`ProgressBar IsIndeterminate="True"`) kèm dòng chữ "Đang kết nối tới máy chủ Metadata...".<br>- Sau khi phản hồi xong, trạng thái bận được giải phóng, giao diện mở khóa trở lại. | | Not Run | `[Phân loại: Cần môi trường có độ trễ được kiểm soát]`<br>Code view đã có đầy đủ binding `IsBusy` và ProgressBar. Tuy nhiên `FakeDfsClient` mặc định phản hồi quá nhanh (< 1ms) để đảm bảo quan sát được Loading Indicator; `Metadata Server thật` chỉ kiểm tra được khi thời gian phản hồi đủ dài (không khẳng định máy chủ thật luôn đủ trễ). Môi trường có độ trễ mạng được kiểm soát là phương án đáng tin cậy nhất. |
| `TC-LOGIN-06` | Kiểm tra xử lý lỗi đăng nhập thất bại (Unauthorized, Forbidden, Unavailable) | Đang ở màn hình Login.<br>*(Yêu cầu Metadata Server thật cho Forbidden & Unavailable)* | 1. Nhập sai mật khẩu (`demo` / `wrongpass`), nhấn Đăng nhập.<br>2. *(Cần Metadata Server thật)* Nhập tài khoản bị khóa (`locked_user` / `locked123`), nhấn Đăng nhập.<br>3. *(Cần Metadata Server thật)* Nhập địa chỉ Server không tồn tại (`127.0.0.1:9999`), nhấn Đăng nhập. | - Trường hợp 1: Hiển thị lỗi "Tên đăng nhập hoặc mật khẩu không chính xác." (Mã: `Unauthorized`).<br>- Trường hợp 2: Hiển thị lỗi "Tài khoản của bạn đã bị Quản trị viên vô hiệu hóa." (Mã: `Forbidden`).<br>- Trường hợp 3: Hiển thị lỗi kết nối "Máy chủ Metadata tại 127.0.0.1:9999 hiện không khả dụng..." (Mã: `Unavailable`). | | Not Run | `[Phân loại: Cần Metadata Server thật]`<br>`FakeDfsClient` chỉ hỗ trợ sinh lỗi `Unauthorized`. Tình huống `Forbidden` và `Unavailable` bắt buộc kiểm thử trên Metadata Server thật. |
| `TC-LOGIN-07` | Kiểm tra đăng nhập thành công và chuyển hướng màn hình chính | Đang ở màn hình Login.<br>*(Môi trường: FakeDfsClient)* | 1. Nhập thông tin tài khoản hợp lệ (`demo` / `demo` trên Fake client, hoặc `admin` / `admin123` trên Server thật).<br>2. Nhấn nút "Đăng nhập". | - Xác thực thành công không xuất hiện thông báo lỗi.<br>- Cửa sổ chuyển hướng sang giao diện làm việc chính (`MainView`).<br>- Tên người dùng và vai trò hiển thị chính xác (ví dụ: `demo (Admin)`).<br>- Tự động kích hoạt tab "Tệp tin" và nạp danh sách tệp ban đầu. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Kiểm tra `MainViewModel.IsLoggedIn = true` và `CurrentPage = FileBrowserVm`. |

---

## 3. Checklist Màn Hình Duyệt Tệp (File Browser View)

*Căn cứ thiết kế:* `docs/ui/02-file-browser.md`  
*Mã nguồn triển khai:* `FileBrowserView.axaml`, `FileBrowserViewModel.cs`

| Test ID | Test Scenario | Preconditions | Test Steps | Expected Result | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|---|
| `TC-FILE-01` | Kiểm tra bố cục hiển thị ban đầu của File Browser | Đăng nhập thành công với tài khoản `demo`. Đang mở tab "Tệp tin".<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát bố cục 3 phần: Cây thư mục bên trái (TreeView), Thanh Breadcrumbs & Toolbar phía trên, Bảng danh sách tệp bên phải.<br>2. Nhấp chuột phải vào một hàng tệp bất kỳ. | - Cây thư mục hiển thị phân cấp từ gốc `/` và `/projects`.<br>- Thanh Breadcrumbs hiển thị đường dẫn hiện tại (ví dụ: `Trang chủ / projects`).<br>- Thanh Toolbar có đầy đủ các nút: Lên cấp, Thư mục mới, Tải xuống, Đổi tên, Di chuyển, Xóa, Làm mới.<br>- **Context Menu chuột phải mở ra đầy đủ các mục:** Mở thư mục, Tải xuống, Đổi tên, Di chuyển, Thuộc tính, Xóa. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>**Đính chính:** Code thực tế tại `FileBrowserView.axaml` **ĐÃ CÓ Context Menu** chuột phải. Nút Upload ở thiết kế được đặt tại Transfer Manager. |
| `TC-FILE-02` | Điều hướng thư mục qua cây thư mục (TreeView Navigation) | Đang ở File Browser, cây thư mục có node `/projects` và `/projects/dfs`.<br>*(Môi trường: FakeDfsClient)* | 1. Nhấp chọn node `dfs` trên cây thư mục bên trái. | - Node `dfs` được chọn làm tiêu điểm.<br>- Danh sách tệp bên phải cập nhật nội dung của thư mục `/projects/dfs`.<br>- Thanh Breadcrumbs cập nhật thành `Trang chủ / projects / dfs`. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>TreeView tự động nạp các thư mục con khi mở rộng. |
| `TC-FILE-03` | Điều hướng qua Breadcrumb Bar | Đang ở thư mục con sâu `/projects/dfs`.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm vào phân đoạn `projects` trên thanh Breadcrumbs. | - Hệ thống điều hướng ngay về thư mục `/projects`.<br>- Danh sách tệp tải lại nội dung của `/projects`.<br>- Breadcrumbs rút ngắn lại tương ứng. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Mỗi nút Breadcrumb gắn liền với `NavigateBreadcrumbCommand`. |
| `TC-FILE-04` | Điều hướng bằng cách nhấp đúp hoặc bấm mở thư mục trong DataGrid | Đang ở thư mục có chứa thư mục con (ví dụ: `/projects`).<br>*(Môi trường: FakeDfsClient)* | 1. Nhấp đúp chuột trái vào hàng thư mục con (hoặc bấm vào tên thư mục). | - Hệ thống mở thư mục con đó.<br>- Danh sách tệp hiển thị nội dung bên trong thư mục vừa mở.<br>- Đường dẫn Breadcrumb tăng thêm 1 cấp. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Nhấp vào tệp tin sẽ kích hoạt lệnh tải xuống thay vì mở thư mục. |
| `TC-FILE-05` | Xử lý hiển thị thư mục trống (Empty Folder State) | Điều hướng đến một thư mục rỗng không có tệp con.<br>*(Môi trường: FakeDfsClient)* | 1. Mở thư mục rỗng.<br>2. Quan sát hiển thị danh sách. | - Bảng dữ liệu không hiển thị hàng nào.<br>- Có watermark thông báo rõ ràng: "Thư mục trống" kèm hướng dẫn tạo mới. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Kiểm tra thuộc tính `IsEmpty => !HasItems && !IsLoading`. |
| `TC-FILE-06` | Tìm kiếm và lọc tệp theo tên (Search/Filter) | Đang ở thư mục có nhiều tệp tin.<br>*(Môi trường: FakeDfsClient)* | 1. Nhập từ khóa tìm kiếm vào ô Search trên thanh công cụ.<br>2. Xóa ô Search. | - Khi nhập từ khóa: Danh sách lập tức lọc chỉ hiển thị các tệp chứa chuỗi tìm kiếm (không phân biệt hoa/thường).<br>- Khi xóa ô tìm kiếm: Danh sách khôi phục đầy đủ toàn bộ tệp trong thư mục. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Lọc in-memory trên `ObservableCollection` qua getter `FilteredItems`. |
| `TC-FILE-07` | Tạo thư mục mới (Create Folder Dialog & Action) | Đang ở File Browser.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "📁+ Thư mục mới" trên Toolbar.<br>2. Nhập tên thư mục: `Tai_Lieu_Moi`, bấm "Tạo mới".<br>3. Thử tạo lại với cùng tên đó. | - Hộp thoại tạo thư mục đóng lại.<br>- Thư mục mới xuất hiện ngay trong danh sách tệp và trên TreeView.<br>- Khi tạo trùng tên: Báo lỗi "Tên thư mục đã tồn tại trong thư mục." (`AlreadyExists`). | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Trên `FakeDfsClient`, thao tác được nuốt `NotSupportedException` và thêm trực tiếp vào danh sách UI. |
| `TC-FILE-08` | Đổi tên tệp hoặc thư mục (Rename Dialog & Action) | Đã chọn một mục trong danh sách tệp.<br>*(Môi trường: FakeDfsClient)* | 1. Chọn 1 mục, bấm nút "✏️ Đổi tên" trên Toolbar (hoặc Context Menu).<br>2. Nhập tên mới hợp lệ, bấm "Lưu thay đổi". | - Tên mới của mục được cập nhật trên bảng danh sách và cây thư mục.<br>- Hiển thị thông báo thành công: "Đã đổi tên... thành công." | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Cập nhật thuộc tính `Name` của `UiFileItem` trên UI. |
| `TC-FILE-09` | Xóa tệp hoặc thư mục (Delete Dialog & Action) | Đã chọn một mục trong danh sách tệp.<br>*(Môi trường: FakeDfsClient)* | 1. Chọn mục cần xóa, bấm nút "🗑️ Xóa" trên Toolbar.<br>2. Hộp thoại xác nhận hiện ra, bấm "Không xóa" (Hủy).<br>3. Bấm lại nút "🗑️ Xóa", bấm "Đồng ý xóa". | - Khi bấm "Không xóa": Mục vẫn còn nguyên vẹn.<br>- Khi bấm "Đồng ý xóa": Mục biến mất khỏi danh sách, hiển thị thông báo đã xóa thành công. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Mục được xóa khỏi `Items` collection của ViewModel. |
| `TC-FILE-10` | Di chuyển tệp hoặc thư mục (Move Dialog & Action) | Đã chọn một mục trong danh sách tệp.<br>*(Môi trường: FakeDfsClient)* | 1. Chọn mục, bấm nút "✂️ Di chuyển" trên Toolbar.<br>2. Nhập đường dẫn thư mục đích (ví dụ: `/projects/dfs`), bấm "Di chuyển". | - Hộp thoại đóng lại, mục được chuyển khỏi thư mục hiện tại.<br>- Hiển thị thông báo: "Đã di chuyển... sang... thành công." | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Thiết kế đề xuất kéo thả (Drag-drop); code hiện triển khai qua hộp thoại nhập đường dẫn đích. |
| `TC-FILE-11` | Xem hộp thoại thuộc tính tệp (File Properties Dialog) | Đã chọn một tệp trong danh sách tệp.<br>*(Môi trường: FakeDfsClient)* | 1. Nhấp chuột phải chọn "ℹ️ Thuộc tính chi tiết..." (hoặc bấm phím tắt).<br>2. Quan sát các thông tin hiển thị. | - Hộp thoại hiển thị đầy đủ: Đường dẫn đầy đủ, Dung lượng đã format, Phiên bản, Số bản sao lưu, Mã băm toàn vẹn SHA-256, Thời gian sửa đổi.<br>- Bấm "Đóng" để đóng hộp thoại. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Dữ liệu trích xuất từ metadata `ItemInfo` của tệp mẫu. |

---

## 4. Checklist Màn Hình Quản Lý Truyền Tải (Transfer Manager View)

*Căn cứ thiết kế:* `docs/ui/03-transfers.md`  
*Mã nguồn triển khai:* `TransferManagerView.axaml`, `TransferManagerViewModel.cs`

| Test ID | Test Scenario | Preconditions | Test Steps | Expected Result | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|---|
| `TC-XFER-01` | Khởi tạo tác vụ Tải lên tệp mới (New Upload Dialog) | Mở tab "Truyền tải" (Transfers).<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "⬆ Tải lên tệp mới..." trên thanh công cụ.<br>2. Nhập/chọn tên tệp và đường dẫn thư mục đích DFS trong dialog.<br>3. Bấm "Bắt đầu tải lên". | - Hộp thoại tải lên đóng lại.<br>- Tác vụ tải lên mới lập tức xuất hiện đầu bảng với hướng Upload (↑), trạng thái `Active`, thanh tiến độ bắt đầu chạy từ 0%. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Kiểm tra việc thêm mới `UiTransferItem` và kích hoạt luồng background. |
| `TC-XFER-02` | Kiểm tra hiển thị tiến độ và tốc độ truyền tải (Progress, Speed và ETA) | Đang có tác vụ truyền tải đang chạy.<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát hàng tác vụ đang truyền tải.<br>2. Theo dõi các chỉ số thời gian thực.<br>3. Kiểm tra hiển thị thời gian còn lại (ETA countdown). | - Thanh ProgressBar cập nhật tỷ lệ phần trăm hoàn thành tăng dần đều (0% đến 100%).<br>- Chỉ số tốc độ truyền (Speed Text) hiển thị rõ ràng và biến thiên hợp lý (ví dụ: `4.5 MB/s`).<br>- **Yêu cầu thiết kế `03-transfers.md`:** Hiển thị thời gian ước tính còn lại (ETA countdown, ví dụ: `00:00:20`). | | Not Run | `[Phân loại: Thao tác được trên Fake nhưng phát hiện Gap]`<br>**Phân định rõ ràng giữa hành vi thực tế và yêu cầu thiết kế:**<br>- Trên `FakeDfsClient`, tester có thể kiểm tra và xác nhận đầy đủ hai chỉ số: **Tiến độ (% Progress)** và **Tốc độ truyền (Speed)**.<br>- **Ghi nhận Implementation Gap:** Bộ đếm thời gian còn lại (ETA countdown) là yêu cầu thiết kế trong `03-transfers.md` nhưng ViewModel hiện tại chưa hỗ trợ bộ đếm ETA riêng.<br>- Khi tester đối chiếu với toàn bộ tiêu chí thiết kế (bao gồm cả ETA), ca kiểm thử này sẽ ghi nhận `Fail` cho phần ETA để theo dõi bổ sung gap. |
| `TC-XFER-03` | Chi tiết tiến độ từng Chunk (Chunk-by-chunk Expander) | Đang có tác vụ truyền tải gồm nhiều chunk.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút mở rộng Expander "Chi tiết phân tán các Chunk 4 MB..." tại hàng tác vụ.<br>2. Quan sát danh sách các chunk con bên dưới. | - Khung hiển thị mở rộng danh sách chi tiết từng chunk:<br>  + Thứ tự Chunk (Index: 0, 1, 2...)<br>  + Dung lượng Chunk (4.0 MB)<br>  + Tên Data Server tiếp nhận (ví dụ: `DS-1`, `DS-2`)<br>  + Tiến độ % và trạng thái từng chunk (`100% (SHA-256 Verified)` hoặc `Đang truyền`). | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Khớp đặc tả kỹ thuật phân mảnh 4 MB và lưu trữ đa node trong kiến trúc hệ thống. |
| `TC-XFER-04` | Tính năng Tạm dừng/Hủy bỏ truyền tải (Cancel Transfer) | Đang có tác vụ ở trạng thái `Active` (đang truyền).<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "✕ Hủy tác vụ" tại hàng tác vụ đang chạy.<br>2. Quan sát phản ứng của tác vụ. | - Tác vụ dừng ngay lập tức thông qua kích hoạt `CancellationTokenSource`.<br>- Trạng thái chuyển thành "Đã hủy bởi người dùng" (`IsCancelled = true`).<br>- Tốc độ truyền về `0 MB/s`, nút Hủy chuyển thành nút Thử lại (↻). | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Kiểm tra token hủy hoạt động chuẩn xác trong vòng lặp truyền tải. |
| `TC-XFER-05` | Tính năng Thử lại khi truyền tải bị gián đoạn (Retry Transfer) | Có tác vụ đang ở trạng thái lỗi (`Failed`) hoặc đã hủy (`Cancelled`).<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "↻ Thử lại" tại hàng tác vụ.<br>2. Quan sát tiến trình phục hồi. | - Tác vụ chuyển lại sang trạng thái `Active`.<br>- Hệ thống tái thiết lập kết nối và chạy lại tiến trình truyền tải từ đầu đến khi hoàn tất 100%. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>`RetryTransferCommand` gọi `StartTransferAsync(item, simulateFailure: false)`. |
| `TC-XFER-06` | Kiểm tra các nút lọc trạng thái (All, Active, Completed) | Danh sách có đồng thời tác vụ đang chạy và tác vụ hoàn tất.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm chọn nút lọc "Tất cả".<br>2. Bấm chọn nút lọc "Đang truyền".<br>3. Bấm chọn nút lọc "Hoàn tất". | - Nút "Tất cả": Hiển thị toàn bộ tác vụ.<br>- Nút "Đang truyền": Chỉ hiển thị các tác vụ có `IsActive = true`.<br>- Nút "Hoàn tất": Chỉ hiển thị các tác vụ có `IsCompleted = true`. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Thiết kế có thêm tab "Lỗi"; code hiện gom tác vụ lỗi vào view chung hoặc filter riêng. |
| `TC-XFER-07` | Kiểm tra nút Xóa lịch sử hoàn tất (Clear Completed) | Có ít nhất 1 tác vụ ở trạng thái `Completed` hoặc `Cancelled`.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "🧹 Xóa hoàn tất" trên thanh công cụ.<br>2. Quan sát danh sách tác vụ. | - Toàn bộ các tác vụ đã hoàn tất hoặc đã hủy bị xóa khỏi bảng hiển thị.<br>- Các tác vụ đang hoạt động (`IsActive = true`) vẫn được giữ nguyên vẹn. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Làm sạch danh sách giao diện, giải phóng bộ nhớ client. |
| `TC-XFER-08` | Kiểm tra giả lập sự cố máy chủ mất kết nối (Simulate Unavailable) | Đang ở tab Transfer Manager.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "⚡ Mô phỏng lỗi Unavailable" trên thanh công cụ.<br>2. Quan sát phản hồi của Transfer Manager. | - Hiển thị banner cảnh báo: "⚠️ Data Server 3 (192.168.10.13:7001) mất kết nối mạng LAN (Unavailable)..."<br>- Xuất hiện tác vụ lỗi mới với trạng thái "Lỗi: Data Server không khả dụng", hiển thị chunk 1 bị ngắt kết nối.<br>- Nút "↻ Thử lại" sẵn sàng cho phép phục hồi. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Tính năng tích hợp trong `TransferManagerViewModel` để kiểm chứng kịch bản T-04. |
| `TC-XFER-09` | Kích hoạt Download từ File Browser chuyển sang Transfer Manager | Đang ở File Browser, chọn 1 tệp tin.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "⬇ Tải xuống" trên File Browser.<br>2. Quan sát thông báo và chuyển sang tab Transfer Manager.<br>3. Kiểm tra tệp tin tải về trên ổ cứng máy cục bộ. | - File Browser hiển thị thông báo "Đã xếp tệp... vào Hàng đợi truyền tải (Download)."<br>- Trong Transfer Manager: Xuất hiện tác vụ Download mới (icon ↓), tiến độ tăng dần đến 100%.<br>- **Yêu cầu thiết kế:** Mở SaveFileDialog chọn vị trí lưu và ghi file vật lý hoàn chỉnh xuống ổ cứng.<br>*(Lưu ý: Hiện tại code chỉ mô phỏng in-memory, chưa ghi file thật - xem Notes).* | | Not Run | `[Phân loại: Thao tác được trên Fake nhưng phát hiện Gap]`<br>**Ghi nhận Gap:** Tác vụ xếp hàng chuyển giao diện đã hoạt động; tính năng ghi file vật lý xuống đĩa cục bộ thuộc milestone sau. Giữ nguyên Expected Result theo thiết kế để theo dõi gap. |

---

## 5. Checklist Màn Hình Quản Trị Hệ Thống (Admin Panel View)

*Căn cứ thiết kế:* `docs/ui/06-admin-panel.md`  
*Mã nguồn triển khai:* `AdminPanelView.axaml`, `AdminPanelViewModel.cs`

| Test ID | Test Scenario | Preconditions | Test Steps | Expected Result | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|---|
| `TC-ADMIN-01` | Phân quyền hiển thị tab Admin Panel theo vai trò (RBAC Visibility) | Ứng dụng ở màn hình chính.<br>*(Yêu cầu Metadata Server thật để test Non-Admin)* | 1. Đăng nhập với tài khoản người dùng thường (`alice` / `alice123`). Quan sát menu điều hướng.<br>2. Đăng xuất và đăng nhập lại với tài khoản quản trị (`demo` hoặc `admin`). Quan sát menu điều hướng. | - Với người dùng thường (`IsAdmin = false`): Tab "Quản trị" hoàn toàn ẩn khỏi thanh điều hướng.<br>- Với quản trị viên (`IsAdmin = true`): Tab "Quản trị" hiển thị nổi bật và truy cập bình thường. | | Not Run | `[Phân loại: Cần Metadata Server thật]`<br>Trên `FakeDfsClient`, tài khoản `demo` luôn có `IsAdmin = true`. Kiểm tra ẩn tab cần Server thật hoặc Fake client có Non-Admin. |
| `TC-ADMIN-02` | Quản lý Người dùng: Hiển thị danh sách và tìm kiếm User | Đang ở tab "Quản trị", chọn tab con "Người dùng" (Users).<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát danh sách người dùng hiển thị.<br>2. Nhập từ khóa `tan` vào ô tìm kiếm người dùng.<br>3. Xóa ô tìm kiếm. | - Bảng hiển thị danh sách người dùng với các cột: ID, Username, Trạng thái (Hoạt động/Khóa), Nhóm thuộc về.<br>- Khi tìm `tan`: Bảng lọc chỉ còn người dùng `tan.phan`.<br>- Khi xóa tìm kiếm: Khôi phục đầy đủ toàn bộ người dùng trong danh sách. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Dữ liệu mẫu nạp từ `LoadFallbackSamples()` trong ViewModel. |
| `TC-ADMIN-03` | Quản lý Người dùng: Thêm tài khoản người dùng mới (Create User) | Đang ở tab "Người dùng".<br>*(Yêu cầu Metadata Server thật để xác minh đăng nhập)* | 1. Bấm nút "➕ Tạo người dùng mới".<br>2. Nhập Username: `tester1`, Password: `Pass123!`, Nhóm: `Team A`.<br>3. Bấm "Tạo tài khoản".<br>4. Đăng xuất và thử đăng nhập lại bằng `tester1` với mật khẩu vừa tạo. | - Người dùng `tester1` xuất hiện ngay trong danh sách người dùng UI, có thông báo thành công.<br>- **Yêu cầu thiết kế:** Tài khoản mới được ghi nhận vào hệ thống xác thực và đăng nhập thành công.<br>*(Lưu ý: Trên FakeDfsClient, user mới chỉ lưu in-memory trên UI, không lưu vào DB xác thực - xem Notes).* | | Not Run | `[Phân loại: Cần Metadata Server thật]`<br>Thao tác UI chạy được trên `FakeDfsClient`. Nhưng bước kiểm tra tính bền vững và đăng nhập lại bắt buộc cần Metadata Server thật. |
| `TC-ADMIN-04` | Quản lý Người dùng: Khóa và Mở khóa tài khoản (Lock / Unlock User) | Đang ở tab "Người dùng", chọn một tài khoản.<br>*(Yêu cầu Metadata Server thật để xác minh chặn đăng nhập)* | 1. Chọn tài khoản `an.nguyen`, bấm nút "Khóa tài khoản".<br>2. Quan sát badge trạng thái của tài khoản.<br>3. Bấm lại nút "Mở khóa".<br>4. Khi tài khoản đang ở trạng thái Khóa: Mở phiên khác, đăng nhập bằng tài khoản bị khóa. | - Bước 1-2: Trạng thái đổi thành "Bị khóa" (Badge Đỏ), nút chuyển thành "Mở khóa".<br>- Bước 3: Trạng thái trở lại "Hoạt động" (Badge Xanh), nút chuyển thành "Khóa tài khoản".<br>- **Yêu cầu thiết kế:** Đăng nhập bằng tài khoản bị khóa bị từ chối với mã lỗi `Forbidden`.<br>*(Lưu ý: FakeDfsClient không kiểm tra trạng thái khóa khi login - xem Notes).* | | Not Run | `[Phân loại: Cần Metadata Server thật]`<br>Toggle UI chạy được trên `FakeDfsClient`. Nhưng bước kiểm tra từ chối đăng nhập với `Forbidden` bắt buộc cần Metadata Server thật. |
| `TC-ADMIN-05` | Quản lý Người dùng: Đổi/Đặt lại mật khẩu người dùng (Reset Password) | Đang ở tab "Người dùng", chọn một tài khoản.<br>*(Yêu cầu Metadata Server thật để xác minh mật khẩu mới)* | 1. Chọn tài khoản, bấm nút "Đổi mật khẩu".<br>2. Nhập mật khẩu mới: `NewPass123!`, bấm "Lưu mật khẩu".<br>3. Đăng xuất và đăng nhập lại bằng tài khoản đó với mật khẩu mới. | - Bước 1-2: Hộp thoại đóng lại, hiển thị thông báo "Đã đổi mật khẩu cho... thành công."<br>- **Yêu cầu thiết kế:** Xác thực thành công với mật khẩu mới; mật khẩu cũ hết hiệu lực.<br>*(Lưu ý: FakeDfsClient không lưu mật khẩu mới vào DB xác thực - xem Notes).* | | Not Run | `[Phân loại: Cần Metadata Server thật]`<br>Thao tác UI chạy được trên `FakeDfsClient`. Nhưng bước kiểm tra hiệu lực mật khẩu mới bắt buộc cần Metadata Server thật. |
| `TC-ADMIN-06` | Quản lý Nhóm: Hiển thị danh sách nhóm và tìm kiếm Group | Đang ở tab "Quản trị", chọn tab con "Nhóm quyền" (Groups).<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát danh sách nhóm người dùng bên trái.<br>2. Nhập từ khóa `Team` vào ô tìm kiếm nhóm. | - Bảng hiển thị các cột: Mã nhóm, Tên nhóm, Số lượng thành viên.<br>- Tìm kiếm nhóm lọc chính xác theo từ khóa nhập vào (ví dụ chỉ hiện `Team A`). | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Các nhóm mẫu ban đầu: `Team A`, `Guests`, `Auditors`. |
| `TC-ADMIN-07` | Quản lý Nhóm: Tạo nhóm mới (Create Group) | Đang ở tab "Nhóm quyền".<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "➕ Tạo nhóm mới".<br>2. Nhập Tên nhóm: `qa_team`, bấm "Tạo nhóm". | - Nhóm `qa_team` xuất hiện ngay trong danh sách với số thành viên ban đầu là `0 thành viên`.<br>- Hiển thị thông báo: "Đã tạo nhóm 'qa_team' thành công." | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Thêm mới `UiGroupItem` vào `Groups` collection của ViewModel. |
| `TC-ADMIN-08` | Quản lý Nhóm: Thêm và Xóa thành viên khỏi nhóm (Group Members) | Đang ở tab "Nhóm quyền", chọn nhóm `qa_team`.<br>*(Môi trường: FakeDfsClient)* | 1. Chọn nhóm `qa_team`, bấm nút "➕ Thêm user" bên danh sách thành viên.<br>2. Nhập username `tan.phan`, bấm "Thêm vào nhóm".<br>3. Chọn `tan.phan` vừa thêm, bấm nút "Xóa khỏi nhóm". | - Bước 2: `tan.phan` xuất hiện trong bảng thành viên, số lượng thành viên tăng lên 1.<br>- Bước 3: `tan.phan` bị xóa khỏi danh sách thành viên, số lượng thành viên giảm về 0.<br>- Có thông báo trạng thái tương ứng cho từng thao tác. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Cập nhật trực tiếp `group.Members` và `MemberCountText`. |
| `TC-ADMIN-09` | Phân quyền (Grants): Hiển thị và tạo quyền mới (Create Grant) | Đang ở tab "Quản trị", chọn tab con "Cấp quyền" (Grants RBAC).<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát danh sách các quyền hiện tại.<br>2. Bấm "➕ Cấp quyền mới (Grant)".<br>3. Nhập Đường dẫn: `/projects/test`, Đối tượng: `Team A` (Group), Vai trò: `Editor`.<br>4. Bấm "Xác nhận cấp quyền". | - Danh sách quyền cập nhật thêm mục mới với đầy đủ thông tin: Mã quyền, Vai trò (`Editor`), Đối tượng (`Group: Team A`), Đường dẫn, Trạng thái hoạt động, Nguồn gốc (`Trực tiếp`).<br>- Thông báo thành công hiển thị rõ ràng. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Tạo `UiGrantItem` in-memory; kiểm soát quyền động trên File Browser yêu cầu Server thật. |
| `TC-ADMIN-10` | Phân quyền (Grants): Phân biệt quyền Direct vs Inherited; Thu hồi quyền | Đang ở tab "Cấp quyền" (Grants RBAC).<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát cột "Nguồn gốc" của các quyền trong bảng.<br>2. Chọn một quyền Kế thừa (ví dụ: `Kế thừa từ /projects`), quan sát nút "Thu hồi quyền".<br>3. Chọn một quyền Trực tiếp (`Trực tiếp`), bấm "Thu hồi quyền". | - Cột Nguồn gốc phân biệt rõ quyền Trực tiếp và quyền Kế thừa.<br>- **Yêu cầu thiết kế `06-admin-panel.md`:** Quyền Kế thừa phải bị vô hiệu hóa nút Thu hồi (hoặc cảnh báo phải thu hồi tại thư mục gốc). Quyền Trực tiếp thu hồi thành công và biến mất khỏi danh sách.<br>*(Lưu ý: Hiện tại code chưa disable nút Thu hồi của quyền kế thừa - xem Notes).* | | Not Run | `[Phân loại: Thao tác được trên Fake nhưng phát hiện Gap]`<br>**Ghi nhận Gap:** `AdminPanelView.axaml` chưa chặn nút "Thu hồi quyền" cho quyền kế thừa. Giữ nguyên Expected Result theo thiết kế để theo dõi gap. |

---

## 6. Checklist Màn Hình Trạng Thái Cụm (Cluster Status View)

*Căn cứ thiết kế:* `docs/ui/07-cluster-status.md`  
*Mã nguồn triển khai:* `ClusterStatusView.axaml`, `ClusterStatusViewModel.cs`

| Test ID | Test Scenario | Preconditions | Test Steps | Expected Result | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|---|
| `TC-CLUSTER-01` | Kiểm tra danh sách Storage Nodes trong cụm | Đăng nhập tài khoản `demo`, mở tab "Cụm máy chủ" (Cluster Status).<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát bảng danh sách Storage Nodes.<br>2. Kiểm tra các cột thông tin của từng node. | - Bảng hiển thị đầy đủ danh sách 5 Storage Nodes (`ds-demo`, `ds-laptop-02` đến `05`).<br>- Các cột thông tin hiển thị chính xác:<br>  + Tên Node (Node ID)<br>  + Địa chỉ IP & Cổng (ví dụ: `192.168.1.102:7001`)<br>  + Trạng thái (Badge Xanh/Đỏ)<br>  + Dung lượng tổng (FormattedCapacity)<br>  + Dung lượng trống (FormattedFree)<br>  + Số lượng khối dữ liệu (FormattedChunks). | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>ViewModel tự động bổ sung danh sách 5 node mẫu theo đúng thiết kế Stage 1 khi chạy với Fake. |
| `TC-CLUSTER-02` | Hiển thị Badge trạng thái Node (Healthy vs Dead) | Đang ở màn hình Cluster Status.<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát màu sắc và chữ trên Badge trạng thái của từng node. | - Các node online (`Alive`): Hiển thị Badge Xanh lá (`#28A745`) với chữ "Healthy (Online)".<br>- Node offline (`Dead` - ví dụ `ds-laptop-05`): Hiển thị Badge Đỏ (`#DC3545`) với chữ "Dead (Offline)". | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Màu sắc và trạng thái được mapping qua thuộc tính `StatusText` và `StatusColor` của `UiNodeItem`. |
| `TC-CLUSTER-03` | Tính năng Làm mới thủ công (Manual Refresh) | Đang ở màn hình Cluster Status.<br>*(Môi trường: FakeDfsClient)* | 1. Bấm nút "🔄 Làm mới" trên thanh công cụ.<br>2. Quan sát hiệu ứng và dữ liệu cập nhật. | - Nút Làm mới kích hoạt `LoadClusterStatusAsync()`.<br>- Bảng dữ liệu cập nhật lại thông tin trạng thái cụm mà không gây giật lag hay treo UI.<br>- Thanh tổng quan `ClusterOverview` hiển thị tóm tắt: "Cụm 5 Data Server (4 Online, 1 Offline)..." | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Đảm bảo phương thức async chạy trơn tru trên UI thread. |
| `TC-CLUSTER-04` | Cảnh báo dung lượng lưu trữ của từng Node (ProgressBar thresholds) | Đang ở màn hình Cluster Status.<br>*(Môi trường: FakeDfsClient)* | 1. Quan sát hiển thị dung lượng của từng node.<br>2. Kiểm tra thanh dung lượng (ProgressBar) đổi màu cảnh báo theo ngưỡng (<80% xanh, 80-90% vàng, >90% đỏ). | - **Yêu cầu thiết kế `07-cluster-status.md`:** Mỗi node có thanh ProgressBar đổi màu cảnh báo khi dung lượng sử dụng đạt mức cao.<br>*(Lưu ý: Hiện tại code chưa triển khai thanh ProgressBar đổi màu - xem Notes).* | | Not Run | `[Phân loại: Thao tác được trên Fake nhưng phát hiện Gap]`<br>**Ghi nhận Gap:** `ClusterStatusView.axaml` hiện chỉ hiển thị text `FormattedCapacity` và `FormattedFree`, chưa có thanh ProgressBar đổi màu. Giữ nguyên Expected Result theo thiết kế để theo dõi gap. |
| `TC-CLUSTER-05` | Đối chiếu các tính năng thiết kế chưa có trong code (KPI Cards & Auto-refresh) | Đang ở màn hình Cluster Status.<br>*(Môi trường: FakeDfsClient)* | 1. Kiểm tra sự hiện diện của 4 thẻ KPI tóm tắt trên đầu trang (Tổng dung lượng, Đã dùng, Số Chunk, Số Node hoạt động).<br>2. Kiểm tra tùy chọn tự động làm mới chu kỳ 3 giây (Auto-refresh checkbox). | - Xác nhận hiện trạng mã nguồn:<br>  + Chưa có 4 thẻ KPI tách rời (hiện gom thành dòng text tóm tắt `ClusterOverview`).<br>  + Chưa có timer tự động làm mới mỗi 3s (chỉ có nút Làm mới thủ công).<br>- Ghi nhận các điểm chênh lệch vào báo cáo nghiệm thu. | | Not Run | `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`<br>Test case đối chiếu kiểm tra độ khớp với thiết kế. |

---

## 7. Kịch Bản Kiểm Thử Tích Hợp Đầu Cuối (Cross-Screen / E2E Scenarios)

*Mục tiêu:* Kiểm tra luồng tương tác liên màn hình trong các tình huống thực tế của người dùng.

### Scenario 1: Đăng nhập → Duyệt thư mục → Tải tệp xuống → Theo dõi tiến độ Transfer
- **Mã kịch bản:** `TC-E2E-01`
- **Mục đích:** Đảm bảo luồng duyệt và tải tệp diễn ra liền mạch trên giao diện.
- **Môi trường:** `FakeDfsClient` (Dùng tài khoản `demo` / `demo`, Download mô phỏng in-memory).

| Bước | Thao tác | Kết quả kỳ vọng | Kết quả thực tế | Trạng thái |
|---|---|---|---|---|
| 1 | Khởi động ứng dụng, nhập `demo` / `demo`, bấm Đăng nhập. | Đăng nhập thành công, chuyển hướng vào màn hình File Browser, thanh tiêu đề hiện `demo (Admin)`. | | Not Run |
| 2 | Điều hướng vào thư mục `/projects/dfs`. | Danh sách tệp trong thư mục hiển thị đầy đủ (ví dụ: `presentation_slides_v2.pptx`). | | Not Run |
| 3 | Chọn một tệp tin, bấm nút "⬇ Tải xuống" (hoặc nhấp chuột phải chọn Tải xuống). | Ứng dụng hiển thị thông báo "Đã xếp tệp vào Hàng đợi truyền tải (Download)". | | Not Run |
| 4 | Chuyển sang tab "Truyền tải" (Transfer Manager). | Xuất hiện tác vụ tải xuống với hướng mũi tên (↓), tiến độ % tăng dần, tốc độ hiển thị > 0 MB/s. | | Not Run |
| 5 | Mở rộng chi tiết Expander của tác vụ. | Thấy rõ các chunk 4 MB đang được truyền tải song song. | | Not Run |
| 6 | Chờ tác vụ hoàn thành (100% `Completed`). | Tác vụ hoàn tất thành công, trạng thái báo "Hoàn tất (Mã băm SHA-256 đã xác minh)".<br>*(Lưu ý: Download mô phỏng in-memory, không ghi file vật lý).* | | Not Run |

*Ghi chú phân loại TC-E2E-01:* `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`.

---

### Scenario 2: Admin tạo User & Phân quyền → User mới đăng nhập → Xác minh quyền truy cập
- **Mã kịch bản:** `TC-E2E-02`
- **Mục đích:** Kiểm chứng tính toàn vẹn của chu trình quản trị người dùng và kiểm soát truy cập RBAC động.
- **Môi trường:** **Yêu cầu Metadata Server thật** (Cụm phân tán có backend xác thực và lưu trữ phân quyền).

| Bước | Thao tác | Kết quả kỳ vọng | Kết quả thực tế | Trạng thái |
|---|---|---|---|---|
| 1 | Đăng nhập với tài khoản quản trị `admin`, truy cập vào tab "Quản trị" (Admin Panel). | Màn hình Admin hiển thị đầy đủ 3 tab: Người dùng, Nhóm quyền, Cấp quyền. | | Not Run |
| 2 | Tại tab "Người dùng", tạo user mới: `carol` với mật khẩu `Carol123!`, không cấp quyền Admin. | Người dùng `carol` được tạo thành công trên Metadata Server. | | Not Run |
| 3 | Tại tab "Cấp quyền", tạo quyền mới: Gán quyền `ReadWrite` cho user `carol` tại `/projects/alpha`. | Quyền mới được ghi nhận trong cơ sở dữ liệu phân quyền. | | Not Run |
| 4 | Bấm nút "🚪 Thoát" để đăng xuất tài khoản `admin`. | Đăng xuất thành công, ứng dụng trở về màn hình Login. | | Not Run |
| 5 | Đăng nhập với tài khoản mới `carol` / `Carol123!`. | Đăng nhập thành công, giao diện **hoàn toàn ẩn tab Admin Panel**. | | Not Run |
| 6 | Trong File Browser, điều hướng đến `/projects/alpha`. | Thư mục mở bình thường, `carol` có thể thao tác tệp tin tại đây. | | Not Run |
| 7 | Điều hướng sang thư mục khác không được cấp quyền (ví dụ: `/finance/salaries`). | Hệ thống chặn truy cập và hiển thị thông báo lỗi "Bạn không có quyền..." (`Forbidden`). | | Not Run |

*Ghi chú phân loại TC-E2E-02:* `[Phân loại: Cần Metadata Server thật]`. Khi kiểm thử trên bản build mặc định dùng `FakeDfsClient`, tester đánh dấu kịch bản này là `Blocked` do Fake client không lưu trữ tài khoản và không phân quyền động.

---

### Scenario 3: Tải lên tệp lớn → Giả lập mất kết nối / Node Dead → Thử lại và phục hồi
- **Mã kịch bản:** `TC-E2E-03`
- **Mục đích:** Kiểm thử khả năng chịu lỗi và phục hồi Retry của client khi gặp sự cố mạng.
- **Môi trường:** `FakeDfsClient` (sử dụng tính năng mô phỏng sự cố tích hợp sẵn).

| Bước | Thao tác | Kết quả kỳ vọng | Kết quả thực tế | Trạng thái |
|---|---|---|---|---|
| 1 | Đăng nhập tài khoản `demo`, mở tab Transfer Manager. | Màn hình Transfer Manager sẵn sàng. | | Not Run |
| 2 | Bấm "⬆ Tải lên tệp mới...", nhập thông tin và bấm Bắt đầu tải lên. | Tác vụ bắt đầu truyền tải, thanh tiến độ bắt đầu tăng. | | Not Run |
| 3 | Bấm nút "⚡ Mô phỏng lỗi Unavailable" trên thanh công cụ. | Hiển thị banner cảnh báo ngắt kết nối; tác vụ lỗi xuất hiện với trạng thái mất kết nối Data Server. | | Not Run |
| 4 | Vào tab Cluster Status kiểm tra trạng thái cụm. | Thấy node `ds-laptop-05` hiển thị Badge Đỏ "Dead (Offline)". | | Not Run |
| 5 | Quay lại Transfer Manager, bấm nút "↻ Thử lại" tại tác vụ lỗi. | Hệ thống khởi động lại tiến trình truyền tải, tiến độ tiếp tục chạy lại từ đầu. | | Not Run |
| 6 | Chờ tác vụ hoàn thành 100%. | Tác vụ chuyển sang trạng thái `Completed`, mã băm SHA-256 được xác minh đầy đủ. | | Not Run |

*Ghi chú phân loại TC-E2E-03:* `[Phân loại: Có thể kiểm thử đầy đủ trên Fake]`.

---

### Scenario 4: Đăng xuất → Xóa phiên làm việc và bảo mật State → Trở về màn hình Login
- **Mã kịch bản:** `TC-E2E-04`
- **Mục đích:** Đảm bảo khi người dùng đăng xuất, toàn bộ thông tin phiên làm việc được giải phóng an toàn.
- **Môi trường:** `FakeDfsClient` & `Metadata Server`.

| Bước | Thao tác | Kết quả kỳ vọng | Kết quả thực tế | Trạng thái |
|---|---|---|---|---|
| 1 | Người dùng đang làm việc bình thường ở bất kỳ màn hình nào. | Dữ liệu đang hiển thị bình thường. | | Not Run |
| 2 | Bấm nút "🚪 Thoát" ở góc trên bên phải thanh điều hướng. | - Phiên làm việc kết thúc (`IsLoggedIn = false`, `CurrentUser = null`, `IsAdmin = false`).<br>- Giao diện chuyển hướng ngay lập tức về màn hình Login.<br>- Nhấn phím tắt hoặc thao tác khác không thể quay lại màn hình làm việc trước đó.<br>- **Yêu cầu thiết kế:** Các trường Username/Password bị xóa trắng để đảm bảo an toàn.<br>*(Lưu ý: Hiện tại code chưa xóa trắng text Username/Password trên form Login - xem Notes).* | | Not Run |

*Ghi chú phân loại TC-E2E-04:* `[Phân loại: Thao tác được trên Fake nhưng phát hiện Gap]`. `MainViewModel.cs` hiện chưa xóa trắng trường `Username` và `Password` trong `LoginViewModel` khi logout (vẫn giữ nguyên `demo`/`demo`). Giữ nguyên kỳ vọng thiết kế để theo dõi gap.

---

## 8. Danh Mục Kiểm Thử Chờ Triển Khai (Pending Implementation)

> **Lưu ý quan trọng:** Hai màn hình dưới đây thuộc phạm vi Milestone 3 (`M3-06` và `M3-08`), hiện đã có tài liệu thiết kế chi tiết tại `docs/ui/` nhưng **chưa có mã nguồn giao diện (XAML/ViewModel)** trên các nhánh feature. Các ca kiểm thử dưới đây được xây dựng sẵn từ bản vẽ thiết kế nhằm phục vụ nghiệm thu ngay khi code được triển khai. **Không đánh dấu Pass/Fail hay thực thi cho các mục này tại thời điểm hiện tại (trạng thái N/A hoặc Not Run).**

### 8.1 Hộp thoại Lịch sử Phiên bản (Version History Dialog - M3-06)
*Căn cứ thiết kế:* `docs/ui/04-version-history.md` (Feature ID: `FR-06`)  
*Trạng thái mã nguồn:* `Pending Implementation`

| Test ID | Test Scenario | Preconditions | Expected Test Criteria (Dựa trên thiết kế `04-version-history.md`) | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|
| `TC-PEND-VH-01` | Mở hộp thoại Lịch sử phiên bản từ File Browser | Chọn 1 tệp tin trong File Browser có nhiều phiên bản lưu trữ. | - Nhấp chuột phải chọn "Lịch sử phiên bản" hoặc bấm nút trên Toolbar.<br>- Hộp thoại hiển thị tiêu đề kèm tên tệp (ví dụ: `Lịch sử phiên bản: design_draft.psd`).<br>- Danh sách hiển thị các version từ mới nhất đến cũ nhất (`v3 (Hiện tại)`, `v2`, `v1`). | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Chưa có nút gọi hộp thoại trên File Browser. |
| `TC-PEND-VH-02` | Kiểm tra thông tin chi tiết từng phiên bản | Đang mở hộp thoại Version History. | - Bảng danh sách phiên bản hiển thị đầy đủ:<br>  + Số phiên bản (`Version Number`)<br>  + Người cập nhật (`Modified By`)<br>  + Thời gian cập nhật (`Timestamp`)<br>  + Dung lượng phiên bản (`Size`)<br>  + Mã băm toàn vẹn (`SHA-256 Checksum`)<br>  + Ghi chú thay đổi (`Commit/Change Note` nếu có). | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Chờ triển khai ViewModel và Model Versioning. |
| `TC-PEND-VH-03` | Tải xuống một phiên bản cũ (Download Historical Version) | Đang chọn một phiên bản cũ (ví dụ: `v1`) trong danh sách. | - Bấm nút "Tải phiên bản này" (Download).<br>- Tệp phiên bản `v1` được tải về máy mà không ghi đè lên phiên bản hiện tại trên server.<br>- Tên tệp tải về có hậu tố nhận diện (ví dụ: `design_draft_v1.psd`). | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Cần tích hợp với Transfer Manager. |
| `TC-PEND-VH-04` | Khôi phục phiên bản cũ thành phiên bản hiện tại (Rollback Version) | Đang chọn một phiên bản cũ (ví dụ: `v2`). | - Bấm nút "Khôi phục phiên bản này" (Restore / Rollback).<br>- Có cảnh báo xác nhận khôi phục.<br>- Sau khi xác nhận, hệ thống tạo ra một phiên bản mới `v4` có nội dung giống hệt `v2`.<br>- Danh sách phiên bản cập nhật lại ngay lập tức. | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Logic tạo oplog `RESTORE_VERSION` phía Metadata Server. |

---

### 8.2 Hộp thoại Xử lý Xung đột Ghi (Conflict Resolution Dialog - M3-08)
*Căn cứ thiết kế:* `docs/ui/05-conflict-dialog.md` (Feature ID: `FR-07`)  
*Trạng thái mã nguồn:* `Pending Implementation`

| Test ID | Test Scenario | Preconditions | Expected Test Criteria (Dựa trên thiết kế `05-conflict-dialog.md`) | Actual Result | Status | Notes |
|---|---|---|---|---|---|---|
| `TC-PEND-CF-01` | Kích hoạt hiển thị hộp thoại Xung đột khi xảy ra Concurrent Write | Người dùng tải lên tệp tin bị xung đột phiên bản với tệp đang có trên máy chủ. | - Hệ thống phát hiện xung đột (`VersionConflictException`).<br>- Hộp thoại Xung đột tệp bật lên với cảnh báo trực quan: "Phát hiện xung đột phiên bản tệp".<br>- Hiển thị đường dẫn tệp đang bị xung đột. | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Kích hoạt khi metadata trả về mã lỗi `Conflict`. |
| `TC-PEND-CF-02` | So sánh trực quan thông tin Bản Cục Bộ vs Bản Máy Chủ | Đang ở hộp thoại Conflict Dialog. | - Bố cục so sánh 2 cột song song (Side-by-side):<br>  + Cột trái: Tệp của bạn (Bản cục bộ - Dung lượng, Thời gian sửa, SHA-256).<br>  + Cột phải: Tệp trên máy chủ (Bản remote - Người sửa gần nhất, Thời gian sửa, SHA-256).<br>- Điểm khác biệt (dung lượng, thời gian) được làm nổi bật (highlight). | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Theo mockup chuẩn của `05-conflict-dialog.md`. |
| `TC-PEND-CF-03` | Lựa chọn giải quyết: Giữ bản cục bộ hoặc Giữ bản máy chủ | Đang ở hộp thoại Conflict Dialog. | - Chọn "Ghi đè bằng tệp của tôi" (Keep Mine): Bản cục bộ được đẩy lên, tạo phiên bản mới trên server.<br>- Chọn "Giữ lại tệp trên máy chủ" (Keep Server): Hủy bỏ tải lên cục bộ, tải bản remote về thay thế.<br>- Quá trình giải quyết diễn ra an toàn, không làm hỏng dữ liệu. | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Hỗ trợ giải quyết xung đột thủ công. |
| `TC-PEND-CF-04` | Lựa chọn giải quyết: Lưu cả hai bản (Save Both / Fork as Copy) | Đang ở hộp thoại Conflict Dialog. | - Chọn "Lưu cả hai bản" (Keep Both).<br>- Tệp cục bộ được tải lên với tên mới có hậu tố đánh dấu xung đột (ví dụ: `report (conflicted copy 2026-11-11).docx`).<br>- Tệp gốc trên máy chủ giữ nguyên vẹn.<br>- Cả hai tệp đều xuất hiện trong File Browser sau khi xử lý xong. | *(Chờ code)* | Not Run | `[Phân loại: Pending M3]`<br>Giải pháp tránh mất mát dữ liệu phổ biến trên các hệ thống đám mây. |

---

## 9. Ma Trận Đối Chiếu & Báo Cáo Phân Loại 54 Test Cases (Traceability & Classification Matrix)

### 9.1 Bảng Tổng Hợp Phân Loại 4 Nhóm

| Nhóm phân loại | Ý nghĩa kiểm thử | Danh sách Test ID cụ thể | Số lượng | Tỷ lệ (%) |
|---|---|---|:---:|:---:|
| **Nhóm 1: Có thể kiểm thử đầy đủ trên FakeDfsClient** | Các tính năng đã có mã nguồn đầy đủ, hoạt động trơn tru trên `FakeDfsClient` và kỳ vọng đạt kết quả `Pass` khi tester đối chiếu với Expected Result. | `TC-LOGIN-01`, `TC-LOGIN-02`, `TC-LOGIN-03`, `TC-LOGIN-04`, `TC-LOGIN-07`<br>`TC-FILE-01`, `TC-FILE-02`, `TC-FILE-03`, `TC-FILE-04`, `TC-FILE-05`, `TC-FILE-06`, `TC-FILE-07`, `TC-FILE-08`, `TC-FILE-09`, `TC-FILE-10`, `TC-FILE-11`<br>`TC-XFER-01`, `TC-XFER-03`, `TC-XFER-04`, `TC-XFER-05`, `TC-XFER-06`, `TC-XFER-07`, `TC-XFER-08`<br>`TC-ADMIN-02`, `TC-ADMIN-06`, `TC-ADMIN-07`, `TC-ADMIN-08`, `TC-ADMIN-09`<br>`TC-CLUSTER-01`, `TC-CLUSTER-02`, `TC-CLUSTER-03`, `TC-CLUSTER-05`<br>`TC-E2E-01`, `TC-E2E-03` | **34** | 63.0% |
| **Nhóm 2: Thao tác được trên FakeDfsClient nhưng phát hiện Gap** | Có thể tương tác trực tiếp trên giao diện `FakeDfsClient`, nhưng khi tester đối chiếu với đầy đủ yêu cầu thiết kế sẽ ghi nhận `Fail` do thiếu tính năng hoặc chưa đạt tiêu chí (Implementation Gap). | `TC-XFER-02` *(Thiếu bộ đếm ETA countdown)*<br>`TC-XFER-09` *(Download mô phỏng in-memory, chưa ghi file thật)*<br>`TC-ADMIN-10` *(Chưa disable nút Thu hồi cho quyền kế thừa)*<br>`TC-CLUSTER-04` *(Chưa có ProgressBar đổi màu theo ngưỡng)*<br>`TC-E2E-04` *(Logout chưa xóa trắng text Username/Password)* | **5** | 9.3% |
| **Nhóm 3: Cần môi trường mạng có độ trễ phù hợp hoặc Metadata Server thật** | Bắt buộc phải có môi trường có độ trễ mạng được kiểm soát hoặc cụm Metadata Server thật. Nếu chỉ chạy trên FakeDfsClient mặc định, tester sẽ đánh dấu `Blocked`. | `TC-LOGIN-05` *(Cần môi trường có độ trễ được kiểm soát để quan sát Loading Indicator và khóa form)*<br>`TC-LOGIN-06` *(Lỗi Forbidden tài khoản khóa và Unavailable rớt mạng thật)*<br>`TC-ADMIN-01` *(Ẩn tab Admin khi đăng nhập Non-Admin)*<br>`TC-ADMIN-03` *(Xác minh lưu DB và đăng nhập lại bằng user mới)*<br>`TC-ADMIN-04` *(Xác minh từ chối đăng nhập khi user bị khóa)*<br>`TC-ADMIN-05` *(Xác minh đăng nhập lại bằng mật khẩu mới)*<br>`TC-E2E-02` *(Admin tạo user & phân quyền động -> User mới đăng nhập)* | **7** | 13.0% |
| **Nhóm 4: Chưa thể kiểm thử vì đang Pending Milestone 3** | Tính năng chưa có mã nguồn (XAML/ViewModel). Đã xây dựng sẵn test criteria từ thiết kế, trạng thái mặc định `Not Run` hoặc `N/A`. | `TC-PEND-VH-01`, `TC-PEND-VH-02`, `TC-PEND-VH-03`, `TC-PEND-VH-04`<br>`TC-PEND-CF-01`, `TC-PEND-CF-02`, `TC-PEND-CF-03`, `TC-PEND-CF-04` | **8** | 14.7% |
| **TỔNG CỘNG** | **Kiểm chứng toàn bộ 54 Test ID** | **Không có ID nào bị trùng lặp hoặc bỏ sót (34 + 5 + 7 + 8 = 54)** | **54** | **100.0%** |

*(Ghi chú: Tổng số ca kiểm thử có thể thao tác trực tiếp trên giao diện với FakeDfsClient là **39 test cases** = Nhóm 1 [34 TC] + Nhóm 2 [5 TC]).*

---

### 9.2 Bảng Đối Chiếu Theo Từng Màn Hình

| Màn hình | Tài liệu thiết kế | Triển khai mã nguồn | Đầy đủ trên Fake (Nhóm 1) | Phát hiện Gap (Nhóm 2) | Cần Server/Độ trễ (Nhóm 3) | Chờ M3 (Nhóm 4) | Tổng TC |
|---|---|---|:---:|:---:|:---:|:---:|:---:|
| **Login** | `01-login.md` | `LoginView.axaml` / `LoginViewModel.cs` | 5 | 0 | 2 (`TC-LOGIN-05`, `06`) | 0 | **7** |
| **File Browser** | `02-file-browser.md` | `FileBrowserView.axaml` / `FileBrowserViewModel.cs` | 11 | 0 | 0 | 0 | **11** |
| **Transfer Manager** | `03-transfers.md` | `TransferManagerView.axaml` / `TransferManagerViewModel.cs` | 7 | 2 (`TC-XFER-02`, `09`) | 0 | 0 | **9** |
| **Admin Panel** | `06-admin-panel.md` | `AdminPanelView.axaml` / `AdminPanelViewModel.cs` | 5 | 1 (`TC-ADMIN-10`) | 4 (`TC-ADMIN-01`, `03`, `04`, `05`) | 0 | **10** |
| **Cluster Status** | `07-cluster-status.md` | `ClusterStatusView.axaml` / `ClusterStatusViewModel.cs` | 4 | 1 (`TC-CLUSTER-04`) | 0 | 0 | **5** |
| **E2E Scenarios** | `screen-flow.md` | Toàn bộ Shell ViewModels | 2 | 1 (`TC-E2E-04`) | 1 (`TC-E2E-02`) | 0 | **4** |
| **Version History** | `04-version-history.md` | *Chưa triển khai* | 0 | 0 | 0 | 4 (`TC-PEND-VH-01..04`) | **4** |
| **Conflict Dialog** | `05-conflict-dialog.md` | *Chưa triển khai* | 0 | 0 | 0 | 4 (`TC-PEND-CF-01..04`) | **4** |
| **TỔNG CỘNG** | | | **34** | **5** | **7** | **8** | **54** |

---

## 10. Hướng Dẫn Báo Cáo Kết Quả Sau Kiểm Thử

Sau khi tester thực hiện các ca kiểm thử:
1. Ghi nhận tình trạng tại cột **Status**:
   - `Pass`: Chỉ ghi nhận khi tester đã thực sự chạy và hành vi thực tế khớp hoàn toàn với Expected Result (áp dụng cho các ca thuộc Nhóm 1 khi kiểm thử thành công).
   - `Fail`: Chỉ ghi nhận khi tester đã thực sự chạy nhưng không đạt Expected Result (áp dụng cho các ca thuộc Nhóm 2 khi phát hiện Implementation Gap so với thiết kế, hoặc khi phát sinh lỗi/crash).
   - `Blocked`: Chỉ ghi nhận khi môi trường kiểm thử hiện tại của tester không đáp ứng điều kiện tiên quyết (áp dụng cho các ca thuộc Nhóm 3 khi chỉ có bản build `FakeDfsClient`).
   - `N/A`: Áp dụng cho các ca kiểm thử thuộc Nhóm 4 của Milestone 3 do chưa có mã nguồn giao diện.
2. Với các ca kiểm thử `Fail` hoặc `Blocked`:
   - Ghi mô tả lỗi tóm tắt hoặc lý do bị chặn vào cột **Actual Result**.
   - Chụp ảnh màn hình đính kèm mã Test ID.
   - Trích xuất file log của client tại thư mục `logs/client.log` hoặc Output Console của terminal.
   - Tạo issue trên GitHub đính kèm nhãn `bug`, `gui` và liên kết đến Test ID tương ứng.
