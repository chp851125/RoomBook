using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using RoomBook.Application.Services;
using RoomBook.Infrastructure.Background;
using RoomBook.Infrastructure.Email;
using RoomBook.Infrastructure.Json;
using RoomBook.Infrastructure.Security;
using RoomBook.Infrastructure.Seed;
using RoomBook.Infrastructure.Time;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) => config
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "roombook-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

    // ---- 基礎設施：時間、JSON Repository、範例資料 ----
    builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
    builder.Services.AddSingleton(DataPaths.Create(builder.Environment.ContentRootPath));
    builder.Services.AddSingleton<JsonRepositoryRegistry>();
    builder.Services.AddSingleton(typeof(IRepository<>), typeof(JsonFileRepository<>));
    builder.Services.AddSingleton<SeedDataGenerator>();
    builder.Services.AddSingleton<DataBootstrapper>();
    builder.Services.AddSingleton<IDataResetService, DataResetService>();
    builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

    // ---- 應用服務（PageModel 只依賴這一層）----
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddScoped<ISettingService, SettingService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IAuditService, AuditService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<INotificationService, NotificationService>();
    builder.Services.AddSingleton<RoomLockProvider>();
    builder.Services.AddScoped<IBusinessCalendarService, BusinessCalendarService>();
    builder.Services.AddScoped<IBookingQueryService, BookingQueryService>();
    builder.Services.AddScoped<IRoomService, RoomService>();
    builder.Services.AddScoped<IBookingService, BookingService>();
    builder.Services.AddScoped<BookingNumberService>();
    builder.Services.AddScoped<IViolationService, ViolationService>();
    builder.Services.AddScoped<IBookingActionService, BookingActionService>();
    builder.Services.AddScoped<IMyBookingService, MyBookingService>();
    builder.Services.AddScoped<IIcsService, IcsService>();
    builder.Services.AddScoped<IRoomAdminService, RoomAdminService>();
    builder.Services.AddScoped<IUserAdminService, UserAdminService>();
    builder.Services.AddScoped<IAccountService, AccountService>();
    builder.Services.AddScoped<ISettingAdminService, SettingAdminService>();
    builder.Services.AddScoped<IScheduleAdminService, ScheduleAdminService>();
    builder.Services.AddScoped<IAdminBookingService, AdminBookingService>();
    builder.Services.AddScoped<IStatsService, StatsService>();
    builder.Services.AddScoped<IReportService, ReportService>();
    builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
    builder.Services.AddSingleton<IEmailSender, EmlEmailSender>();
    builder.Services.AddHostedService<BookingMaintenanceWorker>();

    // ---- 驗證與授權：Cookie + 角色 ----
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = "RoomBook.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = async context =>
            {
                // 每次請求確認使用者仍存在、仍啟用、角色未變動；否則強制重新登入
                var users = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                var idText = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var role = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var user = Guid.TryParse(idText, out var id) ? users.GetById(id) : null;
                if (user is null || !user.IsActive || user.Role.ToString() != role)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
        });

    builder.Services.AddAuthorization(options =>
        options.AddPolicy(AuthConstants.AdminPolicy, policy => policy.RequireRole(AuthConstants.AdminRole)));

    builder.Services.AddHealthChecks().AddCheck<DataHealthCheck>("data");

    builder.Services.AddRazorPages(options =>
    {
        // 預設所有頁面都要登入；Admin 資料夾限管理員（PageModel 上另有 [Authorize] 雙重保護）
        options.Conventions.AuthorizeFolder("/");
        options.Conventions.AuthorizeFolder("/Admin", AuthConstants.AdminPolicy);
        options.Conventions.AllowAnonymousToPage("/Account/Login");
        options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
        options.Conventions.AllowAnonymousToPage("/Error");
        options.Conventions.AllowAnonymousToFolder("/Display");
    });

    var app = builder.Build();

    // 確保 App_Data 有資料（首次啟動從 SeedData 複製）
    app.Services.GetRequiredService<DataBootstrapper>().EnsureData();

    // 命令列：dotnet run -- --generate-seed（僅 Development）重新產生 SeedData/*.json
    if (args.Contains("--generate-seed"))
    {
        if (!app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("--generate-seed 僅限 Development 環境。");
            return 1;
        }
        var paths = app.Services.GetRequiredService<DataPaths>();
        var result = app.Services.GetRequiredService<SeedDataGenerator>().Generate(paths.SeedDirectory);
        foreach (var note in result.Notes) Console.WriteLine("備註：" + note);
        if (!result.Success)
        {
            foreach (var error in result.Errors) Console.Error.WriteLine("驗證失敗：" + error);
            return 2;
        }
        Console.WriteLine($"範例資料已產生至 {paths.SeedDirectory}，自我驗證通過。");
        return 0;
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseSerilogRequestLogging();
    app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
    app.UseHttpsRedirection();

    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "same-origin";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // 所有前端資源都在本站（不依賴 CDN）；頁面仍有少量 inline script／style，故保留 unsafe-inline
        headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'self'; form-action 'self'";
        await next();
    });

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets();
    app.MapRazorPages().WithStaticAssets();
    app.MapHealthChecks("/health").AllowAnonymous();

    app.Run();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "應用程式啟動失敗");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
