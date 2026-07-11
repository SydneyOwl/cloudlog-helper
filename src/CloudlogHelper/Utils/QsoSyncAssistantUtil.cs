using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;
using HtmlAgilityPack;
using NLog;

namespace CloudlogHelper.Utils;

public class QsoSyncAssistantUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private static readonly Dictionary<string, string> FormatMap = new()
    {
        { "d/m/y", "dd/MM/yy HH:mm" },
        { "d/m/Y", "dd/MM/yyyy HH:mm" },
        { "m/d/y", "MM/dd/yy HH:mm" },
        { "m/d/Y", "MM/dd/yyyy HH:mm" },
        { "d.m.Y", "dd.MM.yyyy HH:mm" },
        { "y/m/d", "yy/MM/dd HH:mm" },
        { "Y-m-d", "yyyy-MM-dd HH:mm" },
        { "M d, Y", "MMM dd, yyyy HH:mm" },
        { "M d, y", "MMM dd, yy HH:mm" }
    };

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

    public static async Task<string> DownloadQSOFile(string baseurl, string stationId, int dateRange,
        IEnumerable<FlurlCookie> cookies, CancellationToken cancellationToken)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var nDaysBefore = DateTime.Today.AddDays(dateRange * -1).ToString("yyyy-MM-dd");

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

    public static async Task<string> DownloadQSOs(string baseurl, string stationCallsign,
        int stationId, int qsoCount, IEnumerable<FlurlCookie> cookies, CancellationToken cancellationToken)
    {
        // check for instance
        var instance = await CloudlogUtil.GetCurrentServerInstanceTypeAsync(baseurl, CancellationToken.None).ConfigureAwait(false);
        var tmp = new JsonObject();
        if (instance == ServerInstanceType.Wavelog)
        {
            tmp.Add("dateFrom", "");
            tmp.Add("dateTo", "");
            tmp.Add("de[]", stationId);
            tmp.Add("dx", "*");
            tmp.Add("mode", "");
            tmp.Add("band", "");
            tmp.Add("qslSent", "");
            tmp.Add("qslReceived", "");
            tmp.Add("qslSentMethod", "");
            tmp.Add("qslReceivedMethod", "");
            tmp.Add("iota", "");
            tmp.Add("operator", "*");
            tmp.Add("dxcc", "");
            tmp.Add("propmode", "");
            tmp.Add("gridsquare", "*");
            tmp.Add("state", "*");
            tmp.Add("county", "*");
            tmp.Add("qsoresults", qsoCount.ToString());
            tmp.Add("sats", "All");
            tmp.Add("orbits", "All");
            tmp.Add("cqzone", "All");
            tmp.Add("ituzone", "All");
            tmp.Add("lotwSent", "");
            tmp.Add("lotwReceived", "");
            tmp.Add("clublogSent", "");
            tmp.Add("clublogReceived", "");
            tmp.Add("eqslSent", "");
            tmp.Add("eqslReceived", "");
            tmp.Add("qslvia", "*");
            tmp.Add("sota", "*");
            tmp.Add("pota", "*");
            tmp.Add("wwff", "*");
            tmp.Add("qslimages", "");
            tmp.Add("dupes", "");
            tmp.Add("contest", "*");
            tmp.Add("invalid", "");
            tmp.Add("continent", "");
            tmp.Add("comment", "*");
            tmp.Add("qsoids", "");
        }
        else
        {
            // get callsign
            tmp.Add("de", stationCallsign);
            tmp.Add("dx", "");
            tmp.Add("operator", "");
            tmp.Add("gridsquare", "");
            tmp.Add("state", "");
            tmp.Add("qslvia", "");
            tmp.Add("sota", "");
            tmp.Add("pota", "");
            tmp.Add("wwff", "");
            tmp.Add("contest", "");
            tmp.Add("comment", "");
        }
        
        var formData = tmp.ToDictionary(k => k.Key, v => v.Value?.ToString());

        var recentQs = await baseurl
            .AppendPathSegments(DefaultConfigs.CloudlogQSOAdvancedEndpoint)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.QSODownloadRequestTimeout))
            .WithCookies(cookies)
            .PostUrlEncodedAsync(formData, default, cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("Operation(DownloadQSOs) was canceled.");
        return await recentQs.GetStringAsync().ConfigureAwait(false);
    }

    public static async Task<string> GetDateFormat(string baseurl, IEnumerable<FlurlCookie> cookies,
        CancellationToken cancellationToken)
    {
        var dashboard = await baseurl
            .AppendPathSegments(DefaultConfigs.CloudlogDashboardEndpoint)
            .WithCookies(cookies)
            .GetStringAsync(default, cancellationToken).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(dashboard);

        var accountLink = doc.DocumentNode
            .SelectSingleNode("//li[@class='nav-item dropdown']//a[@class='dropdown-item' and @title='Account']")
            ?.GetAttributeValue("href", string.Empty);

        if (string.IsNullOrEmpty(accountLink)) return string.Empty;

        var settingsPage = await accountLink
            .WithCookies(cookies)
            .GetStringAsync(default, cancellationToken).ConfigureAwait(false);

        doc = new HtmlDocument();
        doc.LoadHtml(settingsPage);

        var dateFormatValue = doc.DocumentNode
            .SelectSingleNode("//select[@name='user_date_format']/option[@selected]")
            ?.GetAttributeValue("value", string.Empty)
            .Trim();

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("Operation(GetDateFormat) was canceled.");
        if (string.IsNullOrEmpty(dateFormatValue)) return string.Empty;
        return FormatMap.TryGetValue(dateFormatValue, out var res) ? res : string.Empty;
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

    private static int FindRecordStart(StringBuilder buffer, int eorIndex)
    {
        if (buffer.Length == 0 || eorIndex >= buffer.Length || eorIndex < 0)
            return -1;

        // Look for the start of the record (either start of buffer or after previous <eor>)
        var bufferStr = buffer.ToString();

        // Check if we're at the start of the buffer
        if (eorIndex == 0)
            return 0;

        // Look for previous <eor>
        var previousEor = bufferStr.LastIndexOf("<eor>", Math.Min(eorIndex - 1, bufferStr.Length - 1),
            StringComparison.OrdinalIgnoreCase);

        return previousEor >= 0 ? previousEor + 5 : 0;
    }

    public static async Task<bool> UploadAdifLogAsync(string baseurl, string adif, string stationId,
        IEnumerable<FlurlCookie> cookies, CancellationToken cancellationToken)
    {
        var resp = await baseurl
            .AppendPathSegments(DefaultConfigs.CloudlogAdifFileUploadEndpoint)
            .WithCookies(cookies)
            .PostMultipartAsync(mp => mp
                    .AddString("station_profile", stationId)
                    .AddFile("userfile",
                        new MemoryStream(Encoding.UTF8.GetBytes(adif)),
                        "cloudlog-helper-generated.adi",
                        "application/octet-stream"), default, cancellationToken
            ).ConfigureAwait(false);
        var res = await resp.GetStringAsync().ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("Operation(UploadAdifLogAsync) was canceled.");
        return res.Contains("ADIF Imported", StringComparison.InvariantCultureIgnoreCase);
    }
}