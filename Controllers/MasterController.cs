using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// จัดการข้อมูลหลัก (ตาราง m_*) ด้วยหน้าเดียว: เพิ่ม / แก้ไข / ปิด-เปิดใช้งาน / ลบ
// ชื่อตารางและคอลัมน์มาจากรายการ Defs ในโค้ดนี้เท่านั้น ไม่รับจากผู้ใช้ (กัน SQL injection)
[Authorize(Roles = "admin")]
public class MasterController : AppController
{
    private const int PageSize = 15;
    public MasterController(Db db) : base(db) { }

    private static MasterField T(string col, string label, int max = 150, bool req = true) =>
        new() { Col = col, Label = label, Kind = "text", MaxLen = max, Required = req };

    private static MasterDef D(string key, string title, string table, string idCol, string? active, params MasterField[] f) =>
        new() { Key = key, Title = title, Table = table, IdCol = idCol, ActiveCol = active, OrderCol = idCol, Fields = f.ToList() };

    private static readonly List<MasterDef> Defs = new()
    {
        D("disease", "โรค", "m_disease", "disease_id", null, T("icd10", "รหัส ICD-10", 10, false), T("name_th", "ชื่อโรค")),
        D("vulnerable", "ประเภทกลุ่มเปราะบาง", "m_vulnerable_type", "vulnerable_type_id", null, T("name_th", "ชื่อกลุ่ม")),
        D("occupation", "อาชีพ", "m_occupation", "occupation_id", "is_active", T("name", "ชื่ออาชีพ")),
        D("prefix", "คำนำหน้าชื่อ", "m_prefix", "prefix_id", "is_active", T("name", "คำนำหน้า", 100)),
        D("education", "ระดับการศึกษา", "m_education", "education_id", "is_active", T("name", "ระดับการศึกษา", 100)),
        D("marital", "สถานภาพสมรส", "m_marital_status", "marital_status_id", "is_active", T("name", "สถานภาพ", 100)),
        D("religion", "ศาสนา", "m_religion", "religion_id", "is_active", T("name", "ศาสนา", 100)),
        D("relationship", "ความสัมพันธ์ในครัวเรือน", "m_relationship", "relationship_id", "is_active", T("name", "ความสัมพันธ์", 100)),
        D("physical", "สถานะทางกาย", "m_physical_status", "physical_status_id", null, T("name_th", "ชื่อ (ไทย)", 100), T("name_en", "ชื่อ (อังกฤษ)", 100, false)),
        D("happiness", "ระดับความอยู่ดีมีสุข", "m_happiness_level", "happiness_level_id", null, T("name_th", "ชื่อระดับ", 50)),
        D("document", "ประเภทเอกสารสิทธิ์", "m_document_type", "document_type_id", "is_active", T("name", "ชื่อเอกสาร")),
        D("jpt", "ตัวชี้วัด JPT", "m_jpt_indicator", "indicator_id", null, T("category", "หมวด", 100, false), T("name_th", "ชื่อตัวชี้วัด", 255)),
    };

    private static MasterDef? Find(string? key) => Defs.FirstOrDefault(d => d.Key == key);

    public async Task<IActionResult> Index(string key = "disease", string? q = null, int page = 1)
    {
        var def = Find(key);
        if (def == null) return NotFound();
        using var c = _db.Open();
        q = Norm(q);
        var p = new DynamicParameters();
        var where = "1=1";
        if (q != null)
        {
            where = "(" + string.Join(" OR ", def.Fields.Select(f => $"{f.Col} LIKE @q")) + ")";
            p.Add("q", "%" + q + "%");
        }
        var total = await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {def.Table} WHERE {where}", p);
        var (pg, pages) = PageInfo(total, page, PageSize);
        p.Add("size", PageSize); p.Add("off", (pg - 1) * PageSize);
        var rows = (await c.QueryAsync($"SELECT * FROM {def.Table} WHERE {where} ORDER BY {def.OrderCol} LIMIT @size OFFSET @off", p))
            .Select(r => (IDictionary<string, object?>)new Dictionary<string, object?>((IDictionary<string, object>)r)).ToList();
        return View(new MasterIndexVm
        {
            Def = def, All = Defs, Q = q,
            Rows = new Paged<IDictionary<string, object?>> { Items = rows, Page = pg, Pages = pages, Total = total }
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string key, long? id)
    {
        var def = Find(key);
        if (def == null) return NotFound();
        var vm = new MasterFormVm { Def = def, IsNew = id == null };
        if (id != null)
        {
            using var c = _db.Open();
            var row = (await c.QueryAsync($"SELECT * FROM {def.Table} WHERE {def.IdCol}=@id", new { id })).FirstOrDefault();
            if (row == null) return NotFound();
            var d = (IDictionary<string, object>)row;
            vm.Id = id.Value;
            foreach (var f in def.Fields) vm.Values[f.Col] = d.TryGetValue(f.Col, out var v) ? v?.ToString() : null;
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string key, long? id, bool isNew)
    {
        var def = Find(key);
        if (def == null) return NotFound();
        var vm = new MasterFormVm { Def = def, IsNew = isNew, Id = id ?? 0 };
        var p = new DynamicParameters();
        string? err = null;
        foreach (var f in def.Fields)
        {
            var v = Norm(Request.Form["f_" + f.Col]);
            vm.Values[f.Col] = v;
            if (v == null && f.Required) { err ??= $"กรุณากรอก{f.Label}"; }
            else if (v != null && v.Length > f.MaxLen) { err ??= $"{f.Label}ต้องไม่เกิน {f.MaxLen} ตัวอักษร"; }
            p.Add(f.Col, v);
        }
        if (err != null) { vm.Error = err; return View(vm); }

        using var c = _db.Open();
        try
        {
            if (isNew)
            {
                var newId = await c.ExecuteScalarAsync<long>($"SELECT COALESCE(MAX({def.IdCol}),0)+1 FROM {def.Table}");
                p.Add("id", newId);
                var cols = string.Join(",", def.Fields.Select(f => f.Col));
                var vals = string.Join(",", def.Fields.Select(f => "@" + f.Col));
                var activeCol = def.ActiveCol != null ? $",{def.ActiveCol}" : "";
                var activeVal = def.ActiveCol != null ? ",1" : "";
                await c.ExecuteAsync($"INSERT INTO {def.Table}({def.IdCol},{cols}{activeCol}) VALUES(@id,{vals}{activeVal})", p);
                await Log(c, "CREATE_MASTER", $"{def.Table}:{newId}");
            }
            else
            {
                if (id == null) return BadRequest();
                p.Add("id", id.Value);
                var sets = string.Join(",", def.Fields.Select(f => $"{f.Col}=@{f.Col}"));
                var n = await c.ExecuteAsync($"UPDATE {def.Table} SET {sets} WHERE {def.IdCol}=@id", p);
                if (n == 0 && await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {def.Table} WHERE {def.IdCol}=@id", new { id }) == 0) return NotFound();
                await Log(c, "UPDATE_MASTER", $"{def.Table}:{id}");
            }
            Flash("บันทึกข้อมูลหลักเรียบร้อยแล้ว");
            return RedirectToAction("Index", new { key });
        }
        catch (MySqlException ex)
        {
            vm.Error = ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry ? "มีข้อมูลนี้อยู่แล้ว (ชื่อซ้ำ)" : "บันทึกไม่ได้: " + ex.Message;
            return View(vm);
        }
    }

    // ปิด/เปิดใช้งาน (เฉพาะตารางที่มี is_active): ข้อมูลเดิมที่อ้างอิงอยู่ไม่เสียหาย
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string key, long id)
    {
        var def = Find(key);
        if (def?.ActiveCol == null) return BadRequest();
        using var c = _db.Open();
        await c.ExecuteAsync($"UPDATE {def.Table} SET {def.ActiveCol} = 1 - {def.ActiveCol} WHERE {def.IdCol}=@id", new { id });
        await Log(c, "TOGGLE_MASTER", $"{def.Table}:{id}");
        Flash("เปลี่ยนสถานะการใช้งานเรียบร้อยแล้ว");
        return RedirectToAction("Index", new { key });
    }

    // ลบจริง: ทำได้เมื่อไม่มีข้อมูลอื่นอ้างอิง ถ้ามีจะแจ้งให้ใช้การปิดใช้งานแทน
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string key, long id)
    {
        var def = Find(key);
        if (def == null) return NotFound();
        using var c = _db.Open();
        try
        {
            await c.ExecuteAsync($"DELETE FROM {def.Table} WHERE {def.IdCol}=@id", new { id });
            await Log(c, "DELETE_MASTER", $"{def.Table}:{id}");
            Flash("ลบข้อมูลเรียบร้อยแล้ว");
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.RowIsReferenced || ex.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            FlashErr(def.ActiveCol != null
                ? "ลบไม่ได้ เพราะมีข้อมูลอื่นใช้งานอยู่ ให้ใช้ \"ปิดใช้งาน\" แทน"
                : "ลบไม่ได้ เพราะมีข้อมูลอื่นใช้งานอยู่");
        }
        return RedirectToAction("Index", new { key });
    }
}
