using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using CloudlogHelper.Database;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels;

namespace CloudlogHelper.Tests;

public class QsoSyncAssistantWindowViewModelTests
{
    [Fact]
    public void SaveConf_WhenMappingThrows_HandsTheDraftBack()
    {
        // M13: _mapper.Map runs before ApplySettings, which is the only thing that clears the
        // claim. A throw here used to leave the draft held for the rest of the process, so no
        // settings window could be opened again.
        var mapper = MapperThatFailsOnApplicationSettings();
        Assert.Throws<AutoMapperMappingException>(
            () => mapper.Map(new ApplicationSettings(), new ApplicationSettings()));

        var settingsService = new RecordingApplicationSettingsService();
        var viewModel = new QsoSyncAssistantWindowViewModel(
            new StubDatabaseService(),
            new StubInAppNotificationService(),
            settingsService,
            new StubWindowManagerService(),
            mapper);

        // ReactiveUI routes command failures to ThrownExceptions on the thread pool when
        // nobody listens, which takes the whole test host down - so listen, and swallow.
        using var _ = viewModel.SaveConf.ThrownExceptions.Subscribe(_ => { });
        var escaped = Record.Exception(() => viewModel.SaveConf.Execute().Subscribe());

        Assert.NotNull(escaped);
        Assert.True(settingsService.RestoreCalled);
        Assert.False(settingsService.ApplyCalled);
    }

    [Fact]
    public void SaveConf_WhenMappingSucceeds_AppliesWithoutRestoring()
    {
        var settingsService = new RecordingApplicationSettingsService();
        var viewModel = new QsoSyncAssistantWindowViewModel(
            new StubDatabaseService(),
            new StubInAppNotificationService(),
            settingsService,
            new StubWindowManagerService(),
            new Mapper(new MapperConfiguration(cfg =>
                cfg.CreateMap<ApplicationSettings, ApplicationSettings>())));

        viewModel.SaveConf.Execute().Subscribe();

        Assert.True(settingsService.ApplyCalled);
        Assert.False(settingsService.RestoreCalled);
    }

    // An empty self-map is silently accepted by AutoMapper's assignable path, so the failing
    // mapper has to be configured to actually throw during the member mapping.
    private static IMapper MapperThatFailsOnApplicationSettings() =>
        new Mapper(new MapperConfiguration(cfg => cfg
            .CreateMap<ApplicationSettings, ApplicationSettings>()
            .ForMember(x => x.BasicSettings, o => o.MapFrom<ThrowingMemberResolver>())));

    private sealed class ThrowingMemberResolver
        : IValueResolver<ApplicationSettings, ApplicationSettings, BasicSettings>
    {
        public BasicSettings Resolve(ApplicationSettings source, ApplicationSettings destination,
            BasicSettings destMember, ResolutionContext context) => throw new InvalidOperationException("boom");
    }

    private sealed class RecordingApplicationSettingsService : IApplicationSettingsService
    {
        public bool ApplyCalled { get; private set; }
        public bool RestoreCalled { get; private set; }
        private object? _owner;
        private readonly ApplicationSettings _settings = new();

        public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null)
        {
            ApplyCalled = true;
            _owner = null;
        }

        public void RestoreSettings(object owner)
        {
            RestoreCalled = true;
            _owner = null;
        }

        public ApplicationSettings GetCurrentSettings() => _settings;

        public ApplicationSettings GetCurrentDraftSettingsSnapshot() => _settings;

        public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
        {
            if (_owner is not null)
            {
                draftSettings = null;
                return false;
            }

            _owner = owner;
            draftSettings = _settings;
            return true;
        }
    }

    private sealed class StubDatabaseService : IDatabaseService
    {
        public Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false) =>
            throw new NotSupportedException();

        public Task UpgradeDatabaseAsync() => throw new NotSupportedException();
        public bool IsUpgradeNeeded() => throw new NotSupportedException();
        public Version GetVersionBeforeUpdate() => throw new NotSupportedException();
        public Task<string> GetParentModeAsync(string mode) => throw new NotSupportedException();
        public Task<CountryDatabase> GetCallsignDetailAsync(string callsign) => throw new NotSupportedException();
        public Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso) => throw new NotSupportedException();
        public Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso) => throw new NotSupportedException();

        public Task BatchAddOrUpdateCallsignGridAsync(List<CollectedGridDatabase> collectedGrid) =>
            throw new NotSupportedException();

        public Task<string?> GetGridByCallsign(string callsign) => throw new NotSupportedException();
        public Task<(int, int)> UpdateCallsignAndCountry(string ctyDat) => throw new NotSupportedException();
    }

    private sealed class StubInAppNotificationService : IInAppNotificationService
    {
        public Task SendInfoNotificationAsync(string message) => throw new NotSupportedException();
        public Task SendSuccessNotificationAsync(string message) => throw new NotSupportedException();
        public Task SendWarningNotificationAsync(string message) => throw new NotSupportedException();
        public Task SendErrorNotificationAsync(string message) => throw new NotSupportedException();
        public void SendInfoNotificationSync(string message) => throw new NotSupportedException();
        public void SendSuccessNotificationSync(string message) => throw new NotSupportedException();
        public void SendWarningNotificationSync(string message) => throw new NotSupportedException();
        public void SendErrorNotificationSync(string message) => throw new NotSupportedException();
    }

    private sealed class StubWindowManagerService : IWindowManagerService
    {
        public string Track(Avalonia.Controls.Window window, IDisposable? lifetime = null) =>
            throw new NotSupportedException();

        public Task<T?> CreateAndShowWindowByVm<T>(Type wType, Avalonia.Controls.Window? toplevel = null,
            bool dialog = true) => throw new NotSupportedException();

        public Task CreateAndShowWindowByVm(Type wType, Avalonia.Controls.Window? toplevel = null,
            bool dialog = true) => throw new NotSupportedException();

        public Task CreateAndShowWindowByVm(CloudlogHelper.Enums.PluginControllableWindow wType,
            Avalonia.Controls.Window? toplevel = null, bool dialog = true) => throw new NotSupportedException();

        public T GetViewModelInstance<T>() => throw new NotSupportedException();
        public void CloseWindowBySeq(string seq) => throw new NotSupportedException();

        public Task LaunchBrowser(string uri, Avalonia.Controls.Window? topLevel = null) =>
            throw new NotSupportedException();

        public Task LaunchDir(string path, Avalonia.Controls.Window? topLevel = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Avalonia.Platform.Storage.IStorageFile?>> OpenFilePickerAsync(
            Avalonia.Platform.Storage.FilePickerOpenOptions options, Avalonia.Controls.Window? topLevel = null) =>
            throw new NotSupportedException();

        public Avalonia.Controls.Window? GetToplevel(Type vmType) => throw new NotSupportedException();

        public Task<Avalonia.Platform.Storage.IStorageFile?> OpenFileSaverAsync(
            Avalonia.Platform.Storage.FilePickerSaveOptions options, Avalonia.Controls.Window? topLevel = null) =>
            throw new NotSupportedException();
    }
}
