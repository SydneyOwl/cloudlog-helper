using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.UserControls;
using DynamicData;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public SettingsWindowViewModel()
    {
        DraftSettings = ApplicationSettings.GetDraftInstance();

        ShowCloudlogStationIdCombobox = DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Count > 0;

        var hamlibCmd = ReactiveCommand.CreateFromTask(_testHamlib, DraftSettings.HamlibSettings.IsHamlibValid);
        HamlibTestButton.SetTestButtonCommand(hamlibCmd);

        RefreshPort = ReactiveCommand.CreateFromTask(_refreshPort);

        var cloudCmd =
            ReactiveCommand.CreateFromTask(_testCloudlogConnection, DraftSettings.CloudlogSettings.IsCloudlogValid);
        CloudlogTestButton.SetTestButtonCommand(cloudCmd);

        var clubCmd =
            ReactiveCommand.CreateFromTask(_testClublogConnection);
        ClublogTestButton.SetTestButtonCommand(clubCmd);

        var hamcqCmd =
            ReactiveCommand.CreateFromTask(_testHamCQConnection);
        HamCQTestButton.SetTestButtonCommand(hamcqCmd);
        
        var eqslCmd =
            ReactiveCommand.CreateFromTask(_testEqslConnection);
        EqslTestButton.SetTestButtonCommand(eqslCmd);

        // save or discard conf
        DiscardConf = ReactiveCommand.Create(_discardConf);
        SaveAndApplyConf = ReactiveCommand.Create(_saveAndApplyConf);

        this.WhenActivated(disposables =>
        {
            // Subscribe language change
            this.WhenAnyValue(x => x.DraftSettings.LanguageType).Subscribe(language =>
                {
                    I18NExtension.Culture = TranslationHelper.GetCultureInfo(language);
                })
                .DisposeWith(disposables);
            hamlibCmd.ThrownExceptions.Subscribe(err => NotificationManager?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            cloudCmd.ThrownExceptions.Subscribe(err =>  NotificationManager?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            clubCmd.ThrownExceptions.Subscribe(err =>  NotificationManager?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            hamcqCmd.ThrownExceptions.Subscribe(err =>  NotificationManager?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            eqslCmd.ThrownExceptions.Subscribe(err =>
                {
                    ClassLogger.Trace(err.Message);
                    NotificationManager?.SendErrorNotificationSync(err.Message);
                })
                .DisposeWith(disposables);


            RefreshPort.ThrownExceptions.Subscribe(err =>
            {
                // SerialUtil.PreSelectSerialByName is likely to fail on win8 and win7!
                ClassLogger.Debug($"RefreshHamlibData error: {err.Message}; Ignored.");
            }).DisposeWith(disposables);

            // refresh hamlib lib after fully inited
            Observable.Return(Unit.Default)
                .InvokeCommand(RefreshPort)
                .DisposeWith(disposables);
        });
        
        if (Design.IsDesignMode)return;
        _ = _initializeHamlibAsync();
        MessageBus.Current.SendMessage(new SettingsChanged
        {
            Part = ChangedPart.NothingJustOpened
        });
    }


    public ReactiveCommand<Unit, Unit> SaveAndApplyConf { get; }
    public ReactiveCommand<Unit, Unit> DiscardConf { get; }
    
    public WindowNotification NotificationManager { get; set; }
    public ApplicationSettings DraftSettings { get; set; }

    private async Task _initializeHamlibAsync()
    {
        if (App.CmdOptions.AutoUdpLogUploadOnly)return;
        var (result, output) = await RigctldUtil.StartOnetimeRigctldAsync("--version");
        // init hamlib
        if (result)
        {
            HamlibVersion = output;
        }
        else
        {
            NotificationManager?.SendErrorNotificationSync(output);
            return;
        }

        var (listResult, opt) = await RigctldUtil.StartOnetimeRigctldAsync("--list");
        if (listResult)
        {
            SupportedModels = RigctldUtil.ParseAllModelsFromRawOutput(opt)
                .OrderBy(x => x.Model)
                .ToList();

            var selection = DraftSettings.HamlibSettings.SelectedRigInfo;
            DraftSettings.HamlibSettings.SelectedRigInfo = null;
            if (selection is not null && SupportedModels.Contains(selection))
                DraftSettings.HamlibSettings.SelectedRigInfo = selection;
            HamlibInitPassed = true;
        }
        else
        {
            NotificationManager?.SendErrorNotificationSync(opt);
        }
    }

    private async Task<bool> _testClublogConnection()
    {
        if (DraftSettings.ThirdPartyLogServiceSettings.ClublogSettings.IsClublogHasErrors())
        {
            NotificationManager?.SendErrorNotificationSync(TranslationHelper.GetString("fillall"));
            return false;
        }

        var res = await ClublogUtil.TestClublogConnectionAsync(DraftSettings.ThirdPartyLogServiceSettings.ClublogSettings.ClublogCallsign,
                DraftSettings.ThirdPartyLogServiceSettings.ClublogSettings.ClublogPassword, DraftSettings.ThirdPartyLogServiceSettings.ClublogSettings.ClublogEmail);

        if (string.IsNullOrEmpty(res)) return true;
        NotificationManager?.SendErrorNotificationSync(res);
        return false;
    }

    private async Task<bool> _testHamCQConnection()
    {
        if (DraftSettings.ThirdPartyLogServiceSettings.HamCQSettings.IsHamCQHasErrors())
        {
            NotificationManager?.SendErrorNotificationSync(TranslationHelper.GetString("fillall"));
            return false;
        }

        var res = await HamCQUtil.TestHamCQConnectionAsync(DraftSettings.ThirdPartyLogServiceSettings.HamCQSettings.HamCQAPIKey);
        if (string.IsNullOrEmpty(res)) return true;
        NotificationManager?.SendErrorNotificationSync(res);
        return false;
    }

    private async Task<bool> _testCloudlogConnection()
    {
        CloudlogInfoPanel.InfoMessage = string.Empty;
        try
        {
            var msg = await CloudlogUtil.TestCloudlogConnectionAsync(DraftSettings.CloudlogSettings.CloudlogUrl,
                DraftSettings.CloudlogSettings.CloudlogApiKey);

            if (!string.IsNullOrEmpty(msg))
            {
                NotificationManager?.SendErrorNotificationSync(msg);
                return false;
            }

            var stationInfo = await CloudlogUtil.GetStationInfoAsync(DraftSettings.CloudlogSettings.CloudlogUrl,
                DraftSettings.CloudlogSettings.CloudlogApiKey);
            if (stationInfo.Count == 0)
            {
                NotificationManager?.SendErrorNotificationSync(TranslationHelper.GetString("failedstationinfo"));
                DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                DraftSettings.CloudlogSettings.CloudlogStationInfo = null;
                ShowCloudlogStationIdCombobox = false;
                return false;
            }

            var oldVal = DraftSettings.CloudlogSettings.CloudlogStationInfo;

            DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
            DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.AddRange(stationInfo);
            ShowCloudlogStationIdCombobox = true;

            if (string.IsNullOrEmpty(DraftSettings.CloudlogSettings.CloudlogStationInfo?.StationId))
            {
                DraftSettings.CloudlogSettings.CloudlogStationInfo = stationInfo[0];
            }
            else
            {
                DraftSettings.CloudlogSettings.CloudlogStationInfo = null;
                DraftSettings.CloudlogSettings.CloudlogStationInfo = oldVal;
            }

            var instType =
                await CloudlogUtil.GetCurrentServerInstanceTypeAsync(DraftSettings.CloudlogSettings.CloudlogUrl);
            // instanceuncompitable
            if (instType != ServerInstanceType.Cloudlog)
                CloudlogInfoPanel.InfoMessage = TranslationHelper.GetString("instanceuncompitable")
                    .Replace("{replace01}", instType.ToString());

            return true;
        }
        catch (Exception e)
        {
            NotificationManager?.SendErrorNotificationSync(e.Message);
        }

        return false;
    }

    private async Task<bool> _testEqslConnection()
    {
        if (DraftSettings.ThirdPartyLogServiceSettings.EqslSettings.IsEqslHasErrors())
        {
            NotificationManager?.SendErrorNotificationSync(TranslationHelper.GetString("fillall"));
            return false;
        }

        var res = await EqslUtil.TestEqslConnectionAsync(
            DraftSettings.ThirdPartyLogServiceSettings.EqslSettings.Username,
            DraftSettings.ThirdPartyLogServiceSettings.EqslSettings.Password,
            DraftSettings.ThirdPartyLogServiceSettings.EqslSettings.QthNickname);
        
        if (string.IsNullOrEmpty(res)) return true;
        NotificationManager?.SendErrorNotificationSync(res);
        return false;
    }

    private (string, int) _getRigctldIpAndPort()
    {
        // parse addr
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;

        if (DraftSettings.HamlibSettings.UseExternalRigctld)
            (ip, port) = IPAddrUtil.ParseAddress(DraftSettings.HamlibSettings.ExternalRigctldHostAddress);

        if (DraftSettings.HamlibSettings.UseRigAdvanced &&
            !string.IsNullOrEmpty(DraftSettings.HamlibSettings.OverrideCommandlineArg))
        {
            var matchPort = Regex.Match(DraftSettings.HamlibSettings.OverrideCommandlineArg, @"-t\s+(\S+)");
            if (matchPort.Success)
            {
                port = int.Parse(matchPort.Groups[1].Value);
                ClassLogger.Debug($"Match port from args: {port}");
            }
            else
            {
                throw new Exception(TranslationHelper.GetString("failextractinfo"));
            }
        }

        return (ip, port);
    }

    private async Task<bool> _testHamlib()
    {
        var (ip, port) = _getRigctldIpAndPort();
        if (DraftSettings.HamlibSettings is { UseExternalRigctld: false, SelectedRigInfo.Id: not null })
        {
            var defaultArgs = RigctldUtil.GenerateRigctldCmdArgs(DraftSettings.HamlibSettings.SelectedRigInfo.Id,
                DraftSettings.HamlibSettings.SelectedPort);

            if (DraftSettings.HamlibSettings.UseRigAdvanced)
            {
                if (string.IsNullOrEmpty(DraftSettings.HamlibSettings.OverrideCommandlineArg))
                    defaultArgs = RigctldUtil.GenerateRigctldCmdArgs(DraftSettings.HamlibSettings.SelectedRigInfo.Id,
                        DraftSettings.HamlibSettings.SelectedPort,
                        DraftSettings.HamlibSettings.DisablePTT,
                        DraftSettings.HamlibSettings.AllowExternalControl);
                else
                    defaultArgs = DraftSettings.HamlibSettings.OverrideCommandlineArg;
            }

            var (res, des) =
                await RigctldUtil.RestartRigctldBackgroundProcessAsync(defaultArgs);
            if (!res)
            {
                NotificationManager?.SendErrorNotificationSync(des);
                return false;
            }
        }
        else
        {
            RigctldUtil.TerminateBackgroundProcess();
        }

        // send freq request to test
        try
        {
            _ = await RigctldUtil.GetAllRigInfo(ip, port, DraftSettings.HamlibSettings.ReportRFPower,
                DraftSettings.HamlibSettings.ReportSplitInfo);
        }
        catch (Exception e)
        {
            NotificationManager?.SendErrorNotificationSync(e.Message);
            return false;
        }

        return true;
    }

    private async Task _refreshPort()
    {
        Ports = SerialPort.GetPortNames().ToList();
        var tmp = DraftSettings.HamlibSettings.SelectedPort;
        if (!string.IsNullOrEmpty(tmp) && !Ports.Contains(tmp)) tmp = string.Empty;
        if (string.IsNullOrEmpty(DraftSettings.HamlibSettings.SelectedPort))
            DraftSettings.HamlibSettings.SelectedPort = SerialUtil.PreSelectSerialByName();
        // reset port name
        DraftSettings.HamlibSettings.SelectedPort = "";
        DraftSettings.HamlibSettings.SelectedPort = tmp;
    }

    private void _discardConf()
    {
        // resume settings
        if (Design.IsDesignMode)return;
        ClassLogger.Trace("Discarding confse");
        DraftSettings.RestoreSettings();
        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.NothingJustClosed });
    }

    private void _saveAndApplyConf()
    {
        var anythingChanged = false;
        var cmp = ApplicationSettings.GetInstance().DeepClone();
        DraftSettings.ApplySettings();
        DraftSettings.WriteCurrentSettingsToFile();
        if (DraftSettings.IsCloudlogConfChanged(cmp))
        {
            ClassLogger.Trace("Cloudlog settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Cloudlog });
            anythingChanged = true;
        }

        if (DraftSettings.IsThirdPartyConfChanged(cmp))
        {
            ClassLogger.Trace("ThirdPartyLogService settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.ThirdPartyLogService });
            anythingChanged = true;
        }

        if (DraftSettings.IsHamlibConfChanged(cmp))
        {
            ClassLogger.Trace("hamlib settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Hamlib }); // maybe user clickedTest
            anythingChanged = true;
        }

        if (DraftSettings.IsUDPConfChanged(cmp))
        {
            ClassLogger.Trace("udp settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged
                { Part = ChangedPart.UDPServer });
            anythingChanged = true;
        }

        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.NothingJustClosed });
        if (anythingChanged) return;
        // MessageBus.Current.SendMessage(part);
    }

    #region CloudlogAPI

    public FixedInfoPanelViewModel CloudlogInfoPanel { get; } = new();
    public TestButtonViewModel CloudlogTestButton { get; } = new();
    [Reactive] public bool ShowCloudlogStationIdCombobox { get; set; }

    #endregion

    #region Clublog

    public TestButtonViewModel ClublogTestButton { get; } = new();

    #endregion

    #region HamCQ

    public TestButtonViewModel HamCQTestButton { get; } = new();

    #endregion
    
    #region EQSL

    public TestButtonViewModel EqslTestButton { get; } = new();

    #endregion

    #region HamLib

    [Reactive] public bool HamlibInitPassed { get; set; }
    public ReactiveCommand<Unit, Unit> RefreshPort { get; }

    public TestButtonViewModel HamlibTestButton { get; } = new();

    [Reactive] public List<string> Ports { get; set; }
    [Reactive] public string HamlibVersion { get; set; } = "Unknown hamlib version";

    [Reactive] public List<RigInfo> SupportedModels { get; set; } = new();

    #endregion
}