using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;

namespace CloudlogHelper.Utils;

public static class RigUtils
{
    public static string GetDescriptionFromReturnCode(string code)
    {
        var codeDesMap = new Dictionary<string, string>
        {
            { "0", "Command completed successfully" },
            { "1", "Invalid parameter" },
            { "2", "Invalid configuration" },
            { "3", "Memory shortage" },
            { "4", "Feature not implemented" },
            { "5", "Communication timed out" },
            { "6", "IO error" },
            { "7", "Internal Hamlib error" },
            { "8", "Protocol error" },
            { "9", "Command rejected by the rig" },
            { "10", "Command performed, but arg truncated, result not guaranteed" },
            { "11", "Feature not available" },
            { "12", "Target VFO unaccessible" },
            { "13", "Communication bus error" },
            { "14", "Communication bus collision" },
            { "15", "NULL RIG handle or invalid pointer parameter" },
            { "16", "Invalid VFO" },
            { "17", "Argument out of domain of func" },
            { "18", "Function deprecated" },
            { "19", "Security error password not provided or crypto failure" },
            { "20", "Rig is not powered on" },
            { "21", "Limit exceeded" },
            { "22", "Access denied" }
        };
        if (codeDesMap.TryGetValue(code, out var result)) return "Hamlib error:" + result;
        return "Failed to init hamlib!";
    }

    public static string GenerateRigctldCmdArgs(string radioId, string port, bool disablePtt = false,
        bool allowExternal = false)
    {
        var args = new StringBuilder();
        args.Append($"-m {radioId} ");
        args.Append($"-r {port} ");

        var defaultHost = IPAddress.Loopback.ToString();
        if (allowExternal) defaultHost = IPAddress.Any.ToString();
        args.Append($"-T {defaultHost} -t {DefaultConfigs.RigctldDefaultPort} ");

        if (disablePtt) args.Append(@"--set-conf=""rts_state=OFF"" --set-conf ""dtr_state=OFF"" ");

        args.Append("-vvvvv");
        return args.ToString();
    }

    public static List<(int Start, int End)> GetColumnBounds(string header)
    {
        var bounds = new List<(int, int)>();
        var columnNames = new[] { "Rig #", "Mfg", "Model", "Version", "Status", "Macro" };

        var currentPos = 0;
        foreach (var column in columnNames)
        {
            var start = header.IndexOf(column, currentPos, StringComparison.Ordinal);
            if (start == -1) throw new FormatException($"Column '{column}' not found in header.");

            var end = column == columnNames.Last()
                ? 9999
                : header.IndexOf(columnNames[Array.IndexOf(columnNames, column) + 1], start, StringComparison.Ordinal);

            bounds.Add((start, end));
            currentPos = end;
        }

        return bounds;
    }

    public static RigInfo ParseRigLine(string line, List<(int Start, int End)> bounds)
    {
        var info = new RigInfo
        {
            Id = GetColumnValue(line, bounds[0]).Trim(),
            Manufacturer = GetColumnValue(line, bounds[1]).Trim(),
            Model = GetColumnValue(line, bounds[2]).Trim(),
            Version = GetColumnValue(line, bounds[3]).Trim(),
            Status = GetColumnValue(line, bounds[4]).Trim(),
            Macro = GetColumnValue(line, bounds[5]).Trim()
        };
        if (string.IsNullOrEmpty(info.Model))
            // e.g. RIG_MODEL_FLRIG
            info.Model = info.Macro;

        return info;
    }

    private static string GetColumnValue(string line, (int Start, int End) bound)
    {
        if (line.Length <= bound.Start) return string.Empty;
        var length = Math.Min(bound.End - bound.Start, line.Length - bound.Start);
        return line.Substring(bound.Start, length);
    }
}