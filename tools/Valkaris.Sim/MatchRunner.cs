using System.Collections.Concurrent;
using Valkaris.Ai;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Sim;

/// <summary>A match that failed. The seed reproduces it.</summary>
public sealed record MatchFailure(ulong Seed, string Error);

/// <summary>Runs AI-only matches, each seeded, in parallel. Results come back in match order whatever the thread count.</summary>
public static class MatchRunner
{
    public static (IReadOnlyList<MatchResult> Results, IReadOnlyList<MatchFailure> Failures) RunAll(SimOptions options, GameData data)
    {
        var results = new MatchResult?[options.Matches];
        var failures = new ConcurrentBag<MatchFailure>();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.Threads ?? Environment.ProcessorCount };

        Parallel.For(0, options.Matches, parallel, index =>
        {
            var seed = options.Seed + (ulong)index;
            try
            {
                results[index] = RunOne(options, data, seed, index);
            }
            catch (Exception ex)
            {
                failures.Add(new MatchFailure(seed, ex.ToString()));
            }
        });

        return (results.OfType<MatchResult>().ToList(), failures.OrderBy(f => f.Seed).ToList());
    }

    /// <summary>Difficulties go round the seats and rotate each match, so no level always gets the same start.</summary>
    public static AiDifficulty DifficultyOf(SimOptions options, int seat, int matchIndex) =>
        options.Difficulties[(seat + matchIndex) % options.Difficulties.Count];

    public static MatchResult RunOne(SimOptions options, GameData data, ulong seed, int matchIndex)
    {
        var slots = Enumerable.Range(0, options.Players)
            .Select(seat => new PlayerSlot($"AI {seat + 1}", Nation.Valdren, true, DifficultyOf(options, seat, matchIndex)))
            .ToArray();
        var settings = new GameSettings(options.Minutes * 60, options.Speed, options.Map, seed);
        var state = GameFactory.CreateMatch(settings, slots, data);
        var session = new LocalGameSession(state, data, 1);
        var recorder = new MatchRecorder(state, data);
        recorder.Attach(session);

        foreach (var id in state.Players.Keys.OrderBy(id => id))
        {
            var player = id;
            session.AddAgent(new AiPlayer(new AiView(state, data, player, command =>
            {
                var result = session.SendAs(player, command);
                recorder.RecordCommand(result);
                return result;
            })));
        }

        while (!state.IsMatchOver)
        {
            session.Tick(options.SampleSeconds);
            recorder.Sample();
        }

        return recorder.Finish(seed);
    }
}
