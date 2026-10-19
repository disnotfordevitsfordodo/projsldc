using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// ทะเบียนโรคของประชากร (member_disease): เพิ่ม / แก้ไข / ลบ
public class DiseaseController : AppController
{
    private const int PageSize = 15;
    public DiseaseController(Db db) : base(db) { }

    public async Task<IActionResult> Index(int year = 0, string? amphurId = null, string? tumbolId = null,
                                           int? diseaseId = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (amp, tum) = Scope(amphurId, tumbolId); q = Norm(q);
        var hasArea = amp != null || tum != null;      // ยังไม่เลือกพื้นที่ = ไม่คิวรีทั้งจังหวัด

        var vm = new DiseaseListVm
        {
            DiseaseId = diseaseId, HasArea = hasArea,
            Filter = new FilterVm { Action = "Index", Year = year, AmphurId = amp, TumbolId = tum, Q = q,
                                    Amphurs = await Amphurs(c), Tumbols = await Tumbols(c, amp) },
            Diseases = await SimpleOptsAsync(c, "m_disease", "disease_id", "name_th", false)
        };
        if (!hasArea) return View(vm);

        var p = new DynamicParameters();
        p.Add("year", year);
        var w = new List<string> { "h.survey_year=@year", "h.is_active=1", "m.is_active=1" };
        if (amp != null) { w.Add("tb.amphur_id=@amp"); p.Add("amp", amp); }
        if (tum != null) { w.Add("tb.tumbol_id=@tum"); p.Add("tum", tum); }
        if (diseaseId != null) { w.Add("md.disease_id=@dis"); p.Add("dis", diseaseId); }
        if (q != null) { w.Add("(m.first_name LIKE @q OR m.last_name LIKE @q OR h.house_code LIKE @q)"); p.Add("q", "%" + q + "%"); }
        var from = @"FROM member_disease md
            JOIN t_house_member m ON m.member_id = md.member_id
            JOIN t_house h ON h.house_id = m.house_id
            JOIN m_village v ON v.village_id = h.village_id
            JOIN m_tumbol tb ON tb.tumbol_id = v.tumbol_id
            JOIN m_amphur a ON a.amphur_id = tb.amphur_id
            JOIN m_disease d ON d.disease_id = md.disease_id
            LEFT JOIN m_prefix pf ON pf.prefix_id = m.prefix_id
            WHERE " + string.Join(" AND ", w);

        var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + from, p, commandTimeout: 120);
        var (pg, pages) = PageInfo(total, page, PageSize);
        p.Add("size", PageSize); p.Add("off", (pg - 1) * PageSize);
        vm.Rows = new Paged<DiseaseRecordRow>
        {
            Page = pg, Pages = pages, Total = total,
            Items = (await c.QueryAsync<DiseaseRecordRow>(@"SELECT md.member_disease_id, md.member_id,
                    CONCAT(COALESCE(pf.name,''), m.first_name, ' ', COALESCE(m.last_name,'')) AS full_name,
                    h.house_code, d.name_th AS disease_name, md.diagnosed_date,
                    v.name_th AS village_name, tb.name_th AS tumbol_name, a.name_th AS amphur_name "
                + from + " ORDER BY md.member_disease_id DESC LIMIT @size OFFSET @off", p, commandTimeout: 120)).ToList()
        };
        return View(vm);
    }

    // ---------- เพิ่ม ----------
    private async Task<DiseaseFormVm> BuildVmAsync(IDbConnection c, DiseaseForm f, PickerVm seed, string? err)
    {
        seed.ShowHouse = true; seed.ShowMember = true;
        var pick = await BuildPickerAsync(c, seed);
        f.Year = pick.Year;
        return new DiseaseFormVm
        {
            Form = f, Picker = pick, Error = err,
            Diseases = await SimpleOptsAsync(c, "m_disease", "disease_id", "name_th", false)
        };
    }

    [HttpGet]
    public async Task<IActionResult> Create(long? memberId)
    {
        using var c = _db.Open();
        var seed = new PickerVm();
        if (memberId != null)
        {
            if (await MemberHouseAsync(c, memberId.Value) == null) return NotFound();
            seed = await ResolveMemberPickAsync(c, memberId.Value) ?? seed;
        }
        return View(await BuildVmAsync(c, new DiseaseForm { DiagnosedDate = DateTime.Today }, seed, null));
    }

    // คืนข้อความผิดพลาด (null = สำเร็จ)
    private async Task<string?> AddAsync(IDbConnection c, long? memberId, int? diseaseId, DateTime? date)
    {
        if (memberId == null) return "กรุณาเลือกบุคคล";
        if (!await ActiveMemberAsync(c, memberId.Value)) return "ไม่พบบุคคลที่เลือก หรือไม่อยู่ในพื้นที่ที่คุณมีสิทธิ์";
        if (diseaseId == null || await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_disease WHERE disease_id=@d", new { d = diseaseId }) == 0)
            return "กรุณาเลือกโรค";
        if (date != null && (date > DateTime.Today || date < new DateTime(1900, 1, 1))) return "วันที่วินิจฉัยไม่ถูกต้อง";
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM member_disease WHERE member_id=@m AND disease_id=@d", new { m = memberId, d = diseaseId }) > 0)
            return "บุคคลนี้มีการบันทึกโรคนี้อยู่แล้ว";

        await c.ExecuteAsync("INSERT INTO member_disease(member_id, disease_id, diagnosed_date) VALUES(@m, @d, @date)",
            new { m = memberId, d = diseaseId, date });
        await RefreshAsync(c, memberId.Value);
        await Log(c, "ADD_DISEASE", memberId.ToString(), $"โรค {diseaseId}");
        return null;
    }

    private async Task<bool> ActiveMemberAsync(IDbConnection c, long memberId) =>
        await MemberHouseAsync(c, memberId) != null &&
        await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house_member WHERE member_id=@memberId AND is_active=1", new { memberId }) > 0;

    private async Task RefreshAsync(IDbConnection c, long memberId)
    {
        var y = await MemberYearAsync(c, memberId);
        if (y == null) return;
        var e = await Caches.RefreshDiseaseAsync(c, y.Value);
        if (e != null) await Log(c, "CACHE_REFRESH_FAILED", memberId.ToString(), e);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DiseaseForm f)
    {
        using var c = _db.Open();
        var err = await AddAsync(c, f.MemberId, f.DiseaseId, f.DiagnosedDate);
        if (err != null)
        {
            var seed = new PickerVm { Year = f.Year, AmphurId = Norm(f.AmphurId), TumbolId = Norm(f.TumbolId), VillageId = Norm(f.VillageId), HouseId = f.HouseId, MemberId = f.MemberId };
            return View(await BuildVmAsync(c, f, seed, err));
        }
        Flash("บันทึกโรคของผู้ป่วยเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = f.MemberId });
    }

    // เพิ่มโรคแบบเร็วจากหน้ารายละเอียดบุคคล
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAdd(long memberId, int? diseaseId, DateTime? diagnosedDate)
    {
        using var c = _db.Open();
        var err = await AddAsync(c, memberId, diseaseId, diagnosedDate);
        if (err != null) FlashErr(err); else Flash("บันทึกโรคของผู้ป่วยเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = memberId });
    }

    // ---------- แก้ไข ----------
    public class Rec { public long MemberDiseaseId { get; set; } public long MemberId { get; set; } public int DiseaseId { get; set; } public DateTime? DiagnosedDate { get; set; } }

    [HttpGet]
    public async Task<IActionResult> Edit(long id)
    {
        using var c = _db.Open();
        var r = await c.QueryFirstOrDefaultAsync<Rec>("SELECT member_disease_id, member_id, disease_id, diagnosed_date FROM member_disease WHERE member_disease_id=@id", new { id });
        if (r == null || !await ActiveMemberAsync(c, r.MemberId)) return NotFound();
        return View(new DiseaseFormVm
        {
            Form = new DiseaseForm { MemberDiseaseId = r.MemberDiseaseId, MemberId = r.MemberId, DiseaseId = r.DiseaseId, DiagnosedDate = r.DiagnosedDate },
            MemberName = await c.ExecuteScalarAsync<string?>(@"SELECT CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,''))
                FROM t_house_member m LEFT JOIN m_prefix p ON p.prefix_id=m.prefix_id WHERE m.member_id=@m", new { m = r.MemberId }),
            Diseases = await SimpleOptsAsync(c, "m_disease", "disease_id", "name_th", false)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DiseaseForm f)
    {
        using var c = _db.Open();
        var r = await c.QueryFirstOrDefaultAsync<Rec>("SELECT member_disease_id, member_id, disease_id, diagnosed_date FROM member_disease WHERE member_disease_id=@id", new { id = f.MemberDiseaseId });
        if (r == null || !await ActiveMemberAsync(c, r.MemberId)) return NotFound();
        f.MemberId = r.MemberId;

        string? err = null;
        if (f.DiseaseId == null || await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_disease WHERE disease_id=@d", new { d = f.DiseaseId }) == 0) err = "กรุณาเลือกโรค";
        else if (f.DiagnosedDate != null && (f.DiagnosedDate > DateTime.Today || f.DiagnosedDate < new DateTime(1900, 1, 1))) err = "วันที่วินิจฉัยไม่ถูกต้อง";
        else if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM member_disease WHERE member_id=@m AND disease_id=@d AND member_disease_id<>@id",
                     new { m = r.MemberId, d = f.DiseaseId, id = f.MemberDiseaseId }) > 0) err = "บุคคลนี้มีการบันทึกโรคนี้อยู่แล้ว";
        if (err != null)
            return View(new DiseaseFormVm { Form = f, Error = err, Diseases = await SimpleOptsAsync(c, "m_disease", "disease_id", "name_th", false) });

        await c.ExecuteAsync("UPDATE member_disease SET disease_id=@d, diagnosed_date=@date WHERE member_disease_id=@id",
            new { d = f.DiseaseId, date = f.DiagnosedDate, id = f.MemberDiseaseId });
        await RefreshAsync(c, r.MemberId);
        await Log(c, "UPDATE_DISEASE", f.MemberDiseaseId.ToString());
        Flash("แก้ไขข้อมูลโรคเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = r.MemberId });
    }

    // ---------- ลบ ----------
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, string? returnUrl)
    {
        using var c = _db.Open();
        var r = await c.QueryFirstOrDefaultAsync<Rec>("SELECT member_disease_id, member_id, disease_id, diagnosed_date FROM member_disease WHERE member_disease_id=@id", new { id });
        if (r == null || await MemberHouseAsync(c, r.MemberId) == null) return NotFound();
        await c.ExecuteAsync("DELETE FROM member_disease WHERE member_disease_id=@id", new { id });
        await RefreshAsync(c, r.MemberId);
        await Log(c, "DELETE_DISEASE", id.ToString());
        Flash("ลบข้อมูลโรคเรียบร้อยแล้ว");
        return Back(returnUrl, RedirectToAction("Person", "House", new { id = r.MemberId }));
    }
}
