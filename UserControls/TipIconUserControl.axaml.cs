using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.UserControls;

public partial class TipIconUserControl : UserControl
{
    public static readonly StyledProperty<string> TooltipTextProperty =
        AvaloniaProperty.Register<TipIconUserControl, string>(nameof(TooltipText));

    public string TooltipText
    {
        get => GetValue(TooltipTextProperty);
        set => SetValue(TooltipTextProperty, value);
    }

    public TipIconUserControl()
    {
        InitializeComponent();
        DataContext = this;
    }
}