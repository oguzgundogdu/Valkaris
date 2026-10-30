using System.Text.Json.Nodes;
using Valkaris.Core.Data;

namespace Valkaris.Core.Tests;

/// <summary>Reads the real data/ files, which the test project links into its output.</summary>
internal sealed class ShippedDataSource : IDataSource
{
    public string ReadText(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", fileName));
}

/// <summary>Shipped data with one file edited, for tests that need invalid or altered values.</summary>
internal sealed class MutatedDataSource : IDataSource
{
    private readonly string _fileName;
    private readonly string _text;
    private readonly IDataSource _inner = new ShippedDataSource();

    public MutatedDataSource(string fileName, Action<JsonNode> mutate)
    {
        _fileName = fileName;
        var root = JsonNode.Parse(_inner.ReadText(fileName))!;
        mutate(root);
        _text = root.ToJsonString();
    }

    public string ReadText(string fileName) => fileName == _fileName ? _text : _inner.ReadText(fileName);
}

internal static class TestData
{
    public static readonly GameData Shipped = GameData.Load(new ShippedDataSource());

    public static GameData Mutated(string fileName, Action<JsonNode> mutate) =>
        GameData.Load(new MutatedDataSource(fileName, mutate));
}
