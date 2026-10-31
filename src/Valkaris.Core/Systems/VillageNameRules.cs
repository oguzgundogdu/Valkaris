using System.Text;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>
/// What a player may call a village, used alike by the start screen and by renaming. The result never depends on
/// the machine's culture, so every player in a match gets the same answer.
/// </summary>
public static class VillageNameRules
{
    /// <summary>Trims, keeps one space between words, and gives the same characters the same encoding.</summary>
    public static string Clean(string name)
    {
        var words = name.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }

    /// <summary>The form two names are compared in: case is ignored, and I, ı, İ and i count as one letter.</summary>
    public static string Folded(string name)
    {
        var folded = new StringBuilder(name.Length);
        foreach (var letter in Clean(name))
            folded.Append(letter is 'I' or 'ı' or 'İ' ? 'i' : char.ToLowerInvariant(letter));
        return folded.ToString();
    }

    /// <summary>Why this text can't be a name at all, whatever else is on the map. Null if it can.</summary>
    public static CommandRejection? FormatRejection(VillageNameConfig limits, string name)
    {
        var clean = Clean(name);
        if (clean.Length < limits.MinLength)
            return CommandRejection.VillageNameTooShort;
        if (clean.Length > limits.MaxLength)
            return CommandRejection.VillageNameTooLong;
        return clean.All(IsAllowed) && clean.Any(IsLetter) ? null : CommandRejection.VillageNameInvalid;
    }

    /// <summary>Why a new name can't be used next to the existing names. Null if it can.</summary>
    public static CommandRejection? Rejection(GameData data, IEnumerable<string> existing, string name) =>
        FormatRejection(data.Game.VillageNames, name)
        ?? (IsReserved(data, name) || existing.Select(Folded).Contains(Folded(name)) ? CommandRejection.VillageNameTaken : null);

    /// <summary>Why a player can't give their village this name. Fixing the case of its current name is allowed.</summary>
    public static CommandRejection? Rejection(GameState state, GameData data, Village village, string name)
    {
        if (Clean(name) == village.Name)
            return CommandRejection.VillageNameTaken;

        var others = state.Villages.Values.Where(v => v.Id != village.Id).Select(v => v.Name);
        return Rejection(data, others, name);
    }

    /// <summary>
    /// Names on the data lists are kept for camps and founded villages, which take them later without asking.
    /// A number after one is kept too, since the founding list reuses names that way.
    /// </summary>
    private static bool IsReserved(GameData data, string name)
    {
        var folded = Folded(name);
        var space = folded.LastIndexOf(' ');
        var withoutNumber = space > 0 && folded[(space + 1)..].All(char.IsAsciiDigit) ? folded[..space] : folded;

        return data.Map.PlayerVillageNames.Concat(data.Map.FoundedVillageNames).Concat(data.Map.BarbarianVillageNames)
            .Select(Folded)
            .Any(listed => listed == folded || listed == withoutNumber);
    }

    /// <summary>Latin letters, including Turkish ones, that the bundled font can draw. The UI test checks the font.</summary>
    private static bool IsLetter(char character) =>
        char.IsAsciiLetter(character) || (character >= 'À' && character <= 'ſ' && character is not ('×' or '÷' or 'ĳ' or 'ſ'));

    private static bool IsAllowed(char character) =>
        IsLetter(character) || char.IsAsciiDigit(character) || character is ' ' or '-' or '\'';
}
