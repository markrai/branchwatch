namespace BranchWatch;

internal static class StickyNoteAlarmTime
{
    internal static DateTime NextMinute(DateTime localNow) =>
        new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, localNow.Minute, 0).AddMinutes(1);

    internal static (int Hour, bool IsPm) ToTwelveHour(int hour)
    {
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        return (hour % 12 == 0 ? 12 : hour % 12, hour >= 12);
    }

    internal static string FormatLocal(DateTimeOffset due) =>
        $"{due.ToLocalTime():d} {due.ToLocalTime().ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}";

    internal static bool TryResolveTwelveHour(DateTime date, int hour, int minute, string? period, TimeZoneInfo zone,
        DateTimeOffset now, out DateTimeOffset due, out string error)
    {
        if (hour is < 1 or > 12 || period is not ("AM" or "PM"))
        {
            due = default;
            error = "Choose a valid hour and AM or PM.";
            return false;
        }

        return TryResolve(date, hour % 12 + (period == "PM" ? 12 : 0), minute, zone, now, out due, out error);
    }

    internal static bool TryResolve(DateTime date, int hour, int minute, TimeZoneInfo zone, DateTimeOffset now,
        out DateTimeOffset due, out string error)
    {
        due = default;
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            error = "Choose a valid hour and minute.";
            return false;
        }

        var local = date.Date.AddHours(hour).AddMinutes(minute);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
        {
            error = "Choose a time outside the daylight-saving clock change.";
            return false;
        }

        try { due = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime(); }
        catch (ArgumentException)
        {
            error = "Choose a valid future date and time.";
            return false;
        }

        if (due <= now)
        {
            error = "Choose a date and time in the future.";
            return false;
        }

        error = "";
        return true;
    }
}
