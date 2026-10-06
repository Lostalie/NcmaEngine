using System.Text.Json.Serialization;

namespace Ncma.Ui;

public enum UiKind { Frame, Group, Rectangle, Text, Image, Button, Slider, TextInput, ScrollView }
public enum UiFlow { Free, Horizontal, Vertical }
public enum UiSizing { Fixed, Hug, Fill }
public enum UiTokenKind { Color, Scalar }
public readonly record struct UiColor([property: JsonRequired] float R, [property: JsonRequired] float G,
    [property: JsonRequired] float B, [property: JsonRequired] float A)
{
    public static UiColor White => new(1, 1, 1, 1);
    public static UiColor Transparent => new(0, 0, 0, 0);
}
public readonly record struct UiInsets([property: JsonRequired] float Left, [property: JsonRequired] float Top,
    [property: JsonRequired] float Right, [property: JsonRequired] float Bottom);
public sealed record UiToken([property: JsonRequired] Guid Id, [property: JsonRequired] string Name,
    [property: JsonRequired] UiTokenKind Kind, [property: JsonRequired] UiColor Color, [property: JsonRequired] float Scalar);
public sealed record UiLayout([property: JsonRequired] UiFlow Flow, [property: JsonRequired] UiSizing WidthMode,
    [property: JsonRequired] UiSizing HeightMode, [property: JsonRequired] float X, [property: JsonRequired] float Y,
    [property: JsonRequired] float Width, [property: JsonRequired] float Height,
    [property: JsonRequired] float MinWidth, [property: JsonRequired] float MinHeight,
    [property: JsonRequired] float MaxWidth, [property: JsonRequired] float MaxHeight,
    [property: JsonRequired] float AnchorX, [property: JsonRequired] float AnchorY,
    [property: JsonRequired] float Rotation, [property: JsonRequired] float Gap, [property: JsonRequired] UiInsets Padding)
{
    public static UiLayout Fixed(float x, float y, float width, float height) =>
        new(UiFlow.Free, UiSizing.Fixed, UiSizing.Fixed, x, y, width, height, 0, 0, 65536, 65536, 0, 0, 0, 0, default);
}
public sealed record UiStyle([property: JsonRequired] UiColor Fill, [property: JsonRequired] UiColor Foreground,
    [property: JsonRequired] float Opacity, [property: JsonRequired] float CornerRadius,
    [property: JsonRequired] bool Clip, [property: JsonRequired] Guid FillToken,
    [property: JsonRequired] Guid ForegroundToken, [property: JsonRequired] Guid OpacityToken)
{
    public static UiStyle Default => new(UiColor.Transparent, UiColor.White, 1, 0, false, Guid.Empty, Guid.Empty, Guid.Empty);
}
public sealed record UiElement([property: JsonRequired] Guid Id, [property: JsonRequired] Guid Parent,
    [property: JsonRequired] int Order, [property: JsonRequired] string Name, [property: JsonRequired] UiKind Kind,
    [property: JsonRequired] bool Visible, [property: JsonRequired] bool Enabled, [property: JsonRequired] bool Locked,
    [property: JsonRequired] UiLayout Layout, [property: JsonRequired] UiStyle Style,
    [property: JsonRequired] string Text, [property: JsonRequired] Guid Font, [property: JsonRequired] float FontSize,
    [property: JsonRequired] Guid Image, [property: JsonRequired] string Action)
{
    public static UiElement Create(Guid id, string name, UiKind kind, Guid parent = default) =>
        new(id, parent, 0, name, kind, true, true, false, UiLayout.Fixed(0, 0, 100, 30), UiStyle.Default, "", Guid.Empty, 16, Guid.Empty, "");
}
public sealed record UiDefinition([property: JsonRequired] int Version, [property: JsonRequired] Guid AssetId,
    [property: JsonRequired] string Name, [property: JsonRequired] Guid Root,
    [property: JsonRequired] UiElement[] Elements, [property: JsonRequired] UiToken[] Tokens)
{
    public static UiDefinition Create(Guid assetId, string name)
    {
        Guid root = Guid.NewGuid();
        return new(1, assetId, name, root, [UiElement.Create(root, "Root", UiKind.Frame) with {
            Layout = UiLayout.Fixed(0, 0, 1280, 720) with { WidthMode = UiSizing.Fill, HeightMode = UiSizing.Fill } }], []);
    }
}
