using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Services;

public class ClipboardService : IClipboardService, IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;

    public ClipboardService(IClassicDesktopStyleApplicationLifetime topLevel)
    {
        _desktop = topLevel;
    }

    public Task<string?> GetTextAsync()
    {
        return _desktop.MainWindow!.Clipboard!.GetTextAsync();
    }

    public Task SetTextAsync(string? text)
    {
        return _desktop.MainWindow!.Clipboard!.SetTextAsync(text);
    }

    public Task ClearAsync()
    {
        return _desktop.MainWindow!.Clipboard!.ClearAsync();
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}