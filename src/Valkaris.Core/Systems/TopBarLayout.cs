namespace Valkaris.Core.Systems;

/// <summary>What the top bar shows at one level of detail.</summary>
public sealed record TopBarPlan(
    bool Brand = true,
    bool Rates = true,
    bool Capacity = true,
    bool ViewLabels = true,
    bool WideSpacing = true,
    bool StatLabels = true,
    TopBarWarning Warning = TopBarWarning.Full);

public enum TopBarWarning
{
    Full,
    Short,
    TimeOnly,
}

/// <summary>How wide each part of the top bar would be, in pixels. Warnings are the ones showing now.</summary>
public sealed record TopBarWidths(
    double Brand,
    double Amounts,
    double Rates,
    double Capacities,
    double Stats,
    double StatLabels,
    double ViewLabels,
    double ViewIcons,
    double WarningsFull,
    double WarningsShort,
    double WarningsTimeOnly,
    double Fixed,
    int Gaps,
    int ChipGaps);

/// <summary>Drops detail in a fixed order until the bar fits. The clock, the menu and warnings' times are never dropped.</summary>
public static class TopBarLayout
{
    public const double WideGap = 28;
    public const double NarrowGap = 10;
    public const double WideChipGap = 26;
    public const double NarrowChipGap = 10;

    /// <summary>From the most detail to the least. Each step keeps what the one before dropped.</summary>
    public static readonly IReadOnlyList<TopBarPlan> Levels = new TopBarPlan[]
    {
        new(),
        new(Brand: false),
        new(Brand: false, Rates: false),
        new(Brand: false, Rates: false, Capacity: false),
        new(Brand: false, Rates: false, Capacity: false, ViewLabels: false),
        new(Brand: false, Rates: false, Capacity: false, ViewLabels: false, WideSpacing: false),
        new(Brand: false, Rates: false, Capacity: false, ViewLabels: false, WideSpacing: false, Warning: TopBarWarning.Short),
        new(Brand: false, Rates: false, Capacity: false, ViewLabels: false, WideSpacing: false, Warning: TopBarWarning.Short, StatLabels: false),
        new(Brand: false, Rates: false, Capacity: false, ViewLabels: false, WideSpacing: false, Warning: TopBarWarning.TimeOnly, StatLabels: false),
    };

    public static double Width(TopBarPlan plan, TopBarWidths widths)
    {
        var warnings = plan.Warning switch
        {
            TopBarWarning.Full => widths.WarningsFull,
            TopBarWarning.Short => widths.WarningsShort,
            _ => widths.WarningsTimeOnly,
        };

        return (plan.Brand ? widths.Brand : 0)
            + widths.Amounts
            + (plan.Rates ? widths.Rates : 0)
            + (plan.Capacity ? widths.Capacities : 0)
            + widths.Stats
            + (plan.StatLabels ? widths.StatLabels : 0)
            + (plan.ViewLabels ? widths.ViewLabels : widths.ViewIcons)
            + warnings
            + widths.Fixed
            + (widths.Gaps - (plan.Brand ? 0 : 1)) * (plan.WideSpacing ? WideGap : NarrowGap)
            + widths.ChipGaps * (plan.WideSpacing ? WideChipGap : NarrowChipGap);
    }

    /// <summary>The most detail that fits, or the least there is if nothing does.</summary>
    public static TopBarPlan Choose(TopBarWidths widths, double available)
    {
        foreach (var plan in Levels)
        {
            if (Width(plan, widths) <= available)
                return plan;
        }
        return Levels[^1];
    }
}
