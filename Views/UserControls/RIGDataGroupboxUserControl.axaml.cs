using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views.UserControls;

public partial class RIGDataGroupboxUserControl : ReactiveUserControl<RIGDataGroupboxUserControlViewModel>
{ 
    public RIGDataGroupboxUserControl()
    {
        InitializeComponent();
    }
}