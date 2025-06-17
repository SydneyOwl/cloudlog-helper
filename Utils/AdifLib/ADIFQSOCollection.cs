using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// List of QSOs
/// </summary>

namespace ADIFLib;

public class ADIFQSOCollection : List<ADIFQSO>
{
    /// <summary>
    ///     Return the list of QSOs as a string containing all QSOs.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        var retQSOs = new StringBuilder();
        foreach (var qso in this) retQSOs.Append(qso).Append(Environment.NewLine);
        return retQSOs.ToString();
    }
}