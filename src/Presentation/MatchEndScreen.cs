using System;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The result of the match, with everyone's final score.</summary>
public partial class MatchEndScreen : ColorRect
{
    private static readonly string[] Headers = { "#", "Player", "Villages", "Population", "Captured", "Score" };

    public event Action? BackRequested;

    public override void _Ready() => GetNode<Button>("%Back").Pressed += () => BackRequested?.Invoke();

    public void ShowOutcome(IGameSession session, MatchOutcome outcome)
    {
        var localWon = outcome.WinnerIds.Contains(session.LocalPlayerId);
        GetNode<Label>("%Title").Text = !localWon ? "Defeat" : outcome.WinnerIds.Count > 1 ? "Draw" : "Victory";

        var names = string.Join(" and ", outcome.WinnerIds.Select(id => UiContent.OwnerName(session, id)));
        var soleWinnerIsYou = localWon && outcome.WinnerIds.Count == 1;
        GetNode<Label>("%Reason").Text = outcome.Reason == MatchEndReason.LastPlayerStanding
            ? (soleWinnerIsYou ? "You are the last player standing." : $"{names} is the last player standing.")
            : outcome.WinnerIds.Count > 1
                ? $"Time ran out with {names} tied for the highest score."
                : soleWinnerIsYou ? "Time ran out. You have the highest score." : $"Time ran out. {names} has the highest score.";

        var table = GetNode<GridContainer>("%Table");
        foreach (var child in table.GetChildren())
        {
            table.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var header in Headers)
            table.AddChild(new Label { Text = header, ThemeTypeVariation = "MutedLabel" });

        for (var i = 0; i < outcome.Scores.Count; i++)
        {
            var score = outcome.Scores[i];
            var style = outcome.WinnerIds.Contains(score.PlayerId) ? "StrongLabel" : "";
            var name = UiContent.OwnerName(session, score.PlayerId)
                + (session.State.Players[score.PlayerId].IsEliminated ? " (eliminated)" : "");

            foreach (var cell in new[] { $"{i + 1}", name, $"{score.Villages}", $"{score.Population}", $"{score.Captures}", $"{score.Score:0}" })
                table.AddChild(new Label { Text = cell, ThemeTypeVariation = style });
        }

        Visible = true;
    }
}
