using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace Sts2LlmCoop;

internal static class AiTeammateStatus
{
    private static CanvasLayer? _layer;
    private static Label? _label;
    private static string _message = "AI teammate ready";

    public static void Set(string message)
    {
        if (_message == message) return;
        _message = message;
        if (GodotObject.IsInstanceValid(_label)) _label!.Text = message;
    }

    public static void ShowForRun()
    {
        if (AiTeammateSessionRegistry.Current == null || NRun.Instance == null) return;
        if (GodotObject.IsInstanceValid(_layer) && _layer!.GetParent() == NRun.Instance) return;
        if (GodotObject.IsInstanceValid(_layer)) _layer!.QueueFree();
        _layer = new CanvasLayer { Name = "LocalAiTeammateStatus", Layer = 90 };
        _label = new Label
        {
            Position = new Vector2(24, 100),
            Text = _message,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 0.9f, 0.6f)
        };
        _label.AddThemeFontSizeOverride("font_size", 20);
        _label.AddThemeConstantOverride("outline_size", 4);
        _layer.AddChild(_label);
        NRun.Instance.AddChild(_layer);
    }

    public static void Clear()
    {
        if (GodotObject.IsInstanceValid(_layer)) _layer!.QueueFree();
        _layer = null;
        _label = null;
        _message = "AI teammate ready";
    }
}
