using System.Collections.Generic;
using System.Text;

/// <summary>
/// Collection of QSO tokens
/// </summary>

namespace ADIFLib
{
    // This class is common between ADIFHeader and ADIFQSO.

    public class TokenCollection : List<Token>
    {
        public TokenCollection()
        { }

        /// <summary>
        /// Instantiate a collection of tokens.  Header or QSO is determined by the trailing tag.
        /// </summary>
        /// <param name="LineToPullApart"></param>
        public TokenCollection(string LineToPullApart)
        {
            PullApartLine(LineToPullApart);
        }

        /// <summary>
        /// Instantiate a collection of tokens.  Header or token is specified.
        /// </summary>
        /// <param name="LineToPullApart"></param>
        /// <param name="IsHeader"></param>
        public TokenCollection(string LineToPullApart, bool IsHeader)
        {
            PullApartLine(LineToPullApart, IsHeader);
        }

        /// <summary>
        /// Pull apart a line of text and parse into a collection of tokens.  Header or QSO is determined by the trailing tag.
        /// </summary>
        /// <param name="LineToPullApart"></param>
        public void PullApartLine(string LineToPullApart)
        {
            InternalPullApart(LineToPullApart, LineToPullApart.ToUpper().EndsWith("<EOH>"));
        }

        /// <summary>
        /// Pull apart a line of text and parse into a collection of tokens.  Header or token is specified.
        /// </summary>
        /// <param name="LineToPullApart"></param>
        /// <param name="IsHeader"></param>
        public void PullApartLine(string LineToPullApart, bool IsHeader)
        {
            InternalPullApart(LineToPullApart, IsHeader);
        }

        /// <summary>
        /// Internal method that performs the function of pulling apart the passed string into individual tokens.
        /// </summary>
        /// <param name="LineToPullApart"></param>
        /// <param name="IsHeader"></param>
        private void InternalPullApart(string LineToPullApart, bool IsHeader)
        {
            // Do the heavy lifting of pulling apart the line and parsing into individual tokens.
            string[] tokenStrings = LineToPullApart.Split(new char[] {'<'}, System.StringSplitOptions.RemoveEmptyEntries);

            // loop through each token
            foreach (string tokenString in tokenStrings)
            {
                if (!"EOH>~EOR>".Contains(tokenString.ToUpper()))  /* Don't parse the terminator if it is <EOH> or <EOR> */
                    this.Add(new Token("<" + tokenString, IsHeader));
            }
        }

        /// <summary>
        /// Add a token.
        /// </summary>
        /// <param name="TokenString"></param>
        /// <param name="IsHeader"></param>
        public void AddToken(string TokenString, bool IsHeader)
        {
            this.Add(new Token(TokenString, IsHeader));
        }

        /// <summary>
        /// Add a token.
        /// </summary>
        /// <param name="TokenString"></param>
        public void AddToken(string TokenString)
        {
            this.Add(new Token(TokenString));
        }

        /// <summary>
        /// Add a token.
        /// </summary>
        /// <param name="TagName"></param>
        /// <param name="Data"></param>
        /// <param name="IsHeader"></param>
        public void AddToken(string TagName, string Data, bool IsHeader = false)
        {
            this.Add(new Token(TagName, Data, IsHeader));
        }

        /// <summary>
        /// Add a token.
        /// </summary>
        /// <param name="TagName"></param>
        /// <param name="Data"></param>
        /// <param name="DataType"></param>
        /// <param name="Enumerations"></param>
        public void AddToken(string TagName, string Data, char DataType, string Enumerations = "")
        {
            this.Add(new Token(TagName, Data, DataType, Enumerations));
        }

        /// <summary>
        /// Add a token.
        /// </summary>
        /// <param name="TheTokenData"></param>
        public void AddToken(TokenNameData TheTokenData)
        {
            this.Add(new Token(TheTokenData.TagName, TheTokenData.Data));
        }

        /// <summary>
        /// Add several tokens.
        /// </summary>
        /// <param name="TheTokenNameDataList"></param>
        public void AddTokens(TokenNameDataList TheTokenNameDataList)
        {
            foreach(TokenNameData AnItem in TheTokenNameDataList)
            {
                this.Add(new Token(AnItem.TagName, AnItem.Data));
            }
        }
        

        /// <summary>
        /// Return the token collection as a proper ADIF string.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder retTokenCollection = new StringBuilder();
            foreach (Token thisToken in this)
            {
                retTokenCollection.Append(thisToken.ToString()).Append(" ");
            }
            return retTokenCollection.ToString();
        }


    }

}

