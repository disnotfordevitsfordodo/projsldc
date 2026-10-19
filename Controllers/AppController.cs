using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

[Authorize]
public abstract class AppController : Controller
{
    protected readonly Db _db;
    protected AppController(Db db) => _db = db;

    protected static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    protected string? Role => User.FindFirst(ClaimTypes.Role)?.Value;
    protected bool IsAdmin => Role == "admin";
    protected int? UserId => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var i) ? i : null;

    // จำกัดสิทธิ์: amphur เห็นเฉพาะอำเภอตัวเอง, staff เห็นเฉพาะตำบลตัวเอง
    protected (string? amphur, string? tumbol) Scope(string? a, string? t)
    {
        a = Norm(a); t = Norm(t);
        if (Role == "amphur") a = User.FindFirst("amphur_id")?.Value;
        else if (Role == "staff") { t = User.FindFirst("tumbol_id")?.Value; a = null; }
        return (a, t);
    }

    protected static (int page, int pages) PageInfo(long total, int page, int size)
    {
        var pages = (int)Math.Max(1, (total + size - 1) / size);
        return (Math.Clamp(page, 1, pages), pages);
    }

    protected async Task<List<Opt>> Amphurs(IDbConnection c) =>
        (await c.QueryAsync<Opt>("SELECT amphur_id AS Id, name_th AS Name FROM m_amphur WHERE is_active=1 ORDER BY amphur_id")).ToList();

    protected async Task<List<Opt>> Tumbols(IDbConnection c, string? amphurId) =>
        amphurId == null ? new List<Opt>()
        : (await c.QueryAsync<Opt>("SELECT tumbol_id AS Id, name_th AS Name FROM m_tumbol WHERE amphur_id=@amphurId AND is_active=1 ORDER BY tumbol_id", new { amphurId })).ToList();

    protected async Task Log(IDbConnection c, string action, string? target = null, string? details = null)
    {
        try
        {
            await c.ExecuteAsync(@"INSERT INTO web_system_logs(user_id,username,action_type,target_id,details,ip_address)
                VALUES(@uid,@uname,@action,@target,@details,@ip)",
                new { uid = UserId, uname = User.FindFirst("username")?.Value, action, target, details, ip = HttpContext.Connection.RemoteIpAddress?.ToString() });
        }
        catch { /* log ล้มเหลวต้องไม่ทำให้หน้าหลักพัง */ }
    }

    // ---------- ข้อความแจ้งผล (แสดงใน _Layout) ----------
    protected void Flash(string msg) => TempData["Ok"] = msg;
    protected void FlashErr(string msg) => TempData["Err"] = msg;

    // กลับไปหน้าเดิมเฉพาะ URL ภายในเว็บเท่านั้น (กัน open redirect)
    protected IActionResult Back(string? returnUrl, IActionResult fallback) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : fallback;

    // ---------- สิทธิ์ตามพื้นที่ (ใช้ตรวจก่อนเพิ่ม/แก้ไข/ลบทุกครั้ง) ----------
    protected async Task<(string? a, string? t)> AllowedAreaAsync(IDbConnection c)
    {
        var (a, t) = Scope(null, null);
        if (t != null && a == null)
            a = await c.ExecuteScalarAsync<string?>("SELECT amphur_id FROM m_tumbol WHERE tumbol_id=@t", new { t });
        return (a, t);
    }

    protected async Task<bool> VillageAllowedAsync(IDbConnection c, string? villageId)
    {
        if (string.IsNullOrEmpty(villageId)) return false;
        var (a, t) = Scope(null, null);
        return await c.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM m_village v JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE v.village_id=@villageId AND v.is_active=1 AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)",
            new { villageId, a, t }) > 0;
    }

    protected async Task<bool> HouseAllowedAsync(IDbConnection c, long houseId)
    {
        var (a, t) = Scope(null, null);
        return await c.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM t_house h
            JOIN m_village v ON v.village_id=h.village_id JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE h.house_id=@houseId AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)",
            new { houseId, a, t }) > 0;
    }

    // คืน house_id ของสมาชิก ถ้าสมาชิกอยู่ในพื้นที่ที่มีสิทธิ์ (ไม่เช็ค is_active)
    protected async Task<long?> MemberHouseAsync(IDbConnection c, long memberId)
    {
        var (a, t) = Scope(null, null);
        return await c.ExecuteScalarAsync<long?>(@"SELECT m.house_id FROM t_house_member m
            JOIN t_house h ON h.house_id=m.house_id
            JOIN m_village v ON v.village_id=h.village_id JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE m.member_id=@memberId AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)",
            new { memberId, a, t });
    }

    protected async Task<int?> HouseYearAsync(IDbConnection c, long houseId) =>
        await c.ExecuteScalarAsync<int?>("SELECT survey_year FROM t_house WHERE house_id=@houseId", new { houseId });

    protected async Task<int?> MemberYearAsync(IDbConnection c, long memberId) =>
        await c.ExecuteScalarAsync<int?>(@"SELECT h.survey_year FROM t_house_member m JOIN t_house h ON h.house_id=m.house_id
            WHERE m.member_id=@memberId", new { memberId });

    // ---------- รายการตัวเลือก ----------
    protected static async Task<List<Opt>> HouseOptsAsync(IDbConnection c, string villageId, int year) =>
        (await c.QueryAsync<Opt>(@"SELECT CAST(house_id AS CHAR) AS Id,
                CONCAT(house_code, IF(house_no IS NULL OR house_no='', '', CONCAT(' (บ้านเลขที่ ', house_no, ')'))) AS Name
            FROM t_house WHERE village_id=@villageId AND survey_year=@year AND is_active=1
            ORDER BY house_code LIMIT 500", new { villageId, year })).ToList();

    protected static async Task<List<Opt>> MemberOptsAsync(IDbConnection c, long houseId) =>
        (await c.QueryAsync<Opt>(@"SELECT CAST(m.member_id AS CHAR) AS Id,
                CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,''),
                       IF(m.age IS NULL, '', CONCAT(' (อายุ ', m.age, ' ปี)'))) AS Name
            FROM t_house_member m LEFT JOIN m_prefix p ON p.prefix_id = m.prefix_id
            WHERE m.house_id=@houseId AND m.is_active=1 ORDER BY m.member_id", new { houseId })).ToList();

    protected static async Task<List<Opt>> SimpleOptsAsync(IDbConnection c, string table, string idCol, string nameCol, bool onlyActive)
    {
        // table / column มาจากโค้ดภายในเท่านั้น (ไม่ใช้ค่าจากผู้ใช้)
        var sql = $"SELECT CAST({idCol} AS CHAR) AS Id, {nameCol} AS Name FROM {table} " +
                  (onlyActive ? "WHERE is_active=1 " : "") + $"ORDER BY {idCol}";
        return (await c.QueryAsync<Opt>(sql)).ToList();
    }

    // ตำแหน่งของสมาชิก (ใช้เติมตัวเลือกพื้นที่ล่วงหน้า)
    protected static async Task<PickerVm?> ResolveMemberPickAsync(IDbConnection c, long memberId) =>
        await c.QueryFirstOrDefaultAsync<PickerVm>(@"SELECT h.survey_year AS Year, tb.amphur_id AS AmphurId, v.tumbol_id AS TumbolId,
                h.village_id AS VillageId, h.house_id AS HouseId, m.member_id AS MemberId
            FROM t_house_member m JOIN t_house h ON h.house_id=m.house_id
            JOIN m_village v ON v.village_id=h.village_id JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE m.member_id=@memberId", new { memberId });

    // เติมรายการตัวเลือกให้ _Picker (ปี > อำเภอ > ตำบล > หมู่บ้าน > บ้าน > บุคคล) ตามสิทธิ์ผู้ใช้
    protected async Task<PickerVm> BuildPickerAsync(IDbConnection c, PickerVm p)
    {
        var (a, t) = await AllowedAreaAsync(c);
        if (a != null && string.IsNullOrEmpty(p.AmphurId)) p.AmphurId = a;
        if (t != null && string.IsNullOrEmpty(p.TumbolId)) p.TumbolId = t;
        if (p.Year == 0) p.Year = await _db.YearAsync(c, 0);
        p.Amphurs = (await Amphurs(c)).Where(x => a == null || x.Id == a).ToList();
        p.Tumbols = (await Tumbols(c, Norm(p.AmphurId))).Where(x => t == null || x.Id == t).ToList();
        p.Villages = p.TumbolId == null ? new() : (await c.QueryAsync<Opt>(
            "SELECT village_id AS Id, CONCAT('หมู่ ', moo, ' ', name_th) AS Name FROM m_village WHERE tumbol_id=@t AND is_active=1 ORDER BY moo",
            new { t = p.TumbolId })).ToList();
        if (p.ShowHouse && p.VillageId != null) p.Houses = await HouseOptsAsync(c, p.VillageId, p.Year);
        if (p.ShowMember && p.HouseId != null) p.Members = await MemberOptsAsync(c, p.HouseId.Value);
        return p;
    }
}
