using System;

/// <summary>
/// ADIF QSO Class
/// </summary>

namespace ADIFLib
{
    public class ADIFQSO : TokenCollection
    {
        /// <summary>
        /// Instantiate ADIFQSO with no initial population.
        /// </summary>
        public ADIFQSO()
        { }

        /// <summary>
        /// Instantiate ADIFQSO with an initial QSO string to parse.
        /// </summary>
        /// <param name="ParseThisString"></param>
        public ADIFQSO(string ParseThisString)
        {
            ParseStringToADIFQSO(ParseThisString);
        }

        /// <summary>
        /// Instantiate ADIFQSO with an initial collection of name/data items.
        /// </summary>
        /// <param name="TagNameDataListForThisQSO"></param>
        public ADIFQSO(TokenNameDataList TagNameDataListForThisQSO)
        {
            foreach(TokenNameData anItem in TagNameDataListForThisQSO)
            {
                this.Add(new Token(anItem.TagName, anItem.Data));
            }
        }

        /// <summary>
        /// Populate this ADIFQSO with this QSO string.
        /// </summary>
        /// <param name="ParseThisString"></param>
        public void ParseStringToADIFQSO(string ParseThisString)
        {
            this.PullApartLine(ParseThisString, false);
        }

        /// <summary>
        /// Return the QSO as a proper ADIF string.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return base.ToString() + "<eor>";
        }
    }
}
