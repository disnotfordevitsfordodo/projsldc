using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// ศูนย์ซ่อมแซมบ้านเรือน / อุปกรณ์ทางการแพทย์ + แบบฟอร์มคำร้องขอความช่วยเหลือ (เพิ่ม / แก้ไข / ลบ)
public class RepairController : AppController
{
    private const int PageSize = 20;
    private static readonly string[] Statuses = { "waiting", "in_progress", "done" };
    public RepairController(Db db) : base(db) { }

    public Task<IActionResult> House(string? status = null, string? q = null, int page = 1) => ListAsync("house", status, q, page);
    public Task<IActionResult> Medical(string? status = null, string? q = null, int page = 1) => ListAsync("medical", status, q, page);

    public class StatusCount { public string Status { get; set; } = ""; public long N { get; set; } }
    private static bool Valid(string kind) => kind == "house" || kind == "medical";
    private static string Table(string kind) => kind == "house" ? "repair_house_request" : "repair_medical_request";

    private async Task<IActionResult> ListAsync(string kind, string? status, string? q, int page)
    {
        using var c = _db.Open();
        var (a, t) = Scope(null, null);
        status = Statuses.Contains(status ?? "") ? status : null;
        q = Norm(q);

        // เชื่อมถึงบ้านของผู้ร้องขอ: คำร้องซ่อมบ้านใช้ r.house_id, อุปกรณ์การแพทย์ใช้บ้านของสมาชิก
        var houseJoin = kind == "house" ? "JOIN t_house h ON h.house_id = r.house_id" : "JOIN t_house h ON h.house_id = m.house_id";
        var from = $@"FROM {Table(kind)} r
            JOIN t_house_member m ON m.member_id = r.member_id
            {houseJoin}
            JOIN m_village v ON v.village_id = h.village_id
            JOIN m_tumbol t ON t.tumbol_id = v.tumbol_id
            JOIN m_amphur a ON a.amphur_id = t.amphur_id
            LEFT JOIN m_prefix p ON p.prefix_id = m.prefix_id";
        var itemCol = kind == "house" ? "r.repair_type" : "r.device_type";
        var scopeSql = " (@a IS NULL OR t.amphur_id=@a) AND (@t IS NULL OR t.tumbol_id=@t)";

        var p = new DynamicParameters();
        p.Add("a", a); p.Add("t", t);
        var w = new List<string> { scopeSql };
        if (status != null) { w.Add("r.status=@status"); p.Add("status", status); }
        if (q != null) { w.Add($"(m.first_name LIKE @q OR m.last_name LIKE @q OR {itemCol} LIKE @q)"); p.Add("q", "%" + q + "%"); }

        // ตัวเลขสรุป (ไม่ขึ้นกับตัวกรองสถานะ/คำค้น แต่ขึ้นกับสิทธิ์พื้นที่)
        var counts = (await c.QueryAsync<StatusCount>(
            $"SELECT r.status AS Status, COUNT(*) AS N {from} WHERE {scopeSql} GROUP BY r.status", p)).ToList();
        long N(string s) => counts.Where(x => x.Status == s).Sum(x => x.N);

        var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + from + " WHERE " + string.Join(" AND ", w), p);
        var (pg, pages) = PageInfo(total, page, PageSize);
        p.Add("size", PageSize); p.Add("off", (pg - 1) * PageSize);

        var cols = kind == "house"
            ? "r.repair_type AS item_type, r.budget, CAST(NULL AS CHAR) AS detail, r.request_date AS req_date, h.house_code"
            : "r.device_type AS item_type, CAST(NULL AS DECIMAL(12,2)) AS budget, r.detail, r.received_date AS req_date, h.house_code";
        var order = kind == "house" ? "r.request_date" : "r.received_date";
        var rows = (await c.QueryAsync<RepairRow>($@"SELECT r.request_id, r.member_id,
                CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,'')) AS full_name,
                a.name_th AS amphur_name, t.name_th AS tumbol_name, r.status, {cols}
            {from} WHERE {string.Join(" AND ", w)} ORDER BY {order} DESC, r.request_id DESC LIMIT @size OFFSET @off", p)).ToList();

        return View("List", new RepairVm
        {
            Kind = kind,
            Title = kind == "house" ? "ศูนย์ซ่อมแซมบ้านเรือน" : "ศูนย์ซ่อมแซมอุปกรณ์ทางการแพทย์",
            Rows = rows, Status = status, Q = q, Page = pg, Pages = pages, RowCount = total,
            Waiting = N("waiting"), InProgress = N("in_progress"), Done = N("done"),
            Total = N("waiting") + N("in_progress") + N("done")
        });
    }

    // ======================= แบบฟอร์มคำร้อง =======================
    private static readonly string[] HouseSuggest =
        { "ซ่อมหลังคา", "ซ่อมพื้นบ้าน", "ซ่อมฝาผนัง", "ซ่อมประตู/หน้าต่าง", "ซ่อมห้องน้ำ/ส้วม", "ปรับปรุงทางลาด/ราวจับ", "ระบบไฟฟ้า", "ระบบประปา", "ต่อเติมห้องนอนผู้ป่วย" };
    private static readonly string[] DeviceSuggest =
        { "รถเข็นผู้ป่วย", "เตียงผู้ป่วย", "ที่นอนลมป้องกันแผลกดทับ", "ไม้เท้า/วอล์กเกอร์", "เครื่องช่วยฟัง", "เครื่องผลิตออกซิเจน", "เครื่องวัดความดัน", "อุปกรณ์ช่วยเดิน", "กายอุปกรณ์เทียม" };

    private async Task<RepairFormVm> BuildVmAsync(IDbConnection c, RepairForm f, PickerVm seed, bool isNew, string? err)
    {
        seed.ShowHouse = true; seed.ShowMember = true;
        var pick = await BuildPickerAsync(c, seed);
        f.Year = pick.Year;
        string? name = null;
        if (!isNew && f.MemberId != null)
            name = await c.ExecuteScalarAsync<string?>(@"SELECT CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,''))
                FROM t_house_member m LEFT JOIN m_prefix p ON p.prefix_id=m.prefix_id WHERE m.member_id=@m", new { m = f.MemberId });
        return new RepairFormVm
        {
            Form = f, Picker = pick, IsNew = isNew, Error = err, MemberName = name,
            Suggestions = f.Kind == "house" ? HouseSuggest : DeviceSuggest
        };
    }

    [HttpGet]
    public async Task<IActionResult> Create(string kind = "house", long? memberId = null)
    {
        if (!Valid(kind)) return BadRequest();
        using var c = _db.Open();
        var seed = new PickerVm();
        if (memberId != null)
        {
            if (await MemberHouseAsync(c, memberId.Value) == null) return NotFound();
            seed = await ResolveMemberPickAsync(c, memberId.Value) ?? seed;
        }
        return View(await BuildVmAsync(c, new RepairForm { Kind = kind, ReqDate = DateTime.Today }, seed, true, null));
    }

    private async Task<string?> ValidateAsync(IDbConnection c, RepairForm f, bool isNew)
    {
        f.ItemType = (f.ItemType ?? "").Trim(); f.Detail = Norm(f.Detail);
        if (!Valid(f.Kind)) return "ประเภทคำร้องไม่ถูกต้อง";
        if (isNew)
        {
            if (f.MemberId == null) return "กรุณาเลือกบุคคลผู้ยื่นคำร้อง";
            if (await MemberHouseAsync(c, f.MemberId.Value) == null ||
                await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house_member WHERE member_id=@m AND is_active=1", new { m = f.MemberId }) == 0)
                return "ไม่พบบุคคลที่เลือก หรือไม่อยู่ในพื้นที่ที่คุณมีสิทธิ์";
        }
        if (f.ItemType.Length == 0) return f.Kind == "house" ? "กรุณาระบุประเภทการซ่อม" : "กรุณาระบุประเภทอุปกรณ์";
        if (f.ItemType.Length > 100) return "ประเภทต้องไม่เกิน 100 ตัวอักษร";
        if (!Statuses.Contains(f.Status)) return "สถานะไม่ถูกต้อง";
        if (f.ReqDate == null) return f.Kind == "house" ? "กรุณาระบุวันที่ยื่นคำร้อง" : "กรุณาระบุวันที่รับ";
        if (f.ReqDate > DateTime.Today.AddDays(1) || f.ReqDate < new DateTime(2000, 1, 1)) return "วันที่ไม่ถูกต้อง";
        if (f.Kind == "house")
        {
            if (f.Budget != null && (f.Budget < 0 || f.Budget > 9_999_999_999m)) return "งบประมาณต้องไม่ติดลบ";
            if (f.FinishedDate != null && f.FinishedDate < f.ReqDate) return "วันที่แล้วเสร็จต้องไม่ก่อนวันที่ยื่นคำร้อง";
            if (f.Status == "done" && f.FinishedDate == null) f.FinishedDate = DateTime.Today;
            if (f.Status != "done") f.FinishedDate = null;
        }
        else if ((f.Detail?.Length ?? 0) > 5000) return "รายละเอียดยาวเกินไป";
        return null;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RepairForm f)
    {
        using var c = _db.Open();
        var err = await ValidateAsync(c, f, true);
        if (err == null)
        {
            try
            {
                var houseId = await MemberHouseAsync(c, f.MemberId!.Value);
                if (f.Kind == "house")
                    await c.ExecuteAsync(@"INSERT INTO repair_house_request(member_id, house_id, repair_type, budget, status, request_date, finished_date, created_by)
                        VALUES(@MemberId, @houseId, @ItemType, @Budget, @Status, @ReqDate, @FinishedDate, @uid)", new { f.MemberId, houseId, f.ItemType, f.Budget, f.Status, f.ReqDate, f.FinishedDate, uid = UserId });
                else
                    await c.ExecuteAsync(@"INSERT INTO repair_medical_request(member_id, device_type, detail, status, received_date, created_by)
                        VALUES(@MemberId, @ItemType, @Detail, @Status, @ReqDate, @uid)", new { f.MemberId, f.ItemType, f.Detail, f.Status, f.ReqDate, uid = UserId });
                await Log(c, "CREATE_REPAIR", f.MemberId.ToString(), $"{f.Kind}: {f.ItemType}");
                Flash("บันทึกคำร้องเรียบร้อยแล้ว");
                return RedirectToAction(f.Kind == "house" ? "House" : "Medical");
            }
            catch (MySqlException ex) { err = "บันทึกไม่ได้: " + ex.Message; }
        }
        var seed = new PickerVm { Year = f.Year, AmphurId = Norm(f.AmphurId), TumbolId = Norm(f.TumbolId), VillageId = Norm(f.VillageId), HouseId = f.HouseId, MemberId = f.MemberId };
        return View(await BuildVmAsync(c, f, seed, true, err));
    }

    // ---------- แก้ไข ----------
    [HttpGet]
    public async Task<IActionResult> Edit(string kind, long id)
    {
        if (!Valid(kind)) return BadRequest();
        using var c = _db.Open();
        var f = kind == "house"
            ? await c.QueryFirstOrDefaultAsync<RepairForm>(@"SELECT request_id, member_id, house_id, repair_type AS item_type, budget, status,
                    request_date AS req_date, finished_date FROM repair_house_request WHERE request_id=@id", new { id })
            : await c.QueryFirstOrDefaultAsync<RepairForm>(@"SELECT request_id, member_id, device_type AS item_type, detail, status,
                    received_date AS req_date FROM repair_medical_request WHERE request_id=@id", new { id });
        if (f == null || await MemberHouseAsync(c, f.MemberId ?? 0) == null) return NotFound();
        f.Kind = kind;
        return View(await BuildVmAsync(c, f, new PickerVm(), false, null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RepairForm f)
    {
        using var c = _db.Open();
        if (!Valid(f.Kind)) return BadRequest();
        var memberId = await c.ExecuteScalarAsync<long?>($"SELECT member_id FROM {Table(f.Kind)} WHERE request_id=@id", new { id = f.RequestId });
        if (memberId == null || await MemberHouseAsync(c, memberId.Value) == null) return NotFound();
        f.MemberId = memberId;     // ผู้ร้องขอไม่เปลี่ยนตอนแก้ไข

        var err = await ValidateAsync(c, f, false);
        if (err == null)
        {
            if (f.Kind == "house")
                await c.ExecuteAsync(@"UPDATE repair_house_request SET repair_type=@ItemType, budget=@Budget, status=@Status,
                    request_date=@ReqDate, finished_date=@FinishedDate WHERE request_id=@RequestId", f);
            else
                await c.ExecuteAsync(@"UPDATE repair_medical_request SET device_type=@ItemType, detail=@Detail, status=@Status,
                    received_date=@ReqDate WHERE request_id=@RequestId", f);
            await Log(c, "UPDATE_REPAIR", f.RequestId.ToString(), f.Kind);
            Flash("แก้ไขคำร้องเรียบร้อยแล้ว");
            return RedirectToAction(f.Kind == "house" ? "House" : "Medical");
        }
        return View(await BuildVmAsync(c, f, new PickerVm(), false, err));
    }

    // ---------- เปลี่ยนสถานะ / ลบ ----------
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(string kind, long id, string status, string? returnUrl)
    {
        if (!Statuses.Contains(status) || !Valid(kind)) return BadRequest();
        using var c = _db.Open();
        var memberId = await c.ExecuteScalarAsync<long?>($"SELECT member_id FROM {Table(kind)} WHERE request_id=@id", new { id });
        if (memberId == null || await MemberHouseAsync(c, memberId.Value) == null) return NotFound();
        var extra = kind == "house" ? (status == "done" ? ", finished_date = COALESCE(finished_date, CURRENT_DATE)" : ", finished_date = NULL") : "";
        await c.ExecuteAsync($"UPDATE {Table(kind)} SET status=@status{extra} WHERE request_id=@id", new { status, id });
        await Log(c, "UPDATE_REPAIR", id.ToString(), $"{kind} -> {status}");
        Flash("เปลี่ยนสถานะเรียบร้อยแล้ว");
        return Back(returnUrl, RedirectToAction(kind == "house" ? "House" : "Medical"));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string kind, long id, string? returnUrl)
    {
        if (!Valid(kind)) return BadRequest();
        using var c = _db.Open();
        var memberId = await c.ExecuteScalarAsync<long?>($"SELECT member_id FROM {Table(kind)} WHERE request_id=@id", new { id });
        if (memberId == null || await MemberHouseAsync(c, memberId.Value) == null) return NotFound();
        await c.ExecuteAsync($"DELETE FROM {Table(kind)} WHERE request_id=@id", new { id });
        await Log(c, "DELETE_REPAIR", id.ToString(), kind);
        Flash("ลบคำร้องเรียบร้อยแล้ว");
        return Back(returnUrl, RedirectToAction(kind == "house" ? "House" : "Medical"));
    }
}
