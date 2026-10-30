using System;
using System.Collections.Generic;
using System.Globalization;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>
/// Dev-only match overrides from the command line: -- --dev-speed=20 --dev-duration=60 (seconds).
/// Ignored in release builds, so exported games cannot use them.
/// </summary>
public sealed record DevOptions(double? Speed, double? DurationSeconds)
{
    private const string SpeedArgument = "--dev-speed=";
    private const string DurationArgument = "--dev-duration=";

    public static DevOptions Parse(IEnumerable<string> arguments, bool isDebugBuild)
    {
        if (!isDebugBuild)
            return new DevOptions(null, null);

        double? speed = null;
        double? duration = null;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(SpeedArgument, StringComparison.Ordinal))
                speed = ParsePositive(argument[SpeedArgument.Length..]);
            else if (argument.StartsWith(DurationArgument, StringComparison.Ordinal))
                duration = ParsePositive(argument[DurationArgument.Length..]);
        }
        return new DevOptions(speed, duration);
    }

    public GameSettings Apply(GameSettings settings) => settings with
    {
        SpeedMultiplier = Speed ?? settings.SpeedMultiplier,
        MatchDurationSeconds = DurationSeconds ?? settings.MatchDurationSeconds,
    };

    private static double? ParsePositive(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
