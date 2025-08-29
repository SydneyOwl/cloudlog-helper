using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

/// <summary>
/// Main ADIF Class
/// This is the parent class of ADIFLib.
/// </summary>

namespace ADIFLib;

public class ADIF
{
    /// <summary>
    ///     The ADIF header
    /// </summary>
    public ADIFHeader TheADIFHeader;

    /// <summary>
    ///     The collection of QSO records within the ADIF.
    /// </summary>
    public ADIFQSOCollection TheQSOs = new();

    /// <summary>
    ///     Should an exception be thrown when a non-blank line doesn't end with <eoh> or <eor>?
    /// </summary>
    public bool ThrowExceptionOnUnknownLine = false;

    /// <summary>
    ///     Instantiate an empty ADIF.
    /// </summary>
    public ADIF()
    {
    }

    /// <summary>
    ///     Instantiate an ADIF and populate it from the contents of specified file.
    /// </summary>
    /// <param name="FileName"></param>
    public ADIF(string FileName)
    {
        ReadFromFile(FileName);
    }

    /// <summary>
    ///     Does the ADIF have a header?
    /// </summary>
    public bool HasHeader => TheADIFHeader != null;

    /// <summary>
    ///     Number of QSOs within the ADIF.
    /// </summary>
    public int QSOCount => TheQSOs == null ? 0 : TheQSOs.Count;

    /// <summary>
    ///     Get ADIFLib version.
    /// </summary>
    public string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVerInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fileVerInfo.FileVersion;
        }
    }

    /// <summary>
    ///     Add the passed header to the ADIF.
    /// </summary>
    /// <param name="Header"></param>
    public void AddHeader(ADIFHeader Header)
    {
        TheADIFHeader = Header;
    }

    /// <summary>
    ///     Parse and add the passed string as the ADIF header.
    /// </summary>
    /// <param name="RawHeader"></param>
    public void AddHeader(string RawHeader)
    {
        TheADIFHeader = new ADIFHeader(RawHeader);
    }

    /// <summary>
    ///     Add the passed QSO to the ADIF.
    /// </summary>
    /// <param name="QSO"></param>
    public void AddQSO(ADIFQSO QSO)
    {
        TheQSOs.Add(QSO);
    }

    /// <summary>
    ///     Parse and add the passed string as an ADIF QSO.
    /// </summary>
    /// <param name="RawQSO"></param>
    public void AddQSO(string RawQSO)
    {
        TheQSOs.Add(new ADIFQSO(RawQSO));
    }

    /// <summary>
    ///     Save the ADIF to a file.
    /// </summary>
    /// <param name="FileName"></param>
    /// <param name="OverWrite"></param>
    public void SaveToFile(string FileName, bool OverWrite = false)
    {
        if (FileName == "") throw new Exception("Filename cannot be empty!");

        // If not overwriting and the file exists, then complain
        if (!OverWrite && File.Exists(FileName))
            throw new Exception(string.Format("File already exists: {0}", FileName));

        InternalSaveToFile(FileName, OverWrite); // Now, save to file.
    }

    /// <summary>
    ///     Read a file into an ADIF file.
    /// </summary>
    /// <param name="FileName"></param>
    public void ReadFromFile(string FileName)
    {
        uint lineNumber = 0;

        if (!File.Exists(FileName)) throw new Exception(string.Format("File does not exist: {0}", FileName));

        using (var readThisFile = new StreamReader(FileName))
        {
            try
            {
                ReadFromStream(readThisFile, ref lineNumber, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // rethrow with linenumber
                throw new Exception(string.Format("{0} {1}:({2})", ex.Message, FileName, lineNumber.ToString()), ex);
            }
        }
    }

    /// <summary>
    ///     Return the entire ADIF as a ADIF formatted string.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        var retCompleteADIF = new StringBuilder();
        retCompleteADIF.Append(HasHeader ? TheADIFHeader.ToString() : "").Append(TheQSOs);
        return retCompleteADIF.ToString();
    }

    // Save the ADIF to a file.
    private void InternalSaveToFile(string FileName, bool Overwrite)
    {
        File.WriteAllText(FileName, ToString());
    }


    public void ReadFromString(string adifString, CancellationToken cancellationToken = default)
    {
        uint lineNumber = 0;

        var byteArray = Encoding.UTF8.GetBytes(adifString);
        using var memoryStream = new MemoryStream(byteArray);
        using var streamReader = new StreamReader(memoryStream);
        try
        {
            ReadFromStream(streamReader, ref lineNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error parsing ADIF string at line {lineNumber}: {ex.Message}");
        }
    }

    // Read from a stream.
    // Allow multiple lines per header or QSO.
    private void ReadFromStream(StreamReader TheStream, ref uint LineNumber, CancellationToken cancellation)
    {
        var theLine = new StringBuilder();

        while (!TheStream.EndOfStream && !cancellation.IsCancellationRequested)
        {
            var curLine = TheStream.ReadLine().Trim();
            // avoid naughty cases - someone's name contains <!
            if (curLine.ToUpper().Contains("<NAME"))
            {
                // Console.WriteLine("Escaping naughty fields");
                LineNumber++;
                continue;
            }

            ;
            theLine.Append(curLine);
            if (theLine.ToString() != "")
            {
                if (theLine.ToString().EndsWith("<EOH>", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (TheADIFHeader != null)
                        throw new Exception(string.Format("File cannot contain more than one header.  See line {0}",
                            LineNumber.ToString()));

                    TheADIFHeader = new ADIFHeader(theLine.ToString()); // Add the header.
                    LineNumber++;
                    theLine.Clear();
                }
                else
                {
                    if (theLine.ToString().EndsWith("<EOR>", StringComparison.InvariantCultureIgnoreCase))
                    {
                        TheQSOs.Add(new ADIFQSO(theLine.ToString()));
                        LineNumber++;
                        theLine.Clear();
                    }
                    // Line does not end with EOR or EOH
                    //    // Line does not end with <EOR> or <EOH>.  Throw exception?
                    //    if (ThrowExceptionOnUnknownLine)
                    //    {
                    //        throw new Exception(string.Format("Unknown line in ADIF file, line {0}", LineNumber.ToString()));
                    //    }
                    //    else
                    //    {
                    //        LineNumber++;
                    //    }
                }
            }
        }
        // If the last line ends with no EOF or EOH, just ignore. 
    }
}