using System.Globalization;
using Avalonia.Markup.Xaml.MarkupExtensions;
using CloudlogHelper.Database;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class DatabaseTests
{
    private readonly ITestOutputHelper _testOutputHelper;
    public DatabaseTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact(Skip = "NO Need to run")]
    public void Test_ReturnsCorrectResult()
    {
            var callsigns = new List<CallsignDatabase>();
            var countries = new List<CountryDatabase>();

            var result = File.ReadAllText("/home/sydneyowl/Downloads/bigcty-20260103 (4)/cty.dat");
            var st = result.Split(";");
            for (var i = 0; i < st.Length; i++)
            {
                if (!st[i].Contains(":")) continue;
                var cdb = new CountryDatabase(st[i]);
                cdb.Id = i + 1;

                countries.Add(cdb);
                # region callsign

                if (!st[i].Contains(":")) continue;
                var info = st[i].Split(":");
                if (info.Length < 9) continue;
                var ls = info[8].Replace("\n", "").Split(",");
                for (var j = 0; j < ls.Length; j++)
                {
                    if (ls[j].Contains(")")) ls[j] = ls[j].Substring(0, ls[j].IndexOf("("));
                    if (ls[j].Contains("[")) ls[j] = ls[j].Substring(0, ls[j].IndexOf("["));
                    callsigns.Add(new CallsignDatabase
                    {
                        Callsign = ls[j].Trim(),
                        CountryId = i + 1
                    });
                }
                # endregion
            }

        _testOutputHelper.WriteLine($"Got countries: {countries.Count}");
        _testOutputHelper.WriteLine($"Got callsigns: {callsigns.Count}");
    }
    
}