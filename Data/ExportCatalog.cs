namespace SLDC.Web.Data;

/// <summary>คอลัมน์หนึ่งของชุดข้อมูล: ชื่อ, คำอธิบาย (ใช้สร้างพจนานุกรมข้อมูล) และนิพจน์ SQL</summary>
public record Col(string Name, string Desc, string? Expr = null)
{
    public string Select => (Expr ?? Name) + " AS " + Name;
}

/// <summary>
/// ชุดข้อมูลที่ส่งออกได้ — ใช้นิยามเดียวสำหรับ: SQL, พจนานุกรมข้อมูล, ดาวน์โหลด CSV/JSON และแพ็กเกจ ZIP
/// Scoped = จำกัดตามสิทธิ์พื้นที่ของผู้ใช้ (ต้องมี alias a = m_amphur, tb = m_tumbol ใน From)
/// IsPublic = ข้อมูลสรุปรวม ไม่ระบุตัวบุคคล เปิดให้ทุกคนดาวน์โหลดได้
/// </summary>
public class Dataset
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Desc { get; init; } = "";
    public string Icon { get; init; } = "bi-table";
    public bool IsPublic { get; init; }
    public bool Scoped { get; init; }
    public string From { get; init; } = "";
    public string Where { get; init; } = "";
    public string OrderBy { get; init; } = "1";
    public Col[] Cols { get; init; } = Array.Empty<Col>();

    public string BuildSql(bool paged)
    {
        var sql = "SELECT " + string.Join(", ", Cols.Select(c => c.Select)) + "\n" + From +
                  (string.IsNullOrWhiteSpace(Where) ? "" : "\nWHERE " + Where) + "\nORDER BY " + OrderBy;
        return paged ? sql + "\nLIMIT @lim OFFSET @off" : sql;
    }
}

public static class ExportCatalog
{
    // ----- นิพจน์ที่ใช้ซ้ำ -----
    // รหัสสุ่ม (pseudonym) ของบ้านและบุคคล: แฮชพร้อม salt ลับ เชื่อมข้อมูลข้ามไฟล์ได้แต่ย้อนกลับเป็นตัวบุคคลไม่ได้
    const string HK = "LEFT(SHA2(CONCAT(@salt,'H',h.house_id),256),16)";
    const string MK = "LEFT(SHA2(CONCAT(@salt,'M',m.member_id),256),16)";

    // บ้าน + พื้นที่ (alias: h, v, tb, a)
    const string GEO = @"FROM t_house h
JOIN m_village v ON v.village_id = h.village_id
JOIN m_tumbol tb ON tb.tumbol_id = v.tumbol_id
JOIN m_amphur a ON a.amphur_id = tb.amphur_id";
    const string MEM = "\nJOIN t_house_member m ON m.house_id = h.house_id AND m.is_active = 1";
    const string WHERE_GEO = "h.is_active = 1 AND h.survey_year = @year AND (@a IS NULL OR a.amphur_id = @a) AND (@t IS NULL OR tb.tumbol_id = @t)";

    static Col[] Area() => new[]
    {
        new Col("survey_year", "ปีสำรวจ (พ.ศ.)", "h.survey_year"),
        new Col("amphur_id", "รหัสอำเภอ", "a.amphur_id"),
        new Col("amphur_name", "ชื่ออำเภอ", "a.name_th"),
        new Col("tumbol_id", "รหัสตำบล", "tb.tumbol_id"),
        new Col("tumbol_name", "ชื่อตำบล", "tb.name_th"),
        new Col("village_id", "รหัสหมู่บ้าน", "v.village_id"),
        new Col("moo", "หมู่ที่", "v.moo"),
    };

    static Col[] Join(params Col[][] groups) => groups.SelectMany(g => g).ToArray();

    // =========================================================
    //  ชุดข้อมูลระดับครัวเรือน/บุคคล (ไม่ระบุตัวตน) — ต้องล็อกอิน
    // =========================================================
    public static readonly Dataset[] Private =
    {
        new Dataset
        {
            Key = "households", Title = "ครัวเรือน", Icon = "bi-houses", Scoped = true,
            Desc = "1 แถว = 1 ครัวเรือน พร้อมระดับความอยู่ดีมีสุข รายจ่ายและหนี้สิน (ไม่มีบ้านเลขที่/ที่อยู่ พิกัดปัดเศษ ~1 กม.)",
            From = GEO + "\nLEFT JOIN house_expense e ON e.house_id = h.house_id",
            Where = WHERE_GEO, OrderBy = "h.house_id",
            Cols = Join(new[] { new Col("house_key", "รหัสครัวเรือนแบบสุ่ม (เชื่อมกับไฟล์อื่นได้)", HK) }, Area(), new[]
            {
                new Col("lat_approx", "ละติจูด (ปัดทศนิยม 2 ตำแหน่ง)", "ROUND(h.latitude,2)"),
                new Col("lng_approx", "ลองจิจูด (ปัดทศนิยม 2 ตำแหน่ง)", "ROUND(h.longitude,2)"),
                new Col("happiness_level_id", "ระดับความอยู่ดีมีสุข (1-4)", "h.happiness_level_id"),
                new Col("member_count", "จำนวนสมาชิกที่อาศัยอยู่", "(SELECT COUNT(*) FROM t_house_member x WHERE x.house_id = h.house_id AND x.is_active = 1)"),
                new Col("cost_of_living", "ค่าครองชีพ (บาท)", "e.cost_of_living"),
                new Col("needed_expense", "รายจ่ายจำเป็น (บาท)", "e.needed_expense"),
                new Col("internet_expense", "ค่าอินเทอร์เน็ต (บาท)", "e.internet_expense"),
                new Col("unneeded_expense", "รายจ่ายไม่จำเป็น (บาท)", "e.unneeded_expense"),
                new Col("total_expense", "รายจ่ายรวม (บาท)", "e.total_expense"),
                new Col("debt_total", "หนี้สินรวม (บาท)", "e.debt_total"),
            }),
        },
        new Dataset
        {
            Key = "members", Title = "ประชากรรายบุคคล", Icon = "bi-people", Scoped = true,
            Desc = "1 แถว = 1 คน: เพศ อายุ การศึกษา อาชีพ รายได้ สถานะบัตรสวัสดิการ (ไม่มีชื่อ เลขบัตร วันเกิด ศาสนา)",
            From = GEO + MEM + @"
LEFT JOIN member_income mi ON mi.member_id = m.member_id
LEFT JOIN member_welfare mw ON mw.member_id = m.member_id
LEFT JOIN m_gender g ON g.gender_id = m.gender_id
LEFT JOIN m_marital_status ms ON ms.marital_status_id = m.marital_status_id
LEFT JOIN m_education ed ON ed.education_id = m.education_id
LEFT JOIN m_occupation oc ON oc.occupation_id = m.occupation_id",
            Where = WHERE_GEO, OrderBy = "m.member_id",
            Cols = Join(new[] { new Col("member_key", "รหัสบุคคลแบบสุ่ม", MK), new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("gender_id", "รหัสเพศ", "m.gender_id"),
                new Col("gender", "เพศ", "g.name"),
                new Col("age", "อายุ (ปี)", "m.age"),
                new Col("age_group", "ช่วงอายุ", "CASE WHEN m.age IS NULL THEN NULL WHEN m.age < 15 THEN '0-14' WHEN m.age < 25 THEN '15-24' WHEN m.age < 60 THEN '25-59' ELSE '60+' END"),
                new Col("birth_year", "ปีเกิด (พ.ศ.)", "m.birth_year"),
                new Col("marital_status", "สถานภาพสมรส", "ms.name"),
                new Col("education", "ระดับการศึกษา", "ed.name"),
                new Col("occupation", "อาชีพ", "oc.name"),
                new Col("physical_status_id", "รหัสสภาพร่างกาย", "m.physical_status_id"),
                new Col("is_disabled", "เป็นผู้พิการ (1=ใช่)", "m.is_disabled"),
                new Col("is_chronic_patient", "เป็นผู้ป่วยเรื้อรัง (1=ใช่)", "m.is_chronic_patient"),
                new Col("is_self_reliance", "ช่วยเหลือตนเองได้ (1=ใช่)", "m.is_self_reliance"),
                new Col("welfare_status_id", "กลุ่มบัตรสวัสดิการ (0=รอสำรวจ, 1-3)", "mw.welfare_status_id"),
                new Col("has_welfare_card", "ถือบัตรสวัสดิการ (1=ใช่)", "mw.has_welfare_card"),
                new Col("main_income", "รายได้อาชีพหลัก (บาท/ปี)", "mi.main_income"),
                new Col("second_income", "รายได้อาชีพรอง (บาท/ปี)", "mi.second_income"),
                new Col("other_income", "รายได้อื่น ๆ (บาท/ปี)", "mi.other_income"),
                new Col("online_income", "รายได้ออนไลน์ (บาท/ปี)", "mi.online_income"),
                new Col("total_income", "รายได้รวม (บาท/ปี)", "mi.total_income"),
                new Col("saving", "เงินออม (บาท)", "mi.saving"),
            }),
        },
        new Dataset
        {
            Key = "diseases", Title = "ทะเบียนโรค", Icon = "bi-clipboard2-pulse", Scoped = true,
            Desc = "1 แถว = 1 โรคของ 1 คน พร้อมรหัส ICD-10 (ข้อมูลสุขภาพ ใช้เพื่อการวิเคราะห์ภาพรวมเท่านั้น)",
            From = GEO + MEM + @"
JOIN member_disease md ON md.member_id = m.member_id
JOIN m_disease d ON d.disease_id = md.disease_id",
            Where = WHERE_GEO, OrderBy = "md.member_disease_id",
            Cols = Join(new[] { new Col("member_key", "รหัสบุคคลแบบสุ่ม", MK), new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("gender_id", "รหัสเพศ", "m.gender_id"),
                new Col("age", "อายุ (ปี)", "m.age"),
                new Col("disease_id", "รหัสโรค", "d.disease_id"),
                new Col("icd10", "รหัส ICD-10", "d.icd10"),
                new Col("disease_name", "ชื่อโรค", "d.name_th"),
                new Col("diagnosed_year", "ปีที่วินิจฉัย (ค.ศ.)", "YEAR(md.diagnosed_date)"),
            }),
        },
        new Dataset
        {
            Key = "vulnerable", Title = "กลุ่มเปราะบาง", Icon = "bi-shield-exclamation", Scoped = true,
            Desc = "1 แถว = 1 คนต่อ 1 กลุ่มเปราะบาง (14 เป้าหมาย) ไม่รวมหมายเหตุอิสระ",
            From = GEO + MEM + @"
JOIN member_vulnerable mv ON mv.member_id = m.member_id
JOIN m_vulnerable_type vt ON vt.vulnerable_type_id = mv.vulnerable_type_id",
            Where = WHERE_GEO, OrderBy = "mv.member_id, mv.vulnerable_type_id",
            Cols = Join(new[] { new Col("member_key", "รหัสบุคคลแบบสุ่ม", MK), new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("gender_id", "รหัสเพศ", "m.gender_id"),
                new Col("age", "อายุ (ปี)", "m.age"),
                new Col("vulnerable_type_id", "รหัสกลุ่มเปราะบาง", "vt.vulnerable_type_id"),
                new Col("vulnerable_type", "กลุ่มเปราะบาง", "vt.name_th"),
                new Col("identified_year", "ปีที่คัดกรอง (ค.ศ.)", "YEAR(mv.identified_date)"),
            }),
        },
        new Dataset
        {
            Key = "assessment", Title = "คะแนนประเมิน PPPConnext", Icon = "bi-diagram-3", Scoped = true,
            Desc = "คะแนน 5 ด้าน (รายได้ สุขภาพ ภาวะพึ่งพิง เงินออม สิทธิสวัสดิการ) และผลการจัดกลุ่มรายบุคคล",
            From = GEO + MEM + "\nJOIN member_assessment ma ON ma.member_id = m.member_id",
            Where = WHERE_GEO, OrderBy = "m.member_id",
            Cols = Join(new[] { new Col("member_key", "รหัสบุคคลแบบสุ่ม", MK), new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("income_score", "คะแนนด้านรายได้", "ma.income_score"),
                new Col("health_score", "คะแนนด้านสุขภาพ", "ma.health_score"),
                new Col("dependency_score", "คะแนนด้านภาวะพึ่งพิง", "ma.dependency_score"),
                new Col("saving_score", "คะแนนด้านเงินออม", "ma.saving_score"),
                new Col("welfare_score", "คะแนนด้านสิทธิสวัสดิการ", "ma.welfare_score"),
                new Col("result_label", "ผลการประเมิน", "ma.result_label"),
            }),
        },
        new Dataset
        {
            Key = "repairs_house", Title = "คำร้องซ่อมแซมบ้าน", Icon = "bi-tools", Scoped = true,
            Desc = "คำร้องขอซ่อมแซมที่อยู่อาศัย ประเภทงาน งบประมาณ สถานะ และวันที่ (ไม่ระบุผู้ยื่น)",
            From = GEO + "\nJOIN repair_house_request r ON r.house_id = h.house_id",
            Where = WHERE_GEO, OrderBy = "r.request_id",
            Cols = Join(new[] { new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("repair_type", "ประเภทการซ่อม", "r.repair_type"),
                new Col("budget", "งบประมาณ (บาท)", "r.budget"),
                new Col("status", "สถานะ (waiting / in_progress / done)", "r.status"),
                new Col("request_date", "วันที่ยื่นคำร้อง", "r.request_date"),
                new Col("finished_date", "วันที่แล้วเสร็จ", "r.finished_date"),
            }),
        },
        new Dataset
        {
            Key = "repairs_medical", Title = "คำร้องอุปกรณ์ทางการแพทย์", Icon = "bi-bandaid", Scoped = true,
            Desc = "คำร้องซ่อม/ขออุปกรณ์ทางการแพทย์ ประเภทอุปกรณ์ สถานะ วันที่รับ (ไม่รวมรายละเอียดอิสระ)",
            From = GEO + MEM + "\nJOIN repair_medical_request r ON r.member_id = m.member_id",
            Where = WHERE_GEO, OrderBy = "r.request_id",
            Cols = Join(new[] { new Col("member_key", "รหัสบุคคลแบบสุ่ม", MK), new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("device_type", "ประเภทอุปกรณ์", "r.device_type"),
                new Col("status", "สถานะ", "r.status"),
                new Col("received_date", "วันที่รับคำร้อง", "r.received_date"),
            }),
        },
        new Dataset
        {
            Key = "jpt", Title = "ตัวชี้วัด จปฐ.", Icon = "bi-graph-up", Scoped = true,
            Desc = "ผลการประเมินตัวชี้วัด จปฐ. รายครัวเรือน (ผ่าน/ไม่ผ่าน)",
            From = GEO + "\nJOIN jpt_result jr ON jr.house_id = h.house_id\nJOIN m_jpt_indicator ji ON ji.indicator_id = jr.indicator_id",
            Where = WHERE_GEO, OrderBy = "jr.result_id",
            Cols = Join(new[] { new Col("house_key", "รหัสครัวเรือนแบบสุ่ม", HK) }, Area(), new[]
            {
                new Col("indicator_id", "รหัสตัวชี้วัด", "ji.indicator_id"),
                new Col("category", "หมวดตัวชี้วัด", "ji.category"),
                new Col("indicator_name", "ชื่อตัวชี้วัด", "ji.name_th"),
                new Col("passed", "ผ่าน", "jr.passed"),
                new Col("failed", "ไม่ผ่าน", "jr.failed"),
                new Col("unit_passed", "จำนวนหน่วยที่ผ่าน", "jr.unit_passed"),
                new Col("unit_all", "จำนวนหน่วยทั้งหมด", "jr.unit_all"),
            }),
        },
    };

    // =========================================================
    //  ข้อมูลเปิดสำหรับประชาชน (สถิติสรุป) — ไม่ต้องล็อกอิน
    // =========================================================
    // ตัวเลขน้อยกว่า 5 ในระดับหมู่บ้านจะถูกซ่อนเป็น "<5" เพื่อกันการระบุตัวบุคคล
    static string Mask(string c) => $"CASE WHEN {c} BETWEEN 1 AND 4 THEN '<5' ELSE CAST({c} AS CHAR) END";

    static Col[] PopCols() => new[]
    {
        new Col("households", "จำนวนครัวเรือน"), new Col("population", "จำนวนประชากร"),
        new Col("male", "ชาย"), new Col("female", "หญิง"),
        new Col("elderly", "ผู้สูงอายุ (60 ปีขึ้นไป)"), new Col("disabled", "ผู้พิการ"),
    };

    public static readonly Dataset[] Public =
    {
        new Dataset { Key = "pop_amphur", Title = "ประชากรรายอำเภอ", Icon = "bi-people", IsPublic = true,
            Desc = "จำนวนครัวเรือน ประชากร เพศ ผู้สูงอายุ ผู้พิการ และอัตราส่วนเพศ จำแนกอำเภอ",
            From = "FROM v_population_by_amphur", Where = "survey_year = @year", OrderBy = "amphur_id",
            Cols = Join(new[] { new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("amphur_id", "รหัสอำเภอ"), new Col("amphur_name", "ชื่ออำเภอ") }, PopCols(),
                        new[] { new Col("sex_ratio", "อัตราส่วนเพศ (ชายต่อหญิง 100 คน)") }) },
        new Dataset { Key = "pop_tumbol", Title = "ประชากรรายตำบล", Icon = "bi-geo-alt", IsPublic = true,
            Desc = "ข้อมูลประชากรจำแนกตำบล",
            From = "FROM v_population_by_tumbol", Where = "survey_year = @year", OrderBy = "tumbol_id",
            Cols = Join(new[] { new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("amphur_id", "รหัสอำเภอ"), new Col("amphur_name", "ชื่ออำเภอ"),
                                new Col("tumbol_id", "รหัสตำบล"), new Col("tumbol_name", "ชื่อตำบล") }, PopCols()) },
        new Dataset { Key = "pop_village", Title = "ประชากรรายหมู่บ้าน", Icon = "bi-pin-map", IsPublic = true,
            Desc = "ข้อมูลประชากรจำแนกหมู่บ้าน (ค่าที่น้อยกว่า 5 แสดงเป็น \"<5\" เพื่อคุ้มครองข้อมูลส่วนบุคคล)",
            From = "FROM v_population_by_village", Where = "survey_year = @year", OrderBy = "village_id",
            Cols = new[]
            {
                new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("amphur_id", "รหัสอำเภอ"), new Col("amphur_name", "ชื่ออำเภอ"),
                new Col("tumbol_id", "รหัสตำบล"), new Col("tumbol_name", "ชื่อตำบล"),
                new Col("village_id", "รหัสหมู่บ้าน"), new Col("moo", "หมู่ที่"), new Col("village_name", "ชื่อหมู่บ้าน"),
                new Col("households", "จำนวนครัวเรือน"), new Col("population", "จำนวนประชากร"),
                new Col("male", "ชาย", Mask("male")), new Col("female", "หญิง", Mask("female")),
                new Col("elderly", "ผู้สูงอายุ (60 ปีขึ้นไป)", Mask("elderly")), new Col("disabled", "ผู้พิการ", Mask("disabled")),
            } },
        new Dataset { Key = "welfare_tumbol", Title = "บัตรสวัสดิการแห่งรัฐรายตำบล", Icon = "bi-card-checklist", IsPublic = true,
            Desc = "จำนวนคนจำแนกกลุ่มบัตรสวัสดิการ: ผู้ถือบัตร กลุ่มตกหล่น ไม่ใช่เป้าหมาย และรอสำรวจ",
            From = "FROM v_welfare_by_tumbol", Where = "survey_year = @year", OrderBy = "tumbol_id",
            Cols = new[]
            {
                new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("amphur_id", "รหัสอำเภอ"), new Col("tumbol_id", "รหัสตำบล"), new Col("tumbol_name", "ชื่อตำบล"),
                new Col("group1_card_holder", "กลุ่ม 1 ผู้ถือบัตรปัจจุบัน"), new Col("group2_missed", "กลุ่ม 2 ชายขอบ/ตกหล่น"),
                new Col("group3_not_target", "กลุ่ม 3 ไม่ใช่กลุ่มเป้าหมาย"), new Col("waiting_survey", "รอการสำรวจ"), new Col("total", "รวม"),
            } },
        new Dataset { Key = "happiness_amphur", Title = "ความอยู่ดีมีสุขรายอำเภอ", Icon = "bi-emoji-smile", IsPublic = true,
            Desc = "จำนวนประชากรจำแนกระดับความอยู่ดีมีสุข 4 ระดับ",
            From = "FROM v_household_happiness_by_amphur", Where = "survey_year = @year", OrderBy = "amphur_id",
            Cols = new[]
            {
                new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("amphur_id", "รหัสอำเภอ"), new Col("amphur_name", "ชื่ออำเภอ"), new Col("households", "จำนวนครัวเรือน"),
                new Col("level1_people", "ระดับ 1 (คน)"), new Col("level2_people", "ระดับ 2 (คน)"), new Col("level3_people", "ระดับ 3 (คน)"), new Col("level4_people", "ระดับ 4 (คน)"),
                new Col("avg_members", "จำนวนสมาชิกเฉลี่ยต่อครัวเรือน"),
            } },
        new Dataset { Key = "vulnerable_counts", Title = "กลุ่มเปราะบางทั้งจังหวัด", Icon = "bi-shield-exclamation", IsPublic = true,
            Desc = "จำนวนคนในแต่ละกลุ่มเปราะบาง 14 เป้าหมาย ระดับจังหวัด",
            From = "FROM v_vulnerable_counts", Where = "survey_year = @year", OrderBy = "vulnerable_type_id",
            Cols = new[] { new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("vulnerable_type_id", "รหัสกลุ่ม"), new Col("name_th", "ชื่อกลุ่มเปราะบาง"), new Col("people", "จำนวน (คน)") } },
        new Dataset { Key = "summary_province", Title = "สรุปทั้งจังหวัด", Icon = "bi-bar-chart", IsPublic = true,
            Desc = "ตัวเลขสรุประดับจังหวัด",
            From = "FROM v_summary_province", Where = "survey_year = @year", OrderBy = "survey_year",
            Cols = new[] { new Col("survey_year", "ปีสำรวจ (พ.ศ.)"), new Col("population", "ประชากร"), new Col("households", "ครัวเรือน"), new Col("elderly", "ผู้สูงอายุ"),
                           new Col("disabled", "ผู้พิการ"), new Col("male", "ชาย"), new Col("female", "หญิง") } },
    };

    public static IEnumerable<Dataset> All => Private.Concat(Public);
    public static Dataset? Find(string? key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>ข้อความ README ที่แนบในแพ็กเกจ ZIP</summary>
    public static string Readme(int year, string? scope, string purpose)
    {
        var sc = scope ?? "ทุกพื้นที่ที่ผู้ส่งออกมีสิทธิ์";
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return $@"SLDC สกลนคร — แพ็กเกจข้อมูล (ไม่ระบุตัวตน)
=================================================
ปีสำรวจ (พ.ศ.): {year}
ขอบเขตพื้นที่: {sc}
วัตถุประสงค์การใช้งาน: {purpose}
สร้างเมื่อ: {now}

โครงสร้างแพ็กเกจ
- datasets/*.csv        ชุดข้อมูล (UTF-8 มี BOM เปิดด้วย Excel ได้โดยตรง)
- data_dictionary.csv   พจนานุกรมข้อมูล: ชื่อคอลัมน์และความหมายของทุกไฟล์
- README.txt            ไฟล์นี้

การเชื่อมโยงข้อมูล
- house_key  = รหัสครัวเรือนแบบสุ่ม ใช้เชื่อมระหว่างไฟล์
- member_key = รหัสบุคคลแบบสุ่ม ใช้เชื่อมระหว่างไฟล์
- รหัสทั้งสองสร้างจากการแฮชพร้อม salt ลับ จึงย้อนกลับเป็นตัวบุคคลไม่ได้

เงื่อนไขการใช้ข้อมูล
- ห้ามนำไปประกอบกับข้อมูลอื่นเพื่อระบุตัวบุคคลหรือครัวเรือน
- ข้อมูลสุขภาพ/ความพิการ/รายได้ เป็นข้อมูลอ่อนไหว ใช้เพื่อการวิเคราะห์ภาพรวมเท่านั้น
- เมื่อเผยแพร่ผลการวิเคราะห์ ควรรวมกลุ่มให้มีจำนวนไม่น้อยกว่า 5 คนต่อเซลล์
";
    }
}
