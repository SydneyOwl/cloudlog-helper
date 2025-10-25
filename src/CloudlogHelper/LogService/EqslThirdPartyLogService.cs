using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;
using HtmlAgilityPack;

namespace CloudlogHelper.LogService;

[LogService("eqsl.cc", Description = "eqsl Log Service")]
public class EqslThirdPartyLogService : ThirdPartyLogService
{
    private const string EqslTestEndpoint =
        "https://www.eQSL.cc/qslcard/DownloadInBox.cfm";

    private const string EqslQsoUploadEndpoint =
        "https://www.eQSL.cc/qslcard/importADIF.cfm";

    [UserInput("username")] public string Username { get; set; }

    [UserInput("password", InputType = FieldType.Password)]
    public string Password { get; set; }

    [UserInput("qthnickname", IsRequired = false)]
    public string QTHNickname { get; set; }


    public override async Task TestConnectionAsync(CancellationToken token)
    {
        var defaultParam =
            $"UserName={Uri.EscapeDataString(Username)}&Password={Uri.EscapeDataString(Password)}&LimitDateHi={Uri.EscapeDataString("01/01/1990")}";
        if (!string.IsNullOrEmpty(QTHNickname)) defaultParam += $"&QTHNickname={Uri.EscapeDataString(QTHNickname)}";
        var result = await EqslTestEndpoint
            .AppendQueryParam(defaultParam)
            .GetAsync(cancellationToken: token);
        var responseText = await result.GetStringAsync();
        if (!string.IsNullOrEmpty(responseText) && (responseText.Contains("Your ADIF log file has been built") ||
                                                    responseText.Contains("You have no log entries")))
            return;

        if (string.IsNullOrEmpty(responseText))
            throw new Exception("Unknown error occurred while testing eqsl connection!");

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@align='CENTER']/h3");

        if (bodyNode is null) throw new Exception("Unknown error occurred while testing eqsl connection!");
        var bodyText = bodyNode.InnerText.Trim();
        throw new Exception(string.IsNullOrEmpty(bodyText)
            ? "Unknown error occurred while testing eqsl connection!"
            : bodyText);
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        // optional;
        var header = new StringBuilder()
            .Append("<PROGRAMID:14>CloudlogHelper\r\n")
            .Append($"<PROGRAMVERSION:{VersionInfo.Version.Length}>{VersionInfo.Version}\r\n")
            .Append("<EOH>\r\n")
            .ToString();

        var param =
            $"ADIFData={Uri.EscapeDataString(adif)}&EQSL_USER={Uri.EscapeDataString(Username)}&EQSL_PSWD={Uri.EscapeDataString(Password)}";
        var results = await EqslQsoUploadEndpoint
            .AppendQueryParam(param)
            .GetAsync(cancellationToken: token);
        var responseText = await results.GetStringAsync();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);
        var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body");
        if (bodyNode is not null)
        {
            var bodyText = bodyNode.InnerText.Trim();
            if (bodyText.Contains("Result: 1 out of 1 records added")) return;
            if (bodyText.Contains("Result: 0 out of 1 records"))
            {
                if (bodyText.Contains("Duplicate")) throw new Exception("Fail log eQSL.cc (Duplicate)");
                throw new Exception("Fail log eQSL.cc due to unknown reason.");
            }

            if (bodyText.Contains("Error:")) throw new Exception(bodyText);
        }

        throw new Exception("Fail log eQSL.cc due to unknown reason.");
    }
}