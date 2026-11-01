using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Settles a seat's Random choice from the match seed. It has its own arithmetic, so the match's random numbers don't move.</summary>
public static class NationPicker
{
    private const ulong Gamma = 0x9E3779B97F4A7C15;
    private const ulong MixA = 0xBF58476D1CE4E5B9;
    private const ulong MixB = 0x94D049BB133111EB;

    public static Nation Resolve(Nation? chosen, ulong seed, int seat)
    {
        if (chosen is { } nation)
            return nation;

        var nations = Enum.GetValues<Nation>();
        var mixed = seed + Gamma * (ulong)(seat + 1);
        mixed = (mixed ^ (mixed >> 30)) * MixA;
        mixed = (mixed ^ (mixed >> 27)) * MixB;
        return nations[(int)((mixed ^ (mixed >> 31)) % (ulong)nations.Length)];
    }
}
