namespace SLDC.Web.Data;

// SQL ช่วยสำหรับหน้ารายชื่อบุคคล: เลือกเฉพาะ member_id ของหน้านั้น (เรียง + LIMIT บนคอลัมน์แคบ ๆ)
// แล้วค่อย join กับ view เพื่อดึงรายละเอียด 10-200 แถว แทนการเรียงทั้งอำเภอบน view ที่กว้างและมีฟังก์ชัน
public static class MemberQueries
{
    public const string FromJoin = @"FROM t_house_member m
        JOIN t_house h   ON h.house_id = m.house_id AND h.is_active = 1
        JOIN m_village v ON v.village_id = h.village_id
        JOIN m_tumbol t  ON t.tumbol_id = v.tumbol_id
        LEFT JOIN member_welfare w ON w.member_id = m.member_id";

    // extraWhere ต้องขึ้นต้นด้วย AND (หรือว่าง); พารามิเตอร์ที่ต้องมี: @year @size @off
    public static string Page(string viewName, string extraWhere) => $@"
        SELECT l.* FROM (
            SELECT m.member_id {FromJoin}
            WHERE m.is_active = 1 AND h.survey_year = @year {extraWhere}
            ORDER BY h.house_code, m.member_id LIMIT @size OFFSET @off
        ) ids JOIN {viewName} l ON l.member_id = ids.member_id
        ORDER BY l.house_code, l.member_id";

    public static string Count(string extraWhere) => $@"
        SELECT COUNT(*) {FromJoin}
        WHERE m.is_active = 1 AND h.survey_year = @year {extraWhere}";

    public const string Over18Cond = " AND m.age >= 18 AND COALESCE(w.has_welfare_card, 0) = 0";
}
