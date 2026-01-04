using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CloudlogHelper.Enums;
using CloudlogHelper.Exceptions;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using NLog;

namespace CloudlogHelper.Services;

public class FLRigService : IRigService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public void Dispose()
    {
    }

    public RigBackendServiceEnum GetServiceType()
    {
        return RigBackendServiceEnum.FLRig;
    }

    public Task StartService(CancellationToken token, params object[] args)
    {
        ClassLogger.Info("Starting FLRig.");
        return Task.CompletedTask;
    }

    public Task StopService(CancellationToken token)
    {
        ClassLogger.Info("Stopping FLRig.");
        return Task.CompletedTask;
    }

    public bool IsServiceRunning()
    {
        return true;
    }

    public Task<RigInfo[]> GetSupportedRigModels()
    {
        return Task.FromResult(Array.Empty<RigInfo>());
    }

    public async Task<string> GetServiceVersion(params object[] args)
    {
        var ip = args[0].ToString();
        var port = args[1].ToString();
        return _getResultValue(await _sendXMLCmd(ip, port, "main.get_version"));
    }

    public async Task<RadioData> GetAllRigInfo(bool reportRfPower, bool reportSplitInfo, CancellationToken token,
        params object[] args)
    {
        var ip = args[0].ToString();
        var port = args[1].ToString();
        var testbk = new RadioData();

        var freqStr = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_vfo"));
        var mode = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_mode"));

        if (!long.TryParse(freqStr, out var freq))
            throw new RigCommException(TranslationHelper.GetString(LangKeys.unsupportedrigfreq) + freqStr);

        // if (!DefaultConfigs.AvailableRigModes.Contains(mode))
        //     throw new RigCommException(TranslationHelper.GetString(LangKeys.unsupportedrigmode + mode));

        testbk.FrequencyRx = freq;
        testbk.FrequencyTx = freq;
        testbk.ModeRx = mode;
        testbk.ModeTx = mode;

        if (reportRfPower)
        {
            var powerStr = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_power"));
            if (!float.TryParse(powerStr, out var power))
                throw new RigCommException("Invalid rig power!");

            testbk.Power = power;
        }

        if (reportSplitInfo)
        {
            var split = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_split"));
            if (split == "0")
            {
                ClassLogger.Debug("Split is off");
            }
            else
            {
                var txFreqStr = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_vfoB"));
                var txMode = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_modeB"));

                if (!long.TryParse(txFreqStr, out var txFreq))
                    throw new RigCommException(TranslationHelper.GetString(LangKeys.unsupportedrigfreq) + freqStr);

                // We no longer check if tx mode is available due to complex flrig digi modes
                // if (!DefaultConfigs.AvailableRigModes.Contains(txMode))
                //     throw new RigCommException(TranslationHelper.GetString(LangKeys.unsupportedrigmode) + mode);

                testbk.ModeTx = txMode;
                testbk.FrequencyTx = txFreq;
            }
        }

        var rigName = _getResultValue(await _sendXMLCmd(ip, port, "rig.get_xcvr"));
        testbk.RigName = rigName;
        return testbk;
    }

    private async Task<string> _sendXMLCmd(string ip, string port, string cmd)
    {
        var template = $"<?xml version=\"1.0\"?><methodCall><methodName>{cmd}</methodName></methodCall>";
        var targetServer = $"http://{ip}:{port}";
        return await targetServer
            .WithHeader("Content-Type", "application/x-www-form-urlencoded; charset=utf-8")
            .PostStringAsync(template)
            .ReceiveString();
    }

    private string _getResultValue(string raw)
    {
        var xDoc = XDocument.Parse(raw);

        var faultElement = xDoc.Descendants("fault").FirstOrDefault();
        if (faultElement != null)
        {
            var faultCode =
                faultElement.Descendants("name").FirstOrDefault(n => n.Value == "faultCode")?.Parent?.Element("value")
                    ?.Value ?? "Unknown Code";
            var faultString =
                faultElement.Descendants("name").FirstOrDefault(n => n.Value == "faultString")?.Parent?.Element("value")
                    ?.Value ?? "Unknown Error";
            throw new Exception($"XML-RPC Fault (Code {faultCode}): {faultString}");
        }

        var valueElement = xDoc.Descendants("value").FirstOrDefault();
        if (valueElement != null) return valueElement.Value;

        throw new Exception("Invalid XML-RPC response: response is neither a param nor a fault.");
    }
}