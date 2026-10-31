using System.Collections.Generic;
using Godot;

namespace Valkaris.Presentation;

/// <summary>Draws a band of the village's trees, houses and crops. Only redraws when they change.</summary>
public partial class VillageScenery : Control
{
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private List<Placed> _items = new();

    public void Show(List<Placed> items)
    {
        _items = items;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var parent = GetParent<Control>().Size;
        var size = Vector2.One * VillageArt.SpriteCanvas * VillageArt.SpriteScale;
        var ground = new Vector2(VillageArt.SpriteGroundX, VillageArt.SpriteGroundY) * VillageArt.SpriteScale;
        foreach (var item in _items)
        {
            if (!Textures.TryGetValue(item.Sprite, out var texture))
                Textures[item.Sprite] = texture = GD.Load<Texture2D>(item.Sprite);

            var at = new Vector2(item.X, item.Y) * parent;
            var left = item.Flip ? at.X - (size.X - ground.X) : at.X - ground.X;
            DrawTextureRect(texture, new Rect2(left, at.Y - ground.Y, item.Flip ? -size.X : size.X, size.Y), tile: false);
        }
    }
}
