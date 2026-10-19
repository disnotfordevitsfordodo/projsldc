using System.Data;
using Dapper;

namespace SLDC.Web.Data;

// หน้าสุขภาพ/กลุ่มเปราะบางอ่านจากตารางแคช (cache_disease_counts, cache_vulnerable_counts)
// เมื่อมีการเพิ่ม/ลบโรคหรือกลุ่มเปราะบาง ต้องคำนวณแคชของปีนั้นใหม่ ไม่เช่นนั้นตัวเลขในรายงานไม่เปลี่ยน
// หมายเหตุ: โครงสร้างแคชอนุมานจากคอลัมน์ที่โค้ดรายงานใช้อยู่ (survey_year, disease_id, patients / vulnerable_type_id, people)
public static class Caches
{
    public static async Task<string?> RefreshDiseaseAsync(IDbConnection c, int year)
    {
        try
        {
            using var tx = c.BeginTransaction();
            await c.ExecuteAsync("DELETE FROM cache_disease_counts WHERE survey_year=@year", new { year }, tx);
            await c.ExecuteAsync(@"INSERT INTO cache_disease_counts(survey_year, disease_id, patients)
                SELECT h.survey_year, md.disease_id, COUNT(DISTINCT md.member_id)
                FROM member_disease md
                JOIN t_house_member m ON m.member_id = md.member_id AND m.is_active = 1
                JOIN t_house h ON h.house_id = m.house_id AND h.is_active = 1 AND h.survey_year = @year
                GROUP BY h.survey_year, md.disease_id", new { year }, tx, commandTimeout: 120);
            tx.Commit();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static async Task<string?> RefreshVulnerableAsync(IDbConnection c, int year)
    {
        try
        {
            using var tx = c.BeginTransaction();
            await c.ExecuteAsync("DELETE FROM cache_vulnerable_counts WHERE survey_year=@year", new { year }, tx);
            await c.ExecuteAsync(@"INSERT INTO cache_vulnerable_counts(survey_year, vulnerable_type_id, people)
                SELECT h.survey_year, mv.vulnerable_type_id, COUNT(DISTINCT mv.member_id)
                FROM member_vulnerable mv
                JOIN t_house_member m ON m.member_id = mv.member_id AND m.is_active = 1
                JOIN t_house h ON h.house_id = m.house_id AND h.is_active = 1 AND h.survey_year = @year
                GROUP BY h.survey_year, mv.vulnerable_type_id", new { year }, tx, commandTimeout: 120);
            tx.Commit();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
