using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MySqlConnector;

namespace SLDC.Web.Data;

/// <summary>เขียนผลลัพธ์จากฐานข้อมูลเป็น CSV / JSON แบบสตรีม (ไม่โหลดทั้งตารางเข้าหน่วยความจำ)</summary>
public static class ExportWriter
{
    static readonly JsonSerializerOptions JsonOpt = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static readonly UTF8Encoding Utf8Bom = new(true);

    /// <summary>แปลงค่าเป็นช่อง CSV — กัน CSV/Formula injection (ค่าข้อความที่ขึ้นต้นด้วย = + - @ จะถูกนำหน้าด้วย ')</summary>
    public static string Cell(object? v)
    {
        if (v == null || v is DBNull) return "";
        var s = v switch
        {
            bool b => b ? "1" : "0",
            DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? ""
        };
        if (v is string && s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
        if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public static string CsvLine(IEnumerable<object?> cells) => string.Join(",", cells.Select(Cell)) + "\r\n";

    public static byte[] CsvBytes(string csv) => Utf8Bom.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

    /// <summary>รันชุดข้อมูลแล้วเขียนลง output ตามรูปแบบ (csv|json) คืนจำนวนแถว</summary>
    public static async Task<long> WriteAsync(MySqlConnection c, Dataset ds, IDictionary<string, object?> p, bool paged,
                                              Stream output, string format, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(ds.BuildSql(paged), c) { CommandTimeout = 600 };
        foreach (var kv in p) cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await WriteReaderAsync(rd, output, format, ct);
    }

    public static async Task<long> WriteReaderAsync(DbDataReader rd, Stream output, string format, CancellationToken ct)
    {
        long n = 0;
        var csv = format != "json";
        await using var w = new StreamWriter(output, csv ? Utf8Bom : new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        var names = Enumerable.Range(0, rd.FieldCount).Select(rd.GetName).ToArray();
        var buf = new object[rd.FieldCount];

        if (csv)
        {
            await w.WriteAsync(CsvLine(names));
            while (await rd.ReadAsync(ct)) { rd.GetValues(buf); await w.WriteAsync(CsvLine(buf)); n++; }
        }
        else
        {
            await w.WriteAsync("[");
            while (await rd.ReadAsync(ct))
            {
                rd.GetValues(buf);
                var row = new Dictionary<string, object?>(names.Length);
                for (var i = 0; i < names.Length; i++)
                    row[names[i]] = buf[i] is DBNull ? null : buf[i] is DateTime d ? d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : buf[i];
                await w.WriteAsync((n == 0 ? "\n" : ",\n") + JsonSerializer.Serialize(row, JsonOpt));
                n++;
            }
            await w.WriteAsync("\n]");
        }
        await w.FlushAsync();
        return n;
    }

    /// <summary>พจนานุกรมข้อมูล (ชื่อคอลัมน์ + ความหมาย) ของทุกชุดข้อมูลที่ส่งให้</summary>
    public static string DictionaryCsv(IEnumerable<Dataset> sets)
    {
        var sb = new StringBuilder();
        sb.Append(CsvLine(new object[] { "dataset", "ชื่อชุดข้อมูล", "column", "ความหมาย", "ระดับข้อมูล" }));
        foreach (var d in sets)
            foreach (var c in d.Cols)
                sb.Append(CsvLine(new object[] { d.Key, d.Title, c.Name, c.Desc, d.IsPublic ? "ข้อมูลเปิด (สถิติสรุป)" : "ไม่ระบุตัวตน (ต้องล็อกอิน)" }));
        return sb.ToString();
    }
}
