using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

public class PopulationController : AppController
{
    public PopulationController(Db db) : base(db) { }

    // ---------- สิทธิ์ตามพื้นที่ ----------
    // admin = ทุกพื้นที่, amphur = อำเภอตัวเอง, staff = ตำบลตัวเอง
    private async Task<(string? a, string? t)> AllowedAsync(IDbConnection c)
    {
        var (a, t) = Scope(null, null);
        if (t != null && a == null)
            a = await c.ExecuteScalarAsync<string?>("SELECT amphur_id FROM m_tumbol WHERE tumbol_id=@t", new { t });
        return (a, t);
    }

    private async Task<bool> VillageInScopeAsync(IDbConnection c, string? villageId)
    {
        if (string.IsNullOrEmpty(villageId)) return false;
        var (a, t) = Scope(null, null);
        return await c.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM m_village v JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE v.village_id=@villageId AND v.is_active=1 AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)",
            new { villageId, a, t }) > 0;
    }

    // ---------- รายการตัวเลือกแบบเลือกต่อเนื่อง (ใช้กับ JavaScript) ----------
    [HttpGet]
    public async Task<IActionResult> TumbolList(string? amphurId)
    {
        using var c = _db.Open();
        var (a, t) = Scope(null, null);
        amphurId = Norm(amphurId);
        if (amphurId == null || (a != null && a != amphurId && t == null)) return Json(new List<Opt>());
        var list = await c.QueryAsync<Opt>(@"SELECT tumbol_id AS Id, name_th AS Name FROM m_tumbol
            WHERE amphur_id=@amphurId AND is_active=1 AND (@t IS NULL OR tumbol_id=@t) ORDER BY tumbol_id", new { amphurId, t });
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> VillageList(string? tumbolId)
    {
        using var c = _db.Open();
        var (a, t) = Scope(null, null);
        tumbolId = Norm(tumbolId);
        if (tumbolId == null) return Json(new List<Opt>());
        var list = await c.QueryAsync<Opt>(@"SELECT v.village_id AS Id, CONCAT('หมู่ ', v.moo, ' ', v.name_th) AS Name
            FROM m_village v JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE v.tumbol_id=@tumbolId AND v.is_active=1 AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)
            ORDER BY v.moo", new { tumbolId, a, t });
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> HouseList(string? villageId, int year)
    {
        using var c = _db.Open();
        if (!await VillageInScopeAsync(c, Norm(villageId))) return Json(new List<Opt>());
        return Json(await HousesAsync(c, villageId!, year));
    }

    private static async Task<List<Opt>> HousesAsync(IDbConnection c, string villageId, int year) =>
        (await c.QueryAsync<Opt>(@"SELECT CAST(house_id AS CHAR) AS Id,
                CONCAT(house_code, IF(house_no IS NULL OR house_no='', '', CONCAT(' (บ้านเลขที่ ', house_no, ')'))) AS Name
            FROM t_house WHERE village_id=@villageId AND survey_year=@year AND is_active=1
            ORDER BY house_code LIMIT 500", new { villageId, year })).ToList();

    // ---------- ฟอร์ม ----------
    // /Population/Create  หรือ  /Population/Create?houseId=123 (เพิ่มสมาชิกเข้าบ้านที่เลือกไว้แล้ว)
    [HttpGet]
    public async Task<IActionResult> Create(long? houseId = null)
    {
        using var c = _db.Open();
        var f = new PopulationForm { Year = await _db.YearAsync(c, 0) };
        if (houseId != null)
        {
            var h = await c.QueryFirstOrDefaultAsync<HouseLoc>(@"SELECT h.house_id, h.survey_year, h.village_id, v.tumbol_id, tb.amphur_id
                FROM t_house h JOIN m_village v ON v.village_id=h.village_id JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
                WHERE h.house_id=@houseId AND h.is_active=1", new { houseId });
            if (h == null) return NotFound();
            if (!await VillageInScopeAsync(c, h.VillageId)) return Forbid();
            f.Year = h.SurveyYear; f.AmphurId = h.AmphurId; f.TumbolId = h.TumbolId; f.VillageId = h.VillageId; f.HouseId = h.HouseId;
        }
        return View(await BuildVmAsync(c, f, null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PopulationForm f)
    {
        using var c = _db.Open();
        f.FirstName = (f.FirstName ?? "").Trim();
        f.LastName = Norm(f.LastName);
        f.CitizenId = Norm(f.CitizenId);
        f.NewHouseCode = Norm(f.NewHouseCode);
        f.NewHouseNo = Norm(f.NewHouseNo);
        f.AmphurId = Norm(f.AmphurId); f.TumbolId = Norm(f.TumbolId); f.VillageId = Norm(f.VillageId);
        if (f.HouseMode != "new") f.HouseMode = "existing";

        var (err, age, ageMonth) = await ValidateAsync(c, f);
        if (err != null) return View(await BuildVmAsync(c, f, err));

        long memberId;
        using (var tx = await c.BeginTransactionAsync())
        {
            try
            {
                long houseId;
                if (f.HouseMode == "new")
                {
                    houseId = await c.ExecuteScalarAsync<long>(@"INSERT INTO t_house(house_code, survey_year, village_id, has_address_no, house_no, is_active, created_by, created_at)
                        VALUES(@code, @year, @village, @hasNo, @no, 1, @uid, NOW()); SELECT LAST_INSERT_ID();",
                        new { code = f.NewHouseCode, year = f.Year, village = f.VillageId, hasNo = f.NewHouseNo != null, no = f.NewHouseNo, uid = UserId }, tx);
                }
                else houseId = f.HouseId!.Value;

                memberId = await c.ExecuteScalarAsync<long>(@"INSERT INTO t_house_member(house_id, prefix_id, first_name, last_name, citizen_id, is_foreigner,
                        gender_id, birth_day, birth_month, birth_year, age, age_month, occupation_id,
                        marital_status_id, education_id, religion_id, relationship_id, physical_status_id,
                        is_informant, is_house_registration, is_normal, is_disabled, is_chronic_patient, is_self_reliance,
                        is_active, created_by, created_at)
                    VALUES(@houseId, @PrefixId, @FirstName, @LastName, @CitizenId, @IsForeigner,
                        @GenderId, @BirthDay, @BirthMonth, @BirthYear, @age, @ageMonth, @OccupationId,
                        @MaritalStatusId, @EducationId, @ReligionId, @RelationshipId, @PhysicalStatusId,
                        @IsInformant, @IsHouseRegistration, @isNormal, @IsDisabled, @IsChronicPatient, @IsSelfReliance,
                        1, @uid, NOW()); SELECT LAST_INSERT_ID();",
                    new
                    {
                        houseId, f.PrefixId, f.FirstName, f.LastName, f.CitizenId, f.IsForeigner,
                        f.GenderId, f.BirthDay, f.BirthMonth, f.BirthYear, age, ageMonth, f.OccupationId,
                        f.MaritalStatusId, f.EducationId, f.ReligionId, f.RelationshipId, f.PhysicalStatusId,
                        f.IsInformant, f.IsHouseRegistration, isNormal = !(f.IsDisabled || f.IsChronicPatient),
                        f.IsDisabled, f.IsChronicPatient, f.IsSelfReliance, uid = UserId
                    }, tx);

                await c.ExecuteAsync(@"INSERT INTO member_welfare(member_id, welfare_status_id, has_welfare_card, is_new_added, updated_by, updated_at)
                    VALUES(@memberId, @st, @card, 1, @uid, NOW())",
                    new { memberId, st = f.WelfareStatusId, card = f.WelfareStatusId == 1, uid = UserId }, tx);

                await tx.CommitAsync();
            }
            catch (MySqlException ex)
            {
                await tx.RollbackAsync();
                return View(await BuildVmAsync(c, f, "บันทึกไม่ได้: " + ex.Message));
            }
        }

        await Log(c, "CREATE_MEMBER", memberId.ToString(), $"{f.FirstName} {f.LastName} ปี {f.Year}");
        TempData["Ok"] = $"เพิ่มข้อมูล {f.FirstName} {f.LastName} เรียบร้อยแล้ว";
        return RedirectToAction("Person", "House", new { id = memberId });
    }

    // ---------- ตรวจข้อมูล ----------
    private async Task<(string? err, short? age, short? ageMonth)> ValidateAsync(IDbConnection c, PopulationForm f)
    {
        if (f.Year < 2500 || f.Year > DateTime.Now.Year + 543 + 1) return ("ปีสำรวจ (พ.ศ.) ไม่ถูกต้อง", null, null);
        if (!await VillageInScopeAsync(c, f.VillageId)) return ("กรุณาเลือกหมู่บ้านภายในพื้นที่ที่คุณมีสิทธิ์", null, null);

        if (f.HouseMode == "new")
        {
            if (f.NewHouseCode == null) return ("กรุณากรอกรหัสบ้านของบ้านใหม่", null, null);
            if (f.NewHouseCode.Length > 20) return ("รหัสบ้านต้องไม่เกิน 20 ตัวอักษร", null, null);
            var dup = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house WHERE house_code=@code AND survey_year=@year",
                new { code = f.NewHouseCode, year = f.Year });
            if (dup > 0) return ("รหัสบ้านนี้มีอยู่แล้วในปีสำรวจนี้ ให้เลือก \"บ้านที่มีอยู่\" แทน", null, null);
        }
        else
        {
            if (f.HouseId == null) return ("กรุณาเลือกบ้าน หรือเลือกสร้างบ้านใหม่", null, null);
            var ok = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house WHERE house_id=@id AND village_id=@v AND survey_year=@y AND is_active=1",
                new { id = f.HouseId, v = f.VillageId, y = f.Year });
            if (ok == 0) return ("ไม่พบบ้านที่เลือกในหมู่บ้านและปีสำรวจนี้", null, null);
        }

        if (f.FirstName.Length == 0) return ("กรุณากรอกชื่อ", null, null);
        if (f.FirstName.Length > 100 || (f.LastName?.Length ?? 0) > 100) return ("ชื่อหรือนามสกุลยาวเกิน 100 ตัวอักษร", null, null);
        if (f.GenderId == null) return ("กรุณาเลือกเพศ", null, null);

        // เลขบัตรประชาชน
        if (f.CitizenId == null)
        {
            if (!f.IsForeigner) return ("กรุณากรอกเลขบัตรประชาชน 13 หลัก (กรณีไม่มีเลขให้ติ๊ก \"ชาวต่างชาติ\")", null, null);
        }
        else
        {
            if (f.CitizenId.Length != 13 || !f.CitizenId.All(char.IsDigit)) return ("เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก", null, null);
            if (!f.IsForeigner && !ValidCitizenId(f.CitizenId)) return ("เลขบัตรประชาชนไม่ถูกต้อง (ตรวจเลขหลักสุดท้ายไม่ผ่าน)", null, null);
            var dup = await c.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM t_house_member m JOIN t_house h ON h.house_id=m.house_id
                WHERE m.citizen_id=@cid AND h.survey_year=@y AND m.is_active=1 AND h.is_active=1", new { cid = f.CitizenId, y = f.Year });
            if (dup > 0) return ("เลขบัตรประชาชนนี้มีอยู่แล้วในปีสำรวจนี้", null, null);
        }

        // วันเกิด (พ.ศ.) และคำนวณอายุ
        short? age = null, ageMonth = null;
        var nowBe = DateTime.Now.Year + 543;
        if (f.BirthYear == null)
        {
            if (f.BirthMonth != null || f.BirthDay != null) return ("กรุณากรอกปีเกิด (พ.ศ.) ด้วย", null, null);
        }
        else
        {
            if (f.BirthYear < 2400 || f.BirthYear > nowBe) return ($"ปีเกิดต้องเป็น พ.ศ. ระหว่าง 2400 ถึง {nowBe}", null, null);
            if (f.BirthMonth == null)
            {
                if (f.BirthDay != null) return ("กรุณาเลือกเดือนเกิดด้วย", null, null);
                age = (short)(nowBe - f.BirthYear.Value);
            }
            else
            {
                if (f.BirthMonth < 1 || f.BirthMonth > 12) return ("เดือนเกิดไม่ถูกต้อง", null, null);
                if (f.BirthDay != null && (f.BirthDay < 1 || f.BirthDay > DateTime.DaysInMonth(f.BirthYear.Value - 543, f.BirthMonth.Value)))
                    return ("วันเกิดไม่ถูกต้องสำหรับเดือนที่เลือก", null, null);
                var today = DateTime.Today;
                var months = (nowBe - f.BirthYear.Value) * 12 + (today.Month - f.BirthMonth.Value);
                if (f.BirthDay != null && today.Day < f.BirthDay) months--;
                if (months < 0) return ("วันเกิดเป็นอนาคต", null, null);
                age = (short)(months / 12); ageMonth = (short)(months % 12);
            }
        }

        if (f.WelfareStatusId < 0 || f.WelfareStatusId > 3) return ("สถานะบัตรสวัสดิการไม่ถูกต้อง", null, null);
        return (null, age, ageMonth);
    }

    private static bool ValidCitizenId(string s)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (s[i] - '0') * (13 - i);
        return (11 - sum % 11) % 10 == s[12] - '0';
    }

    // ---------- เตรียมข้อมูลให้ฟอร์ม ----------
    private async Task<PopulationFormVm> BuildVmAsync(IDbConnection c, PopulationForm f, string? error)
    {
        var (a, t) = await AllowedAsync(c);
        if (a != null && string.IsNullOrEmpty(f.AmphurId)) f.AmphurId = a;
        if (t != null && string.IsNullOrEmpty(f.TumbolId)) f.TumbolId = t;

        var vm = new PopulationFormVm { Form = f, Error = error };
        vm.Amphurs = (await Amphurs(c)).Where(x => a == null || x.Id == a).ToList();
        vm.Tumbols = (await Tumbols(c, Norm(f.AmphurId))).Where(x => t == null || x.Id == t).ToList();
        vm.Villages = f.TumbolId == null ? new() : (await c.QueryAsync<Opt>(
            "SELECT village_id AS Id, CONCAT('หมู่ ', moo, ' ', name_th) AS Name FROM m_village WHERE tumbol_id=@t AND is_active=1 ORDER BY moo",
            new { t = f.TumbolId })).ToList();
        vm.Houses = f.VillageId == null ? new() : await HousesAsync(c, f.VillageId, f.Year);
        vm.Prefixes = (await c.QueryAsync<Opt>("SELECT CAST(prefix_id AS CHAR) AS Id, name AS Name FROM m_prefix WHERE is_active=1 ORDER BY prefix_id")).ToList();
        vm.Genders = (await c.QueryAsync<Opt>("SELECT CAST(gender_id AS CHAR) AS Id, name AS Name FROM m_gender ORDER BY gender_id")).ToList();
        vm.Occupations = (await c.QueryAsync<Opt>("SELECT CAST(occupation_id AS CHAR) AS Id, name AS Name FROM m_occupation WHERE is_active=1 ORDER BY name")).ToList();
        vm.MaritalStatuses = (await c.QueryAsync<Opt>("SELECT CAST(marital_status_id AS CHAR) AS Id, name AS Name FROM m_marital_status WHERE is_active=1 ORDER BY marital_status_id")).ToList();
        vm.Educations = (await c.QueryAsync<Opt>("SELECT CAST(education_id AS CHAR) AS Id, name AS Name FROM m_education WHERE is_active=1 ORDER BY education_id")).ToList();
        vm.Religions = (await c.QueryAsync<Opt>("SELECT CAST(religion_id AS CHAR) AS Id, name AS Name FROM m_religion WHERE is_active=1 ORDER BY religion_id")).ToList();
        vm.Relationships = (await c.QueryAsync<Opt>("SELECT CAST(relationship_id AS CHAR) AS Id, name AS Name FROM m_relationship WHERE is_active=1 ORDER BY relationship_id")).ToList();
        vm.PhysicalStatuses = (await c.QueryAsync<Opt>("SELECT CAST(physical_status_id AS CHAR) AS Id, name_th AS Name FROM m_physical_status ORDER BY physical_status_id")).ToList();
        return vm;
    }
}
