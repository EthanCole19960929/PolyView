using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PolyView;

public enum ViewportShading
{
    Wireframe,
    Solid,
    MaterialPreview,
    Rendered,
}

/// <summary>Blender 4.2 viewport shading switch (top-right of the 3D view): Wireframe / Solid / Material Preview / Rendered.</summary>
public sealed partial class ModelViewport
{
    readonly ToggleButton[] _shadingButtons = new ToggleButton[4];
    ViewportShading _shading = ViewportShading.Solid;
    ViewportShading _lastNonWire = ViewportShading.Solid;

    public ViewportShading Shading
    {
        get => _shading;
        set
        {
            _shading = value;
            if (value != ViewportShading.Wireframe)
            {
                _lastNonWire = value;
            }

            Wireframe = value == ViewportShading.Wireframe;
            for (int i = 0; i < _shadingButtons.Length; i++)
            {
                if (_shadingButtons[i] != null)
                {
                    _shadingButtons[i].IsChecked = i == (int)value;
                }
            }

            Invalidate();
        }
    }

    /// <summary>Shift+Z: toggle wireframe and back to the previous shading mode.</summary>
    void ToggleWireframeShading() =>
        Shading = _shading == ViewportShading.Wireframe ? _lastNonWire : ViewportShading.Wireframe;

    /// <summary>Value passed to the shader in Ambient.w: 1 scene lights, 2 solid (no textures), 3 material preview.</summary>
    float ShadingCode => _shading switch
    {
        ViewportShading.Rendered => 1f,
        ViewportShading.MaterialPreview => 3f,
        _ => 2f,
    };

    UIElement BuildShadingBar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 10, 0),
        };
        for (int i = 0; i < 4; i++)
        {
            var mode = (ViewportShading)i;
            var button = new ToggleButton
            {
                Template = ToolTemplate(i == 0 ? "11,0,0,11" : i == 3 ? "0,11,11,0" : "0"),
                Content = ShadingIcon(i),
                Width = 28,
                Height = 22,
                Focusable = false,
                IsChecked = mode == _shading,
            };
            button.Click += (_, _) => Shading = mode;
            _shadingButtons[i] = button;
            bar.Children.Add(button);
        }

        ApplyShadingLanguage();
        return bar;
    }

    public void ApplyShadingLanguage()
    {
        string[] names = [UiText.ShadeWireName, UiText.ShadeSolidName, UiText.ShadeMaterialName, UiText.ShadeRenderedName];
        string[] details = [UiText.ShadeWire, UiText.ShadeSolid, UiText.ShadeMaterial, UiText.ShadeRendered];
        for (int i = 0; i < 4; i++)
        {
            if (_shadingButtons[i] == null)
            {
                continue;
            }

            _shadingButtons[i].ToolTip = CreateToolTip(names[i], details[i], i == 0 ? $"{UiText.Shortcut}: Shift+Z" : "");
            ToolTipService.SetInitialShowDelay(_shadingButtons[i], 500);
            ToolTipService.SetShowDuration(_shadingButtons[i], 60000);
        }
    }

    /// <summary>Vector sphere icons in Blender style, same stroke as the toolbar (#E6E6E6).</summary>
    static UIElement ShadingIcon(int mode)
    {
        var grid = new Grid { Width = 14, Height = 14 };
        var circle = Geometry.Parse("M7,1 A6,6 0 1 1 6.99,1 Z");
        System.Windows.Shapes.Path P(string data, bool fill, double opacity = 1) => fill
            ? new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Fill = IconWhite, Opacity = opacity }
            : new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Stroke = IconWhite, StrokeThickness = 1.2, Opacity = opacity };
        switch (mode)
        {
            case 0: // wireframe sphere
                grid.Children.Add(new System.Windows.Shapes.Path { Data = circle, Stroke = IconWhite, StrokeThickness = 1.2 });
                grid.Children.Add(P("M1,7 H13 M7,1 C3.5,3 3.5,11 7,13 M7,1 C10.5,3 10.5,11 7,13 M2,4 C5,5.5 9,5.5 12,4 M2,10 C5,8.5 9,8.5 12,10", false, 0.85));
                break;
            case 1: // solid sphere
                grid.Children.Add(new System.Windows.Shapes.Path { Data = circle, Fill = IconWhite });
                break;
            case 2: // material: half filled, checker hint
                grid.Children.Add(new System.Windows.Shapes.Path { Data = circle, Stroke = IconWhite, StrokeThickness = 1.2 });
                grid.Children.Add(P("M7,1 A6,6 0 0 0 7,13 Z M7,4 H10 V7 H7 Z M10,7 H12.5 V10 H10 Z", true, 0.9));
                break;
            default: // rendered: shaded sphere with highlight
                grid.Children.Add(new System.Windows.Shapes.Path { Data = circle, Fill = IconWhite, Opacity = 0.55 });
                grid.Children.Add(P("M7,1 A6,6 0 0 0 1,7 A6,6 0 0 1 7,4 A3,3 0 0 1 10,7 A6,6 0 0 0 7,1 Z", true));
                break;
        }

        return grid;
    }
}