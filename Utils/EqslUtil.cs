using System;
using System.Linq;
using System.Text;
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
        try
        {


            var defaultParam =
                $"UserName={Uri.EscapeDataString(username)}&Password={Uri.EscapeDataString(password)}&LimitDateHi={Uri.EscapeDataString("01/01/1990")}";
            if (!string.IsNullOrEmpty(qthnickname)) defaultParam += $"&QTHNickname={Uri.EscapeDataString(qthnickname)}";
            var result = await DefaultConfigs.EqslTestEndpoint
                .AppendQueryParam(defaultParam)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
                .GetAsync();
            var responseText = await result.GetStringAsync();
            if (!string.IsNullOrEmpty(responseText) && (responseText.Contains("Your ADIF log file has been built") ||
                                                        responseText.Contains("You have no log entries")))
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(responseText)) return "Unknown error occurred while testing eqsl connection!";

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(responseText);

            var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@align='CENTER']/h3");

            if (bodyNode is null) return "Unknown error occurred while testing eqsl connection!";
            var bodyText = bodyNode.InnerText.Trim();
            return string.IsNullOrEmpty(bodyText) ? "Unknown error occurred while testing eqsl connection!" : bodyText;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static async Task<string> UploadQSOToEqslAsync(string username, string password, string adif)
    {
        try
        {
            // optional;
            var header = new StringBuilder()
                .Append("<PROGRAMID:14>CloudlogHelper\r\n")
                .Append($"<PROGRAMVERSION:{VersionInfo.Version.Length}>{VersionInfo.Version}\r\n")
                .Append("<EOH>\r\n")
                .ToString();

            var param =
                $"ADIFData={Uri.EscapeDataString(adif)}&EQSL_USER={Uri.EscapeDataString(username)}&EQSL_PSWD={Uri.EscapeDataString(password)}";
            var results = await DefaultConfigs.EqslQsoUploadEndpoint
                .AppendQueryParam(param)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
                .GetAsync();
            var responseText = await results.GetStringAsync();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(responseText);
            var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode is not null)
            {
                var bodyText = bodyNode.InnerText.Trim();
                if (bodyText.Contains("Result: 1 out of 1 records added")) return string.Empty;
                if (bodyText.Contains("Result: 0 out of 1 records"))
                {
                    if (bodyText.Contains("Duplicate")) return "Fail log eQSL.cc (Duplicate)";
                    return "Fail log eQSL.cc due to unknown reason.";
                }

                if (bodyText.Contains("Error:")) return bodyText;
                return "Fail log eQSL.cc due to unknown reason.";
            }

            return "Fail log eQSL.cc due to unknown reason.";
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}