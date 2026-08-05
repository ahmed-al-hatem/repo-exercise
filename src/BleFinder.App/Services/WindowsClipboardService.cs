using System.Windows;
using BleFinder.Core.Services;

namespace BleFinder.App.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
    }
}
