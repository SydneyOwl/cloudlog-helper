using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Token Class
/// What is a token?  For example, an ADIF token is this: <CALL:4>NV9U or <BAND:3>80M
/// Tokens are used in the QDIF header and QSO records.
/// </summary>

namespace ADIFLib
{
    public class Token
    {
        // publics
        public string Name { get => _name.Trim(); set { _name = value; UpdateLength(); } }
        public uint Length { get => _length; set => _length = value; }
        public char UserDefType { get => _UserDefType; set => _UserDefType = value; }
        public string Data { get => _data; set  { _data = value; UpdateLength(); } }
        public string EnumerationItems { get => _enumerationItems; set { _enumerationItems = value; UpdateLength(); } }
        public bool IsHeader { get => _isHeader; set {_isHeader = value; UpdateLength(); } }


        // locals
        private string _name = "";
        private uint _length = 0;
        private char _UserDefType = ' ';  // Single character enumerator type.
        private string _data = "";
        private string _enumerationItems = "";  // CSV of valid enumeration items - used for USERDEF items
        private bool _isHeader = false;  // Flag that indicates whether this is a header.

        /// <summary>
        /// Instantiate an empty token.
        /// </summary>
        public Token()
        { }

        /// <summary>
        /// Instantiate an ADIF Token
        /// </summary>
        /// <param name="TokenString">Single token string assuming there are no embedded special characters.</param>
        /// <param name="IsHeader">Specifies whether this token is a header or QSO token.</param>
        public Token(string TokenString, bool IsHeader)
        {
            this.IsHeader = IsHeader;
            if (TokenString.Trim() != "") /* if empty string is passed, assume no initialization */
            {
                ParseToken(TokenString);
            }
        }

        /// <summary>
        /// Instantiate an ADIF Token
        /// </summary>
        /// <param name="TokenString">Single token string assuming there are no embedded special characters.</param>
        public Token(string TokenString)
        {
            if (TokenString.Trim() != "") /* if empty string is passed, assume no initialization */
            {
                ParseToken(TokenString);
            }
        }

        /// <summary>
        /// Instantiate an ADIF token with Tag Name and Data.
        /// </summary>
        /// <param name="TagName"></param>
        /// <param name="Data"></param>
        /// <param name="IsHeader"></param>
        public Token(string TagName, string Data, bool IsHeader = false)
        {
            this._name = TagName;
            this._data = Data;
            this._isHeader = IsHeader;
        }

        /// <summary>
        /// Instantiate an ADIF token with specific header items.
        /// </summary>
        /// <param name="TagName"></param>
        /// <param name="Data"></param>
        /// <param name="DataType"></param>
        /// <param name="Enumerations"></param>
        public Token(string TagName, string Data, char DataType, string Enumerations = "")
        {
            this._name = TagName;
            this._data = Data;
            this._isHeader = true;  // If this instantiator is called, it must be a header.
            this.EnumerationItems = Enumerations;
            this._UserDefType = DataType;
        }


        private void ParseToken(string TokenString)
        {
            // Accept an ADIF token and populate the internal token items.
            string workingToken = TokenString.Trim().Substring((TokenString.Trim().StartsWith("<") ? 1 : 0)); // if it starts with a <, remove it.
            string workingINT = "";  // Working variable to read int values.
            int workingNDX = 0;  // Index used to walk through the token string.

            // Parse the working token.  Should be in the form of NAME:LENGTH[:ENUMTYPE]>value[,{ENUMERATIONITEMS}]
            // Throw an exception if the following is not met:
            // If this form is not valid for the passed string, throw an exception.  
            // If LENGTH does not match the length of value + optional ENUMERATIONITEMS, throw an exception.  
            // If ENUMTYPE is specified but there are no ENUMERATIONITEMS, throw an exception.  
            // If ENUMTYPE is specified, and NAME is not of the format USERDEFn, where n is an integer, throw an exception.

            // Name
            while (workingNDX < workingToken.Length - 1 && workingToken[workingNDX] != ':')
            {
                this.Name += workingToken[workingNDX];
                workingNDX++;
            }

            if (workingNDX >= workingToken.Length - 1)
                throw new Exception(string.Format("Invalid ADIF token string: {0}", workingToken));

            // Length
            workingNDX++;  // go to next character
            workingINT = "";  // reset
            while (workingNDX < workingToken.Length - 1 && workingToken[workingNDX] != ':' && workingToken[workingNDX] != '>')
            {
                workingINT += workingToken[workingNDX];
                workingNDX++;
            }

            if (workingINT == "")
                throw new Exception(string.Format("The LENGTH is required in the ADIF token string: {0}", workingToken));

            if (!uint.TryParse(workingINT, out _length))
                throw new Exception(string.Format("LENGTH must be an integer in the ADIF token string: {0}", workingToken));

            // Is there an expected ENUMERATIONTYPE?
            if (workingToken[workingNDX] == ':')
            {
                workingNDX++;
                // Get the ADIF enum type.  Must be a single character.
                _UserDefType= workingToken[workingNDX];
                workingNDX++;
            }

            // Current character should be a ">"
            if (workingToken[workingNDX] != '>')
                throw new Exception(string.Format("Invalid ADIF token string: {0}", workingToken));

            workingNDX++;
 //           this.RightSideJunk = workingToken.Substring(workingNDX);  // Get the string from the right of the tag for future testing.  

            // Commented out - This can happen and is valid if there is no data for the tag.
 //           if (workingNDX >= workingToken.Length)  /* -1 */
 //               throw new Exception(string.Format("Invalid ADIF token string: {0}", workingToken));

             // get value string - look for EOS, ',' or <
            while (workingNDX < workingToken.Length && workingToken[workingNDX] != '<')
            {
                if (workingToken[workingNDX] == ',' && this.Name.StartsWith("USERDEF"))
                    break;  // Stop when a comma is found, if handling a USERDEF tag.
                else
                {
                    this._data += workingToken[workingNDX];
                    workingNDX++;
                }
            }

            // Are there enumerations?
            if (workingNDX < workingToken.Length - 2)
            {
                if (workingToken[workingNDX] == ',' && workingToken[workingNDX + 1] == '{')
                {
                    workingNDX += 2;
                    while (workingNDX < workingToken.Length - 1 && workingToken[workingNDX] != '{' && workingToken[workingNDX] != '}')
                    {
                        EnumerationItems += workingToken[workingNDX];
                        workingNDX++;
                    }
                }
                else
                    throw new Exception(string.Format("Unexpected data after value: {0}", workingToken));
            }

        }

        /// <summary>
        /// Return the properly constructed ADIF tag.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            if (_isHeader)
                UpdateLength();  // Just be certain we have the updated length.

            // This builds the complete ADIF tag.
            string tagToReturn = string.Format("<{0}:{1}{2}>{3}{4}", _name, _length,
                (_isHeader && _UserDefType != ' ' ? ":" + _UserDefType.ToString() : ""),
                _data, (_isHeader && _enumerationItems != "" ? ",{" + _enumerationItems + "}" : ""));

            return tagToReturn;
        }

        /// <summary>
        /// Automatically update length field.
        /// </summary>
        private void UpdateLength()
        {
            // The constant 3 represents the comma and two brackets around the enumerations.
            // The addition of enumerations is only possible for USERDEF header tags. 
            // Basically, just take the length of data and if enumeration items exist, add the length of
            // those items plus 3.
            _length = (uint)_data.Length + (IsHeader && _name.ToUpper().StartsWith("USERDEF") && _enumerationItems!="" ? 3 + (uint)_enumerationItems.Length:0);
        }
    }
}
