using System.Data;
using Dapper;
using SLDC.Web.Models;

namespace SLDC.Web.Data;

/// <summary>โหลดรายชื่อบ้านและสมาชิก (มีชื่อจริง — ใช้เฉพาะงานภาคสนามและแบบสำรวจ ซึ่งจำกัดตามสิทธิ์พื้นที่)</summary>
public static class Roster
{
    const string Sql = @"SELECT h.house_id, h.house_code, h.house_no, h.house_nearby,
        m.member_id, CONCAT(COALESCE(p.name,''), m.first_name, ' ', COALESCE(m.last_name,'')) AS full_name,
        m.age, g.name AS gender, rel.name AS relationship, oc.name AS occupation,
        IF(m.citizen_id IS NULL OR m.citizen_id = '', '', CONCAT('*********', RIGHT(m.citizen_id,4))) AS citizen_masked,
        ws.name_th AS welfare_name, mw.has_welfare_card AS has_card, m.is_disabled, m.is_chronic_patient
      FROM t_house h
      LEFT JOIN t_house_member m ON m.house_id = h.house_id AND m.is_active = 1
      LEFT JOIN m_prefix p ON p.prefix_id = m.prefix_id
      LEFT JOIN m_gender g ON g.gender_id = m.gender_id
      LEFT JOIN m_relationship rel ON rel.relationship_id = m.relationship_id
      LEFT JOIN m_occupation oc ON oc.occupation_id = m.occupation_id
      LEFT JOIN member_welfare mw ON mw.member_id = m.member_id
      LEFT JOIN m_welfare_status ws ON ws.welfare_status_id = mw.welfare_status_id
      WHERE {where}
      ORDER BY h.house_code, m.member_id";

    static async Task<FieldSheetVm> InfoAsync(IDbConnection c, string villageId) =>
        await c.QueryFirstOrDefaultAsync<FieldSheetVm>(@"SELECT v.village_id, v.moo, v.name_th AS village_name, tb.name_th AS tumbol_name, a.name_th AS amphur_name
            FROM m_village v JOIN m_tumbol tb ON tb.tumbol_id = v.tumbol_id JOIN m_amphur a ON a.amphur_id = tb.amphur_id
            WHERE v.village_id = @villageId", new { villageId }) ?? new FieldSheetVm { VillageId = villageId };

    static List<RosterHouse> Group(IEnumerable<RosterRow> rows) =>
        rows.GroupBy(r => r.HouseId).Select(g =>
        {
            var f = g.First();
            return new RosterHouse
            {
                HouseId = f.HouseId, HouseCode = f.HouseCode, HouseNo = f.HouseNo, Nearby = f.HouseNearby,
                Members = g.Where(r => r.MemberId != null).ToList()
            };
        }).ToList();

    public static async Task<FieldSheetVm> ByVillageAsync(IDbConnection c, string villageId, int year)
    {
        var vm = await InfoAsync(c, villageId);
        vm.Year = year;
        var rows = await c.QueryAsync<RosterRow>(Sql.Replace("{where}", "h.village_id = @villageId AND h.survey_year = @year AND h.is_active = 1"),
            new { villageId, year }, commandTimeout: 120);
        vm.Houses = Group(rows);
        return vm;
    }

    public static async Task<FieldSheetVm?> ByHouseAsync(IDbConnection c, long houseId)
    {
        var loc = await c.QueryFirstOrDefaultAsync<HouseLoc>(@"SELECT h.house_id, h.survey_year, h.village_id, v.tumbol_id, tb.amphur_id
            FROM t_house h JOIN m_village v ON v.village_id = h.village_id JOIN m_tumbol tb ON tb.tumbol_id = v.tumbol_id
            WHERE h.house_id = @houseId", new { houseId });
        if (loc == null) return null;
        var vm = await InfoAsync(c, loc.VillageId);
        vm.Year = loc.SurveyYear;
        var rows = await c.QueryAsync<RosterRow>(Sql.Replace("{where}", "h.house_id = @houseId"), new { houseId });
        vm.Houses = Group(rows);
        return vm;
    }
}
