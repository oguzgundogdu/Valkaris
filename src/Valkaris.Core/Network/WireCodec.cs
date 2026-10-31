using System.Text.Json;
using System.Text.Json.Serialization;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Network;

/// <summary>Turns messages to and from bytes. Only the types listed here can be read, and bad input yields null rather than an exception.</summary>
public static class WireCodec
{
    /// <summary>A client only ever sends short messages, so a long one is someone misbehaving.</summary>
    public const int MaxFromClientBytes = 16 * 1024;

    public const int MaxFromHostBytes = 32 * 1024 * 1024;

    public static readonly IReadOnlyList<Type> Messages = new[]
    {
        typeof(CommandMessage), typeof(RejectionMessage), typeof(UpdateMessage), typeof(MatchStartMessage),
        typeof(HelloMessage), typeof(LobbyMessage), typeof(RefusedMessage),
    };

    public static readonly IReadOnlyList<Type> Commands = new[]
    {
        typeof(BuildCommand), typeof(TrainCommand), typeof(SendTroopsCommand), typeof(FoundVillageCommand),
        typeof(SetCapitalCommand), typeof(SurrenderCommand), typeof(SendResourcesCommand), typeof(RenameVillageCommand),
    };

    public static readonly IReadOnlyList<Type> Events = new[]
    {
        typeof(BuildingCompleted), typeof(TroopsTrained), typeof(TroopsReturned), typeof(ReinforcementsArrived),
        typeof(BattleResolved), typeof(ScoutResolved), typeof(ScoutNoticed), typeof(VillageCaptured),
        typeof(CapitalChoiceRequired), typeof(CapitalChanged), typeof(PlayerEliminated), typeof(PlayerSurrendered), typeof(PlayerDisconnected),
        typeof(MatchEnded), typeof(ResourcesDelivered), typeof(CarriersReturned), typeof(VillageFounded), typeof(SettlingFailed),
    };

    private static readonly JsonSerializerOptions Options = new(SnapshotMapper.JsonOptions)
    {
        MaxDepth = 16,
        Converters =
        {
            new TaggedConverter<INetMessage>(Messages),
            new TaggedConverter<ICommand>(Commands),
            new TaggedConverter<GameEvent>(Events),
        },
    };

    public static byte[] Encode(INetMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    /// <summary>A message from a client: a size limit applies, and anything malformed is null.</summary>
    public static INetMessage? DecodeFromClient(byte[] bytes) =>
        bytes.Length > MaxFromClientBytes ? null : Decode(bytes);

    public static INetMessage? DecodeFromHost(byte[] bytes) =>
        bytes.Length > MaxFromHostBytes ? null : Decode(bytes);

    private static INetMessage? Decode(byte[] bytes)
    {
        try
        {
            var message = JsonSerializer.Deserialize<INetMessage>(bytes, Options);
            return IsComplete(message) ? message : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // A JSON null slips past the record's non-nullable types, and the rules assume they hold.
    private static bool IsComplete(INetMessage? message) => message switch
    {
        null => false,
        CommandMessage { Command: null } => false,
        CommandMessage { Command: SendTroopsCommand { Troops: null } } => false,
        CommandMessage { Command: RenameVillageCommand { Name: null } } => false,
        HelloMessage hello => hello.Version is not null && hello.DataHash is not null && hello.PlayerName is not null,
        LobbyMessage lobby => lobby.Slots is not null && lobby.Settings is not null,
        UpdateMessage update => update.View is not null && update.Events is not null,
        MatchStartMessage start => start.First is not null && IsComplete(start.First),
        _ => true,
    };

    /// <summary>Writes a value as ["TypeName", {...}], and reads back only the types it was given.</summary>
    private sealed class TaggedConverter<T> : JsonConverter<T> where T : class
    {
        private readonly Dictionary<string, Type> _types;

        public TaggedConverter(IEnumerable<Type> types) => _types = types.ToDictionary(t => t.Name);

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray || !reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException("Expected a tagged value.");
            if (!_types.TryGetValue(reader.GetString()!, out var type))
                throw new JsonException("Unknown type.");

            reader.Read();
            var value = (T?)JsonSerializer.Deserialize(ref reader, type, options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Expected the end of a tagged value.");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var type = value.GetType();
            if (!_types.ContainsKey(type.Name) || _types[type.Name] != type)
                throw new InvalidOperationException($"{type.Name} is not registered in WireCodec.");

            writer.WriteStartArray();
            writer.WriteStringValue(type.Name);
            JsonSerializer.Serialize(writer, value, type, options);
            writer.WriteEndArray();
        }
    }
}
