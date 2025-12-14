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
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Settings for cloudlog.
    /// </summary>
    private readonly CloudlogSettings _extraCloudlogSettings;

    /// <summary>
    ///     Settings for log services.
    /// </summary>
    private readonly List<ThirdPartyLogService> _logServices;

    private readonly INotificationManager _nativeNativeNotificationManager;

    /// <summary>
    ///     Settings for UDPServer.
    /// </summary>
    private readonly UDPServerSettings _udpSettings;

    /// <summary>
    ///     To be uploaded QSOs queue.
    /// </summary>
    private readonly ConcurrentQueue<RecordedCallsignDetail> _uploadQueue = new();

    public QSOUploadService(IApplicationSettingsService ss,
        INotificationManager nativeNotificationManager)
    {
        _logServices = ss.GetCurrentSettings().LogServices;
        _extraCloudlogSettings = ss.GetCurrentSettings().CloudlogSettings;
        _udpSettings = ss.GetCurrentSettings().UDPSettings;
        _nativeNativeNotificationManager = nativeNotificationManager;
        Task.Run(UploadQSOFromQueue);
    }

    public void Dispose()
    {
        _extraCloudlogSettings.Dispose();
        _udpSettings.Dispose();
        _nativeNativeNotificationManager.Dispose();
    }

    public void EnqueueQSOForUpload(RecordedCallsignDetail rcd)
    {
        if (rcd.IsUploadable())
        {
            if (_uploadQueue.Contains(rcd)) return;
            rcd.UploadStatus = UploadStatus.Pending;
            _uploadQueue.Enqueue(rcd);
            ClassLogger.Trace($"Enqueued QSO: {rcd}");
            return;
        }

        ClassLogger.Trace($"ignoring enqueue QSO: {rcd}");
    }

    private async Task UploadQSOFromQueue()
    {
        while (true)
            try
            {
                if (!_uploadQueue.TryDequeue(out var rcd)) continue;
                var adif = rcd.RawData?.ToString() ?? rcd.GenerateAdif();
                if (string.IsNullOrEmpty(adif)) continue;
                ClassLogger.Debug($"Try Logging: {adif}");
                if (!_logServices.Any(x => x.AutoQSOUploadEnabled)
                    && !_extraCloudlogSettings.AutoQSOUploadEnabled
                    && !rcd.ForcedUpload)
                {
                    rcd.UploadStatus = UploadStatus.Ignored;
                    rcd.FailReason = TranslationHelper.GetString(LangKeys.qsouploaddisabled);
                    ClassLogger.Debug($"Auto upload not enabled. ignored: {adif}.");
                    continue;
                }

                // do possible retry...
                if (!int.TryParse(_udpSettings.RetryCount, out var retTime)) retTime = 1;
                for (var i = 0; i < retTime; i++)
                {
                    rcd.UploadStatus = i > 0 ? UploadStatus.Retrying : UploadStatus.Uploading;
                    rcd.FailReason = null;
                    var failOutput = new StringBuilder();

                    try
                    {
                        if (!_extraCloudlogSettings.AutoQSOUploadEnabled)
                            rcd.UploadedServices["CloudlogService"] = true;
                        if (!rcd.UploadedServices.GetValueOrDefault("CloudlogService", false))
                        {
                            var cloudlogResult = await CloudlogUtil.UploadAdifLogAsync(
                                _extraCloudlogSettings.CloudlogUrl,
                                _extraCloudlogSettings.CloudlogApiKey,
                                _extraCloudlogSettings.CloudlogStationInfo?.StationId!,
                                adif,
                                CancellationToken.None);
                            if (cloudlogResult.Status != "created")
                            {
                                ClassLogger.Debug("A qso for cloudlog failed to upload.");
                                rcd.UploadedServices["CloudlogService"] = false;
                                failOutput.AppendLine("Cloudlog: " + cloudlogResult.Reason.Trim());
                            }
                            else
                            {
                                ClassLogger.Debug("Qso for cloudlog uploaded successfully.");
                                rcd.UploadedServices["CloudlogService"] = true;
                            }
                        }

                        foreach (var thirdPartyLogService in _logServices)
                        {
                            var serName = thirdPartyLogService.GetType().Name;
                            if (!thirdPartyLogService.AutoQSOUploadEnabled) rcd.UploadedServices[serName] = true;
                            if (!rcd.UploadedServices.GetValueOrDefault(serName, false))
                                try
                                {
                                    await thirdPartyLogService.UploadQSOAsync(adif, CancellationToken.None);
                                    rcd.UploadedServices[serName] = true;
                                    ClassLogger.Info($"Qso for {serName} uploaded successfully.");
                                }
                                catch (Exception ex)
                                {
                                    rcd.UploadedServices[serName] = false;
                                    ClassLogger.Error(ex, $"Qso for {serName} uploaded failed.");
                                    failOutput.AppendLine(serName + ex.Message);
                                }
                        }

                        if (rcd.UploadedServices.Values.All(x => x))
                        {
                            rcd.UploadStatus = UploadStatus.Success;
                            rcd.FailReason = string.Empty;
                            break;
                        }

                        rcd.UploadStatus = UploadStatus.Fail;
                        rcd.FailReason = failOutput.ToString();

                        if (_udpSettings.PushNotificationOnQSOUploaded)
                        {
                            if (rcd.UploadStatus == UploadStatus.Success)
                                _ = _nativeNativeNotificationManager.ShowNotification(new Notification
                                {
                                    Title = TranslationHelper.GetString(LangKeys.uploadedaqso) + rcd.DXCall,
                                    Body = rcd.FormatToReadableContent(true)
                                });
                            else
                                _ = _nativeNativeNotificationManager.ShowNotification(new Notification
                                {
                                    Title = TranslationHelper.GetString(LangKeys.uploadfailedaqso),
                                    Body = rcd.FailReason
                                });
                        }

                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        ClassLogger.Debug(ex, "Qso uploaded failed.");
                        rcd.UploadStatus = UploadStatus.Fail;
                        rcd.FailReason = ex.Message;
                    }
                }
            }
            catch (Exception st)
            {
                ClassLogger.Error(st, "Error occurred while uploading qso data. This is ignored.");
            }
            finally
            {
                await Task.Delay(500);
            }
    }
}