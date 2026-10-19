using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

public class HouseController : AppController
{
    private readonly IWebHostEnvironment _env;
    public HouseController(Db db, IWebHostEnvironment env) : base(db) { _env = env; }

    private string UploadRoot => Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads", "houses");

    private async Task<IActionResult> ToHouseAsync(IDbConnection c, long houseId)
    {
        var h = await c.QueryFirstOrDefaultAsync<HouseHead>("SELECT house_id, survey_year, house_code FROM t_house WHERE house_id=@houseId", new { houseId });
        return h == null ? RedirectToAction("Household", "Report") : RedirectToAction("Detail", new { code = h.HouseCode, year = h.SurveyYear });
    }

    // ======================= รายละเอียดบ้าน =======================
    // /House/Detail?code=47010024669
    public async Task<IActionResult> Detail(string code, int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var head = await c.QueryFirstOrDefaultAsync<HouseHead>(@"
            SELECT h.house_id, h.survey_year, h.house_code, h.house_no, h.latitude, h.longitude,
                   v.name_th AS village_name, t.name_th AS tumbol_name, a.name_th AS amphur_name
            FROM t_house h
            JOIN m_village v ON v.village_id = h.village_id
            JOIN m_tumbol  t ON t.tumbol_id  = v.tumbol_id
            JOIN m_amphur  a ON a.amphur_id  = t.amphur_id
            WHERE h.house_code=@code AND h.survey_year=@year AND h.is_active=1", new { code, year });
        if (head == null) return NotFound();
        if (!await HouseAllowedAsync(c, head.HouseId)) return Forbid();

        var vm = new HouseVm
        {
            Head = head,
            Members = (await c.QueryAsync<MemberRow>(@"SELECT l.* FROM v_member_welfare_list l
                JOIN t_house_member m ON m.member_id = l.member_id AND m.is_active = 1
                WHERE l.house_id=@id ORDER BY l.member_id", new { id = head.HouseId })).ToList(),
            Images = (await c.QueryAsync<ImageRow>(
                "SELECT image_id, file_path, description FROM house_images WHERE house_id=@id ORDER BY uploaded_at DESC", new { id = head.HouseId })).ToList(),
            Expense = await c.QueryFirstOrDefaultAsync<ExpenseForm>(@"SELECT house_id, cost_of_living, needed_expense, internet_expense,
                    unneeded_expense, total_expense, debt_total, debt_savings_group, debt_cooperative, debt_baac, debt_gov_bank,
                    debt_other_bank, debt_finance_co, debt_informal, debt_capital, debt_other
                FROM house_expense WHERE house_id=@id", new { id = head.HouseId }),
            Tenures = (await c.QueryAsync<TenureRow>(@"SELECT t.tenure_id, t.purpose, t.tenure_type, t.area_1, t.area_2,
                    t.document_type_id, d.name AS document_name, t.detail
                FROM house_tenure t LEFT JOIN m_document_type d ON d.document_type_id = t.document_type_id
                WHERE t.house_id=@id ORDER BY t.tenure_id", new { id = head.HouseId })).ToList()
        };
        return View(vm);
    }

    // ======================= เพิ่ม / แก้ไข / ลบ บ้าน =======================
    private async Task<HouseFormVm> BuildHouseVmAsync(IDbConnection c, HouseForm f, string? error)
    {
        var pick = await BuildPickerAsync(c, new PickerVm { Year = f.Year, AmphurId = f.AmphurId, TumbolId = f.TumbolId, VillageId = f.VillageId });
        f.Year = pick.Year;
        return new HouseFormVm
        {
            Form = f, Picker = pick, Error = error,
            Levels = await SimpleOptsAsync(c, "m_happiness_level", "happiness_level_id", "name_th", false)
        };
    }

    [HttpGet]
    public async Task<IActionResult> HouseEdit(long? id)
    {
        using var c = _db.Open();
        var f = new HouseForm { IsNew = true, Year = await _db.YearAsync(c, 0) };
        if (id != null)
        {
            var loaded = await c.QueryFirstOrDefaultAsync<HouseForm>(@"SELECT h.house_id, h.survey_year AS year, h.village_id, v.tumbol_id, tb.amphur_id,
                    h.house_code, h.house_no, h.house_nearby, h.soi, h.road, h.latitude, h.longitude, h.happiness_level_id, h.is_agree, h.sign_date
                FROM t_house h JOIN m_village v ON v.village_id=h.village_id JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
                WHERE h.house_id=@id AND h.is_active=1", new { id });
            if (loaded == null) return NotFound();
            if (!await HouseAllowedAsync(c, id.Value)) return Forbid();
            f = loaded; f.IsNew = false;
        }
        return View(await BuildHouseVmAsync(c, f, null));
    }

    private async Task<string?> ValidateHouseAsync(IDbConnection c, HouseForm f)
    {
        if (f.Year < 2500 || f.Year > DateTime.Now.Year + 543 + 1) return "ปีสำรวจ (พ.ศ.) ไม่ถูกต้อง";
        if (!await VillageAllowedAsync(c, f.VillageId)) return "กรุณาเลือกหมู่บ้านภายในพื้นที่ที่คุณมีสิทธิ์";
        if (f.HouseCode.Length == 0) return "กรุณากรอกรหัสบ้าน";
        if (f.HouseCode.Length > 20) return "รหัสบ้านต้องไม่เกิน 20 ตัวอักษร";
        if ((f.HouseNo?.Length ?? 0) > 30) return "บ้านเลขที่ต้องไม่เกิน 30 ตัวอักษร";
        if ((f.HouseNearby?.Length ?? 0) > 255) return "จุดสังเกตต้องไม่เกิน 255 ตัวอักษร";
        if ((f.Soi?.Length ?? 0) > 100 || (f.Road?.Length ?? 0) > 100) return "ซอยหรือถนนต้องไม่เกิน 100 ตัวอักษร";
        if (f.Latitude != null && (f.Latitude < 5 || f.Latitude > 21)) return "ละติจูดต้องอยู่ระหว่าง 5 ถึง 21 (ประเทศไทย)";
        if (f.Longitude != null && (f.Longitude < 97 || f.Longitude > 106)) return "ลองจิจูดต้องอยู่ระหว่าง 97 ถึง 106 (ประเทศไทย)";
        if ((f.Latitude == null) != (f.Longitude == null)) return "กรุณากรอกพิกัดให้ครบทั้งละติจูดและลองจิจูด";
        if (f.HappinessLevelId != null &&
            await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_happiness_level WHERE happiness_level_id=@x", new { x = f.HappinessLevelId }) == 0)
            return "ระดับความอยู่ดีมีสุขไม่ถูกต้อง";
        var dup = await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM t_house WHERE house_code=@code AND survey_year=@year AND house_id<>@id",
            new { code = f.HouseCode, year = f.Year, id = f.IsNew ? 0 : f.HouseId });
        if (dup > 0) return "รหัสบ้านนี้มีอยู่แล้วในปีสำรวจนี้";
        return null;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> HouseEdit(HouseForm f)
    {
        using var c = _db.Open();
        f.HouseCode = (f.HouseCode ?? "").Trim();
        f.HouseNo = Norm(f.HouseNo); f.HouseNearby = Norm(f.HouseNearby); f.Soi = Norm(f.Soi); f.Road = Norm(f.Road);
        f.AmphurId = Norm(f.AmphurId); f.TumbolId = Norm(f.TumbolId); f.VillageId = Norm(f.VillageId);

        var err = await ValidateHouseAsync(c, f);
        if (err == null && !f.IsNew && !await HouseAllowedAsync(c, f.HouseId)) return Forbid();
        if (err == null)
        {
            try
            {
                var p = new
                {
                    f.HouseId, code = f.HouseCode, year = f.Year, village = f.VillageId, hasNo = f.HouseNo != null, no = f.HouseNo,
                    f.HouseNearby, f.Soi, f.Road, f.Latitude, f.Longitude, f.HappinessLevelId, f.IsAgree,
                    f.SignDate, uid = UserId
                };
                if (f.IsNew)
                {
                    var newId = await c.ExecuteScalarAsync<long>(@"INSERT INTO t_house(house_code, survey_year, village_id, has_address_no, house_no,
                            house_nearby, soi, road, latitude, longitude, happiness_level_id, is_agree, sign_date, is_active, created_by, created_at)
                        VALUES(@code, @year, @village, @hasNo, @no, @HouseNearby, @Soi, @Road, @Latitude, @Longitude, @HappinessLevelId, @IsAgree, @SignDate, 1, @uid, NOW());
                        SELECT LAST_INSERT_ID();", p);
                    await Log(c, "CREATE_HOUSE", newId.ToString(), $"{f.HouseCode} ปี {f.Year}");
                    Flash($"เพิ่มบ้านรหัส {f.HouseCode} เรียบร้อยแล้ว");
                }
                else
                {
                    await c.ExecuteAsync(@"UPDATE t_house SET house_code=@code, survey_year=@year, village_id=@village, has_address_no=@hasNo, house_no=@no,
                            house_nearby=@HouseNearby, soi=@Soi, road=@Road, latitude=@Latitude, longitude=@Longitude,
                            happiness_level_id=@HappinessLevelId, is_agree=@IsAgree, sign_date=@SignDate, updated_by=@uid, updated_at=NOW()
                        WHERE house_id=@HouseId", p);
                    await Log(c, "UPDATE_HOUSE", f.HouseId.ToString(), f.HouseCode);
                    Flash("บันทึกการแก้ไขบ้านเรียบร้อยแล้ว");
                }
                return RedirectToAction("Detail", new { code = f.HouseCode, year = f.Year });
            }
            catch (MySqlException ex) { err = "บันทึกไม่ได้: " + ex.Message; }
        }
        return View(await BuildHouseVmAsync(c, f, err));
    }

    // ลบบ้านแบบซ่อน (is_active=0) พร้อมสมาชิกในบ้าน เพราะมีตารางอื่นอ้างอิง (FK) จึงไม่ลบข้อมูลจริง
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> HouseDelete(long id)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, id)) return Forbid();
        var year = await HouseYearAsync(c, id);
        using (var tx = c.BeginTransaction())
        {
            await c.ExecuteAsync("UPDATE t_house SET is_active=0, updated_by=@uid, updated_at=NOW() WHERE house_id=@id", new { id, uid = UserId }, tx);
            await c.ExecuteAsync("UPDATE t_house_member SET is_active=0, updated_by=@uid, updated_at=NOW() WHERE house_id=@id", new { id, uid = UserId }, tx);
            tx.Commit();
        }
        if (year != null)
        {
            var e1 = await Caches.RefreshDiseaseAsync(c, year.Value);
            var e2 = await Caches.RefreshVulnerableAsync(c, year.Value);
            if (e1 != null || e2 != null) await Log(c, "CACHE_REFRESH_FAILED", id.ToString(), e1 ?? e2);
        }
        await Log(c, "DELETE_HOUSE", id.ToString());
        Flash("ลบบ้านและสมาชิกในบ้านเรียบร้อยแล้ว");
        return RedirectToAction("Household", "Report");
    }

    // ======================= รูปภาพบ้าน =======================
    public class ImgInfo { public long HouseId { get; set; } public string FilePath { get; set; } = ""; }
    private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp" };

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ImageUpload(long houseId, IFormFile? file, string? description)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return Forbid();
        if (file == null || file.Length == 0) { FlashErr("กรุณาเลือกไฟล์รูปภาพ"); return await ToHouseAsync(c, houseId); }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext) || !(file.ContentType ?? "").StartsWith("image/"))
        { FlashErr("รองรับเฉพาะไฟล์ .jpg .jpeg .png .webp"); return await ToHouseAsync(c, houseId); }
        if (file.Length > 5 * 1024 * 1024) { FlashErr("ไฟล์ต้องมีขนาดไม่เกิน 5 MB"); return await ToHouseAsync(c, houseId); }

        var dir = Path.Combine(UploadRoot, houseId.ToString());
        Directory.CreateDirectory(dir);
        var name = Guid.NewGuid().ToString("N") + ext;
        using (var fs = System.IO.File.Create(Path.Combine(dir, name)))
            await file.CopyToAsync(fs);

        description = Norm(description);
        if (description != null && description.Length > 500) description = description[..500];
        await c.ExecuteAsync(@"INSERT INTO house_images(house_id, file_path, description, uploaded_by, uploaded_at)
            VALUES(@houseId, @path, @description, @uid, NOW())",
            new { houseId, path = $"/uploads/houses/{houseId}/{name}", description, uid = UserId });
        await Log(c, "ADD_HOUSE_IMAGE", houseId.ToString());
        Flash("เพิ่มรูปภาพบ้านเรียบร้อยแล้ว");
        return await ToHouseAsync(c, houseId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImageDelete(int id)
    {
        using var c = _db.Open();
        var img = await c.QueryFirstOrDefaultAsync<ImgInfo>(
            "SELECT house_id, file_path FROM house_images WHERE image_id=@id", new { id });
        if (img == null) return NotFound();
        var houseId = img.HouseId;
        if (!await HouseAllowedAsync(c, houseId)) return Forbid();
        await c.ExecuteAsync("DELETE FROM house_images WHERE image_id=@id", new { id });

        // ลบไฟล์จริง เฉพาะไฟล์ที่อยู่ในโฟลเดอร์อัปโหลดของระบบเท่านั้น
        try
        {
            var rel = img.FilePath.Replace('\\', '/');
            if (rel.StartsWith("/uploads/houses/"))
            {
                var root = Path.GetFullPath(UploadRoot) + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(Path.Combine(UploadRoot, rel["/uploads/houses/".Length..]));
                if (full.StartsWith(root) && System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
        }
        catch { /* ลบไฟล์ไม่สำเร็จไม่ต้องทำให้ทั้งคำสั่งล้มเหลว */ }
        await Log(c, "DELETE_HOUSE_IMAGE", id.ToString());
        Flash("ลบรูปภาพเรียบร้อยแล้ว");
        return await ToHouseAsync(c, houseId);
    }

    // ======================= ค่าใช้จ่าย / หนี้สินครัวเรือน (house_expense) =======================
    [HttpGet]
    public async Task<IActionResult> ExpenseEdit(long houseId)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return Forbid();
        var code = await c.ExecuteScalarAsync<string?>("SELECT house_code FROM t_house WHERE house_id=@houseId AND is_active=1", new { houseId });
        if (code == null) return NotFound();
        var f = await c.QueryFirstOrDefaultAsync<ExpenseForm>(@"SELECT house_id, cost_of_living, needed_expense, internet_expense,
                unneeded_expense, total_expense, debt_total, debt_savings_group, debt_cooperative, debt_baac, debt_gov_bank,
                debt_other_bank, debt_finance_co, debt_informal, debt_capital, debt_other FROM house_expense WHERE house_id=@houseId", new { houseId })
            ?? new ExpenseForm { HouseId = houseId };
        return View(new ExpenseFormVm { Form = f, HouseCode = code });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExpenseEdit(ExpenseForm f)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, f.HouseId)) return Forbid();
        var code = await c.ExecuteScalarAsync<string?>("SELECT house_code FROM t_house WHERE house_id=@id AND is_active=1", new { id = f.HouseId }) ?? "";

        var nums = new decimal?[] { f.CostOfLiving, f.NeededExpense, f.InternetExpense, f.UnneededExpense, f.TotalExpense,
            f.DebtSavingsGroup, f.DebtCooperative, f.DebtBaac, f.DebtGovBank, f.DebtOtherBank, f.DebtFinanceCo, f.DebtInformal, f.DebtCapital, f.DebtOther };
        if (nums.Any(x => x != null && (x < 0 || x > 9_999_999_999m)))
            return View(new ExpenseFormVm { Form = f, HouseCode = code, Error = "จำนวนเงินต้องไม่ติดลบและไม่เกิน 9,999,999,999 บาท" });

        // หนี้สินรวม = ผลรวมหนี้แต่ละแหล่ง / ถ้าไม่กรอกรายจ่ายรวม ใช้ จำเป็น + อินเทอร์เน็ต + ไม่จำเป็น
        f.DebtTotal = (f.DebtSavingsGroup ?? 0) + (f.DebtCooperative ?? 0) + (f.DebtBaac ?? 0) + (f.DebtGovBank ?? 0) +
                      (f.DebtOtherBank ?? 0) + (f.DebtFinanceCo ?? 0) + (f.DebtInformal ?? 0) + (f.DebtCapital ?? 0) + (f.DebtOther ?? 0);
        f.TotalExpense ??= (f.NeededExpense ?? 0) + (f.InternetExpense ?? 0) + (f.UnneededExpense ?? 0);

        await c.ExecuteAsync(@"INSERT INTO house_expense(house_id, cost_of_living, needed_expense, internet_expense, unneeded_expense, debt_total,
                total_expense, debt_savings_group, debt_cooperative, debt_baac, debt_gov_bank, debt_other_bank, debt_finance_co, debt_informal, debt_capital, debt_other)
            VALUES(@HouseId, @CostOfLiving, @NeededExpense, @InternetExpense, @UnneededExpense, @DebtTotal,
                @TotalExpense, @DebtSavingsGroup, @DebtCooperative, @DebtBaac, @DebtGovBank, @DebtOtherBank, @DebtFinanceCo, @DebtInformal, @DebtCapital, @DebtOther)
            ON DUPLICATE KEY UPDATE cost_of_living=@CostOfLiving, needed_expense=@NeededExpense, internet_expense=@InternetExpense,
                unneeded_expense=@UnneededExpense, debt_total=@DebtTotal, total_expense=@TotalExpense, debt_savings_group=@DebtSavingsGroup,
                debt_cooperative=@DebtCooperative, debt_baac=@DebtBaac, debt_gov_bank=@DebtGovBank, debt_other_bank=@DebtOtherBank,
                debt_finance_co=@DebtFinanceCo, debt_informal=@DebtInformal, debt_capital=@DebtCapital, debt_other=@DebtOther", f);
        await Log(c, "UPDATE_HOUSE_EXPENSE", f.HouseId.ToString());
        Flash("บันทึกค่าใช้จ่ายและหนี้สินครัวเรือนเรียบร้อยแล้ว");
        return await ToHouseAsync(c, f.HouseId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExpenseDelete(long houseId)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return Forbid();
        await c.ExecuteAsync("DELETE FROM house_expense WHERE house_id=@houseId", new { houseId });
        await Log(c, "DELETE_HOUSE_EXPENSE", houseId.ToString());
        Flash("ลบข้อมูลค่าใช้จ่ายเรียบร้อยแล้ว");
        return await ToHouseAsync(c, houseId);
    }

    // ======================= การถือครองที่ดิน/ที่อยู่อาศัย (house_tenure) =======================
    [HttpGet]
    public async Task<IActionResult> TenureEdit(long houseId, int? id)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return Forbid();
        var code = await c.ExecuteScalarAsync<string?>("SELECT house_code FROM t_house WHERE house_id=@houseId AND is_active=1", new { houseId });
        if (code == null) return NotFound();
        var f = new TenureForm { HouseId = houseId };
        if (id != null)
        {
            var t = await c.QueryFirstOrDefaultAsync<TenureForm>(@"SELECT tenure_id, house_id, purpose, tenure_type, area_1, area_2, document_type_id, detail
                FROM house_tenure WHERE tenure_id=@id AND house_id=@houseId", new { id, houseId });
            if (t == null) return NotFound();
            f = t;
        }
        return View(new TenureFormVm { Form = f, HouseCode = code, Documents = await SimpleOptsAsync(c, "m_document_type", "document_type_id", "name", true) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TenureEdit(TenureForm f)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, f.HouseId)) return Forbid();
        f.Purpose = (f.Purpose ?? "").Trim(); f.TenureType = (f.TenureType ?? "").Trim(); f.Detail = Norm(f.Detail);
        var code = await c.ExecuteScalarAsync<string?>("SELECT house_code FROM t_house WHERE house_id=@id", new { id = f.HouseId }) ?? "";
        string? err = null;
        if (f.Purpose.Length == 0 || f.Purpose.Length > 10) err = "กรุณากรอกวัตถุประสงค์การใช้ (ไม่เกิน 10 ตัวอักษร)";
        else if (f.TenureType.Length == 0 || f.TenureType.Length > 10) err = "กรุณากรอกประเภทการถือครอง (ไม่เกิน 10 ตัวอักษร)";
        else if ((f.Area1 ?? 0) < 0 || (f.Area2 ?? 0) < 0) err = "พื้นที่ต้องไม่ติดลบ";
        else if ((f.Detail?.Length ?? 0) > 255) err = "รายละเอียดต้องไม่เกิน 255 ตัวอักษร";
        if (err != null)
            return View(new TenureFormVm { Form = f, HouseCode = code, Error = err, Documents = await SimpleOptsAsync(c, "m_document_type", "document_type_id", "name", true) });

        if (f.TenureId == 0)
            await c.ExecuteAsync(@"INSERT INTO house_tenure(house_id, purpose, tenure_type, area_1, area_2, document_type_id, detail)
                VALUES(@HouseId, @Purpose, @TenureType, @Area1, @Area2, @DocumentTypeId, @Detail)", f);
        else
            await c.ExecuteAsync(@"UPDATE house_tenure SET purpose=@Purpose, tenure_type=@TenureType, area_1=@Area1, area_2=@Area2,
                document_type_id=@DocumentTypeId, detail=@Detail WHERE tenure_id=@TenureId AND house_id=@HouseId", f);
        await Log(c, f.TenureId == 0 ? "CREATE_TENURE" : "UPDATE_TENURE", f.HouseId.ToString());
        Flash("บันทึกข้อมูลการถือครองเรียบร้อยแล้ว");
        return await ToHouseAsync(c, f.HouseId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TenureDelete(long houseId, int id)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return Forbid();
        await c.ExecuteAsync("DELETE FROM house_tenure WHERE tenure_id=@id AND house_id=@houseId", new { id, houseId });
        await Log(c, "DELETE_TENURE", id.ToString());
        Flash("ลบข้อมูลการถือครองเรียบร้อยแล้ว");
        return await ToHouseAsync(c, houseId);
    }

    // ======================= รายละเอียดบุคคล =======================
    // /House/Person/123
    public async Task<IActionResult> Person(long id)
    {
        using var c = _db.Open();
        var person = await c.QueryFirstOrDefaultAsync<MemberRow>(@"SELECT l.* FROM v_member_welfare_list l
            JOIN t_house_member m ON m.member_id = l.member_id AND m.is_active = 1 WHERE l.member_id=@id", new { id });
        if (person == null) return NotFound();
        if (!await HouseAllowedAsync(c, person.HouseId)) return Forbid();

        var vm = new PersonVm
        {
            Person = person,
            Income = await c.QueryFirstOrDefaultAsync<IncomeRow>(@"
                SELECT o.name AS main_occupation, i.main_income, i.second_income, i.other_income,
                       i.online_income, i.total_income, i.saving
                FROM member_income i
                LEFT JOIN m_occupation o ON o.occupation_id = i.main_occupation_id
                WHERE i.member_id=@id", new { id }),
            Assess = await c.QueryFirstOrDefaultAsync<AssessRow>(
                "SELECT * FROM member_assessment WHERE member_id=@id", new { id }),
            Housemates = (await c.QueryAsync<MemberRow>(@"SELECT l.* FROM v_member_welfare_list l
                JOIN t_house_member m ON m.member_id = l.member_id AND m.is_active = 1
                WHERE l.house_id=@hid ORDER BY l.member_id", new { hid = person.HouseId })).ToList(),
            Diseases = (await c.QueryAsync<MemberDiseaseRow>(@"SELECT md.member_disease_id, d.disease_id, d.name_th AS name, d.icd10, md.diagnosed_date
                FROM member_disease md JOIN m_disease d ON d.disease_id = md.disease_id
                WHERE md.member_id=@id ORDER BY md.member_disease_id DESC", new { id })).ToList(),
            Vulnerables = (await c.QueryAsync<MemberVulRow>(@"SELECT mv.vulnerable_type_id, vt.name_th AS name, mv.identified_date, mv.note
                FROM member_vulnerable mv JOIN m_vulnerable_type vt ON vt.vulnerable_type_id = mv.vulnerable_type_id
                WHERE mv.member_id=@id ORDER BY mv.vulnerable_type_id", new { id })).ToList(),
            HouseRepairs = (await c.QueryAsync<RepairRow>(@"SELECT r.request_id, r.member_id, r.repair_type AS item_type, r.budget, r.status, r.request_date AS req_date
                FROM repair_house_request r WHERE r.member_id=@id ORDER BY r.request_date DESC, r.request_id DESC", new { id })).ToList(),
            MedicalRepairs = (await c.QueryAsync<RepairRow>(@"SELECT r.request_id, r.member_id, r.device_type AS item_type, r.detail, r.status, r.received_date AS req_date
                FROM repair_medical_request r WHERE r.member_id=@id ORDER BY r.received_date DESC, r.request_id DESC", new { id })).ToList(),
            DiseaseOptions = await SimpleOptsAsync(c, "m_disease", "disease_id", "name_th", false),
            VulnerableOptions = await SimpleOptsAsync(c, "m_vulnerable_type", "vulnerable_type_id", "name_th", false)
        };
        return View(vm);
    }

    // เปลี่ยนสถานะบัตรสวัสดิการรายบุคคล
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetWelfare(long memberId, int statusId)
    {
        if (statusId < 0 || statusId > 3) return BadRequest();
        using var c = _db.Open();
        if (await MemberHouseAsync(c, memberId) == null) return Forbid();
        await c.ExecuteAsync(@"INSERT INTO member_welfare(member_id, welfare_status_id, has_welfare_card, updated_by)
            VALUES(@memberId,@statusId,@card,@uid)
            ON DUPLICATE KEY UPDATE welfare_status_id=@statusId, has_welfare_card=@card, updated_by=@uid, updated_at=NOW()",
            new { memberId, statusId, card = statusId == 1, uid = UserId });
        await Log(c, "UPDATE_WELFARE", memberId.ToString(), $"สถานะ {statusId}");
        Flash("บันทึกสถานะบัตรสวัสดิการเรียบร้อยแล้ว");
        return RedirectToAction("Person", new { id = memberId });
    }
}
