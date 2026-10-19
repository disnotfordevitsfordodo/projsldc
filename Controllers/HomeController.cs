using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly Db _db;
    public HomeController(Db db) => _db = db;

    // หน้าแรก: การ์ดสรุป + ตารางรายอำเภอ + สัดส่วนเพศ + บัตรสวัสดิการ
    public async Task<IActionResult> Index(int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var vm = new DashboardVm
        {
            Year = year,
            Summary = await c.QueryFirstOrDefaultAsync<PopRow>(
                "SELECT * FROM v_summary_province WHERE survey_year=@year", new { year }, commandTimeout: 120) ?? new PopRow(),
            Amphurs = (await c.QueryAsync<PopRow>(
                "SELECT * FROM v_population_by_amphur WHERE survey_year=@year ORDER BY amphur_id", new { year }, commandTimeout: 120)).ToList(),
            Welfare = await c.QueryFirstOrDefaultAsync<WelfareRow>(@"
                SELECT CAST(COALESCE(SUM(group1_card_holder),0) AS SIGNED) AS group1_card_holder,
                       CAST(COALESCE(SUM(group2_missed),0)      AS SIGNED) AS group2_missed,
                       CAST(COALESCE(SUM(group3_not_target),0)  AS SIGNED) AS group3_not_target,
                       CAST(COALESCE(SUM(waiting_survey),0)     AS SIGNED) AS waiting_survey,
                       CAST(COALESCE(SUM(total),0)              AS SIGNED) AS total
                FROM v_welfare_by_tumbol WHERE survey_year=@year", new { year }, commandTimeout: 120) ?? new WelfareRow()
        };
        return View(vm);
    }

    // ข้อมูลระดับตำบล ของอำเภอที่เลือก
    public async Task<IActionResult> Tambon(string amphurId, int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var rows = (await c.QueryAsync<PopRow>(
            "SELECT * FROM v_population_by_tumbol WHERE survey_year=@year AND amphur_id=@amphurId ORDER BY population DESC",
            new { year, amphurId })).ToList();
        ViewBag.Year = year;
        ViewBag.AmphurName = rows.FirstOrDefault()?.AmphurName ?? amphurId;
        return View(rows);
    }

    // ข้อมูลระดับหมู่บ้าน ของตำบลที่เลือก
    public async Task<IActionResult> Village(string tumbolId, int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var rows = (await c.QueryAsync<PopRow>(
            "SELECT * FROM v_population_by_village WHERE survey_year=@year AND tumbol_id=@tumbolId ORDER BY moo",
            new { year, tumbolId })).ToList();
        ViewBag.Year = year;
        ViewBag.AmphurId = rows.FirstOrDefault()?.AmphurId;
        ViewBag.AmphurName = rows.FirstOrDefault()?.AmphurName;
        ViewBag.TumbolName = rows.FirstOrDefault()?.TumbolName ?? tumbolId;
        return View(rows);
    }

    [AllowAnonymous]
    public IActionResult Error(int? code) => View();
}
