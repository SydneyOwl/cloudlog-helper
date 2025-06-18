using System;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;
using HtmlAgilityPack;
using NLog;

namespace CloudlogHelper.Utils;

public class EqslUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    
    public static async Task<string> TestEqslConnectionAsync(string username,string password, string? qthnickname)
    {
        var defaultParam = $"UserName={Uri.EscapeDataString(username)}&Password={Uri.EscapeDataString(password)}&LimitDateHi={Uri.EscapeDataString("01/01/1990")}";
        if (!string.IsNullOrEmpty(qthnickname)) defaultParam += $"&QTHNickname={Uri.EscapeDataString(qthnickname)}";
        var result = await DefaultConfigs.EqslTestEndpoint
            .AppendQueryParam(defaultParam)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .GetAsync();
        var responseText = await result.GetStringAsync();
        if (!string.IsNullOrEmpty(responseText) && (responseText.Contains("Your ADIF log file has been built") || responseText.Contains("You have no log entries")))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(responseText))return "Unknown error occurred while testing eqsl connection!";
        
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@align='CENTER']/h3");

        if (bodyNode is null) return "Unknown error occurred while testing eqsl connection!";
        var bodyText = bodyNode.InnerText.Trim();
        return string.IsNullOrEmpty(bodyText) ? "Unknown error occurred while testing eqsl connection!" : bodyText;
    }
}