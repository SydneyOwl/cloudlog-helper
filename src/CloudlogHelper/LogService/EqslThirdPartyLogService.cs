using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
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
        var result = EqslTestEndpoint
            .AppendQueryParam("UserName", Username, isEncoded: false)
            .AppendQueryParam("Password", Password, isEncoded: false)
            .AppendQueryParam("LimitDateHi", "01/01/1990", isEncoded: false);

        if (!string.IsNullOrEmpty(QTHNickname))
            result.AppendQueryParam("QTHNickname", QTHNickname, isEncoded: false);

        await result.GetAsync(cancellationToken: token).ConfigureAwait(false);
        var responseText = await result.GetStringAsync(cancellationToken: token).ConfigureAwait(false);
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
        var results = await EqslQsoUploadEndpoint
            .SetQueryParam("ADIFData", adif, isEncoded:false)
            .PostUrlEncodedAsync(new
            {
                EQSL_USER = Username,
                EQSL_PSWD = Password
            },cancellationToken: token).ConfigureAwait(false);
        var responseText = await results.GetStringAsync().ConfigureAwait(false);
        
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
            throw new Exception($"Fail log eQSL.cc due to unknown reason, please check adif: {bodyText}");
        }

        throw new Exception("Fail log eQSL.cc due to unknown reason.");
    }
}