using System;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The result of the match, with everyone's final score.</summary>
public partial class MatchEndScreen : ColorRect
{
    private static readonly string[] Headers = { "end.header.rank", "end.header.player", "end.header.villages", "end.header.population", "end.header.captured", "end.header.score" };

    public event Action? BackRequested;

    public override void _Ready() => GetNode<Button>("%Back").Pressed += () => BackRequested?.Invoke();

    public void ShowOutcome(IGameSession session, MatchOutcome outcome)
    {
        var localWon = outcome.WinnerIds.Contains(session.LocalPlayerId);
        Sound.Play(localWon ? SoundEffect.Victory : SoundEffect.Defeat);
        GetNode<Label>("%Title").Text = L.T(!localWon ? "report.defeat" : outcome.WinnerIds.Count > 1 ? "end.draw" : "report.victory");

        var names = outcome.WinnerIds.Select(id => UiContent.OwnerName(session, id)).Aggregate((first, second) => L.T("common.and", ("first", first), ("second", second)));
        var soleWinnerIsYou = localWon && outcome.WinnerIds.Count == 1;
        GetNode<Label>("%Reason").Text = outcome.Reason == MatchEndReason.LastPlayerStanding
            ? (soleWinnerIsYou ? L.T("end.you_last") : L.T("end.other_last", ("names", names)))
            : outcome.WinnerIds.Count > 1
                ? L.T("end.tied", ("names", names))
                : soleWinnerIsYou ? L.T("end.you_highest") : L.T("end.other_highest", ("names", names));

        var table = GetNode<GridContainer>("%Table");
        foreach (var child in table.GetChildren())
        {
            table.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var header in Headers)
            table.AddChild(new Label { Text = L.T(header), ThemeTypeVariation = "MutedLabel" });

        for (var i = 0; i < outcome.Scores.Count; i++)
        {
            var score = outcome.Scores[i];
            var style = outcome.WinnerIds.Contains(score.PlayerId) ? "StrongLabel" : "";
            var player = session.State.Players[score.PlayerId];
            var name = UiContent.OwnerName(session, player.Id);
            if (player.Difficulty is { } difficulty)
                name = L.T("end.difficulty", ("name", name), ("difficulty", L.T(L.Key("difficulty", difficulty))));
            if (player.HasSurrendered)
                name = L.T("end.surrendered", ("name", name));
            else if (player.IsEliminated)
                name = L.T("end.eliminated", ("name", name));

            foreach (var cell in new[] { Format.Whole(i + 1), name, Format.Whole(score.Villages), Format.Whole(score.Population), Format.Whole(score.Captures), Format.Whole(score.Score) })
                table.AddChild(new Label { Text = cell, ThemeTypeVariation = style });
        }

        Visible = true;
    }
}
