using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views.UserControls;

public class LogSystemCard : UserControl
{    
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public LogSystemCard()
    {
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
        itemsControl.ItemTemplate = new FuncDataTemplate<LogSystemConfig>((config, _) =>
        {
            var border = new Border { Classes = { "subsection-card" },
                Padding = new Thickness(12),
                Margin = new Thickness(5,5,5,10) };
            var innerStack = new StackPanel { Spacing = 10 };
            
            var header = new TextBlock 
            { 
                Text = config.DisplayName,
                Classes = { "subsection-title" }
            };
            innerStack.Children.Add(header);

            var grid = new Grid 
            { 
                ColumnDefinitions = 
                { 
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                RowDefinitions = new RowDefinitions(),
                RowSpacing = 5
            };

            for (int i = 0; i < config.Fields.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                var label = new TextBlock 
                { 
                    Text = config.Fields[i].DisplayName,
                    Classes = { "setting-label" }
                };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                Control inputControl = config.Fields[i].Type switch
                {
                    FieldType.Text => new TextBox 
                    { 
                        Classes = { "setting-control" },
                        DataContext = config.Fields[i], 
                        Watermark = config.Fields[i].Watermark ?? string.Empty,
                        [!TextBox.TextProperty] = new Binding("Value")
                        {
                            Mode = BindingMode.TwoWay,
                            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                        }
                    },
                    FieldType.Password => new TextBox 
                    { 
                        Classes = { "setting-control" },
                        PasswordChar = '•',
                        DataContext = config.Fields[i],
                        [!TextBox.TextProperty] = new Binding("Value") 
                        {
                            Mode = BindingMode.TwoWay,
                            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                        }
                    },
                    _ => new TextBox()
                };

                Grid.SetColumn(inputControl, 1);
                Grid.SetRow(inputControl, i);
                grid.Children.Add(inputControl);
                
                
                // help button
                var cont = config.Fields[i].Description;
                if (!string.IsNullOrEmpty(cont))
                {
                    var helpIconControl = new TipIconUserControl
                    {
                        Margin = new Thickness(5,0,0,0),
                        TooltipText = cont
                    };
                    Grid.SetColumn(helpIconControl, 2);
                    Grid.SetRow(helpIconControl, i);
                    grid.Children.Add(helpIconControl);
                }
            }
            
            // add upload enabled checkbox
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var uploadCheckbox = new CheckBox()
            {
                Content =TranslationHelper.GetString("autoqsoupload"),
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

            var testButtonViewModel = new TestButtonUserControlViewModel();
            var methodInfo = config.RawType.GetMethod(nameof(ThirdPartyLogService.TestConnectionAsync));
            testButtonViewModel.SetTestButtonCommand( ReactiveCommand.CreateFromTask(async () =>
            {
                try
                {
                    // init a new instance and give values...
                    var instance = Activator.CreateInstance(config.RawType);
                    foreach (var logSystemField in config.Fields)
                    {
                        // check required fields
                        if (logSystemField.IsRequired && string.IsNullOrWhiteSpace(logSystemField.Value))
                        {
                            throw new ArgumentException(TranslationHelper.GetString("fillall"));
                        }
                        config.RawType
                            .GetProperty(logSystemField.PropertyName)!
                            .SetValue(instance, logSystemField.Value);
                    }
                    
                    await (Task)methodInfo?.Invoke(instance, null)!;
                }
                catch (Exception ex)
                {
                    var actualEx = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
                    ClassLogger.Error(ex, "failed to test connection");
                    await ((SettingsWindowViewModel)DataContext!).NotificationManager.SendErrorNotificationAsync(
                        actualEx.Message);
                    throw;
                }
            }));
            var testButton = new TestButtonUserControl
            {
                Margin = new Thickness(20, 0, 0, 0),
                DataContext = testButtonViewModel
            };
            Grid.SetColumn(testButton, 3);
            Grid.SetRow(testButton, (config.Fields.Count) / 2 );
            grid.Children.Add(testButton);

            innerStack.Children.Add(grid);
            border.Child = innerStack;
            
            return border;
        });

        stackPanel.Children.Add(itemsControl);
        Content = stackPanel;
    }
}