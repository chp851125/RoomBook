# RoomBook 開發慣例（每階段開始前必讀，結束前更新）

專案：`RoomBook/RoomBook.csproj`（net10.0，根命名空間 `RoomBook`）。Razor Pages 為主，不使用 Controller（僅少數 handler 回傳 JSON）。

## 資料夾與命名

| 位置 | 用途 |
|---|---|
| `Domain/Entities` | 實體，皆繼承 `BaseEntity`（Id、Version、CreatedAt、UpdatedAt） |
| `Domain/Enums` | `BookingStatus` `RoomStatus` `UserRole` `RecurrenceType` `NotificationType` |
| `Domain/` | `ConcurrencyException`、`BookingNumber`（單號格式） |
| `Application/Interfaces` | `IRepository<T>`、`ITimeProvider`、`ICurrentUser`、各 `IXxxService` |
| `Application/Services` | `XxxService`（只依賴 `IRepository<T>`／其他 Service 介面，**不得出現 JSON 型別**）、`SettingKeys`、`RecurrenceExpander` |
| `Application/Dtos` | 資料傳輸物件 |
| `Infrastructure/Json` | `JsonFileRepository<T>`、`DataBootstrapper`、`DataResetService`、`EntityFileNames` |
| `Infrastructure/Seed` | `SeedDataGenerator`（Development 限定） |
| `Infrastructure/Security` | `HttpCurrentUser`、`ClaimsFactory`、`DataHealthCheck` |
| `Infrastructure/Time` | `SystemTimeProvider` |
| `Infrastructure/Background`、`Email` | 階段 3 使用（BackgroundService、.eml outbox） |
| `Pages/{Rooms,Bookings,MyBookings,Admin,Account,Display,Notifications,Shared}` | 依功能分資料夾；`Shared` 放 Layout、Partial、`AppPageModel`、`Components/` |
| `ViewComponents/` | ViewComponent 類別；檢視在 `Pages/Shared/Components/<Name>/Default.cshtml` |
| `SeedData/` | 範例資料（進 Git）；`App_Data/` 為執行資料（不進 Git） |

- Service：`XxxService` / `IXxxService`；PageModel：`XxxModel`；ViewComponent：`XxxViewComponent`（標籤 `<vc:xxx>`）。
- JSON 檔名＝實體名複數、camelCase 起頭：`users.json departments.json rooms.json bookings.json bookingSeries.json bookingExceptions.json notifications.json emailLogs.json violations.json businessHours.json holidays.json auditLogs.json appSettings.json`；欄位 camelCase，列舉為字串，縮排排版，中文不跳脫。
- 全域 using（`GlobalUsings.cs`）：`Domain`、`Domain.Entities`、`Domain.Enums`、`Application.Interfaces`。

## 時間

- 一律 UTC 儲存（`DateTime`，Kind=Utc）；只在顯示時用 `ITimeProvider.ToLocal()` 轉台北時間；使用者輸入的台北時間用 `ToUtc()` 轉回。
- **禁止** `DateTime.Now` / `DateTime.UtcNow` 直接呼叫（唯一例外是 `SystemTimeProvider` 本身）；一律注入 `ITimeProvider`。
- 時段為左閉右開 `[start, end)`；單次預約不得跨午夜。
- 營業時段、週期預約的 `StartTime/EndTime`、假日 `Date` 為「台北本地」時刻（`TimeOnly`/`DateOnly`），計算時再轉 UTC。

## 資料層

- `IRepository<T>`：`GetAll / GetById / Query / Add / Update / Delete`；回傳的都是**複本**，修改後要 `Update` 才會生效。
- `Update` 版本不符丟 `ConcurrencyException`（頁面需顯示友善提示）；成功後 `Version + 1`。
- `JsonFileRepository<T>` 為單例，單鎖保護，寫入採「暫存檔再取代」。
- 重置：`IDataResetService.Reset()`（清空 `App_Data`、複製 `SeedData`、通知所有 Repository 重新載入）。Development 重置時會先重新產生 SeedData，讓相對日期回到今天。
- 「每間會議室一把鎖」於階段 2 的預約 Service 實作，衝突檢查與寫入需在同一把鎖內完成。
- Mock 資料只能出現在 JSON；程式碼不得寫死。

## 驗證與授權

- Cookie Authentication（`RoomBook.Auth`）+ `PasswordHasher<User>`；角色 `Employee`、`Admin`（claim `ClaimTypes.Role`）。
- `Program.cs` 慣例：`AuthorizeFolder("/")` 全站需登入；`AuthorizeFolder("/Admin", "AdminOnly")`；匿名頁面：`/Account/Login`、`/Account/AccessDenied`、`/Error`、`/Display/*`。
- **PageModel 層級授權**：管理頁面繼承 `AdminPageModel`（帶 `[Authorize(Roles="Admin")]`）；一般頁面繼承 `AppPageModel`。不得只靠隱藏按鈕。
- 每次請求 `OnValidatePrincipal` 檢查使用者仍存在、啟用、角色未變。
- 登入失敗鎖定、密碼最短長度由 `AppSetting`（`Security.*`）控制。
- 稽核：重要動作呼叫 `IAuditService.Log(action, targetType, targetId, detail)`。

## 設定（AppSetting）

Key 常數在 `SettingKeys`，預設值在 `SeedData/appSettings.json`；讀取用 `ISettingService.GetInt/GetBool/GetString`。`appsettings.json` 的 `Features:AllowDataReset` 控制是否提供重置功能。

## UI

- Bootstrap 5 + Bootstrap Icons（`wwwroot/lib`，無 CDN）；FullCalendar（`lib/fullcalendar`，含 zh-tw）、ECharts（`lib/echarts`）已備好，階段 2/4 使用。
- 導覽：`ViewData["Nav"]` 設為 `calendar | rooms | my | notifications | me | admin` 以標示作用中項目。
- 桌機頂部導覽、手機（< lg）底部導覽五項：行事曆、我的預約、新增預約、通知、我。
- 手機優先：按鈕與輸入框在手機上至少 ~44px；簽到/延長/提前結束要放在預約卡片上。
- 訊息提示用 `TempData["Success"]`／`TempData["Error"]`（`AppPageModel.Success()/Error()`）。
- 尚未實作的頁面用 `_ComingSoon` partial 佔位，實作時直接取代內容。

## Development 專用

- `dotnet run -- --generate-seed`：重新產生 `SeedData/*.json` 並自我驗證（失敗中止、不寫檔）。
- 範例資料日期相對於「產生當天」；啟動時若 App_Data 缺檔且 SeedData 非今天產生，會自動重新產生。
- 快速切換使用者：`/Account/SwitchUser`（非 Development 回 404）。
- Demo 密碼：`RoomBook#2026`。

## 預約核心（階段 2）

- **佔用時段** = `BookingSlot`：Confirmed / Pending 的 Booking，加上週期預約（BookingSeries）動態展開的每一次。展開由 `IBookingQueryService.ExpandSeries` 負責（最多 52 次；排除假日、單次取消、已實體化的那次；單次改時間以 BookingException 為準）。
- **衝突**：`FindRoomConflicts`（硬性）與 `FindPersonConflicts`（只看 Confirmed，警告）。比較一律 `s.Start < e && start < s.End`（左閉右開）。
- **建立流程**：`IBookingService.Preview`（不寫入，給即時檢查）與 `Create` 共用同一條 `Process`。衝突檢查加寫入必須在 `RoomLockProvider.For(roomId)` 鎖內完成；單號 `NextBookingNo` 另有全域鎖。
- **BookingResult.Outcome**：`Created` / `Invalid`（Errors、RoomConflicts）/ `NeedsParticipantConfirm`（送出 `ConfirmParticipantConflicts=true`）/ `NeedsConflictDecision`（週期預約有衝突日期，送出 `ConflictPolicy=Skip|CancelAll`）。
- **假日與公休日**：週期預約遇到的日期放進 `SkippedDates` 並提示；單次預約落在假日則是錯誤。
- **審核**：`room.RequiresApproval && !admin` → Pending（仍佔用時段），建立時通知管理員。
- **週期單次例外**：`CancelOccurrence` / `ModifyOccurrence`（預約人或管理員；取消受「開始前 N 分鐘」限制）。建立時被略過的衝突日期也以「已取消的例外」記錄。
- **日曆 JSON**：`/Bookings?handler=Events`；FullCalendar 用 `timeZone:"UTC"` 並傳「台北牆上時間」字串，點選空白時段帶 `start/end`（`yyyy-MM-ddTHH:mm`，台北時間）到 `/Bookings/Create`。
- **即時檢查**：`/Bookings/Create?handler=Check` 回傳 HTML 片段（`_BookingCheck` partial，與送出後的提示共用）。
- **詳情頁路由**：`/Bookings/Details/{id}`（單次預約）或 `/Bookings/Details?seriesId=..&date=yyyy-MM-dd`（週期預約的某一次）。複製：`Create?copyFrom=id` 或 `copySeries=..&copyDate=..`。
- 共用 Partial：`_RoomCard`、`_RoomStatusBadge`、`_BookingStatusBadge`、`_BookingCheck`。前端腳本：`wwwroot/js/calendar.js`、`booking-form.js`。
- 檢視時間格式用 `ITimeProvider.Format(utc, "HH:mm")`（擴充方法）。

## 我的預約、簽到、通知（階段 3）

- **動作服務** `IBookingActionService`：`CheckIn / Cancel / Extend / FinishEarly`，以及背景用的 `ReleaseNoShows / SendReminders`。每個動作都在 `RoomLockProvider.For(roomId)` 鎖內重新讀取狀態再寫入。目標以 `BookingRef(BookingId | SeriesId+Date)` 指定。
- **週期預約的某一次需要「實體化」**：簽到、延長、提前結束、提醒、釋出都會把該次建立成一筆 `Booking`（帶 `SeriesId`、`OccurrenceDate`），`ExpandSeries` 之後會略過已實體化的那一次。尚未實體化時取消走 `CancelOccurrence`（單次例外）。
- **簽到時窗**：`start - CheckIn.EarlyMinutes` ≤ 現在 ≤ `start + CheckIn.LateMinutes`（含邊界）。超過（嚴格大於）才會被釋出。
- **自動釋出**（`BookingMaintenanceWorker`，每分鐘）：只處理「剛超過時窗、回溯 30 分鐘內」的預約，避免停機後對舊資料一次大量記點。釋出 → 狀態 `Released`、通知 `AutoReleased`、`IViolationService.Record` 記 1 點；累計達 `Violation.PointLimit` 暫停 `Violation.SuspendDays` 天（點數不會自動歸零，管理員於階段 4 可解除）。
- **提醒**：開始前 `Notification.ReminderMinutes` 分鐘內，對預約人與參與者各發一則 `Reminder`，並以 `ReminderSentAtUtc` 防止重複。
- **取消規則**：員工在「開始前 N 分鐘」內不可取消（進行中的請用提前結束）；管理員取消他人的預約為「強制取消」，必須填原因並發 `ForceCancelled` 通知。
- **延長**：只限進行中（Confirmed、已開始未結束）、延長後仍在同一天營業時段內、且延長區間 `[原結束, 新結束)` 沒有其他預約。**提前結束**：狀態 `Finished`、`EndUtc` 改為現在。
- **修改預約**：沿用建立流程（`CreateBookingRequest.EditBookingId`，頁面是 `/Bookings/Create?editId=`），衝突與規則檢查排除自己；只限尚未開始的單次預約；員工在需審核的會議室修改 Confirmed 預約後會回到 Pending。
- **通知**：一律呼叫 `INotificationService.Notify`（寫站內通知，並依使用者偏好 `EmailDisabledTypes` 呼叫 `IEmailSender`）。Email 為模擬：寫 `.eml` 到 `outbox/`（不進 Git）並在 `emailLogs.json` 留紀錄。
- **我的預約**：`IMyBookingService.Search`（分頁籤 `MyTab`：Upcoming / Now / History / Cancelled / Invited，搜尋、會議室與日期篩選、每頁 8 筆）。所有卡片動作（簽到、延長、提前結束、取消）的 POST handler 都在 `/MyBookings/Index`，帶 `returnUrl`（僅限本站路徑）；Partial：`_BookingCard`、`_BookingActions`、`_CancelModal`。
- **.ics**：`IIcsService`（時間用 UTC、依 RFC 5545 折行）；`/Bookings/Details?handler=Ics`、`/MyBookings?handler=IcsAll`。
- **看板**：`/Display/{roomId}`（匿名、唯讀）；只顯示主題（`Display.ShowTitle=false` 時隱藏）與預約人姓名；自動更新呼叫 `?handler=Board` 取得 `_Board` 片段。樣式在 `wwwroot/css/display.css`。

## 管理端、個人設定與安全（階段 4）

- **管理頁一律放在 `Pages/Admin/**`，繼承 `AdminPageModel`**（`[Authorize(Roles="Admin")]`），並且資料夾慣例 `AuthorizeFolder("/Admin", "AdminOnly")` 雙重保護；共用導覽 `_AdminNav`（傳入目前頁籤 key）、分頁 `_Pager`。**新增管理頁務必放在 Admin 資料夾。**
- **管理服務**（皆在 `Application/Services`，每個動作寫稽核紀錄）：`RoomAdminService`、`UserAdminService`、`SettingAdminService`、`ScheduleAdminService`（營業時段、假日）、`AdminBookingService`（所有預約搜尋、核准／駁回）、`StatsService`、`ReportService`（ClosedXML）、`AuditQueryService`、`AccountService`。
- **編輯表單的樂觀並行**：表單帶 `Version` 隱藏欄位，服務把它寫回實體再 `Update`；版本不符顯示「剛被其他人修改，請重新載入」。
- **審核**：Pending 先佔用時段；核准 → Confirmed（通知 BookingApproved），駁回 → Rejected（必填原因、通知 BookingRejected、時段釋出）。週期預約整組審核。
- **代為預約**：`CreateBookingRequest.ActorId` 為管理員且不是預約人時視為代預約（直接確認，不受過去時段、上限、暫停限制，並通知預約人）。伺服器端只有管理員的 `Input.OrganizerId` 才會生效。
- **強制取消**：管理員取消他人的預約走 `/MyBookings?handler=Cancel`（`IBookingActionService.Cancel`），必須填原因並發 `ForceCancelled` 通知。
- **設定頁**：`SettingAdminService` 依 key 驗證範圍（整數）或布林；新增設定時要同時更新 `SettingKeys`、`SeedDataGenerator.BuildSettings` 與 `IntRanges`／`BoolKeys`。
- **統計**：`IStatsService.Get(from,to)`；只計 Confirmed 與 Finished 的時段，可用時數為營業日的營業時間；圖表在 `wwwroot/js/stats.js`（資料以 `<script type="application/json">` 內嵌）。
- **密碼原則**：`PasswordPolicy`（長度 `Security.PasswordMinLength` + 英文字母與數字）；管理員新增帳號／重設密碼會產生臨時密碼（只顯示一次）並設 `MustChangePassword`，登入後導向個人設定。
- **安全標頭**（`Program.cs`）：CSP（`self`＋`unsafe-inline`＋font `data:`）、`X-Content-Type-Options`、`X-Frame-Options`、`Referrer-Policy`、`Permissions-Policy`。內嵌在 `<script type="application/json">` 的資料一律用 `System.Text.Json` 序列化（預設會跳脫 `<`、`>`）。確認訊息用 `<form data-confirm="...">`，**不要把使用者資料放進 inline JavaScript**。
- **Excel 匯出**：文字欄位用 `Sanitize` 避免公式注入（以 `=+-@` 開頭會補單引號）。

## 階段進度

- [x] 階段 1：骨架、Domain、Repository、SeedData、登入與授權、版型、重置、快速切換使用者
- [x] 階段 2：會議室、行事曆、預約表單與衝突檢查
- [x] 階段 3：我的預約、簽到、違規、.ics、通知、Email、看板
- [x] 階段 4：管理端、個人設定、安全強化、README、docs/notes.md
