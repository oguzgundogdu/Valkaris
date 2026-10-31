using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Network;

/// <summary>Anything sent over the wire. Each type must be registered in WireCodec.</summary>
public interface INetMessage
{
}

/// <summary>A player's command. The host decides who sent it from the connection, never from the message.</summary>
public sealed record CommandMessage(ICommand Command) : INetMessage;

/// <summary>What the host told one player about one command that failed.</summary>
public sealed record RejectionMessage(CommandRejection Rejection) : INetMessage;

/// <summary>One player's view of the match. The state is applied before the events, so a handler can find the report an event names.</summary>
public sealed record UpdateMessage(PlayerViewSnapshot View, IReadOnlyList<GameEvent> Events) : INetMessage;

/// <summary>The match has begun: which player the receiver is, and the first full view.</summary>
public sealed record MatchStartMessage(int PlayerId, UpdateMessage First) : INetMessage;
