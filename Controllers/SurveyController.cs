using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

/// <summary>แบบสำรวจอัปเดตข้อมูลครัวเรือน: ฟอร์มบนเว็บ + แบบพิมพ์สำหรับลงพื้นที่ + รายการคำตอบ + ส่งออก CSV</summary>
public class SurveyController : AppController
{
    public SurveyController(Db db) : base(db) { }

    static readonly JsonSerializerOptions JsonOpt = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static string Marker(int year) => "update-" + year;

    const string AnswerFrom = @"FROM house_survey_answer s
        JOIN t_house h ON h.house_id = s.house_id
        JOIN m_village v ON v.village_id = h.village_id
        JOIN m_tumbol tb ON tb.tumbol_id = v.tumbol_id
        JOIN m_amphur a ON a.amphur_id = tb.amphur_id
        LEFT JOIN web_users u ON u.user_id = s.created_by";
    const string AnswerWhere = @"s.is_active = 1 AND h.survey_year = @year AND (@a IS NULL OR a.amphur_id = @a) AND (@t IS NULL OR tb.tumbol_id = @t)";

    // ---------- รายการคำตอบ ----------
    public async Task<IActionResult> Index(int year = 0, string? amphurId = null, string? tumbolId = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (a, t) = Scope(amphurId, tumbolId);
        q = Norm(q);
        var prm = new { year, a, t, q = q == null ? null : "%" + q + "%" };
        var where = AnswerWhere + " AND (@q IS NULL OR h.house_code LIKE @q OR h.house_no LIKE @q)";
        var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + AnswerFrom + " WHERE " + where, prm);
        var (pg, pages) = PageInfo(total, page, 15);
        var items = (await c.QueryAsync<SurveyRow>(@"SELECT s.answer_id, s.house_id, h.house_code, h.house_no, v.name_th AS village_name,
                tb.name_th AS tumbol_name, a.name_th AS amphur_name, s.marker, u.full_name AS surveyor, s.created_at " + AnswerFrom +
            " WHERE " + where + " ORDER BY s.created_at DESC LIMIT @size OFFSET @off",
            new { year, a, t, q = prm.q, size = 15, off = (pg - 1) * 15 })).ToList();
        return View(new SurveyListVm
        {
            Filter = new FilterVm { Action = "Index", Year = year, AmphurId = a, TumbolId = t, Q = q, Amphurs = await Amphurs(c), Tumbols = await Tumbols(c, a) },
            Rows = new Paged<SurveyRow> { Items = items, Page = pg, Pages = pages, Total = total }
        });
    }

    // ---------- ฟอร์ม ----------
    [HttpGet]
    public async Task<IActionResult> Form(long? houseId = null, int year = 0, string? amphurId = null, string? tumbolId = null, string? villageId = null)
    {
        using var c = _db.Open();
        if (houseId == null)
        {
            var seed = new PickerVm { Year = year, AmphurId = Norm(amphurId), TumbolId = Norm(tumbolId), VillageId = Norm(villageId), ShowHouse = true };
            return View(new SurveyFormVm { Picker = await BuildPickerAsync(c, seed) });
        }
        if (!await HouseAllowedAsync(c, houseId.Value)) return NotFound();
        return View(await BuildFormAsync(c, houseId.Value, null, null));
    }

    private async Task<SurveyFormVm> BuildFormAsync(IDbConnection c, long houseId, Dictionary<string, string[]>? posted, string? error)
    {
        var sheet = await Roster.ByHouseAsync(c, houseId);
        var vm = new SurveyFormVm { Sheet = sheet, Error = error };
        if (posted != null) { vm.Values = posted; return vm; }
        // เปิดซ้ำ: เติมคำตอบล่าสุดของบ้านนี้ในปีเดียวกัน
        var last = await c.ExecuteScalarAsync<string?>(
            "SELECT CAST(answers AS CHAR) FROM house_survey_answer WHERE house_id=@houseId AND marker=@m AND is_active=1 ORDER BY created_at DESC LIMIT 1",
            new { houseId, m = Marker(sheet?.Year ?? 0) });
        if (last != null) vm.Values = Flatten(last);
        return vm;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(long houseId)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return NotFound();
        var sheet = await Roster.ByHouseAsync(c, houseId);
        if (sheet == null || sheet.Houses.Count == 0) return NotFound();
        var members = sheet.Houses[0].Members;

        var (vals, err) = Collect(Request.Form, members);
        if (!vals.ContainsKey("q_a_status")) err ??= "กรุณาเลือกสถานะของบ้านที่พบ";
        if (!vals.ContainsKey("q_f_surveyor")) err ??= "กรุณาระบุชื่อผู้สำรวจ";
        if (!vals.ContainsKey("q_f_consent")) err ??= "กรุณาระบุความยินยอมให้เก็บและใช้ข้อมูล";
        foreach (var k in new[] { "q_a_lat", "q_a_lng" })
            if (vals.TryGetValue(k, out var g) && (!double.TryParse(g[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dv) || Math.Abs(dv) > 180))
            { err ??= "พิกัดต้องเป็นตัวเลขทศนิยม เช่น 17.1612"; vals.Remove(k); }
        if (err != null) return View("Form", await BuildFormAsync(c, houseId, vals, err));

        var json = JsonSerializer.Serialize(new
        {
            version = SurveyDef.Version,
            survey_year = sheet.Year,
            house_id = houseId,
            house_code = sheet.Houses[0].HouseCode,
            house = SurveyDef.AllHouseQs.Where(q => vals.ContainsKey("q_" + q.Id))
                .ToDictionary(q => q.Id, q => q.Type == "check" ? (object)vals["q_" + q.Id] : vals["q_" + q.Id][0]),
            members = members.Select(m =>
            {
                var d = new Dictionary<string, object?> { ["member_id"] = m.MemberId, ["name"] = m.FullName?.Trim() };
                foreach (var q in SurveyDef.MemberQs) if (vals.TryGetValue($"m{m.MemberId}_{q.Id}", out var v)) d[q.Id] = v[0];
                return d;
            }).Where(d => d.Count > 2).ToList(),
            new_members = Enumerable.Range(1, SurveyDef.NewMemberRows).Select(i =>
            {
                var d = new Dictionary<string, object?>();
                foreach (var q in SurveyDef.NewMemberQs) if (vals.TryGetValue($"nm{i}_{q.Id}", out var v)) d[q.Id] = v[0];
                return d;
            }).Where(d => d.ContainsKey("name")).ToList()
        }, JsonOpt);

        // คำตอบเดิมของปีเดียวกันถูกเก็บเป็นประวัติ (is_active = 0) ไม่ลบทิ้ง
        var marker = Marker(sheet.Year);
        await c.ExecuteAsync("UPDATE house_survey_answer SET is_active=0, updated_at=NOW() WHERE house_id=@houseId AND marker=@marker AND is_active=1", new { houseId, marker });
        await c.ExecuteAsync(@"INSERT INTO house_survey_answer(house_id, answers, marker, is_active, created_by, created_at)
            VALUES(@houseId, @json, @marker, 1, @uid, NOW())", new { houseId, json, marker, uid = UserId });
        await Log(c, "SURVEY_SAVE", houseId.ToString(), $"{marker} house={sheet.Houses[0].HouseCode}");
        Flash("บันทึกแบบสำรวจบ้าน " + sheet.Houses[0].HouseCode + " เรียบร้อยแล้ว");
        return RedirectToAction("Index", new { year = sheet.Year });
    }

    // ---------- ดูคำตอบ ----------
    public async Task<IActionResult> Detail(long id)
    {
        using var c = _db.Open();
        var row = await c.QueryFirstOrDefaultAsync<(long house_id, string json, DateTime created_at, string? surveyor, string? marker)>(
            @"SELECT s.house_id, CAST(s.answers AS CHAR) AS json, s.created_at, u.full_name AS surveyor, s.marker
              FROM house_survey_answer s LEFT JOIN web_users u ON u.user_id = s.created_by WHERE s.answer_id=@id", new { id });
        if (row.json == null || !await HouseAllowedAsync(c, row.house_id)) return NotFound();
        var sheet = await Roster.ByHouseAsync(c, row.house_id);
        if (sheet == null) return NotFound();
        return View(new SurveyDetailVm { AnswerId = id, Sheet = sheet, CreatedAt = row.created_at, Surveyor = row.surveyor, Marker = row.marker, Values = Flatten(row.json) });
    }

    // ---------- แบบพิมพ์ ----------
    // /Survey/Print                → แบบเปล่า
    // /Survey/Print?houseId=123    → บ้านหลังเดียว (พิมพ์ข้อมูลเดิมไว้ให้)
    // /Survey/Print?villageId=...  → ทั้งหมู่บ้าน (ชุดลงพื้นที่) ครั้งละไม่เกิน 40 หลัง ใช้ skip เลื่อนชุด
    [HttpGet]
    public async Task<IActionResult> Print(long? houseId = null, string? villageId = null, int year = 0, int skip = 0)
    {
        using var c = _db.Open();
        FieldSheetVm vm;
        if (houseId != null)
        {
            if (!await HouseAllowedAsync(c, houseId.Value)) return NotFound();
            vm = await Roster.ByHouseAsync(c, houseId.Value) ?? new FieldSheetVm();
        }
        else if (Norm(villageId) != null)
        {
            if (!await VillageAllowedAsync(c, villageId)) return NotFound();
            year = await _db.YearAsync(c, year);
            vm = await Roster.ByVillageAsync(c, villageId!, year);
            ViewBag.TotalHouses = vm.Houses.Count;
            ViewBag.Skip = skip;
            vm.Houses = vm.Houses.Skip(Math.Max(0, skip)).Take(40).ToList();
        }
        else
        {
            vm = new FieldSheetVm { Year = await _db.YearAsync(c, year), Houses = new() { new RosterHouse() } };
            ViewBag.Blank = true;
        }
        await Log(c, "SURVEY_PRINT", houseId?.ToString() ?? villageId ?? "blank", $"houses={vm.Houses.Count}");
        return View(vm);
    }

    // ---------- ส่งออกคำตอบทั้งหมด (CSV) ----------
    [HttpGet]
    public async Task<IActionResult> ExportAnswers(int year = 0, string? amphurId = null, string? tumbolId = null)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (a, t) = Scope(amphurId, tumbolId);
        var rows = await c.QueryAsync<(long answer_id, string house_code, string? house_no, string village_name, string tumbol_name, string amphur_name, DateTime created_at, string json)>(
            @"SELECT s.answer_id, h.house_code, h.house_no, v.name_th AS village_name, tb.name_th AS tumbol_name, a.name_th AS amphur_name,
                     s.created_at, CAST(s.answers AS CHAR) AS json " + AnswerFrom + " WHERE " + AnswerWhere + " ORDER BY s.answer_id",
            new { year, a, t }, commandTimeout: 120);

        var sb = new StringBuilder();
        var head = new List<object?> { "answer_id", "รหัสบ้าน", "บ้านเลขที่", "หมู่บ้าน", "ตำบล", "อำเภอ", "วันที่บันทึก" };
        head.AddRange(SurveyDef.AllHouseQs.Select(q => (object?)q.Label));
        head.AddRange(new object?[] { "สมาชิกที่สถานะไม่ใช่ 'อยู่ปกติ' (คน)", "สมาชิกเพิ่มใหม่ (คน)", "รายละเอียดสมาชิก (JSON)" });
        sb.Append(ExportWriter.CsvLine(head));
        foreach (var r in rows)
        {
            var flat = Flatten(r.json);
            var line = new List<object?> { r.answer_id, r.house_code, r.house_no, r.village_name, r.tumbol_name, r.amphur_name, r.created_at };
            line.AddRange(SurveyDef.AllHouseQs.Select(q => flat.TryGetValue("q_" + q.Id, out var v) ? (object?)string.Join(" | ", v) : null));
            line.Add(flat.Where(kv => kv.Key.StartsWith("m") && kv.Key.EndsWith("_status") && kv.Value[0] != "อยู่ปกติ").Count());
            line.Add(flat.Keys.Count(k => k.StartsWith("nm") && k.EndsWith("_name")));
            using var doc = JsonDocument.Parse(r.json);
            line.Add(doc.RootElement.TryGetProperty("members", out var ms) ? ms.GetRawText() : "[]");
            sb.Append(ExportWriter.CsvLine(line));
        }
        await Log(c, "EXPORT_SURVEY", year.ToString(), $"area={a ?? "-"}/{t ?? "-"}");
        return File(ExportWriter.CsvBytes(sb.ToString()), "text/csv; charset=utf-8", $"sldc_survey_answers_{year}.csv");
    }

    // ---------- ตัวช่วย ----------
    private static (Dictionary<string, string[]> vals, string? err) Collect(IFormCollection f, List<RosterRow> members)
    {
        var vals = new Dictionary<string, string[]>();
        string? err = null;

        void Take(string name, Q q)
        {
            var max = q.Type == "textarea" ? 1000 : 300;
            var v = f.TryGetValue(name, out var raw)
                ? raw.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => { x = x!.Trim(); return x.Length > max ? x[..max] : x; }).ToArray()
                : Array.Empty<string>();
            if (q.Opts != null) v = v.Where(x => q.Opts.Contains(x)).ToArray();
            if (q.Type != "check" && v.Length > 1) v = v.Take(1).ToArray();
            if (q.Type == "number" && v.Length > 0)
            {
                if (decimal.TryParse(v[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0 && d < 10_000_000_000m)
                    v = new[] { d.ToString(CultureInfo.InvariantCulture) };
                else { err ??= $"ค่าตัวเลขของ \"{q.Label}\" ไม่ถูกต้อง"; v = Array.Empty<string>(); }
            }
            if (q.Type == "date" && v.Length > 0 && !DateTime.TryParseExact(v[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                v = Array.Empty<string>();
            if (v.Length > 0) vals[name] = v;
        }

        foreach (var q in SurveyDef.AllHouseQs) Take("q_" + q.Id, q);
        foreach (var m in members) foreach (var q in SurveyDef.MemberQs) Take($"m{m.MemberId}_{q.Id}", q);
        for (var i = 1; i <= SurveyDef.NewMemberRows; i++) foreach (var q in SurveyDef.NewMemberQs) Take($"nm{i}_{q.Id}", q);
        return (vals, err);
    }

    private static Dictionary<string, string[]> Flatten(string json)
    {
        var d = new Dictionary<string, string[]>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            static string[] S(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.ToString()).ToArray() : new[] { e.ToString() };
            if (root.TryGetProperty("house", out var h) && h.ValueKind == JsonValueKind.Object)
                foreach (var p in h.EnumerateObject()) d["q_" + p.Name] = S(p.Value);
            if (root.TryGetProperty("members", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray())
                {
                    var id = m.GetProperty("member_id").GetInt64();
                    foreach (var p in m.EnumerateObject())
                        if (p.Name != "member_id" && p.Name != "name") d[$"m{id}_{p.Name}"] = S(p.Value);
                }
            if (root.TryGetProperty("new_members", out var nm) && nm.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var m in nm.EnumerateArray()) { i++; foreach (var p in m.EnumerateObject()) d[$"nm{i}_{p.Name}"] = S(p.Value); }
            }
        }
        catch (JsonException) { /* ข้อมูลเสียหาย: คืนค่าว่าง */ }
        return d;
    }
}
