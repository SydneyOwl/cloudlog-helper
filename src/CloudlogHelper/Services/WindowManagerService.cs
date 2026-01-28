using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CloudlogHelper.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace CloudlogHelper.Services;

internal struct WindowData
{
    public WeakReference<Window> Window { get; set; }
    public string Seq { get; set; }
}

public class WindowManagerService : IWindowManagerService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IServiceProvider _provider;
    private readonly List<WindowData> _windows = new();

    public WindowManagerService(IServiceProvider prov,
        IClassicDesktopStyleApplicationLifetime desk)
    {
        _provider = prov;
        _desktop = desk;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public Window? GetToplevel(Type vmType)
    {
        foreach (var windowData in _windows)
        {
            if (windowData.Window.TryGetTarget(out var window))
            {
                if (window.DataContext?.GetType() == vmType)
                {
                    return window;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Track a window and return uuid related to this window.
    /// </summary>
    /// <param name="window"></param>
    /// <returns></returns>
    public string Track(Window window)
    {
        AutoRemove();
        ClassLogger.Trace($"Tracking window {window.DataContext?.GetType()}.");
        var reference = new WeakReference<Window>(window);
        var seq = Guid.NewGuid().ToString();
        _windows.Add(new WindowData
        {
            Window = reference,
            Seq = seq
        });

        window.Closed += OnWindowClosed;
        return seq;

        void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is not Window closedWindow) return;
            ClassLogger.Trace($"Window {window.DataContext?.GetType()} released.");
            closedWindow.Closed -= OnWindowClosed;
            _windows.RemoveAll(wr => !wr.Window.TryGetTarget(out var w) || w == closedWindow);
        }
    }

    public async Task<T?> CreateAndShowWindowByVm<T>(Type vmType, Window? toplevel = null, bool dialog = true)
    {
        var finalName = vmType.FullName!.Split(".").Last();
        var pFinalName = finalName.Replace("ViewModel", "");

        var viewPath = vmType.FullName!.Replace(finalName, "")
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        viewPath += pFinalName;

        var winType = Type.GetType(viewPath);
        if (winType == null)
            throw new Exception($"Window not found for {viewPath}");

        if (TryGetWindow(winType, out var target))
        {
            target!.Show();
            target.Activate();
            return default;
        }

        var tl = toplevel ?? _desktop.MainWindow;
        if (tl is Window parentWindow)
        {
            var newWindow = (Window)Activator.CreateInstance(winType)!;
            newWindow.DataContext = _provider.GetRequiredService(vmType);
            Track(newWindow);

            if (dialog) return await newWindow.ShowDialog<T>(parentWindow);
            newWindow.Show();
        }

        return default;
    }

    public async Task CreateAndShowWindowByVm(Type vmType, Window? toplevel = null, bool dialog = true)
    {
        await CreateAndShowWindowByVm<object>(vmType, toplevel, dialog);
    }

    public T GetViewModelInstance<T>()
    {
        return _provider.GetRequiredService<T>();
    }

    public void CloseWindowBySeq(string seq)
    {
        GetWindowBySeq(seq)?.Close();
    }

    public async Task LaunchBrowser(string uri, Window? topLevel = null)
    {
        var tl = topLevel ?? _desktop.MainWindow;
        await tl!.Launcher.LaunchUriAsync(new Uri(uri));
    }

    public async Task LaunchDir(string path, Window? topLevel = null)
    {
        var tl = topLevel ?? _desktop.MainWindow;
        await tl!.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    public async Task<IReadOnlyList<IStorageFile?>> OpenFilePickerAsync(FilePickerOpenOptions options, Window? topLevel = null)
    {
        var tl = topLevel ?? _desktop.MainWindow;

        return await tl!.StorageProvider.OpenFilePickerAsync(options);
    }
    
    public async Task<IStorageFile?> OpenFileSaverAsync(FilePickerSaveOptions options, Window? topLevel = null)
    {
        var tl = topLevel ?? _desktop.MainWindow;

        return await tl!.StorageProvider.SaveFilePickerAsync(options);
    }

    private bool TryGetWindow(Type wType, out Window? targetWindow)
    {
        AutoRemove();
        foreach (var weakRef in _windows)
            if (weakRef.Window.TryGetTarget(out var window) && window.GetType() == wType)
            {
                targetWindow = window;
                return true;
            }

        targetWindow = null;
        return false;
    }

    private bool TryGetWindowByVm(Type vmType, out Window? targetWindow)
    {
        AutoRemove();
        foreach (var weakRef in _windows)
            if (weakRef.Window.TryGetTarget(out var window) && window.DataContext?.GetType() == vmType)
            {
                targetWindow = window;
                return true;
            }

        targetWindow = null;
        return false;
    }

    private Window? GetWindowBySeq(string seq)
    {
        AutoRemove();
        foreach (var data in _windows)
            if (data.Seq == seq && data.Window.TryGetTarget(out var res))
                return res;

        return null;
    }

    private void AutoRemove()
    {
        _windows.RemoveAll(wr => !wr.Window.TryGetTarget(out var res));
    }
}