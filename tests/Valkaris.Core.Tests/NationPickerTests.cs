using Valkaris.Core.Model;
using Valkaris.Core.Systems;
using Xunit;

namespace Valkaris.Core.Tests;

public class NationPickerTests
{
    [Fact]
    public void AChoiceIsKept() => Assert.Equal(Nation.Solari, NationPicker.Resolve(Nation.Solari, 5, 2));

    [Fact]
    public void RandomIsTheSameForTheSameSeedAndSeat() =>
        Assert.Equal(NationPicker.Resolve(null, 77, 1), NationPicker.Resolve(null, 77, 1));

    [Fact]
    public void RandomGivesEachNationAFairShare()
    {
        var counts = new Dictionary<Nation, int>();
        for (ulong seed = 0; seed < 3000; seed++)
        {
            for (var seat = 0; seat < 4; seat++)
            {
                var nation = NationPicker.Resolve(null, seed, seat);
                counts[nation] = counts.GetValueOrDefault(nation) + 1;
            }
        }

        Assert.All(Enum.GetValues<Nation>(), nation => Assert.InRange(counts[nation], 3700, 4300));
    }
}
