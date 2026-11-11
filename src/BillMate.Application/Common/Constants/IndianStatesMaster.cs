namespace BillMate.Application.Common.Constants;

public sealed record IndianStateInfo(string Code, string Name);

/// <summary>
/// Official Indian GST State & Union Territory codes (28 States + 8 UTs + Other Territory).
/// </summary>
public static class IndianStatesMaster
{
    public static readonly IReadOnlyList<IndianStateInfo> All =
    [
        new("01", "Jammu & Kashmir"),
        new("02", "Himachal Pradesh"),
        new("03", "Punjab"),
        new("04", "Chandigarh"),
        new("05", "Uttarakhand"),
        new("06", "Haryana"),
        new("07", "Delhi"),
        new("08", "Rajasthan"),
        new("09", "Uttar Pradesh"),
        new("10", "Bihar"),
        new("11", "Sikkim"),
        new("12", "Arunachal Pradesh"),
        new("13", "Nagaland"),
        new("14", "Manipur"),
        new("15", "Mizoram"),
        new("16", "Tripura"),
        new("17", "Meghalaya"),
        new("18", "Assam"),
        new("19", "West Bengal"),
        new("20", "Jharkhand"),
        new("21", "Odisha"),
        new("22", "Chhattisgarh"),
        new("23", "Madhya Pradesh"),
        new("24", "Gujarat"),
        new("26", "Dadra & Nagar Haveli and Daman & Diu"),
        new("27", "Maharashtra"),
        new("29", "Karnataka"),
        new("30", "Goa"),
        new("31", "Lakshadweep"),
        new("32", "Kerala"),
        new("33", "Tamil Nadu"),
        new("34", "Puducherry"),
        new("35", "Andaman & Nicobar Islands"),
        new("36", "Telangana"),
        new("37", "Andhra Pradesh"),
        new("38", "Ladakh"),
        new("97", "Other Territory")
    ];
}
