using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

public class AccountController : Controller
{
    private readonly Db _db;
    public AccountController(Db db) => _db = db;

    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        using var c = _db.Open();
        var u = await c.QueryFirstOrDefaultAsync<UserRow>(
            "SELECT * FROM web_users WHERE username=@username AND is_active=1", new { username });

        if (u == null || !BCrypt.Net.BCrypt.Verify(password ?? "", u.PasswordHash))
        {
            ViewBag.Error = "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง";
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.UserId.ToString()),
            new(ClaimTypes.Name, u.FullName ?? u.Username),
            new(ClaimTypes.Role, u.Role),
            new("username", u.Username)
        };
        if (u.AmphurId != null) claims.Add(new Claim("amphur_id", u.AmphurId));
        if (u.TumbolId != null) claims.Add(new Claim("tumbol_id", u.TumbolId));

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        await c.ExecuteAsync(
            "INSERT INTO web_system_logs(user_id,username,action_type,ip_address) VALUES(@id,@username,'login',@ip)",
            new { id = u.UserId, username = u.Username, ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

        return RedirectToAction("Index", "Home");
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // เปลี่ยนรหัสผ่านของตนเอง
    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string? current, string? newPassword, string? confirm)
    {
        var uid = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var i) ? i : 0;
        using var c = _db.Open();
        var u = await c.QueryFirstOrDefaultAsync<UserRow>("SELECT * FROM web_users WHERE user_id=@uid AND is_active=1", new { uid });
        if (u == null) return RedirectToAction("Login");

        string? err = null;
        if (!BCrypt.Net.BCrypt.Verify(current ?? "", u.PasswordHash)) err = "รหัสผ่านปัจจุบันไม่ถูกต้อง";
        else if ((newPassword ?? "").Length < 8) err = "รหัสผ่านใหม่ต้องมีอย่างน้อย 8 ตัวอักษร";
        else if (newPassword != confirm) err = "ยืนยันรหัสผ่านใหม่ไม่ตรงกัน";
        else if (newPassword == current) err = "รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสผ่านเดิม";
        if (err != null) { ViewBag.Error = err; return View(); }

        await c.ExecuteAsync("UPDATE web_users SET password_hash=@hash WHERE user_id=@uid",
            new { hash = BCrypt.Net.BCrypt.HashPassword(newPassword!), uid });
        await c.ExecuteAsync("INSERT INTO web_system_logs(user_id,username,action_type,ip_address) VALUES(@uid,@username,'change_password',@ip)",
            new { uid, username = u.Username, ip = HttpContext.Connection.RemoteIpAddress?.ToString() });
        TempData["Ok"] = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
        return RedirectToAction("Index", "Home");
    }
}
