using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using Flurl.Http;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views.UserControls;

public class LogSystemCard : UserControl
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly CancellationTokenSource _cancellationTokenSource;

    public LogSystemCard()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        
        MessageBus.Current.Listen<SettingsChanged>()
            .Subscribe(res =>
            {
                if (res.Part == ChangedPart.NothingJustClosed)
                    _cancellationTokenSource.Cancel();
            });
            
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var stackPanel = new StackPanel { Spacing = 15 };

        var title = new TextBlock
        {
            Text = TranslationHelper.GetString("thirdpartylogsys"),
            Classes = { "section-title" }
        };
        stackPanel.Children.Add(title);

        var itemsControl = new ItemsControl();
        itemsControl.Bind(ItemsControl.ItemsSourceProperty, new Binding("LogSystems"));
        itemsControl.ItemTemplate = CreateItemTemplate();

        stackPanel.Children.Add(itemsControl);
        Content = stackPanel;
    }

    private IDataTemplate CreateItemTemplate() => 
        new FuncDataTemplate<LogSystemConfig>((config, _) => CreateConfigCard(config));

    private Border CreateConfigCard(LogSystemConfig config)
    {
        var border = new Border
        {
            Classes = { "subsection-card" },
            Padding = new Thickness(12),
            Margin = new Thickness(5, 5, 5, 10)
        };

        var innerStack = new StackPanel { Spacing = 10 };

        var header = new TextBlock
        {
            Text = config.DisplayName,
            Classes = { "subsection-title" }
        };
        innerStack.Children.Add(header);

        var grid = CreateConfigGrid(config);
        innerStack.Children.Add(grid);

        border.Child = innerStack;
        return border;
    }

    private Grid CreateConfigGrid(LogSystemConfig config)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            RowDefinitions = new RowDefinitions(),
            RowSpacing = 5
        };

        AddFieldControls(grid, config);
        AddUploadCheckbox(grid, config);
        AddTestButton(grid, config);

        return grid;
    }

    private void AddFieldControls(Grid grid, LogSystemConfig config)
    {
        for (var i = 0; i < config.Fields.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddFieldLabel(grid, config.Fields[i], i);
            AddInputControl(grid, config.Fields[i], i);
            AddHelpIcon(grid, config.Fields[i], i);
        }
    }

    private void AddFieldLabel(Grid grid, LogSystemField field, int row)
    {
        var label = new TextBlock
        {
            Text = TranslationHelper.GetString(field.DisplayNameLangKey),
            Classes = { "setting-label" }
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }

    private void AddInputControl(Grid grid, LogSystemField field, int row)
    {
        var inputControl = CreateInputControl(field);
        Grid.SetColumn(inputControl, 1);
        Grid.SetRow(inputControl, row);
        grid.Children.Add(inputControl);
    }

    private Control CreateInputControl(LogSystemField field)
    {
        return field.Type switch
        {
            FieldType.Text => CreateTextInput(field),
            FieldType.Password => CreatePasswordInput(field),
            FieldType.CheckBox => CreateCheckboxInput(field),
            FieldType.ComboBox => CreateComboBoxInput(field),
            FieldType.FilePicker => CreateFilePickerInput(field),
            _ => new TextBox()
        };
    }

    private TextBox CreateTextInput(LogSystemField field)
    {
        return new TextBox
        {
            Classes = { "setting-control" },
            DataContext = field,
            Watermark = field.Watermark ?? string.Empty,
            [!TextBox.TextProperty] = new Binding("Value")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
    }

    private TextBox CreatePasswordInput(LogSystemField field)
    {
        return new TextBox
        {
            Classes = { "setting-control" },
            PasswordChar = '•',
            DataContext = field,
            [!TextBox.TextProperty] = new Binding("Value")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
    }

    private CheckBox CreateCheckboxInput(LogSystemField field)
    {
        return new CheckBox
        {
            Classes = { "setting-control" },
            DataContext = field,
            [!CheckBox.IsCheckedProperty] = new Binding("Value")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
    }

    private ComboBox CreateComboBoxInput(LogSystemField field)
    {
        return new ComboBox
        {
            Classes = { "setting-control" },
            DataContext = field,
            ItemsSource = field.Selections,
            [!SelectingItemsControl.SelectedValueProperty] = new Binding("Value")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
    }

    private FilePickerTextboxUserControl CreateFilePickerInput(LogSystemField field)
    {
        var viewModel = new FilePickerTextboxUserControlViewModel
        {
            SelectedFilePath = field.Value as string ?? string.Empty
        };

        viewModel.WhenAnyValue(vm => vm.SelectedFilePath)
                .BindTo(field, f => f.Value);

        return new FilePickerTextboxUserControl
        {
            Classes = { "setting-control" },
            DataContext = viewModel
        };
    }

    private void AddHelpIcon(Grid grid, LogSystemField field, int row)
    {
        if (string.IsNullOrEmpty(field.Description)) return;

        var helpIconControl = new TipIconUserControl
        {
            Margin = new Thickness(5, 0, 0, 0),
            TooltipText = field.Description
        };
        Grid.SetColumn(helpIconControl, 2);
        Grid.SetRow(helpIconControl, row);
        grid.Children.Add(helpIconControl);
    }

    private void AddUploadCheckbox(Grid grid, LogSystemConfig config)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var uploadCheckbox = new CheckBox
        {
            Content = TranslationHelper.GetString("autoqsoupload"),
            Classes = { "setting-label" },
            [!ToggleButton.IsCheckedProperty] = new Binding("UploadEnabled")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
        Grid.SetRow(uploadCheckbox, config.Fields.Count);
        Grid.SetColumn(uploadCheckbox, 0);
        grid.Children.Add(uploadCheckbox);
    }

    private void AddTestButton(Grid grid, LogSystemConfig config)
    {
        var testButtonViewModel = new TestButtonUserControlViewModel(
            ReactiveCommand.CreateFromTask(() => TestConnectionAsync(config)));

        var testButton = new TestButtonUserControl
        {
            Margin = new Thickness(20, 0, 0, 0),
            DataContext = testButtonViewModel
        };
        
        // Place button in the middle row of the grid
        var middleRow = config.Fields.Count / 2;
        Grid.SetColumn(testButton, 3);
        Grid.SetRow(testButton, middleRow);
        grid.Children.Add(testButton);
    }

    private async Task TestConnectionAsync(LogSystemConfig config)
    {
        try
        {
            var instance = CreateServiceInstance(config);
            var methodInfo = config.RawType.GetMethod(nameof(ThirdPartyLogService.TestConnectionAsync));
            
            await (Task)methodInfo?.Invoke(instance, new object[] { _cancellationTokenSource.Token })!;
        }
        catch (Exception ex)
        {
            await HandleTestConnectionException(ex);
            throw;
        }
    }

    private object CreateServiceInstance(LogSystemConfig config)
    {
        var instance = Activator.CreateInstance(config.RawType);
        
        foreach (var field in config.Fields)
        {
            ValidateRequiredField(field);
            SetFieldValue(config, instance, field);
        }
        
        return instance!;
    }

    private void ValidateRequiredField(LogSystemField field)
    {
        if (!field.IsRequired) return;
        
        if (field.Value == null || 
            (field.Value is string stringValue && string.IsNullOrWhiteSpace(stringValue)))
        {
            throw new ArgumentException(
                $"{TranslationHelper.GetString("fillall")}({field.PropertyName})");
        }
    }

    private void SetFieldValue(LogSystemConfig config, object instance, LogSystemField field)
    {
        var propertyInfo = config.RawType.GetProperty(field.PropertyName);
        if (propertyInfo == null) return;

        if (propertyInfo.PropertyType == typeof(bool))
        {
            propertyInfo.SetValue(instance, field.Value?.ToString() == "True");
        }
        else
        {
            propertyInfo.SetValue(instance, field.Value);
        }
    }

    private async Task HandleTestConnectionException(Exception ex)
    {
        if (ex is FlurlHttpException flurlEx && flurlEx.InnerException is TaskCanceledException)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                ClassLogger.Trace("User closed settings window. QSO service test cancelled.");
                return;
            }
        }

        var actualEx = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
        ClassLogger.Error(actualEx, "Failed to test connection");
        
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            await viewModel.Notification.SendErrorNotificationAsync(actualEx.Message);
        }
    }
}