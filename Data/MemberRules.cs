namespace SLDC.Web.Data;

// กติกาตรวจข้อมูลบุคคลที่ใช้ร่วมกันหลายหน้า
public static class MemberRules
{
    public static bool ValidCitizenId(string s)
    {
        if (s.Length != 13 || !s.All(char.IsDigit)) return false;
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (s[i] - '0') * (13 - i);
        return (11 - sum % 11) % 10 == s[12] - '0';
    }

    // คืน (ข้อความผิดพลาด, อายุ(ปี), อายุ(เดือนที่เหลือ)) จากวันเกิด พ.ศ.
    public static (string? err, short? age, short? ageMonth) CalcAge(int? birthYear, int? birthMonth, int? birthDay)
    {
        var nowBe = DateTime.Now.Year + 543;
        if (birthYear == null)
            return (birthMonth != null || birthDay != null ? "กรุณากรอกปีเกิด (พ.ศ.) ด้วย" : null, null, null);
        if (birthYear < 2400 || birthYear > nowBe) return ($"ปีเกิดต้องเป็น พ.ศ. ระหว่าง 2400 ถึง {nowBe}", null, null);
        if (birthMonth == null)
        {
            if (birthDay != null) return ("กรุณาเลือกเดือนเกิดด้วย", null, null);
            return (null, (short)(nowBe - birthYear.Value), null);
        }
        if (birthMonth < 1 || birthMonth > 12) return ("เดือนเกิดไม่ถูกต้อง", null, null);
        if (birthDay != null && (birthDay < 1 || birthDay > DateTime.DaysInMonth(birthYear.Value - 543, birthMonth.Value)))
            return ("วันเกิดไม่ถูกต้องสำหรับเดือนที่เลือก", null, null);
        var today = DateTime.Today;
        var months = (nowBe - birthYear.Value) * 12 + (today.Month - birthMonth.Value);
        if (birthDay != null && today.Day < birthDay) months--;
        if (months < 0) return ("วันเกิดเป็นอนาคต", null, null);
        return (null, (short)(months / 12), (short)(months % 12));
    }
}
