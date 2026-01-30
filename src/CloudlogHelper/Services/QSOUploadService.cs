using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DesktopNotifications;
using NLog;

namespace CloudlogHelper.Services;
public class QSOUploadService : IQSOUploadService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    
    private readonly CloudlogSettings _cloudlogSettings;
    private readonly List<ThirdPartyLogService> _logServices;
    private readonly INotificationManager _notificationManager;
    private readonly IUdpServerService _udpService;
    
    private readonly BlockingCollection<UploadItem> _uploadQueue = new();
    
    private readonly CancellationTokenSource _serviceCts = new();
    private Task? _processingTask;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private bool _isStarted;
    private bool _isDisposed;

    public QSOUploadService(
        IApplicationSettingsService settingsService,
        IUdpServerService udpService,
        INotificationManager notificationManager)
    {
        if (settingsService == null) throw new ArgumentNullException(nameof(settingsService));
        if (udpService == null) throw new ArgumentNullException(nameof(udpService));
        if (notificationManager == null) throw new ArgumentNullException(nameof(notificationManager));

        var settings = settingsService.GetCurrentSettings();
        _logServices = settings.LogServices;
        _cloudlogSettings = settings.CloudlogSettings ?? throw new InvalidOperationException("Cloudlog settings not configured");
        _udpService = udpService;
        _notificationManager = notificationManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _processingLock.WaitAsync(cancellationToken);
        try
        {
            if (_isStarted)
            {
                return;
            }

            ClassLogger.Info("Starting QSO upload service...");
            _processingTask = Task.Run(() => ProcessUploadQueueAsync(_serviceCts.Token), cancellationToken);
            _isStarted = true;
            ClassLogger.Info("QSO upload service started successfully.");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public void StopSync()
    {
        _processingLock.Wait();
        try
        {
            if (!_isStarted)
            {
                return;
            }

            ClassLogger.Info("Stopping QSO upload service...");
            _uploadQueue.CompleteAdding();
            _serviceCts.Cancel();
            
            if (_processingTask is { IsCompleted: false })
            {
                try
                {
                    _processingTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    //ignored
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex, "Error during service shutdown");
                }
            }

            _isStarted = false;
            ClassLogger.Info("QSO upload service stopped successfully.");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _processingLock.WaitAsync();
        try
        {
            if (!_isStarted)
            {
                return;
            }

            ClassLogger.Info("Stopping QSO upload service...");
            _uploadQueue.CompleteAdding();
            _serviceCts.Cancel();
            if (_processingTask is not null && !_processingTask.IsCompleted)
            {
                try
                {
                    await _processingTask;
                }
                catch (OperationCanceledException)
                {
                    
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex, "Error during service shutdown");
                }
            }

            _isStarted = false;
            ClassLogger.Info("QSO upload service stopped successfully.");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public Task EnqueueQSOForUploadAsync(RecordedCallsignDetail rcd, CancellationToken cancellationToken = default)
    {
        if (rcd == null) throw new ArgumentNullException(nameof(rcd));
        
        if (!rcd.IsUploadable())
        {
            ClassLogger.Trace($"Ignoring non-uploadable QSO: {rcd.DXCall}");
            return Task.CompletedTask;
        }

        // reserved
        var itemKey = GenerateItemKey(rcd);
        
        // now we check whether this qso is already in upload queue instead of if it is uploaded 
        // todo add lock here?
        // todo same qsos from difference sources will cause dupe upload

        if (rcd.IsInUploadQueue)
        {
            ClassLogger.Trace($"Skipping {rcd.DXCall}; It's already in queue");
            return Task.CompletedTask;
        }
        rcd.IsInUploadQueue = true;

        rcd.UploadStatus = UploadStatus.Pending;
        var uploadItem = new UploadItem(rcd, itemKey);

        try
        {
            _uploadQueue.Add(uploadItem, cancellationToken);
            ClassLogger.Trace($"Enqueued QSO for upload: {rcd.DXCall}");
        }
        catch (InvalidOperationException ex) when (_uploadQueue.IsAddingCompleted)
        {
            ClassLogger.Warn($"Failed to enqueue QSO - service is stopping: {rcd.DXCall}");
            throw new InvalidOperationException("Upload service is stopping", ex);
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, $"Failed to enqueue QSO: {rcd.DXCall}");
            throw;
        }
        return  Task.CompletedTask;
    }

    public Task<int> GetPendingCountAsync()
    {
        return Task.FromResult(_uploadQueue.Count);
    }

    private async Task ProcessUploadQueueAsync(CancellationToken cancellationToken)
    {
        ClassLogger.Info("Started processing upload queue.");

        try
        {
            foreach (var uploadItem in _uploadQueue.GetConsumingEnumerable(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                using var uploadTimeoutSrc = new CancellationTokenSource(DefaultConfigs.QSOUploadTimeoutSec);
                
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, 
                    uploadTimeoutSrc.Token);

                await ProcessUploadItemAsync(uploadItem, cancellationToken).ConfigureAwait(false);
                uploadItem.RecordedCallsignDetail.IsInUploadQueue = false;
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClassLogger.Info("Upload queue processing was cancelled.");
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Fatal error in upload queue processing");
            throw;
        }
        finally
        {
            ClassLogger.Info("Stopped processing upload queue.");
        }
    }

    private async Task ProcessUploadItemAsync(UploadItem uploadItem, CancellationToken cancellationToken)
    {
        var rcd = uploadItem.RecordedCallsignDetail;
        ClassLogger.Debug($"Processing QSO: {rcd.DXCall}");

        try
        {
            var adif = GetAdifData(rcd);
            if (string.IsNullOrWhiteSpace(adif))
            {
                rcd.UploadStatus = UploadStatus.Fail;
                rcd.FailReason = TranslationHelper.GetString(LangKeys.invalidadif);
                ClassLogger.Warn($"Invalid ADIF data for QSO: {rcd.DXCall}");
                return;
            }

            if (!ShouldUpload(rcd))
            {
                rcd.UploadStatus = UploadStatus.Ignored;
                rcd.FailReason = TranslationHelper.GetString(LangKeys.qsouploaddisabled);
                ClassLogger.Debug($"Auto upload not enabled, ignoring: {rcd.DXCall}");
                return;
            }

            var result = await UploadWithRetryAsync(rcd, adif, cancellationToken).ConfigureAwait(false);

            if (_udpService.IsNotifyOnQsoUploaded())
            {
                await SendUploadNotificationAsync(rcd, result).ConfigureAwait(false);
            }

            ClassLogger.Info($"QSO processing completed: {rcd.DXCall}, Status: {rcd.UploadStatus}");
        }
        catch (Exception ex)
        {
            // this in theory should never happen?
            rcd.UploadStatus = UploadStatus.Fail;
            rcd.FailReason = ex.Message;
            if (_udpService.IsNotifyOnQsoUploaded())
            {
                await SendUploadNotificationAsync(rcd, false).ConfigureAwait(false);
            }
            ClassLogger.Error(ex, $"Error processing QSO: {rcd.DXCall}");
        }
    }

    private string? GetAdifData(RecordedCallsignDetail rcd)
    {
        return rcd.RawData?.ToString()?.Trim() ?? rcd.GenerateAdif()?.Trim();
    }

    private bool ShouldUpload(RecordedCallsignDetail rcd)
    {
        return (_cloudlogSettings.AutoQSOUploadEnabled || 
                _logServices.Any(x => x.AutoQSOUploadEnabled) || 
                rcd.ForcedUpload);
    }

    private async Task<bool> UploadWithRetryAsync(
        RecordedCallsignDetail rcd, 
        string adif, 
        CancellationToken cancellationToken)
    {
        var maxRetries = _udpService.QSOUploadRetryCount();
        var result = false;
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            rcd.UploadStatus = attempt == 0 ? UploadStatus.Uploading : UploadStatus.Retrying;
            rcd.FailReason = null;

            ClassLogger.Debug($"Upload attempt {attempt + 1}/{maxRetries} for QSO: {rcd.DXCall}");

            try
            {
                var uploadTasks = new List<Task<ServiceUploadResult>>();
                
                if (_cloudlogSettings.AutoQSOUploadEnabled && 
                    !rcd.UploadedServices.GetValueOrDefault("CloudlogService", false))
                {
                    uploadTasks.Add(UploadToCloudlogAsync(rcd, adif, cancellationToken));
                }

                foreach (var service in _logServices.Where(s => s.AutoQSOUploadEnabled))
                {
                    var serviceName = service.GetType().Name;
                    if (!rcd.UploadedServices.GetValueOrDefault(serviceName, false))
                    {
                        uploadTasks.Add(UploadToThirdPartyServiceAsync(service, serviceName, adif, cancellationToken));
                    }
                }

                if (uploadTasks.Any())
                {
                    var results = await Task.WhenAll(uploadTasks).ConfigureAwait(false);
                    
                    foreach (var serviceUploadResult in results)
                    {
                        rcd.UploadedServices[serviceUploadResult.ServiceName] = serviceUploadResult.Success;
                        
                        if (!serviceUploadResult.Success)
                        {
                            rcd.UploadedServicesErrorMessage[serviceUploadResult.ServiceName] = serviceUploadResult.ErrorMessage;
                        }
                    }

                    if (results.All(x => x.Success)) result = true;
                }
                else
                {
                    result = true;
                }

                if (result)
                {
                    rcd.UploadStatus = UploadStatus.Success;
                    rcd.FailReason = "";
                    ClassLogger.Info($"QSO uploaded successfully: {rcd.DXCall}");
                    break;
                }
                else
                {
                    rcd.UploadStatus = UploadStatus.Fail;
                    rcd.FailReason = string.Join(Environment.NewLine, 
                        rcd.UploadedServices.Where(kv => !kv.Value)
                            .Select(kv => $"{kv.Key}: {rcd.UploadedServicesErrorMessage.GetValueOrDefault(kv.Key, "Failed")}"));
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(CalculateRetryDelay(attempt), cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                ClassLogger.Warn(ex, $"Upload attempt {attempt + 1} failed for QSO: {rcd.DXCall}");
                await Task.Delay(CalculateRetryDelay(attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                rcd.UploadStatus = UploadStatus.Fail;
                rcd.FailReason = ex.Message;
                throw;
            }
        }

        if (!result && lastException != null)
        {
            throw new AggregateException("All upload attempts failed", lastException);
        }

        return result;
    }

    private async Task<ServiceUploadResult> UploadToCloudlogAsync(
        RecordedCallsignDetail rcd, 
        string adif, 
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await CloudlogUtil.UploadAdifLogAsync(
                _cloudlogSettings.CloudlogUrl,
                _cloudlogSettings.CloudlogApiKey,
                _cloudlogSettings.CloudlogStationInfo?.StationId!,
                adif,
                cancellationToken).ConfigureAwait(false);

            var success = result.Status == "created";
            ClassLogger.Debug($"Cloudlog upload {(success ? "succeeded" : "failed")}: {rcd.DXCall}");
            
            return new ServiceUploadResult
            {
                ServiceName = "CloudlogService",
                Success = success,
                ErrorMessage = success ? null : result.Reason
            };
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, $"Cloudlog upload failed: {rcd.DXCall}");
            return new ServiceUploadResult
            {
                ServiceName = "CloudlogService",
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<ServiceUploadResult> UploadToThirdPartyServiceAsync(
        ThirdPartyLogService service,
        string serviceName,
        string adif,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.UploadQSOAsync(adif, cancellationToken).ConfigureAwait(false);
            ClassLogger.Info($"QSO uploaded to {serviceName}");
            
            return new ServiceUploadResult
            {
                ServiceName = serviceName,
                Success = true
            };
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, $"QSO upload to {serviceName} failed");
            return new ServiceUploadResult
            {
                ServiceName = serviceName,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task SendUploadNotificationAsync(RecordedCallsignDetail rcd, bool success)
    {
        try
        {
            var notification = new Notification
            {
                Title = success 
                    ? $"{TranslationHelper.GetString(LangKeys.uploadedaqso)} - {rcd.DXCall}"
                    : $"{TranslationHelper.GetString(LangKeys.failedqso)} - {rcd.DXCall}",
                Body = success 
                    ? rcd.FormatToReadableContent(true)
                    : rcd.FailReason ?? TranslationHelper.GetString(LangKeys.uploadfailedaqso)
            };

            await _notificationManager.ShowNotification(notification);
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Failed to send upload notification");
        }
    }

    // just pow for now
    private TimeSpan CalculateRetryDelay(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(30);
        var delay = TimeSpan.FromTicks(baseDelay.Ticks * (long)Math.Pow(2, attempt));
        
        return delay > maxDelay ? maxDelay : delay;
    }

    private string GenerateItemKey(RecordedCallsignDetail rcd)
    {
        return $"{rcd.DXCall}_{rcd.Mode}_{rcd.DateTimeOn:yyyyMMddHHmmss}";
    }
    

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            StopSync();
            _uploadQueue.Dispose();
            _serviceCts.Dispose();
            _processingLock.Dispose();
        }
        finally
        {
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
    #region Helper Classes

    private class UploadItem
    {
        public RecordedCallsignDetail RecordedCallsignDetail { get; }
        
        // reserved field..
        public string ItemKey { get; }

        public UploadItem(RecordedCallsignDetail rcd, string itemKey)
        {
            RecordedCallsignDetail = rcd ?? throw new ArgumentNullException(nameof(rcd));
            ItemKey = itemKey ?? throw new ArgumentNullException(nameof(itemKey));
        }
    }

    private class ServiceUploadResult
    {
        public string ServiceName { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    #endregion

}