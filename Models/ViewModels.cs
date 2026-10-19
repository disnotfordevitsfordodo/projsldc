namespace SLDC.Web.Models;

public class PopRow
{
    public int SurveyYear { get; set; }
    public string AmphurId { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public string TumbolId { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string VillageId { get; set; } = "";
    public string VillageName { get; set; } = "";
    public int Moo { get; set; }
    public long Households { get; set; }
    public long Population { get; set; }
    public long Male { get; set; }
    public long Female { get; set; }
    public long Elderly { get; set; }
    public long Disabled { get; set; }
    public decimal? SexRatio { get; set; }
}

public class WelfareRow
{
    public string TumbolId { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public long Group1CardHolder { get; set; }
    public long Group2Missed { get; set; }
    public long Group3NotTarget { get; set; }
    public long WaitingSurvey { get; set; }
    public long Total { get; set; }
}

public class MemberRow
{
    public long MemberId { get; set; }
    public long HouseId { get; set; }
    public int SurveyYear { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public string? CitizenIdMasked { get; set; }
    public string FullName { get; set; } = "";
    public int? Age { get; set; }
    public int? GenderId { get; set; }
    public int WelfareStatusId { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
}

public class HouseHead
{
    public long HouseId { get; set; }
    public int SurveyYear { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
}

public class IncomeRow
{
    public string? MainOccupation { get; set; }
    public decimal? MainIncome { get; set; }
    public decimal? SecondIncome { get; set; }
    public decimal? OtherIncome { get; set; }
    public decimal? OnlineIncome { get; set; }
    public decimal? TotalIncome { get; set; }
    public decimal? Saving { get; set; }
}

public class AssessRow
{
    public int? IncomeScore { get; set; }
    public int? HealthScore { get; set; }
    public int? DependencyScore { get; set; }
    public int? SavingScore { get; set; }
    public int? WelfareScore { get; set; }
    public string? ResultLabel { get; set; }
    public string? Summary { get; set; }
}

public class UserRow
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? FullName { get; set; }
    public string Role { get; set; } = "staff";
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public bool IsActive { get; set; }
    public string? AmphurName { get; set; }
    public string? TumbolName { get; set; }
}

public class Opt { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }

public class DashboardVm
{
    public int Year { get; set; }
    public PopRow Summary { get; set; } = new();
    public List<PopRow> Amphurs { get; set; } = new();
    public WelfareRow Welfare { get; set; } = new();
}

public class WelfareVm
{
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public int? Group { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int Pages { get; set; }
    public long RowCount { get; set; }
    public List<WelfareRow> Summary { get; set; } = new();
    public List<MemberRow> Members { get; set; } = new();
    public List<Opt> Amphurs { get; set; } = new();
    public List<Opt> Tumbols { get; set; } = new();
    public long SumG1 => Summary.Sum(x => x.Group1CardHolder);
    public long SumG2 => Summary.Sum(x => x.Group2Missed);
    public long SumG3 => Summary.Sum(x => x.Group3NotTarget);
    public long SumWait => Summary.Sum(x => x.WaitingSurvey);
    public long SumAll => Summary.Sum(x => x.Total);
}

public class HouseVm
{
    public HouseHead Head { get; set; } = new();
    public List<MemberRow> Members { get; set; } = new();
    public List<ImageRow> Images { get; set; } = new();
    public ExpenseForm? Expense { get; set; }
    public List<TenureRow> Tenures { get; set; } = new();
}

public class PersonVm
{
    public MemberRow Person { get; set; } = new();
    public IncomeRow? Income { get; set; }
    public AssessRow? Assess { get; set; }
    public List<MemberRow> Housemates { get; set; } = new();
    public List<MemberDiseaseRow> Diseases { get; set; } = new();
    public List<MemberVulRow> Vulnerables { get; set; } = new();
    public List<RepairRow> HouseRepairs { get; set; } = new();
    public List<RepairRow> MedicalRepairs { get; set; } = new();
    public List<Opt> DiseaseOptions { get; set; } = new();
    public List<Opt> VulnerableOptions { get; set; } = new();
}

public static partial class Lbl
{
    public static string Welfare(int s) => s switch
    {
        1 => "กลุ่ม 1 : ผู้ถือบัตรสวัสดิการฯ",
        2 => "กลุ่ม 2 : กลุ่มขายขอบ / ตกหล่น",
        3 => "กลุ่ม 3 : ไม่ใช่กลุ่มเป้าหมาย",
        _ => "รอการสำรวจ"
    };
    public static string Css(int s) => s switch
    {
        1 => "bg-success",
        2 => "bg-warning text-dark",
        3 => "bg-danger",
        _ => "bg-secondary"
    };
    public static string Gender(int? g) => g switch { 1 => "ชาย", 2 => "หญิง", 3 => "LGBTI+", _ => "-" };
}
