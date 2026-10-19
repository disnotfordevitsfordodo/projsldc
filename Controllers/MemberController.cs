using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// แก้ไข/ลบข้อมูลบุคคล รายได้ และผลประเมิน (การเพิ่มบุคคลใหม่อยู่ที่ PopulationController)
public class MemberController : AppController
{
    public MemberController(Db db) : base(db) { }

    private static async Task<string> MemberNameAsync(IDbConnection c, long id) =>
        await c.ExecuteScalarAsync<string?>(@"SELECT CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,''))
            FROM t_house_member m LEFT JOIN m_prefix p ON p.prefix_id=m.prefix_id WHERE m.member_id=@id", new { id }) ?? "";

    private async Task<bool> ActiveMemberAllowedAsync(IDbConnection c, long id)
    {
        if (await MemberHouseAsync(c, id) == null) return false;
        return await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house_member WHERE member_id=@id AND is_active=1", new { id }) > 0;
    }

    // ======================= แก้ไขข้อมูลบุคคล =======================
    private async Task<PersonLookups> LoadLookupsAsync(IDbConnection c) => new()
    {
        Prefixes = await SimpleOptsAsync(c, "m_prefix", "prefix_id", "name", true),
        Genders = await SimpleOptsAsync(c, "m_gender", "gender_id", "name", false),
        Occupations = (await c.QueryAsync<Opt>("SELECT CAST(occupation_id AS CHAR) AS Id, name AS Name FROM m_occupation WHERE is_active=1 ORDER BY name")).ToList(),
        MaritalStatuses = await SimpleOptsAsync(c, "m_marital_status", "marital_status_id", "name", true),
        Educations = await SimpleOptsAsync(c, "m_education", "education_id", "name", true),
        Religions = await SimpleOptsAsync(c, "m_religion", "religion_id", "name", true),
        Relationships = await SimpleOptsAsync(c, "m_relationship", "relationship_id", "name", true),
        PhysicalStatuses = await SimpleOptsAsync(c, "m_physical_status", "physical_status_id", "name_th", false)
    };

    [HttpGet]
    public async Task<IActionResult> Edit(long id)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAllowedAsync(c, id)) return NotFound();
        var f = await c.QueryFirstOrDefaultAsync<MemberForm>(@"SELECT member_id, house_id, prefix_id, first_name, last_name, is_foreigner, citizen_id,
                gender_id, birth_day, birth_month, birth_year, age, occupation_id, marital_status_id, education_id, religion_id, relationship_id,
                physical_status_id, is_informant, is_house_registration, is_disabled, is_chronic_patient, is_self_reliance
            FROM t_house_member WHERE member_id=@id", new { id });
        if (f == null) return NotFound();
        return View(await BuildVmAsync(c, f, null));
    }

    private async Task<MemberFormVm> BuildVmAsync(IDbConnection c, MemberForm f, string? err) => new()
    {
        Form = f, Error = err, L = await LoadLookupsAsync(c),
        HouseCode = await c.ExecuteScalarAsync<string?>("SELECT house_code FROM t_house WHERE house_id=@h", new { h = f.HouseId }) ?? ""
    };

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MemberForm f)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAllowedAsync(c, f.MemberId)) return NotFound();
        // บ้านของสมาชิกไม่เปลี่ยนจากฟอร์ม: ใช้ค่าจากฐานข้อมูลเสมอ
        f.HouseId = await c.ExecuteScalarAsync<long>("SELECT house_id FROM t_house_member WHERE member_id=@id", new { id = f.MemberId });
        f.FirstName = (f.FirstName ?? "").Trim(); f.LastName = Norm(f.LastName); f.CitizenId = Norm(f.CitizenId);

        string? err = null;
        short? age = null, ageMonth = null;
        if (f.FirstName.Length == 0) err = "กรุณากรอกชื่อ";
        else if (f.FirstName.Length > 100 || (f.LastName?.Length ?? 0) > 100) err = "ชื่อหรือนามสกุลยาวเกิน 100 ตัวอักษร";
        else if (f.GenderId == null) err = "กรุณาเลือกเพศ";
        else if (f.CitizenId == null && !f.IsForeigner) err = "กรุณากรอกเลขบัตรประชาชน 13 หลัก (กรณีไม่มีเลขให้ติ๊ก \"ชาวต่างชาติ\")";
        else if (f.CitizenId != null && (f.CitizenId.Length != 13 || !f.CitizenId.All(char.IsDigit))) err = "เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก";
        else if (f.CitizenId != null && !f.IsForeigner && !MemberRules.ValidCitizenId(f.CitizenId)) err = "เลขบัตรประชาชนไม่ถูกต้อง (ตรวจเลขหลักสุดท้ายไม่ผ่าน)";

        if (err == null && f.CitizenId != null)
        {
            var dup = await c.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM t_house_member m JOIN t_house h ON h.house_id=m.house_id
                WHERE m.citizen_id=@cid AND m.member_id<>@id AND m.is_active=1 AND h.is_active=1
                  AND h.survey_year=(SELECT survey_year FROM t_house WHERE house_id=@hid)",
                new { cid = f.CitizenId, id = f.MemberId, hid = f.HouseId });
            if (dup > 0) err = "เลขบัตรประชาชนนี้มีอยู่แล้วในปีสำรวจนี้";
        }
        if (err == null)
        {
            (err, age, ageMonth) = MemberRules.CalcAge(f.BirthYear, f.BirthMonth, f.BirthDay);
            if (err == null && f.BirthYear == null && f.Age != null)
            {
                if (f.Age < 0 || f.Age > 120) err = "อายุต้องอยู่ระหว่าง 0 ถึง 120 ปี";
                else age = f.Age;
            }
        }
        if (err != null) return View(await BuildVmAsync(c, f, err));

        try
        {
            await c.ExecuteAsync(@"UPDATE t_house_member SET prefix_id=@PrefixId, first_name=@FirstName, last_name=@LastName, citizen_id=@CitizenId,
                    is_foreigner=@IsForeigner, gender_id=@GenderId, birth_day=@BirthDay, birth_month=@BirthMonth, birth_year=@BirthYear,
                    age=@age, age_month=@ageMonth, occupation_id=@OccupationId, marital_status_id=@MaritalStatusId, education_id=@EducationId,
                    religion_id=@ReligionId, relationship_id=@RelationshipId, physical_status_id=@PhysicalStatusId,
                    is_informant=@IsInformant, is_house_registration=@IsHouseRegistration, is_normal=@isNormal, is_disabled=@IsDisabled,
                    is_chronic_patient=@IsChronicPatient, is_self_reliance=@IsSelfReliance, updated_by=@uid, updated_at=NOW()
                WHERE member_id=@MemberId",
                new
                {
                    f.MemberId, f.PrefixId, f.FirstName, f.LastName, f.CitizenId, f.IsForeigner, f.GenderId,
                    f.BirthDay, f.BirthMonth, f.BirthYear, age, ageMonth, f.OccupationId, f.MaritalStatusId, f.EducationId,
                    f.ReligionId, f.RelationshipId, f.PhysicalStatusId, f.IsInformant, f.IsHouseRegistration,
                    isNormal = !(f.IsDisabled || f.IsChronicPatient), f.IsDisabled, f.IsChronicPatient, f.IsSelfReliance, uid = UserId
                });
        }
        catch (MySqlException ex) { return View(await BuildVmAsync(c, f, "บันทึกไม่ได้: " + ex.Message)); }

        await Log(c, "UPDATE_MEMBER", f.MemberId.ToString(), $"{f.FirstName} {f.LastName}");
        Flash("บันทึกการแก้ไขข้อมูลบุคคลเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = f.MemberId });
    }

    // ลบแบบซ่อน (is_active=0) เพราะมีข้อมูลโรค/รายได้/คำร้องที่อ้างอิงบุคคลนี้
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id)
    {
        using var c = _db.Open();
        var houseId = await MemberHouseAsync(c, id);
        if (houseId == null) return NotFound();
        var year = await HouseYearAsync(c, houseId.Value);
        await c.ExecuteAsync("UPDATE t_house_member SET is_active=0, updated_by=@uid, updated_at=NOW() WHERE member_id=@id", new { id, uid = UserId });
        if (year != null)
        {
            var e1 = await Caches.RefreshDiseaseAsync(c, year.Value);
            var e2 = await Caches.RefreshVulnerableAsync(c, year.Value);
            if (e1 != null || e2 != null) await Log(c, "CACHE_REFRESH_FAILED", id.ToString(), e1 ?? e2);
        }
        await Log(c, "DELETE_MEMBER", id.ToString());
        Flash("ลบข้อมูลบุคคลเรียบร้อยแล้ว");
        var code = await c.ExecuteScalarAsync<string?>("SELECT house_code FROM t_house WHERE house_id=@h", new { h = houseId });
        return code == null ? RedirectToAction("Household", "Report") : RedirectToAction("Detail", "House", new { code, year });
    }

    // ======================= อาชีพและรายได้ =======================
    [HttpGet]
    public async Task<IActionResult> IncomeEdit(long id)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAllowedAsync(c, id)) return NotFound();
        var f = await c.QueryFirstOrDefaultAsync<IncomeForm>(@"SELECT member_id, main_occupation_id, second_occupation_id, main_income,
                second_income, other_income, online_income, saving FROM member_income WHERE member_id=@id", new { id }) ?? new IncomeForm { MemberId = id };
        return View(await IncomeVmAsync(c, f, null));
    }

    private async Task<IncomeFormVm> IncomeVmAsync(IDbConnection c, IncomeForm f, string? err) => new()
    {
        Form = f, Error = err, MemberName = await MemberNameAsync(c, f.MemberId),
        Occupations = (await c.QueryAsync<Opt>("SELECT CAST(occupation_id AS CHAR) AS Id, name AS Name FROM m_occupation WHERE is_active=1 ORDER BY name")).ToList()
    };

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> IncomeEdit(IncomeForm f)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAllowedAsync(c, f.MemberId)) return NotFound();
        var nums = new[] { f.MainIncome, f.SecondIncome, f.OtherIncome, f.OnlineIncome, f.Saving };
        if (nums.Any(x => x != null && (x < 0 || x > 9_999_999_999m)))
            return View(await IncomeVmAsync(c, f, "จำนวนเงินต้องไม่ติดลบและไม่เกิน 9,999,999,999 บาท"));
        foreach (var occ in new[] { f.MainOccupationId, f.SecondOccupationId })
            if (occ != null && await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_occupation WHERE occupation_id=@occ", new { occ }) == 0)
                return View(await IncomeVmAsync(c, f, "อาชีพที่เลือกไม่ถูกต้อง"));

        var total = (f.MainIncome ?? 0) + (f.SecondIncome ?? 0) + (f.OtherIncome ?? 0) + (f.OnlineIncome ?? 0);
        await c.ExecuteAsync(@"INSERT INTO member_income(member_id, main_occupation_id, second_occupation_id, main_income, second_income,
                other_income, online_income, total_income, saving)
            VALUES(@MemberId, @MainOccupationId, @SecondOccupationId, @MainIncome, @SecondIncome, @OtherIncome, @OnlineIncome, @total, @Saving)
            ON DUPLICATE KEY UPDATE main_occupation_id=@MainOccupationId, second_occupation_id=@SecondOccupationId, main_income=@MainIncome,
                second_income=@SecondIncome, other_income=@OtherIncome, online_income=@OnlineIncome, total_income=@total, saving=@Saving",
            new { f.MemberId, f.MainOccupationId, f.SecondOccupationId, f.MainIncome, f.SecondIncome, f.OtherIncome, f.OnlineIncome, total, f.Saving });
        await Log(c, "UPDATE_INCOME", f.MemberId.ToString());
        Flash("บันทึกข้อมูลอาชีพและรายได้เรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = f.MemberId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> IncomeDelete(long id)
    {
        using var c = _db.Open();
        if (await MemberHouseAsync(c, id) == null) return NotFound();
        await c.ExecuteAsync("DELETE FROM member_income WHERE member_id=@id", new { id });
        await Log(c, "DELETE_INCOME", id.ToString());
        Flash("ลบข้อมูลรายได้เรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id });
    }

    // ======================= ผลประเมินความยากจน/ความเปราะบาง =======================
    [HttpGet]
    public async Task<IActionResult> AssessEdit(long id)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAllowedAsync(c, id)) return NotFound();
        var f = await c.QueryFirstOrDefaultAsync<AssessForm>(@"SELECT member_id, income_score, health_score, dependency_score, saving_score,
                welfare_score, result_label, summary FROM member_assessment WHERE member_id=@id", new { id }) ?? new AssessForm { MemberId = id };
        return View(new AssessFormVm { Form = f, MemberName = await MemberNameAsync(c, id) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssessEdit(AssessForm f)
    {
        using var c = _db.Open();
        if (!await ActiveMemberAllowedAsync(c, f.MemberId)) return NotFound();
        f.ResultLabel = Norm(f.ResultLabel); f.Summary = Norm(f.Summary);
        string? err = null;
        var scores = new[] { f.IncomeScore, f.HealthScore, f.DependencyScore, f.SavingScore, f.WelfareScore };
        if (scores.Any(x => x != null && (x < 0 || x > 100))) err = "คะแนนแต่ละด้านต้องอยู่ระหว่าง 0 ถึง 100";
        else if ((f.ResultLabel?.Length ?? 0) > 50) err = "ผลการประเมินต้องไม่เกิน 50 ตัวอักษร";
        else if ((f.Summary?.Length ?? 0) > 2000) err = "สรุปผลต้องไม่เกิน 2,000 ตัวอักษร";
        if (err != null) return View(new AssessFormVm { Form = f, Error = err, MemberName = await MemberNameAsync(c, f.MemberId) });

        await c.ExecuteAsync(@"INSERT INTO member_assessment(member_id, income_score, health_score, dependency_score, saving_score, welfare_score, result_label, summary, assessed_at)
            VALUES(@MemberId, @IncomeScore, @HealthScore, @DependencyScore, @SavingScore, @WelfareScore, @ResultLabel, @Summary, NOW())
            ON DUPLICATE KEY UPDATE income_score=@IncomeScore, health_score=@HealthScore, dependency_score=@DependencyScore,
                saving_score=@SavingScore, welfare_score=@WelfareScore, result_label=@ResultLabel, summary=@Summary, assessed_at=NOW()", f);
        await Log(c, "UPDATE_ASSESSMENT", f.MemberId.ToString());
        Flash("บันทึกผลประเมินเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id = f.MemberId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssessDelete(long id)
    {
        using var c = _db.Open();
        if (await MemberHouseAsync(c, id) == null) return NotFound();
        await c.ExecuteAsync("DELETE FROM member_assessment WHERE member_id=@id", new { id });
        await Log(c, "DELETE_ASSESSMENT", id.ToString());
        Flash("ลบผลประเมินเรียบร้อยแล้ว");
        return RedirectToAction("Person", "House", new { id });
    }
}
