using CloudlogHelper.Resources;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class RigUtilsTests
{
    private const int DefaultPort = DefaultConfigs.RigctldDefaultPort;

    [Fact]
    public void GenerateRigctldCmdArgs_WithoutDisablingSignalLines_OmitsSetConf()
    {
        var args = RigUtils.GenerateRigctldCmdArgs("1006", "/dev/ttyUSB0");

        Assert.DoesNotContain("--set-conf", args, StringComparison.Ordinal);
        Assert.Contains($"-m 1006 -r /dev/ttyUSB0 -T 127.0.0.1 -t {DefaultPort} -vvvvv", args,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateRigctldCmdArgs_WithDisablingSignalLines_PassesBothLinesInOneFlag()
    {
        var args = RigUtils.GenerateRigctldCmdArgs("1006", "/dev/ttyUSB0", disablePtt: true);

        // rigctld splits --set-conf on ',' (Hamlib tests/rigctld.c: strtok(conf_parms, ",")).
        // Repeating the flag only works on getopt implementations that accept a space
        // separator, and the quotes the old code emitted leaked into argv.
        Assert.Contains("--set-conf=rts_state=OFF,dtr_state=OFF", args, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", args, StringComparison.Ordinal);

        // exactly one --set-conf token, not two
        Assert.Equal(1, CountOccurrences(args, "--set-conf"));
    }

    [Fact]
    public void GenerateRigctldCmdArgs_WithDisablingSignalLines_IsAcceptedByRigctldGetopt()
    {
        var args = RigUtils.GenerateRigctldCmdArgs("1006", "/dev/ttyUSB0", disablePtt: true);

        // a bare "--set-conf" without '=' would only parse on implementations that
        // tolerate a space separator; Hamlib's man page requires the '=' form
        Assert.DoesNotContain("--set-conf ", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "127.0.0.1")]
    [InlineData(true, "0.0.0.0")]
    public void GenerateRigctldCmdArgs_BindsLoopbackUnlessExternalControlIsAllowed(bool allowExternal,
        string expectedHost)
    {
        var args = RigUtils.GenerateRigctldCmdArgs("1006", "/dev/ttyUSB0", false, allowExternal);

        Assert.Contains($"-T {expectedHost} -t {DefaultPort}", args, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var index = source.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}

