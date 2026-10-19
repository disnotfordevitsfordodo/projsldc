namespace SLDC.Web.Models;

public class PopulationForm
{
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }

    // existing = เพิ่มเข้าบ้านที่มีอยู่, new = สร้างบ้านใหม่
    public string HouseMode { get; set; } = "existing";
    public long? HouseId { get; set; }
    public string? NewHouseCode { get; set; }
    public string? NewHouseNo { get; set; }

    public int? PrefixId { get; set; }
    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }
    public bool IsForeigner { get; set; }
    public string? CitizenId { get; set; }
    public short? GenderId { get; set; }
    public int? BirthDay { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthYear { get; set; }          // พ.ศ.
    public int? OccupationId { get; set; }
    public int? MaritalStatusId { get; set; }
    public int? EducationId { get; set; }
    public int? ReligionId { get; set; }
    public int? RelationshipId { get; set; }
    public int? PhysicalStatusId { get; set; }

    public bool IsInformant { get; set; }
    public bool IsHouseRegistration { get; set; } = true;
    public bool IsDisabled { get; set; }
    public bool IsChronicPatient { get; set; }
    public bool IsSelfReliance { get; set; } = true;

    public int WelfareStatusId { get; set; }     // 0 รอสำรวจ, 1-3 กลุ่มบัตรสวัสดิการ
}

public class PopulationFormVm
{
    public PopulationForm Form { get; set; } = new();
    public List<Opt> Amphurs { get; set; } = new();
    public List<Opt> Tumbols { get; set; } = new();
    public List<Opt> Villages { get; set; } = new();
    public List<Opt> Houses { get; set; } = new();
    public List<Opt> Prefixes { get; set; } = new();
    public List<Opt> Genders { get; set; } = new();
    public List<Opt> Occupations { get; set; } = new();
    public List<Opt> MaritalStatuses { get; set; } = new();
    public List<Opt> Educations { get; set; } = new();
    public List<Opt> Religions { get; set; } = new();
    public List<Opt> Relationships { get; set; } = new();
    public List<Opt> PhysicalStatuses { get; set; } = new();
    public string? Error { get; set; }
}

public class HouseLoc
{
    public long HouseId { get; set; }
    public int SurveyYear { get; set; }
    public string VillageId { get; set; } = "";
    public string TumbolId { get; set; } = "";
    public string AmphurId { get; set; } = "";
}
