using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// List of QSOs
/// </summary>

namespace ADIFLib
{
    public class ADIFQSOCollection : List<ADIFQSO>
    {
        /// <summary>
        /// Return the list of QSOs as a string containing all QSOs.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder retQSOs = new StringBuilder();
            foreach (ADIFQSO qso in this)
            {
                retQSOs.Append(qso.ToString()).Append(Environment.NewLine);
            }
            return retQSOs.ToString();
        }

    }
}
