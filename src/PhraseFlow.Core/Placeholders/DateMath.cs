using System.Globalization;
using System.Text.RegularExpressions;

namespace PhraseFlow.Core.Placeholders;

/// <summary>
/// Shifts dates using compact expressions such as <c>+1d</c>, <c>-2w</c>, <c>+1M -1d</c>, <c>+3bd</c> (business days)
/// or anchors (<c>som</c>/<c>eom</c> start/end of month, <c>sow</c>/<c>eow</c> ISO week, <c>soy</c>/<c>eoy</c> year).
/// </summary>
public static partial class DateMath
{
    [GeneratedRegex(@"([+-]?\d+)\s*([A-Za-z]+)")]
    private static partial Regex OffsetToken();

    public static DateTime Apply(DateTime value, string? shift)
    {
        if (string.IsNullOrWhiteSpace(shift))
        {
            return value;
        }

        var result = value;
        foreach (var part in shift.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryApplyAnchor(result, part, out var anchored))
            {
                result = anchored;
                continue;
            }

            var matches = OffsetToken().Matches(part);
            if (matches.Count == 0 || matches.Sum(m => m.Length) != part.Length)
            {
                throw new FormatException($"Invalid date shift '{part}'. Use values like +1d, -2w, +1M, +3bd or eom.");
            }

            foreach (Match match in matches)
            {
                var amount = int.Parse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                result = AddUnit(result, amount, match.Groups[2].Value);
            }
        }

        return result;
    }

    private static DateTime AddUnit(DateTime value, int amount, string unit)
    {
        if (unit == "M")
        {
            return value.AddMonths(amount);
        }

        if (unit == "m")
        {
            return value.AddMinutes(amount);
        }

        return unit.ToLowerInvariant() switch
        {
            "y" or "yr" or "yrs" or "year" or "years" => value.AddYears(amount),
            "mo" or "mon" or "month" or "months" => value.AddMonths(amount),
            "w" or "wk" or "wks" or "week" or "weeks" => value.AddDays(7 * amount),
            "d" or "day" or "days" => value.AddDays(amount),
            "bd" or "b" or "wd" or "bday" or "bdays" or "workday" or "workdays" => AddBusinessDays(value, amount),
            "h" or "hr" or "hrs" or "hour" or "hours" => value.AddHours(amount),
            "min" or "mins" or "minute" or "minutes" => value.AddMinutes(amount),
            "s" or "sec" or "secs" or "second" or "seconds" => value.AddSeconds(amount),
            _ => throw new FormatException($"Unknown date unit '{unit}'."),
        };
    }

    public static DateTime AddBusinessDays(DateTime value, int amount)
    {
        var step = Math.Sign(amount);
        var remaining = Math.Abs(amount);
        var result = value;
        while (remaining > 0)
        {
            result = result.AddDays(step);
            if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                remaining--;
            }
        }

        return result;
    }

    private static bool TryApplyAnchor(DateTime value, string token, out DateTime result)
    {
        var date = value.Date;
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        DateTime? anchored = token.ToLowerInvariant() switch
        {
            "today" => date,
            "som" => new DateTime(date.Year, date.Month, 1),
            "eom" => new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)),
            "soy" => new DateTime(date.Year, 1, 1),
            "eoy" => new DateTime(date.Year, 12, 31),
            "sow" => date.AddDays(-daysSinceMonday),
            "eow" => date.AddDays(6 - daysSinceMonday),
            _ => null,
        };
        result = anchored ?? value;
        return anchored.HasValue;
    }
}
