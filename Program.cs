using Dapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using SLDC.Web.Data;

// โฟลเดอร์เก็บรูปภาพบ้านที่อัปโหลด (ต้องมีก่อนสร้าง host เพื่อให้ UseStaticFiles ให้บริการ /uploads ได้)
Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "houses"));

var builder = WebApplication.CreateBuilder(args);

// map snake_case (survey_year) -> PascalCase (SurveyYear) อัตโนมัติ
DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<Db>();
builder.Services.AddMemoryCache();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Home/Error";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

var app = builder.Build();

// เตรียมผู้ใช้เริ่มต้น: ถ้ารหัสผ่านเป็นค่าจำลองจาก seed ($2b$12$AAAA...) จะรีเซ็ตเป็น Admin@1234
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Db>();
    using var c = db.Open();
    var hash = BCrypt.Net.BCrypt.HashPassword("Admin@1234");
    c.Execute("UPDATE web_users SET password_hash=@hash WHERE password_hash LIKE @fake", new { hash, fake = "$2b$12$AAAA%" });
    if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM web_users") == 0)
        c.Execute("INSERT INTO web_users(username,password_hash,full_name,role) VALUES('admin',@hash,'Administrator','admin')", new { hash });
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "เชื่อมต่อฐานข้อมูลไม่ได้ตอนเริ่มระบบ ตรวจ ConnectionStrings:Default ใน appsettings.json");
}

app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();
app.Run();
