using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CloudlogHelper.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace CloudlogHelper.Services;

public class WindowManagerService : IWindowManagerService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly List<WeakReference<Window>> _windows = new();
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly IServiceProvider provider;

    public WindowManagerService(IServiceProvider prov,
        IClassicDesktopStyleApplicationLifetime desk)
    {
        provider = prov;
        desktop = desk;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public void Track(Window window)
    {
        AutoRemove();
        ClassLogger.Trace($"Tracking window {window.DataContext?.GetType()}.");
        var reference = new WeakReference<Window>(window);
        _windows.Add(reference);

        window.Closed += OnWindowClosed;
        return;

        void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is not Window closedWindow) return;
            ClassLogger.Trace($"Window {window.DataContext?.GetType()} released.");
            closedWindow.Closed -= OnWindowClosed;
            _windows.RemoveAll(wr => !wr.TryGetTarget(out var w) || w == closedWindow);
        }
    }

    public bool TryGetWindow(Type wType, out Window? targetWindow)
    {
        AutoRemove();
        foreach (var weakRef in _windows)
            if (weakRef.TryGetTarget(out var window) && window.GetType() == wType)
            {
                targetWindow = window;
                return true;
            }

        targetWindow = null;
        return false;
    }

    public Task CreateOrShowWindowByVm(Type vmType)
    {
        var finalName = vmType!.FullName!.Split(".").Last();
        var pFinalName = finalName.Replace("ViewModel", "");

        var viewPath = vmType!.FullName!.Replace(finalName, "")
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        viewPath += pFinalName;


        var winType = Type.GetType(viewPath);

        if (winType == null) throw new Exception($"Windows not found for {viewPath}");

        if (TryGetWindow(winType, out var target))
        {
            target!.Show();
            target.Activate();
            return Task.CompletedTask;
        }

        //
        var topLevel = desktop.MainWindow;
        if (topLevel is Window window)
        {
            var wd = (Window)Activator.CreateInstance(winType)!;
            // find service...
            wd.DataContext = provider.GetRequiredService(vmType);
            // wd.DataContext = provider.GetRequiredService
            Track(wd);
            return wd.ShowDialog(window);
        }

        return Task.CompletedTask;
    }

    private void AutoRemove()
    {
        _windows.RemoveAll(wr => !wr.TryGetTarget(out var res));
    }
}