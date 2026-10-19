namespace SLDC.Web.Models;

public class Paged<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int Pages { get; set; } = 1;
    public long Total { get; set; }
}

public class PagerVm
{
    public int Page { get; set; }
    public int Pages { get; set; }
    public long Total { get; set; }
    public Func<int, string?> Href { get; set; } = _ => "#";
}

public class FilterVm
{
    public string Action { get; set; } = "";
    public string? Tab { get; set; }
    public int Year { get; set; }
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? VillageId { get; set; }
    public string? Q { get; set; }
    public bool ShowSearch { get; set; } = true;
    public List<Opt> Amphurs { get; set; } = new();
    public List<Opt> Tumbols { get; set; } = new();
}

public class Lvl { public int Id { get; set; } public string Name { get; set; } = ""; }

// ----- ครัวเรือน -----
public class HappyRow
{
    public string AmphurId { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public long Households { get; set; }
    public long Level1People { get; set; }
    public long Level2People { get; set; }
    public long Level3People { get; set; }
    public long Level4People { get; set; }
    public decimal? AvgMembers { get; set; }
}
public class HouseRow
{
    public long HouseId { get; set; }
    public string HouseCode { get; set; } = "";
    public string? HouseNo { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public string? HappinessName { get; set; }
    public long Members { get; set; }
}
public class HouseholdVm
{
    public int Year { get; set; }
    public string? VillageId { get; set; }
    public FilterVm Filter { get; set; } = new();
    public List<Lvl> Levels { get; set; } = new();
    public List<HappyRow> Happy { get; set; } = new();
    public Paged<HouseRow> Houses { get; set; } = new();
    public long TotalHouseholds => Happy.Sum(x => x.Households);
    public long L1 => Happy.Sum(x => x.Level1People);
    public long L2 => Happy.Sum(x => x.Level2People);
    public long L3 => Happy.Sum(x => x.Level3People);
    public long L4 => Happy.Sum(x => x.Level4People);
}

// ----- สุขภาพ -----
public class HealthRes { public long Hospital { get; set; } public long Rpst { get; set; } public long Osm { get; set; } public long Doctor { get; set; } }
public class DiseaseRow { public string Name { get; set; } = ""; public long Patients { get; set; } public decimal? RatePer1000 { get; set; } }
public class HealthVm { public int Year { get; set; } public HealthRes Res { get; set; } = new(); public List<DiseaseRow> Diseases { get; set; } = new(); }

// ----- ซ่อมแซม -----
public class RepairRow
{
    public long RequestId { get; set; }
    public long MemberId { get; set; }
    public string? HouseCode { get; set; }
    public string FullName { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string ItemType { get; set; } = "";
    public decimal? Budget { get; set; }
    public string? Detail { get; set; }
    public string Status { get; set; } = "waiting";
    public DateTime ReqDate { get; set; }
}
public class RepairVm
{
    public string Kind { get; set; } = "house";
    public string Title { get; set; } = "";
    public List<RepairRow> Rows { get; set; } = new();
    public long Total { get; set; }
    public long Waiting { get; set; }
    public long InProgress { get; set; }
    public long Done { get; set; }
    public string? Status { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int Pages { get; set; } = 1;
    public long RowCount { get; set; }
}

// ----- รายงาน -----
public class Over18Vm { public FilterVm Filter { get; set; } = new(); public Paged<MemberRow> Members { get; set; } = new(); }

public class PppRow
{
    public string VillageId { get; set; } = "";
    public int Moo { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public long G1 { get; set; }
    public long G2 { get; set; }
    public long G3 { get; set; }
    public long G4 { get; set; }
    public long Total { get; set; }
}
public class PppVm { public int Year { get; set; } public PppRow Sum { get; set; } = new(); public List<PppRow> Rows { get; set; } = new(); }

public class VulRow { public int VulnerableTypeId { get; set; } public string NameTh { get; set; } = ""; public long People { get; set; } }
public class VulVm { public int Year { get; set; } public int? Selected { get; set; } public List<VulRow> Rows { get; set; } = new(); public List<MemberRow> Members { get; set; } = new(); }

public class PoorRow
{
    public string VillageId { get; set; } = "";
    public int Moo { get; set; }
    public string VillageName { get; set; } = "";
    public string TumbolName { get; set; } = "";
    public string AmphurName { get; set; } = "";
    public long PoorHouseholds { get; set; }
}
public class PoorVm { public int Year { get; set; } public long Total { get; set; } public List<PoorRow> Rows { get; set; } = new(); }

// ----- ผู้ดูแลระบบ -----
public class UserForm
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string? FullName { get; set; }
    public string Role { get; set; } = "staff";
    public string? AmphurId { get; set; }
    public string? TumbolId { get; set; }
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
}
public class UserFormVm { public UserForm Form { get; set; } = new(); public List<Opt> Amphurs { get; set; } = new(); public List<Opt> Tumbols { get; set; } = new(); public string? Error { get; set; } }
public class UsersVm { public string? Q { get; set; } public Paged<UserRow> Users { get; set; } = new(); }

public class VillageRow { public string VillageId { get; set; } = ""; public string ProvinceName { get; set; } = ""; public string AmphurName { get; set; } = ""; public string TumbolName { get; set; } = ""; public int Moo { get; set; } public string NameTh { get; set; } = ""; }
public class TumbolRow { public string TumbolId { get; set; } = ""; public string AmphurName { get; set; } = ""; public string NameTh { get; set; } = ""; public string? PostCode { get; set; } public int? Target { get; set; } }
public class VillageForm { public bool IsNew { get; set; } public string VillageId { get; set; } = ""; public string TumbolId { get; set; } = ""; public int Moo { get; set; } = 1; public string NameTh { get; set; } = ""; public int? Target { get; set; } }
public class TumbolForm { public bool IsNew { get; set; } public string TumbolId { get; set; } = ""; public string AmphurId { get; set; } = ""; public string NameTh { get; set; } = ""; public string? PostCode { get; set; } public int? Target { get; set; } }
public class VillageFormVm { public VillageForm Form { get; set; } = new(); public List<Opt> Tumbols { get; set; } = new(); public string? Error { get; set; } }
public class TumbolFormVm { public TumbolForm Form { get; set; } = new(); public List<Opt> Amphurs { get; set; } = new(); public string? Error { get; set; } }
public class LocVm
{
    public string Tab { get; set; } = "village";
    public FilterVm Filter { get; set; } = new();
    public Paged<VillageRow> Villages { get; set; } = new();
    public Paged<TumbolRow> Tumbols { get; set; } = new();
    public Paged<AmphurRow> Amphurs { get; set; } = new();
}

public static partial class Lbl
{
    public static string Status(string s) => s switch { "done" => "ดำเนินการแล้ว", "in_progress" => "อยู่ระหว่างดำเนินการ", _ => "รอดำเนินการ" };
    public static string StatusCss(string s) => s switch { "done" => "bg-success", "in_progress" => "bg-warning text-dark", _ => "bg-secondary" };
    public static string Role(string r) => r switch { "admin" => "ผู้ดูแลระบบ", "amphur" => "ระดับอำเภอ", _ => "เจ้าหน้าที่ตำบล" };
}
