using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

public class ReportController : AppController
{
    public ReportController(Db db) : base(db) { }

    // ---------- ข้อมูลประชากร (รายอำเภอ) ----------
    public async Task<IActionResult> Population(int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var rows = (await c.QueryAsync<PopRow>(
            "SELECT * FROM v_population_by_amphur WHERE survey_year=@year ORDER BY amphur_id", new { year })).ToList();
        ViewBag.Year = year;
        return View(rows);
    }

    // ---------- ครัวเรือน ----------
    public async Task<IActionResult> Household(int year = 0, string? amphurId = null, string? tumbolId = null,
                                               string? villageId = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (amp, tum) = Scope(amphurId, tumbolId);
        q = Norm(q); villageId = Norm(villageId);

        var p = new DynamicParameters();
        p.Add("year", year);
        var w = new List<string> { "h.is_active=1", "h.survey_year=@year" };
        if (amp != null) { w.Add("a.amphur_id=@amp"); p.Add("amp", amp); }
        if (tum != null) { w.Add("t.tumbol_id=@tum"); p.Add("tum", tum); }
        if (villageId != null) { w.Add("v.village_id=@vil"); p.Add("vil", villageId); }
        if (q != null) { w.Add("(h.house_code LIKE @q OR h.house_no LIKE @q OR v.name_th LIKE @q)"); p.Add("q", "%" + q + "%"); }

        var fromSql = @"FROM t_house h
            JOIN m_village v ON v.village_id = h.village_id
            JOIN m_tumbol  t ON t.tumbol_id  = v.tumbol_id
            JOIN m_amphur  a ON a.amphur_id  = t.amphur_id
            LEFT JOIN m_happiness_level hl ON hl.happiness_level_id = h.happiness_level_id
            WHERE " + string.Join(" AND ", w);

        var happy = (await c.QueryAsync<HappyRow>("SELECT * FROM v_household_happiness_by_amphur WHERE survey_year=@year ORDER BY amphur_id", new { year })).ToList();
        var noFilter = amp == null && tum == null && villageId == null && q == null;
        var total = noFilter
            ? happy.Sum(x => x.Households)   // ไม่มีตัวกรอง: ใช้ผลรวมจากตารางสรุป ไม่นับทั้งตาราง
            : await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + fromSql, p);
        var (pg, pages) = PageInfo(total, page, 10);
        p.Add("size", 10); p.Add("off", (pg - 1) * 10);

        var vm = new HouseholdVm
        {
            Year = year,
            VillageId = villageId,
            Filter = new FilterVm { Action = "Household", Year = year, AmphurId = amp, TumbolId = tum, VillageId = villageId, Q = q,
                                    Amphurs = await Amphurs(c), Tumbols = await Tumbols(c, amp) },
            Levels = (await c.QueryAsync<Lvl>("SELECT happiness_level_id AS id, name_th AS name FROM m_happiness_level ORDER BY happiness_level_id")).ToList(),
            Happy = happy,
            Houses = new Paged<HouseRow>
            {
                Page = pg, Pages = pages, Total = total,
                Items = (await c.QueryAsync<HouseRow>(@"SELECT h.house_id, h.house_code, h.house_no,
                        v.name_th AS village_name, t.name_th AS tumbol_name, a.name_th AS amphur_name,
                        hl.name_th AS happiness_name,
                        (SELECT COUNT(*) FROM t_house_member m WHERE m.house_id = h.house_id AND m.is_active = 1) AS members "
                        + fromSql + " ORDER BY h.house_code LIMIT @size OFFSET @off", p)).ToList()
            }
        };
        return View(vm);
    }

    // ---------- สถิติสุขภาพ ----------
    public async Task<IActionResult> Health(int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var vm = new HealthVm
        {
            Year = year,
            Res = await c.QueryFirstAsync<HealthRes>(@"SELECT
                COUNT(CASE WHEN resource_type='hospital' THEN 1 END) AS hospital,
                COUNT(CASE WHEN resource_type='rpst'     THEN 1 END) AS rpst,
                COUNT(CASE WHEN resource_type='osm'      THEN 1 END) AS osm,
                COUNT(CASE WHEN resource_type='doctor'   THEN 1 END) AS doctor
                FROM health_resource"),
            Diseases = (await c.QueryAsync<DiseaseRow>(@"SELECT d.name_th AS name, k.patients AS patients,
                    ROUND(k.patients * 1000 / NULLIF((
                        SELECT SUM(population) FROM v_summary_province WHERE survey_year=@year), 0), 1) AS rate_per_1000
                FROM cache_disease_counts k
                JOIN m_disease d ON d.disease_id = k.disease_id
                WHERE k.survey_year=@year
                ORDER BY k.patients DESC, d.disease_id LIMIT 10", new { year }, commandTimeout: 120)).ToList()
        };
        return View(vm);
    }

    // ---------- ประชากรอายุ >= 18 ปี ไม่มีบัตรสวัสดิการ ----------
    private (string whereSql, DynamicParameters p) Over18Where(int year, string? amphurId, string? tumbolId, string? q)
    {
        var p = new DynamicParameters();
        p.Add("year", year);
        var w = new List<string> { "survey_year=@year" };
        if (amphurId != null) { w.Add("amphur_id=@amp"); p.Add("amp", amphurId); }
        if (tumbolId != null) { w.Add("tumbol_id=@tum"); p.Add("tum", tumbolId); }
        if (q != null) { w.Add("(full_name LIKE @q OR house_code LIKE @q)"); p.Add("q", "%" + q + "%"); }
        return (string.Join(" AND ", w), p);
    }

    public async Task<IActionResult> Over18(int year = 0, string? amphurId = null, string? tumbolId = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (amp, tum) = Scope(amphurId, tumbolId); q = Norm(q);
        var (whereSql, p) = Over18Where(year, amp, tum, q);
        var hasArea = amp != null || tum != null;     // ยังไม่เลือกพื้นที่ = ไม่คิวรีทั้งจังหวัด
        long total = 0; var pg = 1; var items = new List<MemberRow>();
        if (hasArea)
        {
            var fast = q == null;   // ไม่ได้ค้นชื่อ: ใช้คิวรีแบบแคบ ถ้าค้นชื่อ (LIKE) ใช้ view เดิม
            var extra = (amp != null ? " AND t.amphur_id=@amp" : "") + (tum != null ? " AND t.tumbol_id=@tum" : "") + MemberQueries.Over18Cond;
            total = fast
                ? await c.ExecuteScalarAsync<long>(MemberQueries.Count(extra), p, commandTimeout: 120)
                : await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM v_over18_no_welfare_card WHERE {whereSql}", p, commandTimeout: 120);
            (pg, _) = PageInfo(total, page, 25);
            p.Add("size", 25); p.Add("off", (pg - 1) * 25);
            items = (await c.QueryAsync<MemberRow>(
                fast ? MemberQueries.Page("v_over18_no_welfare_card", extra)
                     : $"SELECT * FROM v_over18_no_welfare_card WHERE {whereSql} ORDER BY house_code, member_id LIMIT @size OFFSET @off",
                p, commandTimeout: 120)).ToList();
        }
        var pages = (int)Math.Max(1, (total + 25 - 1) / 25);
        return View(new Over18Vm
        {
            Filter = new FilterVm { Action = "Over18", Year = year, AmphurId = amp, TumbolId = tum, Q = q, Amphurs = await Amphurs(c), Tumbols = await Tumbols(c, amp) },
            Members = new Paged<MemberRow> { Items = items, Page = pg, Pages = pages, Total = total }
        });
    }

    // ส่งออก CSV (เปิดใน Excel ได้ภาษาไทยถูกต้อง เพราะใส่ BOM)
    public async Task<IActionResult> Over18Export(int year = 0, string? amphurId = null, string? tumbolId = null, string? q = null)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (amp, tum) = Scope(amphurId, tumbolId); q = Norm(q);
        if (amp == null && tum == null)
            return Content("กรุณาเลือกอำเภอหรือตำบลก่อนส่งออก", "text/plain; charset=utf-8");
        var (whereSql, p) = Over18Where(year, amp, tum, q);
        var rows = await c.QueryAsync<MemberRow>($"SELECT * FROM v_over18_no_welfare_card WHERE {whereSql} ORDER BY house_code, member_id", p);

        static string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("ลำดับ,ชื่อ-นามสกุล,เลขประจำตัว,อายุ,เพศ,บ้านเลขที่,หมู่บ้าน,ตำบล,อำเภอ,สถานะ\r\n");
        var i = 0;
        foreach (var m in rows)
            sb.Append($"{++i},{Esc(m.FullName)},{Esc(m.CitizenIdMasked)},{m.Age},{Lbl.Gender(m.GenderId)},{Esc(m.HouseNo)},{Esc(m.VillageName)},{Esc(m.TumbolName)},{Esc(m.AmphurName)},{Esc(Lbl.Welfare(m.WelfareStatusId))}\r\n");
        await Log(c, "EXPORT", null, $"ส่งออกรายชื่อ >=18 ปี ไม่มีบัตร {i} รายการ");

        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "over18_no_welfare_card.csv");
    }

    // ---------- PPPConnext ----------
    public async Task<IActionResult> Ppp(int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        return View(new PppVm
        {
            Year = year,
            Sum = await c.QueryFirstOrDefaultAsync<PppRow>(@"SELECT
                CAST(COALESCE(SUM(g1),0) AS SIGNED) AS g1, CAST(COALESCE(SUM(g2),0) AS SIGNED) AS g2,
                CAST(COALESCE(SUM(g3),0) AS SIGNED) AS g3, CAST(COALESCE(SUM(g4),0) AS SIGNED) AS g4,
                CAST(COALESCE(SUM(total),0) AS SIGNED) AS total
                FROM v_ppp_by_village WHERE survey_year=@year", new { year }, commandTimeout: 120) ?? new PppRow(),
            Rows = (await c.QueryAsync<PppRow>(
                "SELECT * FROM v_ppp_by_village WHERE survey_year=@year ORDER BY g1 DESC, g2 DESC, village_id LIMIT 100", new { year }, commandTimeout: 120)).ToList()
        });
    }

    // ---------- กลุ่มเปราะบาง 14 เป้าหมาย ----------
    public async Task<IActionResult> Vulnerable(int year = 0, int? type = null)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var vm = new VulVm { Year = year, Selected = type };
        vm.Rows = (await c.QueryAsync<VulRow>(@"SELECT vt.vulnerable_type_id, vt.name_th, COALESCE(k.people, 0) AS people
            FROM m_vulnerable_type vt
            LEFT JOIN cache_vulnerable_counts k ON k.vulnerable_type_id = vt.vulnerable_type_id AND k.survey_year = @year
            ORDER BY vt.vulnerable_type_id", new { year }, commandTimeout: 120)).ToList();
        if (type != null)
            vm.Members = (await c.QueryAsync<MemberRow>(@"SELECT l.* FROM (
                    SELECT m.member_id
                    FROM member_vulnerable mv
                    JOIN t_house_member m ON m.member_id = mv.member_id AND m.is_active = 1
                    JOIN t_house h ON h.house_id = m.house_id AND h.is_active = 1 AND h.survey_year = @year
                    WHERE mv.vulnerable_type_id = @type
                    ORDER BY h.house_code, m.member_id LIMIT 200
                ) ids JOIN v_member_welfare_list l ON l.member_id = ids.member_id
                ORDER BY l.house_code, l.member_id", new { type, year }, commandTimeout: 120)).ToList();
        return View(vm);
    }

    // ---------- ครัวเรือนยากจน ----------
    public async Task<IActionResult> Poor(int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var rows = (await c.QueryAsync<PoorRow>(
            "SELECT * FROM v_poor_households_by_village WHERE survey_year=@year ORDER BY poor_households DESC, village_id LIMIT 200", new { year })).ToList();
        return View(new PoorVm { Year = year, Rows = rows, Total = rows.Sum(x => x.PoorHouseholds) });
    }
}
