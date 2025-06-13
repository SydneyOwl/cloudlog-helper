using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
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
        string password)
    {
        var tmp = new JObject();
        tmp.Add("user_name", username);
        tmp.Add("user_password", password);
        var loginRequest = baseurl
            .AllowHttpStatus(303)
            .WithAutoRedirect(false)
            .AppendPathSegments(DefaultConfigs.CloudlogLoginEndpoint)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout));

        var result = await loginRequest
            .PostUrlEncodedAsync(tmp.ToObject<Dictionary<string, string>>());
        var redirectUrl = result.Headers.FirstOrDefault("Location");
        if (!redirectUrl.Contains("dashboard")) throw new Exception("Incorrect username or password");
        return result.Cookies;
    }

    public static async Task<string> DownloadQSOs(string baseurl, string key,
        int stationId, int qsoCount, IEnumerable<FlurlCookie> cookies)
    {
        // check for instance
        var instance = await CloudlogUtil.GetCurrentServerInstanceTypeAsync(baseurl);
        var tmp = new JObject();
        tmp.Add("qsoresults", qsoCount);
        if (instance == ServerInstanceType.Wavelog)
        {
            tmp.Add("de[]", stationId);
            tmp.Add("dx", "*");
            tmp.Add("operator", "*");
            tmp.Add("gridsquare", "*");
            tmp.Add("state", "*");
            tmp.Add("orbits", "All");
            tmp.Add("qslvia", "*");
            tmp.Add("sota", "*");
            tmp.Add("pota", "*");
            tmp.Add("wwff", "*");
            tmp.Add("contest", "*");
            tmp.Add("comment", "*");
        }
        else
        {
            // get callsign
            var statstic = await CloudlogUtil.GetStationInfoAsync(baseurl, key, stationId.ToString());
            if (statstic is null) throw new Exception("Failed to fetch de");
            tmp.Add("de", statstic.Value.StationCallsign);
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

        tmp.Add("dateFrom", "");
        tmp.Add("dateTo", "");
        tmp.Add("sats", "All");
        var recentQs = await baseurl
            .AppendPathSegments(DefaultConfigs.CloudlogQSOAdvancedEndpoint)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.QSODownloadRequestTimeout))
            .WithCookies(cookies)
            .PostUrlEncodedAsync(tmp.ToObject<Dictionary<string, string>>());

        return await recentQs.GetStringAsync();
    }

    public static async Task<string> GetDateFormat(string baseurl, IEnumerable<FlurlCookie> cookies)
    {
        var dashboard = await baseurl
            .AppendPathSegments(DefaultConfigs.CloudlogDashboardEndpoint)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .WithCookies(cookies)
            .GetStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(dashboard);

        var accountLink = doc.DocumentNode
            .SelectSingleNode("//li[@class='nav-item dropdown']//a[@class='dropdown-item' and @title='Account']")
            ?.GetAttributeValue("href", string.Empty);

        if (string.IsNullOrEmpty(accountLink)) return string.Empty;

        var settingsPage = await accountLink
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .WithCookies(cookies)
            .GetStringAsync();

        doc = new HtmlDocument();
        doc.LoadHtml(settingsPage);

        var dateFormatValue = doc.DocumentNode
            .SelectSingleNode("//select[@name='user_date_format']/option[@selected]")
            ?.GetAttributeValue("value", string.Empty)
            .Trim();

        if (string.IsNullOrEmpty(dateFormatValue)) return string.Empty;

        return FormatMap.TryGetValue(dateFormatValue, out var res) ? res : string.Empty;
    }


    /// <summary>
    ///     Get lastest n qsos. Suitable for large files.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="targetCount"></param>
    /// <returns></returns>
    [Obsolete]
    public static string GetLastestQsos(string filePath, int targetCount)
    {
        const int chunkSize = 4096;
        var records = new List<string>(targetCount);
        var buffer = new StringBuilder();

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            var position = fs.Length;
            var chunk = new byte[chunkSize];
            var remainingPartialRecord = string.Empty;

            while (position > 0 && records.Count < targetCount)
            {
                var readSize = (int)Math.Min(chunkSize, position);
                position -= readSize;
                fs.Seek(position, SeekOrigin.Begin);
                _ = fs.Read(chunk, 0, readSize);

                var chunkText = Encoding.UTF8.GetString(chunk, 0, readSize);
                buffer.Insert(0, chunkText);

                // Prepend any partial record from previous chunk
                if (!string.IsNullOrEmpty(remainingPartialRecord))
                {
                    buffer.Insert(0, remainingPartialRecord);
                    remainingPartialRecord = string.Empty;
                }

                var searchPosition = buffer.Length;
                while (records.Count < targetCount)
                {
                    var eorIndex = buffer.ToString()
                        .LastIndexOf("<eor>", searchPosition - 1, StringComparison.OrdinalIgnoreCase);
                    if (eorIndex < 0) break;

                    var recordStart = FindRecordStart(buffer, eorIndex);
                    if (recordStart < 0)
                    {
                        // If we can't find a complete record, save the partial for next chunk
                        remainingPartialRecord = buffer.ToString(0, eorIndex + 5);
                        buffer.Remove(0, eorIndex + 5);
                        break;
                    }

                    var record = buffer.ToString(recordStart, eorIndex + 5 - recordStart);
                    records.Insert(0, record);
                    buffer.Remove(recordStart, eorIndex + 5 - recordStart);
                    searchPosition = recordStart;
                }
            }

            // Handle any remaining complete record in the buffer
            if (records.Count < targetCount && buffer.Length > 0)
            {
                var eorIndex = buffer.ToString().LastIndexOf("<eor>", StringComparison.OrdinalIgnoreCase);
                if (eorIndex >= 0)
                {
                    var recordStart = FindRecordStart(buffer, eorIndex);
                    if (recordStart >= 0)
                    {
                        var record = buffer.ToString(recordStart, eorIndex + 5 - recordStart);
                        records.Insert(0, record);
                    }
                }
            }
        }

        // Return the requested number of records (or all if there aren't enough)
        return string.Join("\n", records.Take(targetCount));
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
        var previousEor = bufferStr.LastIndexOf("<eor>", Math.Min(eorIndex - 1, bufferStr.Length - 1), StringComparison.OrdinalIgnoreCase);
    
        return previousEor >= 0 ? previousEor + 5 : 0;
    }


    public static void owl()
    {
        // foreach (var lastQso in GetLastQsos("C:\\Users\\Administrator\\Desktop\\example.adif", 1000))
        // {
        //     Console.WriteLine(lastQso);
        //     Console.WriteLine("============================");
        // }
    }
}