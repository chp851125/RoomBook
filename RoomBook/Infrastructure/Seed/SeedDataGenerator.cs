using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using RoomBook.Application.Services;
using RoomBook.Domain;
using RoomBook.Infrastructure.Json;

namespace RoomBook.Infrastructure.Seed;

public sealed record SeedResult(bool Success, IReadOnlyList<string> Errors, IReadOnlyList<string> Notes);

public sealed class SeedDataSet
{
    public List<Department> Departments { get; } = new();
    public List<User> Users { get; } = new();
    public List<Room> Rooms { get; } = new();
    public List<Booking> Bookings { get; } = new();
    public List<BookingSeries> BookingSeries { get; } = new();
    public List<BookingException> BookingExceptions { get; } = new();
    public List<Notification> Notifications { get; } = new();
    public List<EmailLog> EmailLogs { get; } = new();
    public List<Violation> Violations { get; } = new();
    public List<BusinessHours> BusinessHours { get; } = new();
    public List<Holiday> Holidays { get; } = new();
    public List<AuditLog> AuditLogs { get; } = new();
    public List<AppSetting> AppSettings { get; } = new();
}

/// <summary>
/// Development 限定：以固定亂數種子批次產生 SeedData/*.json。
/// 日期全部相對於「執行當天」，產生後會自我驗證（參照、營業時段、衝突），失敗則不寫檔。
/// </summary>
public sealed class SeedDataGenerator(ITimeProvider time, IPasswordHasher<User> hasher)
{
    public const int RandomSeed = 20260101;
    public const string MetaFileName = "seed-meta.json";
    public const string DemoPassword = "RoomBook#2026";

    /// <summary>種子資料是否不是「今天」產生的（日期已過期）。</summary>
    public bool IsStale(string seedDirectory)
    {
        var meta = Path.Combine(seedDirectory, MetaFileName);
        if (!File.Exists(meta)) return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            var on = doc.RootElement.GetProperty("generatedOn").GetString();
            return on != time.LocalToday.ToString("yyyy-MM-dd");
        }
        catch { return true; }
    }

    public SeedResult Generate(string seedDirectory)
    {
        var builder = new Builder(time, hasher);
        var set = builder.Build();
        var errors = builder.Validate();
        if (errors.Count > 0)
            return new SeedResult(false, errors, builder.Notes);

        Directory.CreateDirectory(seedDirectory);
        Write(seedDirectory, set.Users);
        Write(seedDirectory, set.Departments);
        Write(seedDirectory, set.Rooms);
        Write(seedDirectory, set.Bookings);
        Write(seedDirectory, set.BookingSeries);
        Write(seedDirectory, set.BookingExceptions);
        Write(seedDirectory, set.Notifications);
        Write(seedDirectory, set.EmailLogs);
        Write(seedDirectory, set.Violations);
        Write(seedDirectory, set.BusinessHours);
        Write(seedDirectory, set.Holidays);
        Write(seedDirectory, set.AuditLogs);
        Write(seedDirectory, set.AppSettings);
        File.WriteAllText(Path.Combine(seedDirectory, MetaFileName),
            JsonSerializer.Serialize(new { generatedOn = time.LocalToday.ToString("yyyy-MM-dd"), seed = RandomSeed }, JsonDefaults.Options));
        return new SeedResult(true, errors, builder.Notes);
    }

    private static void Write<T>(string dir, List<T> items) where T : BaseEntity
        => File.WriteAllText(Path.Combine(dir, EntityFileNames.For<T>()), JsonSerializer.Serialize(items, JsonDefaults.Options));

    // ------------------------------------------------------------------------------------------

    private sealed class Builder
    {
        private readonly ITimeProvider _time;
        private readonly IPasswordHasher<User> _hasher;
        private readonly Random _rng = new(RandomSeed);
        private readonly DateTime _nowUtc;
        private readonly DateTime _nowLocal;
        private readonly DateOnly _today;
        private readonly DateOnly _base;     // 營業日 0（今天；若今天非營業日則為前一個營業日）
        private readonly DateTime _anchor;   // 「現在」錨點（台北時間，對齊 30 分鐘）
        private readonly Dictionary<DateOnly, int> _bookingSeq = new();

        private static readonly TimeOnly Open = new(8, 30);
        private static readonly TimeOnly Close = new(18, 30);
        private const int MaxDurationHours = 4;

        // 刻意示範「參與者時間衝突警告」的兩場會議（只有這組允許參與者重疊）
        private const string OverlapTitleA = "Q4 業務檢討會";
        private const string OverlapTitleB = "新人 1:1 輔導會談";

        public SeedDataSet Set { get; } = new();
        public List<string> Notes { get; } = new();

        private User _admin = null!;
        private Dictionary<string, User> _u = new();
        private Dictionary<string, Room> _r = new();
        private Dictionary<string, Department> _d = new();

        public Builder(ITimeProvider time, IPasswordHasher<User> hasher)
        {
            _time = time;
            _hasher = hasher;
            _nowUtc = time.UtcNow;
            _nowLocal = time.ToLocal(_nowUtc);
            _today = DateOnly.FromDateTime(_nowLocal);

            _base = IsBusinessDay(_today) ? _today : Step(_today, -1);
            if (IsBusinessDay(_today))
            {
                var lo = _today.ToDateTime(new TimeOnly(9, 30));
                var hi = _today.ToDateTime(new TimeOnly(15, 30));
                var clamped = _nowLocal < lo ? lo : _nowLocal > hi ? hi : _nowLocal;
                _anchor = new DateTime(clamped.Year, clamped.Month, clamped.Day, clamped.Hour, clamped.Minute >= 30 ? 30 : 0, 0);
                if (_nowLocal < lo || _nowLocal > hi)
                    Notes.Add($"目前時間 {_nowLocal:HH:mm} 不在 09:30–15:30，「進行中／即將開始」示範會議以 {_anchor:HH:mm} 為基準，可能不是真的進行中。");
            }
            else
            {
                _anchor = _base.ToDateTime(new TimeOnly(14, 0));
                Notes.Add("今天不是營業日：「進行中／即將開始」示範會議放在前一個營業日，實際上已是過去的會議。");
            }
        }

        // ---------- 時間工具 ----------
        private static bool IsBusinessDay(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        private static DateOnly Step(DateOnly d, int direction)
        {
            do { d = d.AddDays(direction); } while (!IsBusinessDay(d));
            return d;
        }

        /// <summary>第 n 個營業日（n 可為負）。</summary>
        private DateOnly BD(int n)
        {
            var d = _base;
            for (var i = 0; i < Math.Abs(n); i++) d = Step(d, n >= 0 ? 1 : -1);
            return d;
        }

        private static DateTime L(DateOnly d, string hhmm) => d.ToDateTime(TimeOnly.Parse(hhmm));
        private DateTime U(DateTime local) => _time.ToUtc(local);

        private Guid NewId()
        {
            var bytes = new byte[16];
            _rng.NextBytes(bytes);
            return new Guid(bytes);
        }

        private T Stamp<T>(T entity, DateTime createdUtc) where T : BaseEntity
        {
            entity.Id = NewId();
            entity.Version = 1;
            entity.CreatedAt = createdUtc;
            entity.UpdatedAt = createdUtc;
            return entity;
        }

        // ---------- 建立資料 ----------
        public SeedDataSet Build()
        {
            var longAgo = _nowUtc.AddDays(-120);
            BuildDepartments(longAgo);
            BuildUsers(longAgo);
            BuildRooms(longAgo);
            BuildBusinessHoursAndHolidays(longAgo);
            BuildSettings(longAgo);
            BuildBookings();
            BuildViolations();
            BuildNotificationsAndEmails();
            BuildAuditLogs();
            return Set;
        }

        private void BuildDepartments(DateTime at)
        {
            foreach (var (code, name) in new[] { ("ADM", "行政部"), ("SAL", "業務部"), ("RND", "研發部") })
            {
                var d = Stamp(new Department { Code = code, Name = name }, at);
                Set.Departments.Add(d);
                _d[code] = d;
            }
        }

        private void BuildUsers(DateTime at)
        {
            void Add(string key, string account, string name, string dept, string job, UserRole role = UserRole.Employee)
            {
                var u = Stamp(new User
                {
                    Account = account,
                    DisplayName = name,
                    Email = $"{account}@roombook.example",
                    DepartmentId = _d[dept].Id,
                    JobTitle = job,
                    Role = role,
                    IsActive = true
                }, at);
                u.PasswordHash = _hasher.HashPassword(u, DemoPassword);
                Set.Users.Add(u);
                _u[key] = u;
            }

            Add("admin", "admin", "林怡君", "ADM", "行政經理", UserRole.Admin);
            Add("zhihao", "zhihao", "陳志豪", "SAL", "業務經理");
            Add("yating", "yating", "王雅婷", "SAL", "業務專員");
            Add("jiaming", "jiaming", "張家銘", "RND", "資深工程師");
            Add("hsinyi", "hsinyi", "李欣怡", "RND", "前端工程師");
            Add("bohan", "bohan", "黃柏翰", "ADM", "行政專員");
            Add("shufen", "shufen", "吳淑芬", "SAL", "業務助理");
            Add("guanyu", "guanyu", "劉冠宇", "RND", "後端工程師");
            _admin = _u["admin"];

            _u["shufen"].IsActive = false; // 已停用（離職）
        }

        private void BuildRooms(DateTime at)
        {
            void Add(string key, string name, int floor, int cap, string[] equip, string desc, bool approval, int sort, RoomStatus status = RoomStatus.Active)
            {
                var r = Stamp(new Room
                {
                    Name = name, Floor = floor, Capacity = cap, Equipment = equip.ToList(), Description = desc,
                    RequiresApproval = approval, SortOrder = sort, Status = status
                }, at);
                Set.Rooms.Add(r);
                _r[key] = r;
            }

            Add("R1", "雲海會議室", 3, 24, ["投影機", "視訊", "白板"],
                "3 樓大型會議室，配備雙投影與視訊會議系統，適合全員會議、客戶簡報與教育訓練。使用前需經管理員審核。", true, 1);
            Add("R2", "天際會議室", 4, 20, ["投影機", "視訊", "電視"],
                "4 樓大型會議室，視野良好，附大型電視牆與視訊設備，適合跨部門workshop與主管會議。使用前需經管理員審核。", true, 2);
            Add("R3", "星河會議室", 3, 10, ["投影機", "白板"],
                "3 樓中型會議室，可容納 10 人，投影機與白板齊備，適合專案討論與部門會議。", false, 3);
            Add("R4", "晨曦會議室", 4, 8, ["視訊", "電視", "白板"],
                "4 樓中型會議室，電視可無線投影，適合例行週會與遠端視訊會議。", false, 4);
            Add("R5", "靜心討論室", 2, 4, ["白板", "電視"],
                "2 樓小型討論室，安靜獨立，適合一對一面談、面試與小組快速討論。", false, 5);
            Add("R6", "拾光討論室", 2, 4, ["白板"],
                "2 樓小型討論室。目前空調設備維修中，預計兩週後恢復使用。", false, 6, RoomStatus.Maintenance);

            _u["jiaming"].FavoriteRoomIds = new List<Guid> { _r["R4"].Id };
            _u["zhihao"].FavoriteRoomIds = new List<Guid> { _r["R3"].Id, _r["R1"].Id };
        }

        private void BuildBusinessHoursAndHolidays(DateTime at)
        {
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            {
                var open = day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
                Set.BusinessHours.Add(Stamp(new BusinessHours { Day = day, IsOpen = open, OpenTime = Open, CloseTime = Close }, at));
            }

            // 假日放在所有示範預約之後；第一個假日刻意落在週一，讓「每週一」的週期預約示範「遇假日略過」
            var h1 = BD(8).AddDays(2);
            while (h1.DayOfWeek != DayOfWeek.Monday) h1 = h1.AddDays(1);
            var h2 = h1.AddDays(14);
            while (h2.DayOfWeek != DayOfWeek.Friday) h2 = h2.AddDays(1);
            var h3 = h2.AddDays(20);
            while (h3.DayOfWeek != DayOfWeek.Wednesday) h3 = h3.AddDays(1);
            Set.Holidays.Add(Stamp(new Holiday { Date = h1, Name = "彈性放假日" }, at));
            Set.Holidays.Add(Stamp(new Holiday { Date = h2, Name = "公司週年慶休假" }, at));
            Set.Holidays.Add(Stamp(new Holiday { Date = h3, Name = "國定假日補假" }, at));
            Notes.Add($"假日 {h1:yyyy-MM-dd}（週一）落在每週一週期預約「研發部週會」範圍內，用來示範假日略過。");
        }

        private void BuildSettings(DateTime at)
        {
            void Add(string key, string value, string group, string desc)
                => Set.AppSettings.Add(Stamp(new AppSetting { Key = key, Value = value, Group = group, Description = desc }, at));

            Add(SettingKeys.MaxDurationHours, "4", "預約規則", "單次預約最長時數（小時）");
            Add(SettingKeys.MaxAdvanceDays, "60", "預約規則", "最早可提前預約天數");
            Add(SettingKeys.MaxFutureBookingsPerUser, "10", "預約規則", "每人同時未來預約上限");
            Add(SettingKeys.CancelCutoffMinutes, "30", "預約規則", "開始前幾分鐘內不可取消");
            Add(SettingKeys.AllowEmployeePast, "false", "預約規則", "員工可否預約過去時段");
            Add(SettingKeys.CheckInEarlyMinutes, "10", "簽到", "開始前幾分鐘可簽到");
            Add(SettingKeys.CheckInLateMinutes, "15", "簽到", "開始後幾分鐘內可簽到，逾時自動釋出");
            Add(SettingKeys.ViolationPointLimit, "3", "違規", "累計點數達此值即暫停預約");
            Add(SettingKeys.ViolationSuspendDays, "7", "違規", "暫停預約天數");
            Add(SettingKeys.ReminderMinutes, "15", "通知", "會議前幾分鐘提醒");
            Add(SettingKeys.DisplayShowTitle, "true", "看板", "看板頁是否顯示會議主題");
            Add(SettingKeys.DisplayRefreshSeconds, "30", "看板", "看板自動更新秒數");
            Add(SettingKeys.MaxLoginFailures, "5", "安全", "連續登入失敗幾次後鎖定");
            Add(SettingKeys.LockoutMinutes, "15", "安全", "登入鎖定分鐘數");
            Add(SettingKeys.PasswordMinLength, "8", "安全", "密碼最短長度");
        }

        private int NextSeq(DateTime createdUtc)
        {
            var date = DateOnly.FromDateTime(_time.ToLocal(createdUtc));
            _bookingSeq[date] = _bookingSeq.GetValueOrDefault(date) + 1;
            return _bookingSeq[date];
        }

        private Booking AddBooking(string roomKey, string organizerKey, string title, DateTime startLocal, DateTime endLocal,
            BookingStatus status, string desc, string[]? participants = null, ExternalGuest[]? guests = null,
            string[]? equip = null, string? note = null, int? attendees = null)
        {
            var room = _r[roomKey];
            var org = _u[organizerKey];
            var start = U(startLocal);
            var end = U(endLocal);

            var created = start.AddDays(-_rng.Next(2, 6)).AddHours(-_rng.Next(0, 5));
            var cap = _nowUtc.AddHours(-_rng.Next(1, 48));
            if (created > cap) created = cap;

            var parts = (participants ?? []).Select(k => _u[k].Id).ToList();
            var b = Stamp(new Booking
            {
                BookingNo = BookingNumber.Format(DateOnly.FromDateTime(_time.ToLocal(created)), NextSeq(created)),
                RoomId = room.Id,
                OrganizerId = org.Id,
                Title = title,
                Description = desc,
                StartUtc = start,
                EndUtc = end,
                ParticipantIds = parts,
                ExternalGuests = (guests ?? []).ToList(),
                RequiredEquipment = (equip ?? []).ToList(),
                AttendeeCount = attendees ?? (1 + parts.Count + (guests?.Length ?? 0)),
                Status = status
            }, created);

            void H(string action, DateTime atUtc, User actor, string? n = null)
                => b.History.Add(new BookingHistoryEntry { AtUtc = atUtc, ActorId = actor.Id, ActorName = actor.DisplayName, Action = action, Note = n });

            H("建立預約", created, org);
            var processedAt = created.AddMinutes(_rng.Next(20, 240));
            if (processedAt > _nowUtc) processedAt = _nowUtc.AddMinutes(-5);

            switch (status)
            {
                case BookingStatus.Confirmed:
                    if (room.RequiresApproval && org.Role != UserRole.Admin)
                    {
                        H("核准", processedAt, _admin);
                        b.ReviewedById = _admin.Id; b.ReviewedAtUtc = processedAt;
                    }
                    if (start <= _nowUtc && start.AddMinutes(2) <= _nowUtc)
                    {
                        b.CheckedInAtUtc = start.AddMinutes(2);
                        H("簽到", b.CheckedInAtUtc.Value, org);
                    }
                    break;
                case BookingStatus.Pending:
                    H("送出審核", created, org, "此會議室需管理員審核");
                    break;
                case BookingStatus.Rejected:
                    H("駁回", processedAt, _admin, note);
                    b.ReviewedById = _admin.Id; b.ReviewedAtUtc = processedAt; b.ReviewNote = note;
                    break;
                case BookingStatus.Cancelled:
                    var cancelledAt = processedAt > start ? created.AddMinutes(30) : processedAt.AddHours(2);
                    if (cancelledAt > _nowUtc) cancelledAt = _nowUtc.AddMinutes(-10);
                    H("取消預約", cancelledAt, org, note);
                    b.CancelReason = note; b.CancelledById = org.Id; b.CancelledAtUtc = cancelledAt;
                    break;
                case BookingStatus.Finished:
                    b.CheckedInAtUtc = start.AddMinutes(3);
                    H("簽到", b.CheckedInAtUtc.Value, org);
                    H("會議結束", end, org, note);
                    break;
                case BookingStatus.Released:
                    b.ReleasedAtUtc = start.AddMinutes(15);
                    H("逾時未簽到，系統自動釋出時段", b.ReleasedAtUtc.Value, _admin, "系統");
                    break;
            }
            Set.Bookings.Add(b);
            return b;
        }

        // 示範預約的 Id，供通知／違規／稽核參照
        private readonly Dictionary<string, Booking> _b = new();

        private void BuildBookings()
        {
            var guestsVendor = new[]
            {
                new ExternalGuest { Name = "Daniel Wu", Company = "宏遠資訊股份有限公司", Email = "daniel.wu@hongyuan-it.example" },
                new ExternalGuest { Name = "蔡佩珊", Company = "宏遠資訊股份有限公司", Email = "ps.tsai@hongyuan-it.example" }
            };
            var guestsInterview = new[]
            {
                new ExternalGuest { Name = "洪嘉豪", Company = "（應徵者）", Email = "jiahao.hung@mail.example" },
                new ExternalGuest { Name = "鄭宇辰", Company = "（應徵者）", Email = null }
            };

            // 已確認 ×2
            _b["confirmed1"] = AddBooking("R3", "yating", OverlapTitleA, L(BD(1), "10:00"), L(BD(1), "11:30"), BookingStatus.Confirmed,
                "彙整本季各區業績、檢討達成率並討論第四季衝刺策略。", ["zhihao", "bohan", "hsinyi"], equip: ["投影機"]);
            _b["confirmed2"] = AddBooking("R5", "bohan", "新人到職訓練：辦公室系統介紹", L(BD(2), "15:00"), L(BD(2), "16:00"), BookingStatus.Confirmed,
                "說明門禁、電子郵件、請假與報帳系統操作。", ["hsinyi"], equip: ["電視"]);

            // 待審核 ×2（需審核的大型會議室）
            _b["pending1"] = AddBooking("R1", "zhihao", "年度客戶大會籌備會議", L(BD(3), "13:00"), L(BD(3), "15:00"), BookingStatus.Pending,
                "討論客戶大會場地、議程、邀請名單與預算分工。", ["yating", "bohan", "jiaming", "hsinyi"], equip: ["投影機", "視訊"]);
            _b["pending2"] = AddBooking("R2", "jiaming", "產品路線圖工作坊", L(BD(4), "09:30"), L(BD(4), "11:00"), BookingStatus.Pending,
                "與業務、研發共同盤點明年度產品路線圖與優先序。", ["hsinyi", "guanyu", "zhihao"], equip: ["投影機", "視訊"]);

            // 已取消 ×2
            _b["cancelled1"] = AddBooking("R3", "zhihao", "供應商議價會議", L(BD(2), "14:00"), L(BD(2), "15:00"), BookingStatus.Cancelled,
                "與主要供應商討論明年度採購價格。", ["yating"], note: "供應商臨時無法出席，改期另訂。");
            _b["cancelled2"] = AddBooking("R5", "hsinyi", "一對一績效面談", L(BD(-2), "10:00"), L(BD(-2), "11:00"), BookingStatus.Cancelled,
                "季度績效回饋。", ["jiaming"], note: "主管請假，面談順延。");

            // 已駁回 ×2
            _b["rejected1"] = AddBooking("R1", "bohan", "部門聚餐活動說明", L(BD(5), "09:00"), L(BD(5), "12:00"), BookingStatus.Rejected,
                "說明年終聚餐地點與報名方式。", ["hsinyi", "guanyu"], note: "非業務用途且佔用三小時，請改用中小型會議室。");
            _b["rejected2"] = AddBooking("R2", "yating", "跨部門團隊建立活動規劃", L(BD(-3), "14:00"), L(BD(-3), "17:00"), BookingStatus.Rejected,
                "規劃下季團康活動。", ["bohan"], note: "該時段天際會議室已保留給董事會使用。");

            // 已結束 ×2
            _b["finished1"] = AddBooking("R3", "jiaming", "每週研發站會與進度檢討", L(BD(-1), "09:00"), L(BD(-1), "10:30"), BookingStatus.Finished,
                "各組報告本週進度、風險與需要協助事項。", ["hsinyi", "guanyu"], equip: ["投影機"], note: "會議正常結束");
            _b["finished2"] = AddBooking("R5", "shufen", "客戶需求訪談：華信物流", L(BD(-3), "16:00"), L(BD(-3), "17:00"), BookingStatus.Finished,
                "訪談客戶倉儲管理需求。", ["zhihao"], note: "提前 10 分鐘結束，剩餘時段已釋出");

            // 未簽到釋出 ×2（劉冠宇）
            _b["released1"] = AddBooking("R3", "guanyu", "API 設計討論", L(BD(-2), "13:00"), L(BD(-2), "14:00"), BookingStatus.Released,
                "討論新版 API 命名與版本策略。", ["jiaming"]);
            _b["released2"] = AddBooking("R5", "guanyu", "資料庫索引優化討論", L(BD(-4), "11:00"), L(BD(-4), "12:00"), BookingStatus.Released,
                "分析慢查詢並規劃索引調整。", []);

            // 跨時段長會議 ×2（3~4 小時，由管理員建立直接確認）
            _b["long1"] = AddBooking("R1", "admin", "年度預算規劃工作坊", L(BD(6), "09:00"), L(BD(6), "12:00"), BookingStatus.Confirmed,
                "各部門提報明年度預算並進行跨部門協調。", ["zhihao", "jiaming", "bohan"], equip: ["投影機", "視訊", "白板"]);
            _b["long2"] = AddBooking("R2", "admin", "客戶端教育訓練（全日制後半場）", L(BD(7), "13:00"), L(BD(7), "17:00"), BookingStatus.Confirmed,
                "新版系統操作教育訓練，含實機演練。", ["yating", "hsinyi", "bohan", "zhihao", "jiaming"], equip: ["投影機", "視訊"]);

            // 今天進行中 ×2（以「現在」為錨點）
            var a = _anchor;
            _b["now1"] = AddBooking("R3", "zhihao", "客戶提案簡報演練", a.AddMinutes(-30), a.AddMinutes(60), BookingStatus.Confirmed,
                "針對明日客戶提案進行完整演練與 Q&A 模擬。", ["yating", "jiaming"], equip: ["投影機"]);
            _b["now2"] = AddBooking("R5", "bohan", "合約條款審閱", a.AddMinutes(-30), a.AddMinutes(30), BookingStatus.Confirmed,
                "與法務逐條確認供應商合約內容。", ["hsinyi"]);

            // 即將開始 ×2（約 30~60 分鐘後）
            _b["soon1"] = AddBooking("R1", "admin", "月度管理例會", a.AddMinutes(60), a.AddMinutes(120), BookingStatus.Confirmed,
                "各部門主管月度營運報告。", ["zhihao", "jiaming"], equip: ["投影機", "視訊"]);
            _b["soon2"] = AddBooking("R2", "bohan", "資安政策宣導", a.AddMinutes(90), a.AddMinutes(150), BookingStatus.Confirmed,
                "宣導近期資安事件與員工注意事項。", ["hsinyi", "guanyu"], equip: ["投影機"]);

            // 含外部訪客 ×2
            _b["guest1"] = AddBooking("R3", "yating", "廠商產品說明會：宏遠資訊", L(BD(3), "10:00"), L(BD(3), "11:00"), BookingStatus.Confirmed,
                "聽取廠商雲端備份方案簡報。", ["zhihao", "jiaming"], guestsVendor, equip: ["投影機"]);
            _b["guest2"] = AddBooking("R5", "jiaming", "面試：資深後端工程師", L(BD(4), "14:00"), L(BD(4), "15:00"), BookingStatus.Confirmed,
                "第二輪技術面試。", ["hsinyi"], [guestsInterview[0]]);

            // 多位參與者 ×2
            _b["multi1"] = AddBooking("R3", "jiaming", "新專案啟動會議：客戶入口網", L(BD(5), "14:00"), L(BD(5), "16:00"), BookingStatus.Confirmed,
                "確認專案範圍、里程碑與分工。", ["zhihao", "yating", "hsinyi", "guanyu", "bohan", "admin"], equip: ["投影機", "白板"]);
            _b["multi2"] = AddBooking("R3", "zhihao", "年度行銷企劃討論", L(BD(2), "10:00"), L(BD(2), "11:30"), BookingStatus.Confirmed,
                "討論明年度行銷預算與活動排程。", ["yating", "hsinyi", "jiaming", "bohan"], equip: ["投影機"]);

            // 其他一般預約（含刻意的「參與者時間衝突」示範：陳志豪同時受邀 Q4 業務檢討會與新人輔導）
            _b["misc1"] = AddBooking("R5", "hsinyi", OverlapTitleB, L(BD(1), "11:00"), L(BD(1), "12:00"), BookingStatus.Confirmed,
                "新進同仁的第一個月輔導與回饋。", ["zhihao"]);
            _b["misc2"] = AddBooking("R3", "bohan", "辦公室搬遷事前協調", L(BD(7), "09:00"), L(BD(7), "10:00"), BookingStatus.Confirmed,
                "確認樓層配置與搬遷時程。", ["yating"]);
            _b["misc3"] = AddBooking("R1", "zhihao", "季度業績頒獎與分享", L(BD(8), "14:00"), L(BD(8), "15:30"), BookingStatus.Confirmed,
                "表揚本季績優同仁並分享成功案例。", ["yating", "bohan", "hsinyi", "jiaming", "guanyu"], equip: ["投影機"]);

            BuildSeries();
            Notes.Add($"刻意示範的參與者衝突：「{OverlapTitleA}」與「{OverlapTitleB}」（陳志豪同時受邀，{BD(1):yyyy-MM-dd} 11:00–11:30）。");
        }

        private void BuildSeries()
        {
            var created = _nowUtc.AddDays(-30);

            // 每週一 09:30–10:30，晨曦會議室，研發部週會（起日為 4 週前的週一）
            var monday = _base;
            while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);
            var s1Start = monday.AddDays(-28);
            var s1 = Stamp(new BookingSeries
            {
                RoomId = _r["R4"].Id, OrganizerId = _u["jiaming"].Id, Title = "研發部週會",
                Description = "每週一固定的研發部進度同步與技術分享。",
                ParticipantIds = [_u["hsinyi"].Id, _u["guanyu"].Id], AttendeeCount = 3,
                RequiredEquipment = ["視訊"], Recurrence = RecurrenceType.Weekly,
                StartDate = s1Start, EndDate = s1Start.AddDays(7 * 14),
                DaysOfWeek = [DayOfWeek.Monday], StartTime = new TimeOnly(9, 30), EndTime = new TimeOnly(10, 30),
                Status = BookingStatus.Confirmed
            }, created);
            s1.History.Add(new BookingHistoryEntry { AtUtc = created, ActorId = s1.OrganizerId, ActorName = "張家銘", Action = "建立週期預約", Note = "每週一 09:30–10:30" });
            Set.BookingSeries.Add(s1);

            // 單次例外：下一個尚未發生的週一取消
            var nextMonday = _today.AddDays(1);
            while (nextMonday.DayOfWeek != DayOfWeek.Monday) nextMonday = nextMonday.AddDays(1);
            Set.BookingExceptions.Add(Stamp(new BookingException
            {
                SeriesId = s1.Id, OccurrenceDate = nextMonday, IsCancelled = true,
                Reason = "技術主管出差，該週暫停一次。", CreatedById = s1.OrganizerId
            }, _nowUtc.AddDays(-2)));

            // 雙週四 16:00–17:00，晨曦會議室，產品評審（管理員）
            var thursday = _base;
            while (thursday.DayOfWeek != DayOfWeek.Thursday) thursday = thursday.AddDays(-1);
            var s2Start = thursday.AddDays(-14);
            var s2 = Stamp(new BookingSeries
            {
                RoomId = _r["R4"].Id, OrganizerId = _admin.Id, Title = "雙週產品評審會",
                Description = "每兩週檢視產品進度與客戶回饋。",
                ParticipantIds = [_u["zhihao"].Id, _u["jiaming"].Id, _u["yating"].Id], AttendeeCount = 4,
                RequiredEquipment = ["電視"], Recurrence = RecurrenceType.BiWeekly,
                StartDate = s2Start, EndDate = s2Start.AddDays(7 * 20),
                DaysOfWeek = [DayOfWeek.Thursday], StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(17, 0),
                Status = BookingStatus.Confirmed
            }, created);
            s2.History.Add(new BookingHistoryEntry { AtUtc = created, ActorId = _admin.Id, ActorName = _admin.DisplayName, Action = "建立週期預約", Note = "每兩週週四 16:00–17:00" });
            Set.BookingSeries.Add(s2);
        }

        private void BuildViolations()
        {
            Violation V(string user, Booking? booking, string reason, DateTime at, bool revoked = false)
            {
                var v = Stamp(new Violation
                {
                    UserId = _u[user].Id, BookingId = booking?.Id, Reason = reason, Points = 1,
                    OccurredAtUtc = at, IsRevoked = revoked, RevokedById = revoked ? _admin.Id : null
                }, at);
                Set.Violations.Add(v);
                return v;
            }

            V("guanyu", _b["released2"], "預約未於簽到時窗內簽到，時段已自動釋出", _b["released2"].ReleasedAtUtc!.Value);
            V("guanyu", null, "使用會議室逾時未離開，影響下一場會議", _nowUtc.AddDays(-6));
            var last = V("guanyu", _b["released1"], "預約未於簽到時窗內簽到，時段已自動釋出", _b["released1"].ReleasedAtUtc!.Value);
            V("jiaming", null, "會議結束後未清理會議室（管理員已撤銷）", _nowUtc.AddDays(-10), revoked: true);

            var guanyu = _u["guanyu"];
            guanyu.ViolationPoints = 3;
            guanyu.SuspendedUntilUtc = last.OccurredAtUtc.AddDays(7).AddDays(0);
            // 讓暫停期限至少還有數天，便於示範「被暫停預約」
            if (guanyu.SuspendedUntilUtc < _nowUtc.AddDays(3)) guanyu.SuspendedUntilUtc = _nowUtc.AddDays(5);
        }

        private void BuildNotificationsAndEmails()
        {
            Notification N(string user, NotificationType type, string title, string msg, Booking? booking, int minutesAgo, bool read)
            {
                var at = _nowUtc.AddMinutes(-minutesAgo);
                var n = Stamp(new Notification
                {
                    UserId = _u[user].Id, Type = type, Title = title, Message = msg, BookingId = booking?.Id,
                    IsRead = read, ReadAtUtc = read ? at.AddMinutes(20) : null
                }, at);
                Set.Notifications.Add(n);
                return n;
            }

            var n1 = N("zhihao", NotificationType.Invited, "你被邀請參加會議", "王雅婷邀請你參加「Q4 業務檢討會」。", _b["confirmed1"], 2600, false);
            var n2 = N("hsinyi", NotificationType.Invited, "你被邀請參加會議", "黃柏翰邀請你參加「新人到職訓練」。", _b["confirmed2"], 2400, true);
            var n3 = N("zhihao", NotificationType.BookingApproved, "預約已核准", "「季度業績頒獎與分享」已核准，雲海會議室時段已為你保留。", _b["misc3"], 180, false);
            var n4 = N("admin", NotificationType.BookingApproved, "有新的預約待審核", "陳志豪申請雲海會議室：年度客戶大會籌備會議。", _b["pending1"], 175, false);
            var n5 = N("bohan", NotificationType.BookingRejected, "預約已被駁回", "「部門聚餐活動說明」已駁回：非業務用途且佔用三小時，請改用中小型會議室。", _b["rejected1"], 900, true);
            var n6 = N("yating", NotificationType.BookingRejected, "預約已被駁回", "「跨部門團隊建立活動規劃」已駁回：該時段已保留給董事會使用。", _b["rejected2"], 4300, true);
            var n7 = N("yating", NotificationType.BookingCancelled, "會議已被取消", "陳志豪取消了「供應商議價會議」：供應商臨時無法出席，改期另訂。", _b["cancelled1"], 1300, false);
            var n8 = N("jiaming", NotificationType.BookingCancelled, "會議已被取消", "李欣怡取消了「一對一績效面談」：主管請假，面談順延。", _b["cancelled2"], 5800, true);
            var n9 = N("zhihao", NotificationType.Reminder, "會議即將開始", "「客戶提案簡報演練」將於 15 分鐘後在星河會議室開始。", _b["now1"], 45, true);
            var n10 = N("guanyu", NotificationType.AutoReleased, "預約時段已釋出", "你的預約「API 設計討論」因開始後 15 分鐘內未簽到，時段已自動釋出。", _b["released1"], 2900, true);
            var n11 = N("guanyu", NotificationType.AutoReleased, "預約時段已釋出", "你的預約「資料庫索引優化討論」因未簽到，時段已自動釋出。", _b["released2"], 5700, true);
            var n12 = N("guanyu", NotificationType.Violation, "違規記點通知", "你因未簽到被記點 1 點，目前累計 3 點。", _b["released1"], 2899, true);
            var n13 = N("guanyu", NotificationType.Suspended, "預約權限已暫停", "你的違規點數已達上限，預約權限暫停至 " + _time.ToLocal(_u["guanyu"].SuspendedUntilUtc!.Value).ToString("yyyy/MM/dd HH:mm") + "。", null, 2898, false);
            var n14 = N("hsinyi", NotificationType.Invited, "你被邀請參加會議", "張家銘邀請你參加「面試：資深後端工程師」。", _b["guest2"], 600, false);
            var n15 = N("bohan", NotificationType.Invited, "你被邀請參加會議", "林怡君邀請你參加「月度管理例會」。", _b["soon1"], 300, false);

            void E(Notification n, string fileSuffix, bool ok = true, string? error = null)
            {
                var user = Set.Users.First(u => u.Id == n.UserId);
                var sentAt = n.CreatedAt.AddSeconds(30);
                Set.EmailLogs.Add(Stamp(new EmailLog
                {
                    UserId = user.Id, ToEmail = user.Email, Subject = $"[RoomBook] {n.Title}", Type = n.Type, SentAtUtc = sentAt,
                    FileName = $"{sentAt:yyyyMMdd-HHmmss}-{fileSuffix}.eml", Success = ok, Error = error
                }, sentAt));
            }

            E(n1, "a1b2c3"); E(n2, "d4e5f6"); E(n5, "0a9b8c"); E(n6, "7d6e5f");
            E(n7, "4a3b2c"); E(n10, "1f2e3d"); E(n13, "5c6b7a");
            E(n14, "9e8d7c", false, "模擬寄送失敗：收件匣已滿（示範資料）");
        }

        private void BuildAuditLogs()
        {
            var ips = new[] { "192.168.10.21", "192.168.10.35", "192.168.10.48", "192.168.10.12" };
            void A(string user, string action, string type, string? target, string? detail, int minutesAgo)
            {
                var u = _u[user];
                var at = _nowUtc.AddMinutes(-minutesAgo);
                Set.AuditLogs.Add(Stamp(new AuditLog
                {
                    UserId = u.Id, UserName = u.DisplayName, Action = action, TargetType = type, TargetId = target, Detail = detail,
                    IpAddress = ips[_rng.Next(ips.Length)], OccurredAtUtc = at
                }, at));
            }

            A("admin", "登入", nameof(User), _admin.Id.ToString(), null, 9000);
            A("yating", "建立預約", nameof(Booking), _b["confirmed1"].BookingNo, "星河會議室 / Q4 業務檢討會", 8700);
            A("bohan", "建立預約", nameof(Booking), _b["confirmed2"].BookingNo, "靜心討論室 / 新人到職訓練", 8500);
            A("admin", "核准預約", nameof(Booking), _b["confirmed2"].BookingNo, null, 8400);
            A("admin", "駁回預約", nameof(Booking), _b["rejected2"].BookingNo, "該時段已保留給董事會使用", 4290);
            A("admin", "駁回預約", nameof(Booking), _b["rejected1"].BookingNo, "非業務用途且佔用三小時", 880);
            A("zhihao", "建立預約", nameof(Booking), _b["pending1"].BookingNo, "雲海會議室，待審核", 190);
            A("jiaming", "建立預約", nameof(Booking), _b["pending2"].BookingNo, "天際會議室，待審核", 150);
            A("admin", "停用使用者", nameof(User), _u["shufen"].Id.ToString(), "吳淑芬離職，帳號停用", 7200);
            A("admin", "修改設定", nameof(AppSetting), SettingKeys.ViolationPointLimit, "違規上限維持 3 點", 6900);
            A("admin", "會議室設為維護中", nameof(Room), _r["R6"].Id.ToString(), "拾光討論室空調維修", 6600);
            A("admin", "新增假日", nameof(Holiday), null, "彈性放假日", 6000);
            A("admin", "重設密碼", nameof(User), _u["bohan"].Id.ToString(), "黃柏翰忘記密碼", 3000);
            A("admin", "暫停使用者預約", nameof(User), _u["guanyu"].Id.ToString(), "違規累計 3 點", 2898);
            A("jiaming", "撤銷違規點數", nameof(Violation), null, "管理員撤銷 1 點", 1500);
        }

        // ---------- 自我驗證 ----------
        private sealed record Slot(Guid RoomId, DateTime StartUtc, DateTime EndUtc, string Title, IReadOnlyList<Guid> People, bool Confirmed, string Source);

        public List<string> Validate()
        {
            var errors = new List<string>();
            var users = Set.Users.ToDictionary(u => u.Id);
            var rooms = Set.Rooms.ToDictionary(r => r.Id);
            var bookings = Set.Bookings.ToDictionary(b => b.Id);
            var depts = Set.Departments.ToDictionary(d => d.Id);
            var holidays = Set.Holidays.Select(h => h.Date).ToHashSet();
            var hours = Set.BusinessHours.ToDictionary(h => h.Day);

            void Ref(Guid id, string kind, string who)
            {
                var ok = kind switch
                {
                    "user" => users.ContainsKey(id),
                    "room" => rooms.ContainsKey(id),
                    "booking" => bookings.ContainsKey(id),
                    "dept" => depts.ContainsKey(id),
                    _ => false
                };
                if (!ok) errors.Add($"參照不存在：{who} → {kind} {id}");
            }

            foreach (var u in Set.Users) Ref(u.DepartmentId, "dept", $"使用者 {u.DisplayName}");
            foreach (var u in Set.Users) foreach (var rid in u.FavoriteRoomIds) Ref(rid, "room", $"使用者 {u.DisplayName} 常用會議室");
            foreach (var b in Set.Bookings)
            {
                Ref(b.RoomId, "room", $"預約 {b.BookingNo}");
                Ref(b.OrganizerId, "user", $"預約 {b.BookingNo} 預約人");
                foreach (var p in b.ParticipantIds) Ref(p, "user", $"預約 {b.BookingNo} 參與者");
                if (rooms.TryGetValue(b.RoomId, out var room) && b.AttendeeCount > room.Capacity)
                    errors.Add($"預約 {b.BookingNo} 人數 {b.AttendeeCount} 超過 {room.Name} 容量 {room.Capacity}");
            }
            foreach (var s in Set.BookingSeries)
            {
                Ref(s.RoomId, "room", $"週期預約 {s.Title}");
                Ref(s.OrganizerId, "user", $"週期預約 {s.Title} 預約人");
                foreach (var p in s.ParticipantIds) Ref(p, "user", $"週期預約 {s.Title} 參與者");
            }
            foreach (var n in Set.Notifications) { Ref(n.UserId, "user", "通知"); if (n.BookingId is { } nb) Ref(nb, "booking", "通知"); }
            foreach (var v in Set.Violations) { Ref(v.UserId, "user", "違規"); if (v.BookingId is { } vb) Ref(vb, "booking", "違規"); }
            foreach (var e in Set.EmailLogs) if (e.UserId is { } eu) Ref(eu, "user", "Email 紀錄");
            foreach (var a in Set.AuditLogs) if (a.UserId is { } au) Ref(au, "user", "稽核紀錄");
            foreach (var x in Set.BookingExceptions)
                if (!Set.BookingSeries.Any(s => s.Id == x.SeriesId)) errors.Add("單次例外參照不存在的週期預約");

            // 收集佔用時段（Confirmed / Pending 的預約 + 週期展開，排除假日與單次取消）
            var slots = new List<Slot>();
            foreach (var b in Set.Bookings.Where(b => b.Status is BookingStatus.Confirmed or BookingStatus.Pending))
            {
                var people = b.ParticipantIds.Append(b.OrganizerId).Distinct().ToList();
                slots.Add(new Slot(b.RoomId, b.StartUtc, b.EndUtc, b.Title, people, b.Status == BookingStatus.Confirmed, b.BookingNo));
                if (rooms.TryGetValue(b.RoomId, out var room) && room.Status != RoomStatus.Active)
                    errors.Add($"預約 {b.BookingNo} 使用了非啟用中的會議室 {room.Name}");
            }
            foreach (var s in Set.BookingSeries.Where(s => s.Status is BookingStatus.Confirmed or BookingStatus.Pending))
            {
                var people = s.ParticipantIds.Append(s.OrganizerId).Distinct().ToList();
                foreach (var date in RecurrenceExpander.Expand(s))
                {
                    if (holidays.Contains(date)) continue;
                    var ex = Set.BookingExceptions.FirstOrDefault(x => x.SeriesId == s.Id && x.OccurrenceDate == date);
                    if (ex is { IsCancelled: true }) continue;
                    var start = ex?.NewStartUtc ?? U(date.ToDateTime(s.StartTime));
                    var end = ex?.NewEndUtc ?? U(date.ToDateTime(s.EndTime));
                    slots.Add(new Slot(s.RoomId, start, end, s.Title, people, true, $"週期:{s.Title} {date:yyyy-MM-dd}"));
                }
            }

            foreach (var sl in slots)
            {
                var ls = _time.ToLocal(sl.StartUtc);
                var le = _time.ToLocal(sl.EndUtc);
                if (ls.Date != le.Date) errors.Add($"{sl.Source}「{sl.Title}」跨午夜");
                if (!hours.TryGetValue(ls.DayOfWeek, out var h) || !h.IsOpen)
                    errors.Add($"{sl.Source}「{sl.Title}」不在營業日（{ls:yyyy-MM-dd ddd}）");
                else if (TimeOnly.FromDateTime(ls) < h.OpenTime || TimeOnly.FromDateTime(le) > h.CloseTime)
                    errors.Add($"{sl.Source}「{sl.Title}」超出營業時段（{ls:yyyy-MM-dd HH:mm}–{le:HH:mm}）");
                if (holidays.Contains(DateOnly.FromDateTime(ls)))
                    errors.Add($"{sl.Source}「{sl.Title}」落在假日");
                if ((sl.EndUtc - sl.StartUtc).TotalHours > MaxDurationHours)
                    errors.Add($"{sl.Source}「{sl.Title}」超過單次最長 {MaxDurationHours} 小時");
                if (sl.EndUtc <= sl.StartUtc) errors.Add($"{sl.Source}「{sl.Title}」結束時間不晚於開始時間");
            }

            // 會議室衝突（左閉右開）
            foreach (var group in slots.GroupBy(s => s.RoomId))
            {
                var list = group.OrderBy(s => s.StartUtc).ToList();
                for (var i = 0; i < list.Count; i++)
                    for (var j = i + 1; j < list.Count && list[j].StartUtc < list[i].EndUtc; j++)
                        errors.Add($"會議室衝突（{rooms[group.Key].Name}）：{list[i].Source}「{list[i].Title}」與 {list[j].Source}「{list[j].Title}」");
            }

            // 參與者衝突：只允許刻意示範的那一組
            var confirmed = slots.Where(s => s.Confirmed).ToList();
            for (var i = 0; i < confirmed.Count; i++)
                for (var j = i + 1; j < confirmed.Count; j++)
                {
                    var x = confirmed[i]; var y = confirmed[j];
                    if (x.StartUtc >= y.EndUtc || y.StartUtc >= x.EndUtc) continue;
                    var shared = x.People.Intersect(y.People).ToList();
                    if (shared.Count == 0) continue;
                    var intentional = (x.Title == OverlapTitleA && y.Title == OverlapTitleB) || (x.Title == OverlapTitleB && y.Title == OverlapTitleA);
                    if (!intentional)
                        errors.Add($"非預期的參與者衝突：{string.Join("、", shared.Select(id => users[id].DisplayName))} 同時在「{x.Title}」與「{y.Title}」");
                }

            // 單號唯一
            var dup = Set.Bookings.GroupBy(b => b.BookingNo).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0) errors.Add("單號重複：" + string.Join(", ", dup));

            // Id 唯一
            var allIds = new List<Guid>();
            allIds.AddRange(Set.Users.Select(x => x.Id)); allIds.AddRange(Set.Rooms.Select(x => x.Id));
            allIds.AddRange(Set.Bookings.Select(x => x.Id)); allIds.AddRange(Set.Departments.Select(x => x.Id));
            allIds.AddRange(Set.Notifications.Select(x => x.Id)); allIds.AddRange(Set.AuditLogs.Select(x => x.Id));
            if (allIds.Count != allIds.Distinct().Count()) errors.Add("Id 重複");

            // 數量摘要（供除錯）
            Notes.Add($"預約 {Set.Bookings.Count} 筆 + 週期 {Set.BookingSeries.Count} 組；通知 {Set.Notifications.Count}；Email {Set.EmailLogs.Count}；違規 {Set.Violations.Count}；稽核 {Set.AuditLogs.Count}。");
            return errors;
        }
    }
}
