namespace SLDC.Web.Models;

public class RosterRow
{
    public long HouseId { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public string? HouseNearby { get; set; }
    public long? MemberId { get; set; }
    public string? FullName { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Relationship { get; set; }
    public string? Occupation { get; set; }
    public string? CitizenMasked { get; set; }
    public string? WelfareName { get; set; }
    public bool? HasCard { get; set; }
    public bool? IsDisabled { get; set; }
    public bool? IsChronicPatient { get; set; }
}

public class RosterHouse
{
    public long HouseId { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public string? Nearby { get; set; }
    public List<RosterRow> Members { get; set; } = new();
}

/// <summary>ใบรายชื่อลงพื้นที่ / ข้อมูลตั้งต้นของแบบสำรวจ (หมู่บ้านหนึ่งแห่ง หรือบ้านหลังเดียว)</summary>
public class FieldSheetVm
{
    public int Year { get; set; }
    public string VillageId { get; set; } = "";
    public int Moo { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public List<RosterHouse> Houses { get; set; } = new();
}

public class ExportVm
{
    public int Year { get; set; }
    public List<int> Years { get; set; } = new();
    public PickerVm Picker { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string? ScopeNote { get; set; }
}

public class SurveyRow
{
    public long AnswerId { get; set; }
    public long HouseId { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public string? Marker { get; set; }
    public string? Surveyor { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SurveyListVm
{
    public FilterVm Filter { get; set; } = new();
    public Paged<SurveyRow> Rows { get; set; } = new();
}

public class SurveyFormVm
{
    public PickerVm Picker { get; set; } = new();
    public FieldSheetVm? Sheet { get; set; }
    public Dictionary<string, string[]> Values { get; set; } = new();
    public string? Error { get; set; }
}

public class SurveyDetailVm
{
    public long AnswerId { get; set; }
    public FieldSheetVm Sheet { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public string? Surveyor { get; set; }
    public string? Marker { get; set; }
    public Dictionary<string, string[]> Values { get; set; } = new();
}

public class QView
{
    public SLDC.Web.Data.Q Q { get; set; } = null!;
    public string Name { get; set; } = "";
    public string[] Value { get; set; } = Array.Empty<string>();
    public bool Compact { get; set; }
}
