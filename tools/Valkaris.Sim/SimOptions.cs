using Valkaris.Core.Model;

namespace Valkaris.Sim;

public sealed record SimOptions
{
    public int Matches { get; init; } = 100;
    public int Players { get; init; } = 4;
    public IReadOnlyList<AiDifficulty> Difficulties { get; init; } = new[] { AiDifficulty.Normal };
    public MapSize Map { get; init; } = MapSize.Small;
    public double Minutes { get; init; } = 30;
    public double Speed { get; init; } = 1;
    public ulong Seed { get; init; } = 1;
    public int? Threads { get; init; }
    public string? OutPath { get; init; }
    public string? DataPath { get; init; }

    /// <summary>Real seconds between samples of the economy, and the step the match runs at.</summary>
    public double SampleSeconds { get; init; } = 10;

    public static SimOptions Parse(IEnumerable<string> arguments)
    {
        var options = new SimOptions();
        foreach (var argument in arguments)
        {
            var parts = argument.Split('=', 2);
            if (parts.Length != 2 || !parts[0].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Expected --name=value, got '{argument}'.");

            var value = parts[1];
            options = parts[0] switch
            {
                "--matches" => options with { Matches = int.Parse(value) },
                "--players" => options with { Players = int.Parse(value) },
                "--difficulty" => options with { Difficulties = value.Split(',').Select(Enum.Parse<AiDifficulty>).ToList() },
                "--map" => options with { Map = Enum.Parse<MapSize>(value) },
                "--minutes" => options with { Minutes = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                "--speed" => options with { Speed = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                "--seed" => options with { Seed = ulong.Parse(value) },
                "--threads" => options with { Threads = int.Parse(value) },
                "--out" => options with { OutPath = value },
                "--data" => options with { DataPath = value },
                _ => throw new ArgumentException($"Unknown option '{parts[0]}'."),
            };
        }
        return options;
    }
}
