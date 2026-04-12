using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Resources.Language;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.WizardSteps;

public sealed class RigBackendWizardStepViewModel : WizardStepViewModelBase
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly ApplicationSettings _draftSettings;
    private readonly IRigBackendManager _rigBackendManager;

    public RigBackendWizardStepViewModel(
        ApplicationSettings draftSettings,
        IRigBackendManager rigBackendManager) : base(4)
    {
        _draftSettings = draftSettings;
        _rigBackendManager = rigBackendManager;

        RefreshPortCommand = ReactiveCommand.CreateFromTask(_refreshPortAsync);

        this.WhenAnyValue(x => x.SelectedRigBackendIndex)
            .Skip(1)
            .Subscribe(index => _applyRigBackendSelection(_backendFromIndex(index)));

        _setInitialRigBackendSelection();
    }

    public ApplicationSettings DraftSettings => _draftSettings;

    [Reactive] public bool HamlibInited { get; private set; } = true;
    [Reactive] public bool OmniRigInited { get; private set; } = true;
    [Reactive] public bool ShowHamlibSettings { get; private set; }
    [Reactive] public bool ShowFlRigSettings { get; private set; }
    [Reactive] public bool ShowOmniRigSettings { get; private set; }
    [Reactive] public List<string> Ports { get; private set; } = new();
    [Reactive] public List<RigInfo> SupportedHamlibModels { get; private set; } = new();
    [Reactive] public string HamlibVersion { get; private set; } = "Unknown hamlib version";
    [Reactive] public int SelectedRigBackendIndex { get; set; }

    public ReactiveCommand<Unit, Unit> RefreshPortCommand { get; }

    public async Task InitializeAsync()
    {
        await _refreshPortAsync();
        await _initializeHamlibAsync();
        await _initializeOmniRigAsync();
    }

    public override Task<WizardValidationResult> ValidateBeforeFinishAsync()
    {
        var backend = _backendFromIndex(SelectedRigBackendIndex);
        switch (backend)
        {
            case WizardRigBackend.Hamlib:
                if (!HamlibInited || _draftSettings.HamlibSettings.IsHamlibHasErrors())
                {
                    return Task.FromResult(
                        WizardValidationResult.Failed(TranslationHelper.GetString(Language.ConfigureHamlibFirst)));
                }

                break;
            case WizardRigBackend.FLRig:
                if (_draftSettings.FLRigSettings.IsFLRigHasErrors())
                {
                    return Task.FromResult(
                        WizardValidationResult.Failed(TranslationHelper.GetString(Language.ConfigureFlRigFirst)));
                }

                break;
            case WizardRigBackend.OmniRig:
                if (!OmniRigInited)
                {
                    return Task.FromResult(
                        WizardValidationResult.Failed(TranslationHelper.GetString(Language.OmniRigInitFailed)));
                }

                if (_draftSettings.OmniRigSettings.IsOmniRigHasErrors())
                {
                    return Task.FromResult(
                        WizardValidationResult.Failed(TranslationHelper.GetString(Language.ConfigureOmniRigFirst)));
                }

                break;
        }

        return Task.FromResult(WizardValidationResult.Success);
    }

    private Task _refreshPortAsync()
    {
        Ports = SerialPort.GetPortNames().OrderBy(x => x).ToList();
        if (string.IsNullOrWhiteSpace(_draftSettings.HamlibSettings.SelectedPort) && Ports.Count > 0)
        {
            _draftSettings.HamlibSettings.SelectedPort = Ports.First();
        }

        return Task.CompletedTask;
    }

    private async Task _initializeHamlibAsync()
    {
        try
        {
            HamlibVersion = await _rigBackendManager
                .GetServiceByName(RigBackendServiceEnum.Hamlib).GetServiceVersion();

            var options = await _rigBackendManager
                .GetServiceByName(RigBackendServiceEnum.Hamlib).GetSupportedRigModels();

            SupportedHamlibModels = options
                .OrderBy(x => x.Model)
                .ToList();

            var oldSelection = _draftSettings.HamlibSettings.SelectedRigInfo;
            _draftSettings.HamlibSettings.SelectedRigInfo = oldSelection is null
                ? null
                : SupportedHamlibModels.FirstOrDefault(x => x.Id == oldSelection.Id);

            if (_draftSettings.HamlibSettings.SelectedRigInfo is null && SupportedHamlibModels.Count > 0)
            {
                _draftSettings.HamlibSettings.SelectedRigInfo = SupportedHamlibModels.First();
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { HamlibInited = false; });
            ClassLogger.Error(ex, "Failed to init hamlib in wizard.");
        }
    }

    private async Task _initializeOmniRigAsync()
    {
        try
        {
            if (VersionInfo.BuildType == "AOT" || !OperatingSystem.IsWindows())
            {
                await Dispatcher.UIThread.InvokeAsync(() => { OmniRigInited = false; });
                return;
            }

            var omniRigType = Type.GetTypeFromProgID(DefaultConfigs.OmniRigEngineProgId);
            OmniRigInited = omniRigType is not null;
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { OmniRigInited = false; });
            ClassLogger.Error(ex, "Failed to init omnirig in wizard.");
        }
    }

    private void _setInitialRigBackendSelection()
    {
        var initial = WizardRigBackend.None;
        if (_draftSettings.HamlibSettings.PollAllowed) initial = WizardRigBackend.Hamlib;
        else if (_draftSettings.FLRigSettings.PollAllowed) initial = WizardRigBackend.FLRig;
        else if (_draftSettings.OmniRigSettings.PollAllowed) initial = WizardRigBackend.OmniRig;

        SelectedRigBackendIndex = _indexFromBackend(initial);
        _applyRigBackendSelection(initial);
    }

    private void _applyRigBackendSelection(WizardRigBackend backend)
    {
        ShowHamlibSettings = backend == WizardRigBackend.Hamlib;
        ShowFlRigSettings = backend == WizardRigBackend.FLRig;
        ShowOmniRigSettings = backend == WizardRigBackend.OmniRig;

        _draftSettings.HamlibSettings.PollAllowed = backend == WizardRigBackend.Hamlib;
        _draftSettings.FLRigSettings.PollAllowed = backend == WizardRigBackend.FLRig;
        _draftSettings.OmniRigSettings.PollAllowed = backend == WizardRigBackend.OmniRig;

        if (backend == WizardRigBackend.Hamlib)
        {
            if (_draftSettings.HamlibSettings.SelectedRigInfo is null && SupportedHamlibModels.Count > 0)
            {
                _draftSettings.HamlibSettings.SelectedRigInfo = SupportedHamlibModels.First();
            }

            if (string.IsNullOrWhiteSpace(_draftSettings.HamlibSettings.SelectedPort) && Ports.Count > 0)
            {
                _draftSettings.HamlibSettings.SelectedPort = Ports.First();
            }
        }
    }

    private static WizardRigBackend _backendFromIndex(int index)
    {
        return index switch
        {
            1 => WizardRigBackend.Hamlib,
            2 => WizardRigBackend.FLRig,
            3 => WizardRigBackend.OmniRig,
            _ => WizardRigBackend.None
        };
    }

    private static int _indexFromBackend(WizardRigBackend backend)
    {
        return backend switch
        {
            WizardRigBackend.Hamlib => 1,
            WizardRigBackend.FLRig => 2,
            WizardRigBackend.OmniRig => 3,
            _ => 0
        };
    }
}
