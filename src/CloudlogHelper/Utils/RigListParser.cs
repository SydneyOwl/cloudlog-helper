using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Models;

namespace CloudlogHelper.Utils;

public static class RigListParser
{
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