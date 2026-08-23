namespace TrmnlApi.Mappings;

public static class HourLabel
{
    public static string Format(string isoTime, bool use24Hour = false)
    {
        var h = int.Parse(isoTime.AsSpan(11, 2));
        if (use24Hour)
            return $"{h:D2}:00";
        return h switch
        {
            0 => "12am",
            < 12 => $"{h}am",
            12 => "12pm",
            _ => $"{h - 12}pm"
        };
    }
}
