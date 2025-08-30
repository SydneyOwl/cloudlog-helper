using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class FreqCalcTests
{
    [Theory]
    [InlineData(144174000,"144.17400")]
    [InlineData(14074000,"14.07400")]
    [InlineData(7074000, "7.07400")]
    [InlineData(136000, "0.13600")]
    public void TestHzToMHZ_ReturnsCorrectResult(long hz, string expected)
    {
        Assert.Equal(FreqHelper.GetFrequencyStr(hz, false), expected);
    }
    
    [Theory]
    [InlineData(7040010,true)]
    [InlineData(14074000,false)]
    [InlineData(1296501400, true)]
    [InlineData(136000, false)]
    [InlineData(144489900, true)]
    [InlineData(14096000, false)]
    public void TestChkWSPR2Freq_ReturnsCorrectResult(long hz, bool expected)
    {
        Assert.Equal(FreqHelper.CheckIsWSPR2(hz), expected);
    }
    
    [Theory]
    [InlineData(135800, "2200m")]
    [InlineData(7074000,"40m")]
    [InlineData(14074000,"20m")]
    [InlineData(220000000, "1.25m")]
    [InlineData(1240000000, "23cm")]
    public void TestChkFreqToMeter_ReturnsCorrectResult(long hz, string expected)
    {
        Assert.Equal(FreqHelper.GetMeterFromFreq(hz), expected);
    }
}