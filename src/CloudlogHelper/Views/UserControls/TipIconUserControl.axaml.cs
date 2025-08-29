using Avalonia;
using Avalonia.Controls;

namespace CloudlogHelper.Views.UserControls;

public partial class TipIconUserControl : UserControl
{
    public static readonly StyledProperty<string> TooltipTextProperty =
        AvaloniaProperty.Register<TipIconUserControl, string>(nameof(TooltipText));

    public TipIconUserControl()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string TooltipText
    {
        get => GetValue(TooltipTextProperty);
        set => SetValue(TooltipTextProperty, value);
    }
}