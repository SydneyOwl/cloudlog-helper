using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;

namespace CloudlogHelper.Views;

public partial class FloatingWindow : ReactiveWindow<FloatingWindowViewModel>
{
    private bool _isResizing;
    private double _startHeight;
    private Point _startPoint;

    public FloatingWindow()
    {
        InitializeComponent();

        PointerPressed += (s, e) =>
        {
            if (_isResizing) return;
            BeginMoveDrag(e);
        };
    }


    private void BottomBorder_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizing = true;
            _startPoint = e.GetPosition(this);
            _startHeight = Height;
        }
    }

    private void BottomBorder_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_isResizing)
        {
            var currentPoint = e.GetPosition(this);
            var deltaY = currentPoint.Y - _startPoint.Y;

            Height = Math.Max(_startHeight + deltaY, 100);
        }
    }

    private void BottomBorder_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        _isResizing = false;
    }

    private void TransparencyMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var transparencyText = menuItem.Header!.ToString()!.Replace("%", "");
        if (double.TryParse(transparencyText, out var percentage)) Opacity = percentage / 100.0;
    }
}