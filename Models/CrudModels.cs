namespace SLDC.Web.Models;

// ---------- ตัวเลือกพื้นที่แบบเลือกต่อเนื่อง ----------
// ชื่อฟิลด์ที่ _Picker ส่งกลับ: Year, AmphurId, TumbolId, VillageId, HouseId, MemberId
public class PickerVm
{
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }
    public long? HouseId { get; set; }
    public long? MemberId { get; set; }
    public bool ShowYear { get; set; } = true;
    public bool ShowHouse { get; set; }
    public bool ShowMember { get; set; }
    public List<Opt> Amphurs { get; set; } = new();
    public List<Opt> Tumbols { get; set; } = new();
    public List<Opt> Villages { get; set; } = new();
    public List<Opt> Houses { get; set; } = new();
    public List<Opt> Members { get; set; } = new();
}

// ---------- บ้าน ----------
public class HouseForm
{
    public bool IsNew { get; set; }
    public long HouseId { get; set; }
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public string? HouseNearby { get; set; }
    public string? Soi { get; set; }
    public string? Road { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public short? HappinessLevelId { get; set; }
    public bool IsAgree { get; set; }
    public DateTime? SignDate { get; set; }
}
public class HouseFormVm
{
    public HouseForm Form { get; set; } = new();
    public PickerVm Picker { get; set; } = new();
    public List<Opt> Levels { get; set; } = new();
    public string? Error { get; set; }
}

public class ImageRow
{
    public int ImageId { get; set; }
    public string FilePath { get; set; } = "";
    public string? Description { get; set; }
}

public class ExpenseForm
{
    public long HouseId { get; set; }
    public decimal? CostOfLiving { get; set; }
    public decimal? NeededExpense { get; set; }
    public decimal? InternetExpense { get; set; }
    public decimal? UnneededExpense { get; set; }
    public decimal? TotalExpense { get; set; }
    public decimal? DebtSavingsGroup { get; set; }
    public decimal? DebtCooperative { get; set; }
    public decimal? DebtBaac { get; set; }
    public decimal? DebtGovBank { get; set; }
    public decimal? DebtOtherBank { get; set; }
    public decimal? DebtFinanceCo { get; set; }
    public decimal? DebtInformal { get; set; }
    public decimal? DebtCapital { get; set; }
    public decimal? DebtOther { get; set; }
    public decimal? DebtTotal { get; set; }
}
public class ExpenseFormVm { public ExpenseForm Form { get; set; } = new(); public string HouseCode { get; set; } = ""; public string? Error { get; set; } }

public class TenureRow
{
    public int TenureId { get; set; }
    public string Purpose { get; set; } = "";
    public string TenureType { get; set; } = "";
    public decimal? Area1 { get; set; }
    public decimal? Area2 { get; set; }
    public int? DocumentTypeId { get; set; }
    public string? DocumentName { get; set; }
    public string? Detail { get; set; }
}
public class TenureForm
{
    public int TenureId { get; set; }
    public long HouseId { get; set; }
    public string Purpose { get; set; } = "";
    public string TenureType { get; set; } = "";
    public decimal? Area1 { get; set; }
    public decimal? Area2 { get; set; }
    public int? DocumentTypeId { get; set; }
    public string? Detail { get; set; }
}
public class TenureFormVm { public TenureForm Form { get; set; } = new(); public string HouseCode { get; set; } = ""; public List<Opt> Documents { get; set; } = new(); public string? Error { get; set; } }

// ---------- บุคคล ----------
public class MemberForm
{
    public long MemberId { get; set; }
    public long HouseId { get; set; }
    public int? PrefixId { get; set; }
    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }
    public bool IsForeigner { get; set; }
    public string? CitizenId { get; set; }
    public short? GenderId { get; set; }
    public int? BirthDay { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthYear { get; set; }
    public short? Age { get; set; }                  // ใช้เมื่อไม่ทราบวันเกิด
    public int? OccupationId { get; set; }
    public int? MaritalStatusId { get; set; }
    public int? EducationId { get; set; }
    public int? ReligionId { get; set; }
    public int? RelationshipId { get; set; }
    public int? PhysicalStatusId { get; set; }
    public bool IsInformant { get; set; }
    public bool IsHouseRegistration { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsChronicPatient { get; set; }
    public bool IsSelfReliance { get; set; }
}
public class PersonLookups
{
    public List<Opt> Prefixes { get; set; } = new();
    public List<Opt> Genders { get; set; } = new();
    public List<Opt> Occupations { get; set; } = new();
    public List<Opt> MaritalStatuses { get; set; } = new();
    public List<Opt> Educations { get; set; } = new();
    public List<Opt> Religions { get; set; } = new();
    public List<Opt> Relationships { get; set; } = new();
    public List<Opt> PhysicalStatuses { get; set; } = new();
}
public class MemberFormVm
{
    public MemberForm Form { get; set; } = new();
    public PersonLookups L { get; set; } = new();
    public string HouseCode { get; set; } = "";
    public string? Error { get; set; }
}

public class IncomeForm
{
    public long MemberId { get; set; }
    public int? MainOccupationId { get; set; }
    public int? SecondOccupationId { get; set; }
    public decimal? MainIncome { get; set; }
    public decimal? SecondIncome { get; set; }
    public decimal? OtherIncome { get; set; }
    public decimal? OnlineIncome { get; set; }
    public decimal? Saving { get; set; }
}
public class IncomeFormVm { public IncomeForm Form { get; set; } = new(); public List<Opt> Occupations { get; set; } = new(); public string MemberName { get; set; } = ""; public string? Error { get; set; } }

public class AssessForm
{
    public long MemberId { get; set; }
    public short? IncomeScore { get; set; }
    public short? HealthScore { get; set; }
    public short? DependencyScore { get; set; }
    public short? SavingScore { get; set; }
    public short? WelfareScore { get; set; }
    public string? ResultLabel { get; set; }
    public string? Summary { get; set; }
}
public class AssessFormVm { public AssessForm Form { get; set; } = new(); public string MemberName { get; set; } = ""; public string? Error { get; set; } }

public class MemberDiseaseRow
{
    public long MemberDiseaseId { get; set; }
    public int DiseaseId { get; set; }
    public string Name { get; set; } = "";
    public string? Icd10 { get; set; }
    public DateTime? DiagnosedDate { get; set; }
}
public class MemberVulRow
{
    public int VulnerableTypeId { get; set; }
    public string Name { get; set; } = "";
    public DateTime? IdentifiedDate { get; set; }
    public string? Note { get; set; }
}

// ---------- โรคของผู้ป่วย ----------
public class DiseaseRecordRow
{
    public long MemberDiseaseId { get; set; }
    public long MemberId { get; set; }
    public string FullName { get; set; } = "";
    public string HouseCode { get; set; } = "";
    public string DiseaseName { get; set; } = "";
    public DateTime? DiagnosedDate { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
}
public class DiseaseListVm
{
    public FilterVm Filter { get; set; } = new();
    public int? DiseaseId { get; set; }
    public List<Opt> Diseases { get; set; } = new();
    public Paged<DiseaseRecordRow> Rows { get; set; } = new();
    public bool HasArea { get; set; }
}
public class DiseaseForm
{
    public long MemberDiseaseId { get; set; }
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }
    public long? HouseId { get; set; }
    public long? MemberId { get; set; }
    public int? DiseaseId { get; set; }
    public DateTime? DiagnosedDate { get; set; }
}
public class DiseaseFormVm { public DiseaseForm Form { get; set; } = new(); public PickerVm Picker { get; set; } = new(); public List<Opt> Diseases { get; set; } = new(); public string? MemberName { get; set; } public string? Error { get; set; } }

// ---------- กลุ่มเปราะบาง ----------
public class VulForm
{
    public bool IsNew { get; set; } = true;
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }
    public long? HouseId { get; set; }
    public long? MemberId { get; set; }
    public int? VulnerableTypeId { get; set; }
    public DateTime? IdentifiedDate { get; set; }
    public string? Note { get; set; }
}
public class VulFormVm { public VulForm Form { get; set; } = new(); public PickerVm Picker { get; set; } = new(); public List<Opt> Types { get; set; } = new(); public string? MemberName { get; set; } public string? Error { get; set; } }

// ---------- คำร้องซ่อมแซม ----------
public class RepairForm
{
    public string Kind { get; set; } = "house";
    public long RequestId { get; set; }
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }
    public long? HouseId { get; set; }
    public long? MemberId { get; set; }
    public string ItemType { get; set; } = "";       // repair_type / device_type
    public decimal? Budget { get; set; }              // เฉพาะซ่อมบ้าน
    public string? Detail { get; set; }               // เฉพาะอุปกรณ์การแพทย์
    public string Status { get; set; } = "waiting";
    public DateTime? ReqDate { get; set; }            // request_date / received_date
    public DateTime? FinishedDate { get; set; }       // เฉพาะซ่อมบ้าน
}
public class RepairFormVm
{
    public RepairForm Form { get; set; } = new();
    public PickerVm Picker { get; set; } = new();
    public bool IsNew { get; set; }
    public string? MemberName { get; set; }
    public string? Error { get; set; }
    public string[] Suggestions { get; set; } = Array.Empty<string>();
}

// ---------- ทรัพยากรสุขภาพ ----------
public class ResourceRow
{
    public int ResourceId { get; set; }
    public string ResourceType { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TumbolId { get; set; }
    public string? TumbolName { get; set; }
    public string? AmphurName { get; set; }
}
public class ResourceForm
{
    public int ResourceId { get; set; }
    public string ResourceType { get; set; } = "hospital";
    public string Name { get; set; } = "";
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
}
public class ResourceListVm
{
    public FilterVm Filter { get; set; } = new();
    public string? Type { get; set; }
    public Paged<ResourceRow> Rows { get; set; } = new();
}
public class ResourceFormVm
{
    public ResourceForm Form { get; set; } = new();
    public List<Opt> Amphurs { get; set; } = new();
    public List<Opt> Tumbols { get; set; } = new();
    public string? Error { get; set; }
}

// ---------- ข้อมูลหลัก (ตาราง m_*) ----------
public class MasterField
{
    public string Col { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "text";    // text | bool | number
    public bool Required { get; set; }
    public int MaxLen { get; set; } = 150;
}
public class MasterDef
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Table { get; set; } = "";
    public string IdCol { get; set; } = "";
    public string? ActiveCol { get; set; }        // มีคอลัมน์ is_active = ลบแบบซ่อน
    public string OrderCol { get; set; } = "";
    public List<MasterField> Fields { get; set; } = new();
}
public class MasterIndexVm
{
    public MasterDef Def { get; set; } = new();
    public List<MasterDef> All { get; set; } = new();
    public string? Q { get; set; }
    public Paged<IDictionary<string, object?>> Rows { get; set; } = new();
}
public class MasterFormVm
{
    public MasterDef Def { get; set; } = new();
    public bool IsNew { get; set; }
    public long Id { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
    public string? Error { get; set; }
}

// ---------- พื้นที่: อำเภอ ----------
public class AmphurRow { public string AmphurId { get; set; } = ""; public string NameTh { get; set; } = ""; public string? NameEn { get; set; } }
public class AmphurForm { public bool IsNew { get; set; } public string AmphurId { get; set; } = ""; public string NameTh { get; set; } = ""; public string? NameEn { get; set; } }
public class AmphurFormVm { public AmphurForm Form { get; set; } = new(); public string? Error { get; set; } }

// ---------- บันทึกการใช้งาน / บัญชี ----------
public class LogRow
{
    public long LogId { get; set; }
    public string? Username { get; set; }
    public string ActionType { get; set; } = "";
    public string? TargetId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
public class LogsVm
{
    public string? Q { get; set; }
    public string? Action { get; set; }
    public List<string> Actions { get; set; } = new();
    public Paged<LogRow> Rows { get; set; } = new();
}
