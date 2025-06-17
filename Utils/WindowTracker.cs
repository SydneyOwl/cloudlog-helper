using System;
using System.Collections.Generic;
using Avalonia.Controls;
using NLog;

namespace CloudlogHelper.Utils;

/// <summary>
///     Make sure same windows won't be created.
/// </summary>
public class WindowTracker
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly List<WeakReference<Window>> _windows = new();

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

    private void AutoRemove()
    {
        _windows.RemoveAll(wr => !wr.TryGetTarget(out var res));
    }
}