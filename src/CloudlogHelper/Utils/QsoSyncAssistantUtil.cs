using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;

namespace CloudlogHelper.Utils;

public class QsoSyncAssistantUtil
{
    public static async Task<IReadOnlyList<FlurlCookie>> LoginAndGetCookies(string baseurl, string username,
        string password, CancellationToken cancellationToken)
    {
        var loginRequest = baseurl
            .AllowHttpStatus(303)
            .WithAutoRedirect(false)
            .AppendPathSegments(DefaultConfigs.CloudlogLoginEndpoint);

        var result = await loginRequest
            .PostUrlEncodedAsync(new
            {
                user_name = username,
                user_password = password
            }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var redirectUrl = result.Headers.FirstOrDefault("Location");
        if (redirectUrl is null) throw new InvalidOperationException("Username or password should not be empty!");
        if (!redirectUrl.Contains("dashboard")) throw new InvalidOperationException("Incorrect username or password");
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("Operation(LoginAndGetCookies) was canceled.");
        return result.Cookies;
    }

    /// <summary>
    ///     Builds the [from, to] range sent to Cloudlog when exporting QSOs.
    /// </summary>
    /// <remarks>
    ///     The format is explicit but the calendar is not: a culture whose default calendar is not
    ///     Gregorian (th-TH uses the Buddhist calendar) shifts the year by 543, so the exported
    ///     range no longer covers any real QSO. Formatting is pinned to the invariant culture.
    /// </remarks>
    public static (string From, string To) GetExportDateRange(int dateRangeDays)
    {
        var today = DateTime.Today;

        return (today.AddDays(dateRangeDays * -1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public static async Task<string> DownloadQSOFile(string baseurl, string stationId, int dateRange,
        IEnumerable<FlurlCookie> cookies, CancellationToken cancellationToken)
    {
        var (nDaysBefore, today) = GetExportDateRange(dateRange);

        var response = await baseurl
            .AppendPathSegments(DefaultConfigs.ExportCustomAdifLogs)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.QSODownloadRequestTimeout))
            .WithCookies(cookies)
            .PostMultipartAsync(mp => mp
                .AddString("station_profile", stationId)
                .AddString("from", nDaysBefore)
                .AddString("to", today), cancellationToken: cancellationToken)
            .ReceiveBytes().ConfigureAwait(false);

        return Encoding.UTF8.GetString(response);
    }

    /// <summary>
    ///     Get lastest n qsos. Suitable for large files.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="targetCount"></param>
    /// <returns></returns>
    public static string ReadLastestQsos(string filePath, int targetCount)
    {
        var queue = new Queue<string>(targetCount);

        using var reader = new StreamReader(filePath);
        var lineNum = 1;
        var logFound = false;
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.Contains("<CALL:", StringComparison.InvariantCultureIgnoreCase)) logFound = true;

            if (!logFound) continue;

            if (!line.Contains("<CALL:", StringComparison.InvariantCultureIgnoreCase)
                || !line.Contains("<MODE:", StringComparison.InvariantCultureIgnoreCase)
                || !line.Contains("<BAND:", StringComparison.InvariantCultureIgnoreCase)
                || !line.Contains("<RST_", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException($"This is not a correct Wsjtx/jtdx log file! - line {lineNum}");
            if (queue.Count == targetCount)
                queue.Dequeue();
            queue.Enqueue(line.Trim());
            lineNum++;
        }

        return string.Join("\n", queue);
    }
}