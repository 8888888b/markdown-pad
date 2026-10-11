using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownPad.Controls;

/// <summary>
/// The draggable divider between the editor and the preview pane.
/// <see cref="Microsoft.UI.Xaml.UIElement.ProtectedCursor"/> is protected, so it
/// is surfaced here for the code-behind to assign the horizontal resize cursor.
/// </summary>
public sealed class SplitterHandle : ContentControl
{
    /// <summary>The horizontal resize cursor shown while hovering the divider.</summary>
    public static InputCursor ResizeCursor { get; } =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    public InputCursor? Cursor
    {
        get => ProtectedCursor;
        set => ProtectedCursor = value;
    }
}
