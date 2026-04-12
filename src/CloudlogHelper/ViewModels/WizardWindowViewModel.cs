using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Resources.Language;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.WizardSteps;
using MsBox.Avalonia.Enums;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public enum WizardRigBackend
{
    None,
    Hamlib,
    FLRig,
    OmniRig
}

public class WizardWindowViewModel : ViewModelBase
{
    private const int WelcomeStepIndex = 0;
    private const int BasicStepIndex = 1;
    private const int CloudlogStepIndex = 2;
    private const int ThirdPartyStepIndex = 3;
    private const int RigBackendStepIndex = 4;
    private const int TotalSteps = 5;

    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IApplicationSettingsService _settingsService;
    private readonly IMessageBoxManagerService _messageBoxManagerService;
    private readonly ILogSystemManager _logSystemManager;
    private readonly IWindowManagerService _windowManager;
    private readonly CancellationTokenSource _source = new();

    private readonly WelcomeWizardStepViewModel _welcomeStep;
    private readonly BasicWizardStepViewModel _basicStep;
    private readonly CloudlogWizardStepViewModel _cloudlogStep;
    private readonly ThirdPartyWizardStepViewModel _thirdPartyStep;
    private readonly RigBackendWizardStepViewModel _rigBackendStep;

    private bool _isApplying;

    public WizardWindowViewModel(
        IApplicationSettingsService settingsService,
        IRigBackendManager rigBackendManager,
        IMessageBoxManagerService messageBoxManagerService,
        IWindowManagerService windowManager,
        ILogSystemManager logSystemManager)
    {
        _settingsService = settingsService;
        _messageBoxManagerService = messageBoxManagerService;
        _logSystemManager = logSystemManager;
        _windowManager = windowManager;

        if (!_settingsService.TryGetDraftSettings(this, out var settings))
            throw new Exception("Draft setting instance is held by another viewmodel!");

        DraftSettings = settings!;

        _initializeLogSystems();

        _welcomeStep = new WelcomeWizardStepViewModel();
        _basicStep = new BasicWizardStepViewModel(DraftSettings.BasicSettings);
        _cloudlogStep = new CloudlogWizardStepViewModel(DraftSettings.CloudlogSettings, _source.Token);
        _thirdPartyStep = new ThirdPartyWizardStepViewModel(LogSystems, _handleLogSystemTestErrorAsync);
        _rigBackendStep = new RigBackendWizardStepViewModel(DraftSettings, rigBackendManager);

        NextCommand = ReactiveCommand.CreateFromTask(_goNextAsync);
        PreviousCommand = ReactiveCommand.Create(_goPreviousStep);
        NavigateCommand = ReactiveCommand.CreateFromTask<object?>(_navigateAsync);
        OpenGithubProfileCommand = ReactiveCommand.CreateFromTask(_openGithubProfileAsync);
        SkipWizardCommand = ReactiveCommand.CreateFromTask(_skipWizardAndPersistAsync);
        FinishCommand = ReactiveCommand.CreateFromTask(_finishWizardAsync);

        foreach (var command in new[]
                 {
                     NextCommand,
                     OpenGithubProfileCommand,
                     SkipWizardCommand,
                     FinishCommand,
                     _cloudlogStep.TestCloudlogConnectionCommand,
                     _rigBackendStep.RefreshPortCommand
                 })
        {
            command.ThrownExceptions.Subscribe(ex =>
            {
                ClassLogger.Error(ex, "Wizard command failed.");
                _ = _showErrorAsync(ex.Message);
            });
        }

        _updateStepState(WelcomeStepIndex);
        _ = _rigBackendStep.InitializeAsync();
    }

    [Reactive] public ApplicationSettings DraftSettings { get; set; }

    [Reactive] public int CurrentStepIndex { get; private set; } = WelcomeStepIndex;
    [Reactive] public int CurrentStepNumber { get; private set; } = 1;
    [Reactive] public double StepProgress { get; private set; } = 100d / TotalSteps;
    [Reactive] public bool ShowWelcomeStep { get; private set; } = true;
    [Reactive] public bool ShowBasicStep { get; private set; }
    [Reactive] public bool ShowCloudlogStep { get; private set; }
    [Reactive] public bool ShowThirdPartyStep { get; private set; }
    [Reactive] public bool ShowRigBackendStep { get; private set; }
    [Reactive] public bool CanNavigateToWelcomeStep { get; private set; } = true;
    [Reactive] public bool CanNavigateToBasicStep { get; private set; }
    [Reactive] public bool CanNavigateToCloudlogStep { get; private set; }
    [Reactive] public bool CanNavigateToThirdPartyStep { get; private set; }
    [Reactive] public bool CanNavigateToRigBackendStep { get; private set; }
    [Reactive] public bool ShowSkipButton { get; private set; } = true;
    [Reactive] public bool ShowPreviousButton { get; private set; }
    [Reactive] public bool ShowNextButton { get; private set; } = true;
    [Reactive] public bool ShowFinishButton { get; private set; }
    [Reactive] public bool CloseRequested { get; private set; }
    [Reactive] public WizardStepViewModelBase? CurrentStep { get; private set; }

    public string StepIndicatorText => $"{CurrentStepNumber}/{TotalSteps}";
    public string CurrentStepTitle => _getStepTitle(CurrentStepIndex);
    public string CurrentStepDescription => _getStepDescription(CurrentStepIndex);

    public ObservableCollection<LogSystemConfig> LogSystems { get; } = new();

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
    public ReactiveCommand<object?, Unit> NavigateCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenGithubProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipWizardCommand { get; }
    public ReactiveCommand<Unit, Unit> FinishCommand { get; }

    public void DisposeDraftIfNeeded()
    {
        try
        {
            if (!_isApplying)
            {
                _settingsService.RestoreSettings(this);
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Debug(ex, "Draft restore skipped.");
        }
        finally
        {
            _source.Cancel();
        }
    }

    private void _initializeLogSystems()
    {
        var config = _logSystemManager.ExtractLogSystemConfigBatch(DraftSettings.LogServices);
        if (config is null) return;

        LogSystems.Clear();
        foreach (var logSystemConfig in config)
        {
            LogSystems.Add(logSystemConfig);
        }
    }

    private async Task _goNextAsync()
    {
        switch (CurrentStepIndex)
        {
            case WelcomeStepIndex:
                _updateStepState(BasicStepIndex);
                return;
            case BasicStepIndex:
                if (!await _validateAndShowAsync(_basicStep.ValidateBeforeContinueAsync)) return;
                _updateStepState(CloudlogStepIndex);
                return;
            case CloudlogStepIndex:
                if (!await _validateAndShowAsync(_cloudlogStep.ValidateBeforeContinueAsync)) return;
                _updateStepState(ThirdPartyStepIndex);
                return;
            case ThirdPartyStepIndex:
                _updateStepState(RigBackendStepIndex);
                return;
        }
    }

    private async Task _navigateAsync(object? targetStepValue)
    {
        if (!int.TryParse(targetStepValue?.ToString(), out var targetStep)) return;
        if (targetStep < WelcomeStepIndex || targetStep > RigBackendStepIndex) return;
        if (targetStep == CurrentStepIndex) return;

        if (targetStep < CurrentStepIndex)
        {
            _updateStepState(targetStep);
            return;
        }

        if (targetStep == CurrentStepIndex + 1)
        {
            await _goNextAsync();
        }
    }

    private void _goPreviousStep()
    {
        if (CurrentStepIndex <= WelcomeStepIndex) return;
        _updateStepState(CurrentStepIndex - 1);
    }

    private async Task<bool> _validateAndShowAsync(
        Func<Task<WizardValidationResult>> validateHandler)
    {
        var validationResult = await validateHandler();
        if (validationResult.IsValid) return true;

        await _showErrorAsync(validationResult.ErrorMessage);
        return false;
    }

    private async Task _finishWizardAsync()
    {
        if (CurrentStepIndex != RigBackendStepIndex) return;
        if (!await _validateAndShowAsync(_rigBackendStep.ValidateBeforeFinishAsync)) return;

        DraftSettings.SkipWizard = true;
        _cloudlogStep.ApplyCloudlogAutomationByChoice();

        _settingsService.ApplySettings(this, LogSystems.ToList());
        _isApplying = true;
        _source.Cancel();
        CloseRequested = true;
    }

    private async Task _skipWizardAndPersistAsync()
    {
        if (_isApplying) return;

        _settingsService.RestoreSettings(this);
        _isApplying = true;
        _source.Cancel();
        CloseRequested = true;
        await Task.CompletedTask;
    }

    private async Task _openGithubProfileAsync()
    {
        await _windowManager.LaunchBrowser(DefaultConfigs.RepoAddress, _windowManager.GetToplevel(GetType()));
    }

    private void _updateStepState(int step)
    {
        CurrentStepIndex = step;
        CurrentStepNumber = step + 1;
        StepProgress = CurrentStepNumber * 100d / TotalSteps;
        this.RaisePropertyChanged(nameof(StepIndicatorText));
        this.RaisePropertyChanged(nameof(CurrentStepTitle));
        this.RaisePropertyChanged(nameof(CurrentStepDescription));

        ShowWelcomeStep = step == WelcomeStepIndex;
        ShowBasicStep = step == BasicStepIndex;
        ShowCloudlogStep = step == CloudlogStepIndex;
        ShowThirdPartyStep = step == ThirdPartyStepIndex;
        ShowRigBackendStep = step == RigBackendStepIndex;
        CurrentStep = step switch
        {
            WelcomeStepIndex => _welcomeStep,
            BasicStepIndex => _basicStep,
            CloudlogStepIndex => _cloudlogStep,
            ThirdPartyStepIndex => _thirdPartyStep,
            RigBackendStepIndex => _rigBackendStep,
            _ => _welcomeStep
        };

        ShowSkipButton = step == WelcomeStepIndex;
        ShowPreviousButton = step != WelcomeStepIndex;
        ShowNextButton = step != RigBackendStepIndex;
        ShowFinishButton = step == RigBackendStepIndex;

        CanNavigateToWelcomeStep = true;
        CanNavigateToBasicStep = step >= BasicStepIndex;
        CanNavigateToCloudlogStep = step >= CloudlogStepIndex;
        CanNavigateToThirdPartyStep = step >= ThirdPartyStepIndex;
        CanNavigateToRigBackendStep = step >= RigBackendStepIndex;
    }

    private static string _getStepTitle(int step)
    {
        var key = step switch
        {
            WelcomeStepIndex => Language.WizardWelcomeTitle,
            BasicStepIndex => Language.WizardBasicTitle,
            CloudlogStepIndex => Language.WizardCloudlogTitle,
            ThirdPartyStepIndex => Language.WizardThirdPartyTitle,
            RigBackendStepIndex => Language.WizardRigTitle,
            _ => Language.WizardTitle
        };

        return TranslationHelper.GetString(key);
    }

    private static string _getStepDescription(int step)
    {
        var key = step switch
        {
            WelcomeStepIndex => Language.WizardWelcomeMessage,
            BasicStepIndex => Language.WizardBasicHint,
            CloudlogStepIndex => Language.WizardCloudlogHint,
            ThirdPartyStepIndex => Language.WizardThirdPartyHint,
            RigBackendStepIndex => Language.WizardRigHint,
            _ => Language.WizardWelcomeMessage
        };

        return TranslationHelper.GetString(key);
    }

    private async Task _showErrorAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        await _messageBoxManagerService.DoShowStandardMessageboxDialogAsync(
            Icon.Error,
            ButtonEnum.Ok,
            TranslationHelper.GetString(Language.Error),
            message,
            _windowManager.GetToplevel(GetType())
            );
    }

    private Task _handleLogSystemTestErrorAsync(Exception ex)
    {
        return _showErrorAsync(ex.Message);
    }
}
