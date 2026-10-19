using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// เพิ่ม / แก้ไข / นำออก บุคคลในกลุ่มเปราะบาง 14 เป้าหมาย (member_vulnerable)
public class VulnerableController : AppController
{
    public VulnerableController(Db db) : base(db) { }

    private async Task<bool> ActiveMemberAsync(IDbConnection c, long memberId) =>
        await MemberHouseAsync(c, memberId) != null &&
        await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house_member WHERE member_id=@memberId AND is_active=1", new { memberId }) > 0;

    private async Task RefreshAsync(IDbConnection c, long memberId)
    {
        var y = await MemberYearAsync(c, memberId);
        if (y == null) return;
        var e = await Caches.RefreshVulnerableAsync(c, y.Value);
        if (e != null) await Log(c, "CACHE_REFRESH_FAILED", memberId.ToString(), e);
    }

    private async Task<VulFormVm> BuildVmAsync(IDbConnection c, VulForm f, PickerVm seed, string? err)
    {
        seed.ShowHouse = true; seed.ShowMember = true;
        var pick = await BuildPickerAsync(c, seed);
        f.Year = pick.Year;
        return new VulFormVm { Form = f, Picker = pick, Error = err, Types = await SimpleOptsAsync(c, "m_vulnerable_type", "vulnerable_type_id", "name_th", false) };
    }

    [HttpGet]
    public async Task<IActionResult> Create(long? memberId, int? typeId)
    {
        using var c = _db.Open();
        var seed = new PickerVm();
        if (memberId != null)
        {
            if (await MemberHouseAsync(c, memberId.Value) == null) return NotFound();
            seed = await ResolveMemberPickAsync(c, memberId.Value) ?? seed;
        }
        return View(await BuildVmAsync(c, new VulForm { VulnerableTypeId = typeId, IdentifiedDate = DateTime.Today }, seed, null));
    }

    private async Task<string?> AddAsync(IDbConnection c, long? memberId, int? typeId, DateTime? date, string? note)
    {
        note = Norm(note);
        if (memberId == null) return "กรุณาเลือกบุคคล";
        if (!await ActiveMemberAsync(c, memberId.Value)) return "ไม่พบบุคคลที่เลือก หรือไม่อยู่ในพื้นที่ที่คุณมีสิทธิ์";
        if (typeId == null || await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_vulnerable_type WHERE vulnerable_type_id=@t", new { t = typeId }) == 0)
            return "กรุณาเลือกประเภทกลุ่มเปราะบาง";
        if (date != null && (date > DateTime.Today || date < new DateTime(1900, 1, 1))) return "วันที่ระบุไม่ถูกต้อง";
        if ((note?.Length ?? 0) > 255) return "หมายเหตุต้องไม่เกิน 255 ตัวอักษร";
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM member_vulnerable WHERE member_id=@m AND vulnerable_type_id=@t", new { m = memberId, t = typeId }) > 0)
            return "บุคคลนี้อยู่ในกลุ่มนี้แล้ว";

        await c.ExecuteAsync("INSERT INTO member_vulnerable(member_id, vulnerable_type_id, identified_date, note) VALUES(@m, @t, @date, @note)",
            new { m = memberId, t = typeId, date, note });
        await RefreshAsync(c, memberId.Value);
        await Log(c, "ADD_VULNERABLE", memberId.ToString(), $"กลุ่ม {typeId}");
        return null;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VulForm f)
    {
        using var c = _db.Open();
        var err = await AddAsync(c, f.MemberId, f.VulnerableTypeId, f.IdentifiedDate, f.Note);
        if (err != null)
        {
            var seed = new PickerVm { Year = f.Year, AmphurId = Norm(f.AmphurId), TumbolId = Norm(f.TumbolId), VillageId = Norm(f.VillageId), HouseId = f.HouseId, MemberId = f.MemberId };
            return View(await BuildVmAsync(c, f, seed, err));
        }
        Flash("เพิ่มบุคคลเข้ากลุ่มเปราะบางเรียบร้อยแล้ว");
        return RedirectToAction("Vulnerable", "Report", new { type = f.VulnerableTypeId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAdd(long memberId, int? typeId, DateTime? identifiedDate, string? note)
    {
        using var c = _db.Open();
        var err = await AddAsync(c, memberId, typeId, identifiedDate, note);
        if (err != null) FlashErr(err); else Flash("เพิ่มบุคคลเข้ากลุ่มเปราะบางเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = memberId });
    }

    // ---------- แก้ไขวันที่/หมายเหตุ ----------
    [HttpGet]
    public async Task<IActionResult> Edit(long memberId, int typeId)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAsync(c, memberId)) return NotFound();
        var f = await c.QueryFirstOrDefaultAsync<VulForm>(@"SELECT member_id, vulnerable_type_id, identified_date, note
            FROM member_vulnerable WHERE member_id=@memberId AND vulnerable_type_id=@typeId", new { memberId, typeId });
        if (f == null) return NotFound();
        f.IsNew = false;
        return View(await EditVmAsync(c, f, null));
    }

    private async Task<VulFormVm> EditVmAsync(IDbConnection c, VulForm f, string? err) => new()
    {
        Form = f, Error = err,
        MemberName = await c.ExecuteScalarAsync<string?>(@"SELECT CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,''))
            FROM t_house_member m LEFT JOIN m_prefix p ON p.prefix_id=m.prefix_id WHERE m.member_id=@m", new { m = f.MemberId }),
        Types = await SimpleOptsAsync(c, "m_vulnerable_type", "vulnerable_type_id", "name_th", false)
    };

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(VulForm f)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAsync(c, f.MemberId ?? 0)) return NotFound();
        f.Note = Norm(f.Note); f.IsNew = false;
        string? err = null;
        if (f.IdentifiedDate != null && (f.IdentifiedDate > DateTime.Today || f.IdentifiedDate < new DateTime(1900, 1, 1))) err = "วันที่ระบุไม่ถูกต้อง";
        else if ((f.Note?.Length ?? 0) > 255) err = "หมายเหตุต้องไม่เกิน 255 ตัวอักษร";
        if (err != null) return View(await EditVmAsync(c, f, err));

        await c.ExecuteAsync(@"UPDATE member_vulnerable SET identified_date=@date, note=@note WHERE member_id=@m AND vulnerable_type_id=@t",
            new { date = f.IdentifiedDate, note = f.Note, m = f.MemberId, t = f.VulnerableTypeId });
        await Log(c, "UPDATE_VULNERABLE", f.MemberId.ToString(), $"กลุ่ม {f.VulnerableTypeId}");
        Flash("แก้ไขข้อมูลกลุ่มเปราะบางเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = f.MemberId });
    }

    // ---------- นำออกจากกลุ่ม ----------
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long memberId, int typeId, string? returnUrl)
    {
        using var c = _db.Open();
        if (await MemberHouseAsync(c, memberId) == null) return NotFound();
        await c.ExecuteAsync("DELETE FROM member_vulnerable WHERE member_id=@memberId AND vulnerable_type_id=@typeId", new { memberId, typeId });
        await RefreshAsync(c, memberId);
        await Log(c, "DELETE_VULNERABLE", memberId.ToString(), $"กลุ่ม {typeId}");
        Flash("นำบุคคลออกจากกลุ่มเปราะบางเรียบร้อยแล้ว");
        return Back(returnUrl, RedirectToAction("Person", "House", new { id = memberId }));
    }
}
