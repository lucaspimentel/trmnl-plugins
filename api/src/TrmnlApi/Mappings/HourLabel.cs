namespace TrmnlApi.Mappings;

public static class HourLabel
{
    public static string Format(string isoTime)
    {
        var h = int.Parse(isoTime.AsSpan(11, 2));
        return h switch
        {
            0 => "12am",
            < 12 => $"{h}am",
            12 => "12pm",
            _ => $"{h - 12}pm"
        };
    }
}
