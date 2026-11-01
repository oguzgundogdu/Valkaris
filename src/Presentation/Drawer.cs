using System;
using Godot;
using Valkaris.Core.Hosting;

namespace Valkaris.Presentation;

/// <summary>The sheet that slides up from the bottom, with a slim tab strip that stays on screen. Its height is remembered.</summary>
public partial class Drawer : Control
{
    public const float MinFraction = 0.12f;
    public const float MaxFraction = 0.7f;
    private const double SlideSeconds = 0.14;
    private const float GripHeight = 10;

    public enum Tab
    {
        Overview,
        Reports,
    }

    private Control _sheet = null!;
    private Control _grip = null!;
    private Control _tabs = null!;
    private Button _overviewTab = null!;
    private Button _reportsTab = null!;
    private Tween? _tween;
    private float _shown;
    private bool _dragging;

    /// <summary>Lets the UI test check layouts without catching the sheet in mid-slide.</summary>
    public static bool Instant { get; set; }

    public bool IsOpen { get; private set; }
    public Tab ActiveTab { get; private set; }
    public float HeightFraction { get; private set; }

    /// <summary>The sheet as it stands on screen, for checking what it covers.</summary>
    public Rect2 SheetRect => _sheet.GetGlobalRect();

    public string ReportsTabText => _reportsTab.Text;

    public override void _Ready()
    {
        _sheet = GetNode<Control>("Sheet");
        _grip = GetNode<Control>("Grip");
        _tabs = GetNode<Control>("Tabs");
        _overviewTab = GetNode<Button>("Tabs/OverviewTab");
        _reportsTab = GetNode<Button>("Tabs/ReportsTab");
        _overviewTab.Pressed += () => Toggle(Tab.Overview);
        _reportsTab.Pressed += () => Toggle(Tab.Reports);
        _grip.GuiInput += OnGripInput;
        HeightFraction = Mathf.Clamp(UserSettings.DrawerHeight, MinFraction, MaxFraction);
        Resized += Place;
        ShowActive();
        Place();
    }

    public void Toggle(Tab tab)
    {
        if (IsOpen && ActiveTab == tab)
            Close();
        else
            Open(tab);
    }

    public void Open(Tab tab)
    {
        ActiveTab = tab;
        IsOpen = true;
        ShowActive();
        Slide(1);
    }

    public void Close()
    {
        IsOpen = false;
        ShowActive();
        Slide(0);
    }

    /// <summary>Clamped to the limits, and remembered.</summary>
    public void SetHeightFraction(float fraction)
    {
        HeightFraction = Mathf.Clamp(fraction, MinFraction, MaxFraction);
        UserSettings.DrawerHeight = HeightFraction;
        Place();
    }

    public void SetReportsTitle(string title) => _reportsTab.Text = title;

    private void ShowActive()
    {
        _overviewTab.ButtonPressed = IsOpen && ActiveTab == Tab.Overview;
        _reportsTab.ButtonPressed = IsOpen && ActiveTab == Tab.Reports;
        GetNode<Control>("%Overview").Visible = ActiveTab == Tab.Overview;
        GetNode<Control>("%Reports").Visible = ActiveTab == Tab.Reports;
    }

    private void Slide(float to)
    {
        _tween?.Kill();
        if (Instant)
        {
            SetShown(to);
            return;
        }

        _sheet.Visible = true;
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(SetShown), _shown, to, SlideSeconds);
    }

    private void SetShown(float shown)
    {
        _shown = shown;
        Place();
    }

    private void Place()
    {
        var height = Size.Y * HeightFraction * _shown;
        _sheet.Visible = _shown > 0;
        _sheet.Position = new Vector2(0, Size.Y - height);
        _sheet.Size = new Vector2(Size.X, height);
        _grip.Visible = _shown > 0;
        _grip.Position = _sheet.Position;
        _grip.Size = new Vector2(Size.X, GripHeight);
        _tabs.Size = _tabs.GetCombinedMinimumSize();
        _tabs.Position = new Vector2((Size.X - _tabs.Size.X) / 2, _sheet.Position.Y - _tabs.Size.Y);
    }

    private void OnGripInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                _grip.AcceptEvent();
                break;
            case InputEventMouseMotion when _dragging:
                SetHeightFraction((Size.Y - GetLocalMousePosition().Y) / Size.Y);
                _grip.AcceptEvent();
                break;
        }
    }
}
