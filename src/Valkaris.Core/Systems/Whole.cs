namespace Valkaris.Core.Systems;

/// <summary>
/// Whole numbers from resource amounts. Amounts carry tiny rounding errors that depend on how time was stepped,
/// so a plain floor or ceiling on a value at a whole number could give a different result at a different frame rate.
/// </summary>
public static class Whole
{
    public const double Noise = 1e-6;

    public static int Down(double value) => (int)Math.Floor(value + Noise);

    public static int Up(double value) => (int)Math.Ceiling(value - Noise);
}
