using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class ChartDataCacheTests
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IChartDataCacheService<int> _cache = new ChartDataCacheService<int>();

    public ChartDataCacheTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestLargeDatasetCache_ReturnsCorrectResult()
    {
        var random = new Random();
        for (var i = 0; i < 50000; i++)
        {
            _cache.Add(random.Next());
        }
        _cache.Add(111);
        _cache.Add(222);
        _cache.Add(373);
        _cache.Add(444);
        _cache.Add(555);
        
        var takeLatestN = _cache.TakeLatestN(10).Take(5).Reverse();
        Assert.Equal(takeLatestN, new []{111,222,373,444,555});
    }
}