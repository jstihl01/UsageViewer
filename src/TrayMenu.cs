using System.Windows;
using System.Windows.Controls;

namespace UsageViewer;

internal static class TrayMenu
{
    internal static ContextMenu Create()
    {
        var menu=new ContextMenu {FontFamily=new("Segoe UI"),FontSize=13};
        // Follow the system menu palette, including high contrast. Panel text styles stay local.
        menu.SetResourceReference(Control.BackgroundProperty,SystemColors.MenuBrushKey);
        menu.SetResourceReference(Control.ForegroundProperty,SystemColors.MenuTextBrushKey);
        return menu;
    }
}
