using System.Data;
using Dapper;
using MySqlConnector;

namespace SLDC.Web.Data;

public class Db
{
    private readonly string _cs;
    public Db(IConfiguration cfg) => _cs = cfg.GetConnectionString("Default")!;

    public MySqlConnection Open()
    {
        var c = new MySqlConnection(_cs);
        c.Open();
        return c;
    }

    // ถ้าไม่ระบุปี ใช้ปีล่าสุดที่มีข้อมูล
    public async Task<int> YearAsync(IDbConnection c, int year)
        => year > 0 ? year
           : await c.ExecuteScalarAsync<int?>("SELECT MAX(survey_year) FROM t_house") ?? DateTime.Now.Year + 543;
}
