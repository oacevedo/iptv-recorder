using System.Windows;

namespace IptvRecorder;

/// <summary>
/// Icono de un botón, indicado como carácter de la tipografía "Segoe MDL2 Assets" que
/// viene con Windows. Se usa así: <c>local:Icon.Glyph="&#38;#xE713;"</c>. La plantilla
/// compartida en App.xaml lo dibuja delante del texto, y lo oculta si está vacío.
/// </summary>
public static class Icon
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached(
            "Glyph", typeof(string), typeof(Icon), new PropertyMetadata(""));

    public static void SetGlyph(DependencyObject element, string value)
        => element.SetValue(GlyphProperty, value);

    public static string GetGlyph(DependencyObject element)
        => (string)element.GetValue(GlyphProperty);
}
