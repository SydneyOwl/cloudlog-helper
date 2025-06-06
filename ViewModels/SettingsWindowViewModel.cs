﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
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
        Settings = ApplicationSettings.GetInstance();
        Settings.BackupSettings();
        // if (Design.IsDesignMode)return;

        _initializeHamlibAsync().ConfigureAwait(false);

        ShowCloudlogStationIdCombobox = Settings.CloudlogSettings.AvailableCloudlogStationInfo.Count > 0;

        var hamlibCmd = ReactiveCommand.CreateFromTask(_testHamlib, Settings.HamlibSettings.IsHamlibValid);
        HamlibTestButton.SetTestButtonCommand(hamlibCmd);

        RefreshPort = ReactiveCommand.CreateFromTask(_refreshPort);

        var cloudCmd =
            ReactiveCommand.CreateFromTask(_testCloudlogConnection, Settings.CloudlogSettings.IsCloudlogValid);
        CloudlogTestButton.SetTestButtonCommand(cloudCmd);

        var clubCmd =
            ReactiveCommand.CreateFromTask(_testClublogConnection);
        ClublogTestButton.SetTestButtonCommand(clubCmd);
        
        var hamcqCmd =
            ReactiveCommand.CreateFromTask(_testHamCQConnection);
        HamCQTestButton.SetTestButtonCommand(hamcqCmd);

        // save or discard conf
        DiscardConf = ReactiveCommand.Create(_discardConf);
        SaveAndApplyConf = ReactiveCommand.Create(_saveAndApplyConf);

        this.WhenActivated(disposables =>
        {
            // Subscribe language change
            this.WhenAnyValue(x => x.Settings.LanguageType).Subscribe(language =>
                {
                    I18NExtension.Culture = TranslationHelper.GetCultureInfo(language);
                })
                .DisposeWith(disposables);
            hamlibCmd.ThrownExceptions.Subscribe(err => HamlibErrorPanel.ErrorMessage = err.Message)
                .DisposeWith(disposables);
            cloudCmd.ThrownExceptions.Subscribe(err => CloudlogErrorPanel.ErrorMessage = err.Message)
                .DisposeWith(disposables);
            clubCmd.ThrownExceptions.Subscribe(err => ClublogErrorPanel.ErrorMessage = err.Message)
                .DisposeWith(disposables);
            hamcqCmd.ThrownExceptions.Subscribe(err => HamCQErrorPanel.ErrorMessage = err.Message)
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
        MessageBus.Current.SendMessage(new SettingsChanged
        {
            Part = ChangedPart.NothingJustOpened
        });
    }


    public ReactiveCommand<Unit, Unit> SaveAndApplyConf { get; }
    public ReactiveCommand<Unit, Unit> DiscardConf { get; }
    public ApplicationSettings Settings { get; set; }

    private async Task _initializeHamlibAsync()
    {
        var (result, output) = await RigctldUtil.StartOnetimeRigctldAsync("--version");
        // init hamlib
        if (result)
        {
            HamlibVersion = output;
        }
        else
        {
            HamlibErrorPanel.ErrorMessage = output;
            return;
        }

        var (listResult, opt) = await RigctldUtil.StartOnetimeRigctldAsync("--list");
        if (listResult)
        {
            _supportedModels = RigctldUtil.ParseAllModelsFromRawOutput(opt);
            SupportedRadios = _supportedModels.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            // refresh selected item
            var sel = Settings.HamlibSettings.SelectedRadio;

            Settings.HamlibSettings.KnownModels = _supportedModels;
            Settings.HamlibSettings.SelectedRadio = string.Empty;
            Settings.HamlibSettings.SelectedRadio = sel;

            HamlibInitPassed = true;
        }
        else
        {
            HamlibErrorPanel.ErrorMessage = opt;
        }
    }

    private async Task<bool> _testClublogConnection()
    {
        ClublogErrorPanel.ErrorMessage = string.Empty;
        if (Settings.ClublogSettings.IsClublogHasErrors())
        {
            ClublogErrorPanel.ErrorMessage = TranslationHelper.GetString("fillall");
            ;
            return false;
        }

        var res = await ClublogUtil.TestClublogConnectionAsync(Settings.ClublogSettings.ClublogCallsign,
            Settings.ClublogSettings.ClublogPassword, Settings.ClublogSettings.ClublogEmail).ConfigureAwait(false);
        ClublogErrorPanel.ErrorMessage = res;
        return string.IsNullOrEmpty(res);
    }
    
    private async Task<bool> _testHamCQConnection()
    {
        HamCQErrorPanel.ErrorMessage = string.Empty;
        if (Settings.HamCQSettings.IsHamCQHasErrors())
        {
            HamCQErrorPanel.ErrorMessage = TranslationHelper.GetString("fillall");
            return false;
        }

        var res = await HamCQUtil.TestHamCQConnectionAsync(Settings.HamCQSettings.HamCQAPIKey).ConfigureAwait(false);
        HamCQErrorPanel.ErrorMessage = res;
        return string.IsNullOrEmpty(res);
    }

    private async Task<bool> _testCloudlogConnection()
    {
        CloudlogInfoPanel.InfoMessage = string.Empty;
        CloudlogErrorPanel.ErrorMessage = string.Empty;
        try
        {
            var msg = await CloudlogUtil.TestCloudlogConnectionAsync(Settings.CloudlogSettings.CloudlogUrl,
                Settings.CloudlogSettings.CloudlogApiKey);

            if (!string.IsNullOrEmpty(msg))
            {
                CloudlogErrorPanel.ErrorMessage = msg;
                return false;
            }

            var stationInfo = await CloudlogUtil.GetStationInfoAsync(Settings.CloudlogSettings.CloudlogUrl,
                Settings.CloudlogSettings.CloudlogApiKey);
            if (stationInfo.Count == 0)
            {
                CloudlogErrorPanel.ErrorMessage = TranslationHelper.GetString("failedstationinfo");
                Settings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                Settings.CloudlogSettings.CloudlogStationId = string.Empty;
                ShowCloudlogStationIdCombobox = false;
                return false;
            }

            var oldVal = Settings.CloudlogSettings.CloudlogStationId;

            Settings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
            Settings.CloudlogSettings.AvailableCloudlogStationInfo.AddRange(stationInfo);
            ShowCloudlogStationIdCombobox = true;

            if (string.IsNullOrEmpty(Settings.CloudlogSettings.CloudlogStationId))
            {
                Settings.CloudlogSettings.CloudlogStationId = stationInfo[0].StationId!;
            }
            else
            {
                Settings.CloudlogSettings.CloudlogStationId = "";
                Settings.CloudlogSettings.CloudlogStationId = oldVal;
            }

            CloudlogErrorPanel.ErrorMessage = string.Empty;

            var instType = await CloudlogUtil.GetCurrentServerInstanceTypeAsync(Settings.CloudlogSettings.CloudlogUrl);
            // instanceuncompitable
            if (instType != ServerInstanceType.Cloudlog)
                CloudlogInfoPanel.InfoMessage = TranslationHelper.GetString("instanceuncompitable")
                    .Replace("{replace01}", instType.ToString());

            return true;
        }
        catch (Exception e)
        {
            CloudlogErrorPanel.ErrorMessage = e.Message;
        }

        return false;
    }

    private (string, int) _getRigctldIpAndPort()
    {
        // parse addr
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;

        if (Settings.HamlibSettings.UseExternalRigctld)
            (ip, port) = IPAddrUtil.ParseAddress(Settings.HamlibSettings.ExternalRigctldHostAddress);

        if (Settings.HamlibSettings.UseRigAdvanced &&
            !string.IsNullOrEmpty(Settings.HamlibSettings.OverrideCommandlineArg))
        {
            var matchPort = Regex.Match(Settings.HamlibSettings.OverrideCommandlineArg, @"-t\s+(\S+)");
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
        HamlibErrorPanel.ErrorMessage = string.Empty;
        var (ip, port) = _getRigctldIpAndPort();
        if (!Settings.HamlibSettings.UseExternalRigctld &&
            _supportedModels.TryGetValue(Settings.HamlibSettings.SelectedRadio!, out var radioId))
        {
            var defaultArgs = RigctldUtil.GenerateRigctldCmdArgs(radioId, Settings.HamlibSettings.SelectedPort);

            if (Settings.HamlibSettings.UseRigAdvanced)
            {
                if (string.IsNullOrEmpty(Settings.HamlibSettings.OverrideCommandlineArg))
                    defaultArgs = RigctldUtil.GenerateRigctldCmdArgs(radioId, Settings.HamlibSettings.SelectedPort,
                        Settings.HamlibSettings.DisablePTT,
                        Settings.HamlibSettings.AllowExternalControl);
                else
                    defaultArgs = Settings.HamlibSettings.OverrideCommandlineArg;
            }

            var (res, des) =
                await RigctldUtil.RestartRigctldBackgroundProcessAsync(defaultArgs);
            if (!res)
            {
                HamlibErrorPanel.ErrorMessage = des;
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
            _ = await RigctldUtil.GetAllRigInfo(ip, port, Settings.HamlibSettings.ReportRFPower,
                Settings.HamlibSettings.ReportSplitInfo);
        }
        catch (Exception e)
        {
            // ClassLogger.Trace(e.Message);
            HamlibErrorPanel.ErrorMessage = e.Message;
            return false;
        }

        // HamlibErrorPanel.ErrorMessage = string.Empty;
        return true;
        // HamlibErrorPanel.ErrorMessage = TranslationHelper.GetString("inithamlibfailed");
        //
        // return false;
    }

    private async Task _refreshPort()
    {
        Ports = SerialPort.GetPortNames().ToList();
        var tmp = Settings.HamlibSettings.SelectedPort;
        if (!string.IsNullOrEmpty(tmp) && !Ports.Contains(tmp)) tmp = string.Empty;
        if (string.IsNullOrEmpty(Settings.HamlibSettings.SelectedPort))
            Settings.HamlibSettings.SelectedPort = SerialUtil.PreSelectSerialByName();
        // reset port name
        Settings.HamlibSettings.SelectedPort = "";
        Settings.HamlibSettings.SelectedPort = tmp;
    }

    private void _discardConf()
    {
        // resume settings
        ClassLogger.Trace("Discarding confse");
        Settings.RestoreSettings();
        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.NothingJustClosed });
    }

    private void _saveAndApplyConf()
    {
        var anythingChanged = false;
        Settings.WriteCurrentSettingsToFile();
        if (Settings.IsCloudlogConfChanged())
        {
            ClassLogger.Trace("Cloudlog settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Cloudlog });
            anythingChanged = true;
        }

        if (Settings.IsClublogConfChanged())
        {
            ClassLogger.Trace("clublog settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Clublog });
            anythingChanged = true;
        }
        
        if (Settings.IsHamCQConfChanged())
        {
            ClassLogger.Trace("hamcq settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.HamCQ });
            anythingChanged = true;
        }

        if (Settings.IsHamlibConfChanged())
        {
            ClassLogger.Trace("hamlib settings changed");
            MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Hamlib }); // maybe user clickedTest
            anythingChanged = true;
        }

        if (Settings.IsUDPConfChanged())
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

    public ErrorPanelViewModel CloudlogErrorPanel { get; } = new();
    public FixedInfoPanelViewModel CloudlogInfoPanel { get; } = new();
    public TestButtonViewModel CloudlogTestButton { get; } = new();
    [Reactive] public bool ShowCloudlogStationIdCombobox { get; set; }

    #endregion

    #region Clublog

    public ErrorPanelViewModel ClublogErrorPanel { get; } = new();
    public TestButtonViewModel ClublogTestButton { get; } = new();

    #endregion
    
    #region HamCQ

    public ErrorPanelViewModel HamCQErrorPanel { get; } = new();
    public TestButtonViewModel HamCQTestButton { get; } = new();

    #endregion

    #region HamLib

    public ErrorPanelViewModel HamlibErrorPanel { get; } = new();
    [Reactive] public bool HamlibInitPassed { get; set; }
    public ReactiveCommand<Unit, Unit> RefreshPort { get; }

    public TestButtonViewModel HamlibTestButton { get; } = new();

    [Reactive] public List<string> SupportedRadios { get; set; }
    [Reactive] public List<string> Ports { get; set; }
    [Reactive] public string HamlibVersion { get; set; } = "Unknown hamlib version";

    private Dictionary<string, string> _supportedModels;

    #endregion
}