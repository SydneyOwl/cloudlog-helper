using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.UserControls;
using Flurl.Http;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.WizardSteps;

public sealed class CloudlogWizardStepViewModel : WizardStepViewModelBase
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly CancellationToken _cancellationToken;

    private bool _cloudlogTestPassed;

    public CloudlogWizardStepViewModel(CloudlogSettings cloudlogSettings, CancellationToken cancellationToken) : base(2)
    {
        _cancellationToken = cancellationToken;
        CloudlogSettings = cloudlogSettings;

        ConnectCloudlog = _resolveInitialCloudlogConnectionState();
        _cloudlogTestPassed = CloudlogSettings.AvailableCloudlogStationInfo.Count > 0;
        ShowCloudlogStationIdCombobox = _cloudlogTestPassed;

        TestCloudlogConnectionCommand = ReactiveCommand.CreateFromTask(_testCloudlogConnectionAsync);
        CloudlogTestButtonUserControl = new TestButtonUserControlViewModel(TestCloudlogConnectionCommand);

        this.WhenAnyValue(x => x.ConnectCloudlog)
            .Skip(1)
            .Subscribe(enabled =>
            {
                if (enabled) return;
                DisableCloudlogAutomation();
                _resetCloudlogTestState();
            });

        this.WhenAnyValue(x => x.CloudlogSettings.CloudlogUrl, x => x.CloudlogSettings.CloudlogApiKey)
            .Skip(1)
            .Subscribe(_ => { _resetCloudlogTestState(); });
    }

    public CloudlogSettings CloudlogSettings { get; }

    [Reactive] public bool ConnectCloudlog { get; set; }

    [Reactive] public bool ShowCloudlogStationIdCombobox { get; set; }

    [Reactive] public bool ShowDetectedServerInstance { get; set; }

    [Reactive] public string DetectedServerInstanceDisplayName { get; set; } =
        TranslationHelper.GetString(LangKeys.Unknown);

    public ReactiveCommand<Unit, Unit> TestCloudlogConnectionCommand { get; }

    public TestButtonUserControlViewModel CloudlogTestButtonUserControl { get; }

    public void ApplyCloudlogAutomationByChoice()
    {
        _setCloudlogAutomation(ConnectCloudlog);
    }

    public void DisableCloudlogAutomation()
    {
        _setCloudlogAutomation(false);
    }

    public override Task<WizardValidationResult> ValidateBeforeContinueAsync()
    {
        if (!ConnectCloudlog)
        {
            DisableCloudlogAutomation();
            return Task.FromResult(WizardValidationResult.Success);
        }

        CloudlogSettings.CloudlogUrl = CloudlogSettings.CloudlogUrl.Trim();
        CloudlogSettings.CloudlogApiKey = CloudlogSettings.CloudlogApiKey.Trim();

        if (CloudlogSettings.IsCloudlogHasErrors())
        {
            return Task.FromResult(
                WizardValidationResult.Failed(TranslationHelper.GetString(LangKeys.InvalidConfiguration)));
        }

        if (!_cloudlogTestPassed)
        {
            return Task.FromResult(
                WizardValidationResult.Failed(TranslationHelper.GetString(LangKeys.WizardCloudlogTestRequired)));
        }

        if (CloudlogSettings.CloudlogStationInfo is null)
        {
            return Task.FromResult(
                WizardValidationResult.Failed(TranslationHelper.GetString(LangKeys.WizardCloudlogStationRequired)));
        }

        _setCloudlogAutomation(true);
        return Task.FromResult(WizardValidationResult.Success);
    }

    private bool _resolveInitialCloudlogConnectionState()
    {
        return !string.IsNullOrWhiteSpace(CloudlogSettings.CloudlogUrl) ||
               !string.IsNullOrWhiteSpace(CloudlogSettings.CloudlogApiKey) ||
               CloudlogSettings.AutoQSOUploadEnabled ||
               CloudlogSettings.AutoRigUploadEnabled ||
               CloudlogSettings.AutoPollStationStatus;
    }

    private void _setCloudlogAutomation(bool enabled)
    {
        CloudlogSettings.AutoQSOUploadEnabled = enabled;
        CloudlogSettings.AutoRigUploadEnabled = enabled;
        CloudlogSettings.AutoPollStationStatus = enabled;
    }

    private void _resetCloudlogTestState()
    {
        _cloudlogTestPassed = false;
        _clearCloudlogSelectionState();
        _clearDetectedServerInstanceState();
    }

    private void _clearCloudlogSelectionState()
    {
        ShowCloudlogStationIdCombobox = false;
        CloudlogSettings.AvailableCloudlogStationInfo.Clear();
        CloudlogSettings.CloudlogStationInfo = null;
    }

    private void _clearDetectedServerInstanceState()
    {
        ShowDetectedServerInstance = false;
        DetectedServerInstanceDisplayName = TranslationHelper.GetString(LangKeys.Unknown);
    }

    private static string _getServerInstanceDisplayName(ServerInstanceType instanceType)
    {
        return instanceType switch
        {
            ServerInstanceType.Cloudlog => nameof(ServerInstanceType.Cloudlog),
            ServerInstanceType.Wavelog => nameof(ServerInstanceType.Wavelog),
            _ => TranslationHelper.GetString(LangKeys.Unknown)
        };
    }

    private async Task _testCloudlogConnectionAsync()
    {
        if (!ConnectCloudlog) return;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(_resetCloudlogTestState);

            var instanceType = await CloudlogUtil.GetCurrentServerInstanceTypeAsync(
                CloudlogSettings.CloudlogUrl,
                _cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DetectedServerInstanceDisplayName = _getServerInstanceDisplayName(instanceType);
                ShowDetectedServerInstance = true;
            });

            var testResultMessage = await CloudlogUtil.TestCloudlogConnectionAsync(
                CloudlogSettings.CloudlogUrl,
                CloudlogSettings.CloudlogApiKey,
                _cancellationToken);

            if (!string.IsNullOrEmpty(testResultMessage))
            {
                throw new Exception(testResultMessage);
            }

            var stationInfo = await CloudlogUtil.GetStationInfoAsync(
                CloudlogSettings.CloudlogUrl,
                CloudlogSettings.CloudlogApiKey,
                _cancellationToken);

            if (stationInfo.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(_clearCloudlogSelectionState);
                throw new Exception(TranslationHelper.GetString(LangKeys.FailedStationInfo));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                foreach (var station in stationInfo)
                {
                    CloudlogSettings.AvailableCloudlogStationInfo.Add(station);
                }

                CloudlogSettings.CloudlogStationInfo ??= stationInfo.First();
                ShowCloudlogStationIdCombobox = true;
                _cloudlogTestPassed = true;
            });
            ClassLogger.Info($"Detected instance {instanceType}");
        }
        catch (FlurlHttpException ex) when (ex.InnerException is TaskCanceledException &&
                                            _cancellationToken.IsCancellationRequested)
        {
            ClassLogger.Trace("Wizard cloudlog test cancelled.");
        }
    }
}
