using System;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Renames the current village. The Village tab shows a button, which opens a field with the rules checked as you type.</summary>
public partial class RenamePanel : VBoxContainer
{
    private Button _edit = null!;
    private Control _form = null!;
    private LineEdit _field = null!;
    private Button _save = null!;
    private Label _reason = null!;

    private IGameSession? _session;
    private int? _villageId;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);

        _edit = new Button { Name = "Edit", Text = L.T("rename.edit"), ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None };
        _edit.Pressed += Open;
        AddChild(_edit);

        var form = new HBoxContainer { Name = "Form", Visible = false };
        form.AddThemeConstantOverride("separation", 6);
        _form = form;
        _field = new LineEdit { Name = "Field", SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = L.T("rename.hint") };
        _field.TextChanged += _ => Check();
        _field.TextSubmitted += _ => Save();
        form.AddChild(_field);
        _save = new Button { Name = "Save", Text = L.T("rename.save"), FocusMode = FocusModeEnum.None };
        _save.Pressed += Save;
        form.AddChild(_save);
        var cancel = new Button { Name = "Cancel", Text = L.T("rename.cancel"), ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None };
        cancel.Pressed += Close;
        form.AddChild(cancel);
        AddChild(form);

        _reason = new Label { Name = "Reason", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _reason.AddThemeColorOverride("font_color", UiContent.Danger);
        AddChild(_reason);
    }

    public void Refresh(IGameSession session, Village village)
    {
        _session = session;
        if (_villageId != village.Id)
            Close();
        _villageId = village.Id;

        if (_form.Visible)
            Check();
    }

    private Village? Current => _session is not null && _villageId is { } id ? _session.State.Villages[id] : null;

    private void Open()
    {
        if (Current is not { } village || _session is null)
            return;

        _field.MaxLength = _session.Data.Game.VillageNames.MaxLength;
        _field.Text = village.Name;
        _edit.Visible = false;
        _form.Visible = true;
        _field.GrabFocus();
        _field.SelectAll();
        Check();
    }

    private void Close()
    {
        _edit.Visible = true;
        _form.Visible = false;
        _reason.Visible = false;
        _field.ReleaseFocus();
    }

    private void Check()
    {
        if (Current is not { } village || _session is null)
            return;

        var rejection = VillageNameRules.Rejection(_session.State, _session.Data, village, _field.Text);

        // Opening the field isn't a mistake, so its current name isn't flagged until something is typed.
        var unchanged = VillageNameRules.Clean(_field.Text) == village.Name;
        _save.Disabled = rejection is not null;
        _reason.Visible = rejection is not null && !unchanged;
        if (rejection is { } reason)
            _reason.Text = UiContent.RejectionText(reason);
    }

    private void Save()
    {
        if (Current is not { } village || _session is null || _save.Disabled)
            return;

        _session.Send(new RenameVillageCommand(village.Id, _field.Text));
        Close();
    }
}
