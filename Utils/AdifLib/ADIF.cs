using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Threading;

/// <summary>
/// Main ADIF Class
/// This is the parent class of ADIFLib.
/// </summary>

namespace ADIFLib
{
    public class ADIF
    {
        /// <summary>
        /// The ADIF header
        /// </summary>
        public ADIFHeader TheADIFHeader;

        /// <summary>
        /// The collection of QSO records within the ADIF.
        /// </summary>
        public ADIFQSOCollection TheQSOs = new ADIFQSOCollection();

        /// <summary>
        /// Does the ADIF have a header?
        /// </summary>
        public bool HasHeader { get => TheADIFHeader != null; }

        /// <summary>
        /// Number of QSOs within the ADIF.
        /// </summary>
        public int QSOCount { get => (TheQSOs == null ? 0 : TheQSOs.Count); }

        /// <summary>
        /// Should an exception be thrown when a non-blank line doesn't end with <eoh> or <eor>?
        /// </summary>
        public bool ThrowExceptionOnUnknownLine = false;

        /// <summary>
        /// Instantiate an empty ADIF. 
        /// </summary>
        public ADIF()
        {
        }

        /// <summary>
        /// Instantiate an ADIF and populate it from the contents of specified file.
        /// </summary>
        /// <param name="FileName"></param>
        public ADIF(string FileName)
        {
            ReadFromFile(FileName);
        }

        /// <summary>
        /// Add the passed header to the ADIF.
        /// </summary>
        /// <param name="Header"></param>
        public void AddHeader(ADIFHeader Header)
        {
            TheADIFHeader = Header;
        }

        /// <summary>
        /// Parse and add the passed string as the ADIF header.
        /// </summary>
        /// <param name="RawHeader"></param>
        public void AddHeader(string RawHeader)
        {
            TheADIFHeader = new ADIFHeader(RawHeader);
        }

        /// <summary>
        /// Add the passed QSO to the ADIF.
        /// </summary>
        /// <param name="QSO"></param>
        public void AddQSO(ADIFQSO QSO)
        {
            TheQSOs.Add(QSO);
        }

        /// <summary>
        /// Parse and add the passed string as an ADIF QSO.
        /// </summary>
        /// <param name="RawQSO"></param>
        public void AddQSO(string RawQSO)
        {
            TheQSOs.Add(new ADIFQSO(RawQSO));
        }

        /// <summary>
        /// Save the ADIF to a file.
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="OverWrite"></param>
        public void SaveToFile(string FileName, bool OverWrite=false)
        {
            if (FileName == "")
            {
                throw new Exception("Filename cannot be empty!");
            }
            else
            {
                // If not overwriting and the file exists, then complain
                if (!OverWrite && File.Exists(FileName))
                {
                    throw new Exception(string.Format("File already exists: {0}", FileName));
                }
                else
                {
                    InternalSaveToFile(FileName, OverWrite); // Now, save to file.
                }
            }
        }

        /// <summary>
        /// Read a file into an ADIF file.
        /// </summary>
        /// <param name="FileName"></param>
        public void ReadFromFile(string FileName, int lastQsoCount = -1, CancellationToken cancellationToken = default)
        {
            uint lineNumber = 0;

            if (!File.Exists(FileName))
            {
                throw new Exception(string.Format("File does not exist: {0}", FileName));
            }
            else
            {
                using (var readThisFile = new StreamReader(FileName))
                {
                    try
                    {
                        ReadFromStream(readThisFile, ref lineNumber, lastQsoCount, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // rethrow with linenumber
                        throw new Exception(string.Format("{0} {1}:({2})", ex.Message, FileName, lineNumber.ToString()), ex);
                    }
                }
            }
        }
        
        /// <summary>
        /// Get ADIFLib version.
        /// </summary>
        public string Version
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var fileVerInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                return fileVerInfo.FileVersion;
            }
        }

        /// <summary>
        /// Return the entire ADIF as a ADIF formatted string.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var retCompleteADIF = new StringBuilder();
            retCompleteADIF.Append((HasHeader ? TheADIFHeader.ToString() : "")).Append(TheQSOs.ToString());
            return retCompleteADIF.ToString();
        }

        // Save the ADIF to a file.
        private void InternalSaveToFile(string FileName, bool Overwrite)
        {
            File.WriteAllText(FileName, this.ToString());
        }

        // Read from a stream.
        // Allow multiple lines per header or QSO.
        private void ReadFromStream(StreamReader TheStream, ref uint LineNumber, int lastQsoCount = -1, CancellationToken cancellationToken=default)
        {
            var theLine = "";
            var lastQsoLines = new Queue<string>();

            while (!TheStream.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                theLine += TheStream.ReadLine().Trim();
                if (theLine != "")
                {
                    if (theLine.ToUpper().EndsWith("<EOH>"))
                    {
                        if (TheADIFHeader != null)
                        {
                            throw new Exception(string.Format("File cannot contain more than one header. See line {0}", LineNumber.ToString()));
                        }
                        else
                        {
                            TheADIFHeader = new ADIFHeader(theLine);
                            LineNumber++;
                            theLine = "";
                        }
                    }
                    else if (theLine.ToUpper().EndsWith("<EOR>"))
                    {
                        if (lastQsoCount > 0)
                        {
                            // 使用队列来保持最后几个QSO
                            lastQsoLines.Enqueue(theLine);
                            if (lastQsoLines.Count > lastQsoCount)
                            {
                                lastQsoLines.Dequeue();
                            }
                        }
                        else
                        {
                            TheQSOs.Add(new ADIFQSO(theLine));
                        }
                        LineNumber++;
                        theLine = "";
                    }
                }
            }

            if (lastQsoCount > 0)
            {
                foreach (var qsoLine in lastQsoLines)
                {
                    TheQSOs.Add(new ADIFQSO(qsoLine));
                }
            }
        }

    }
}
