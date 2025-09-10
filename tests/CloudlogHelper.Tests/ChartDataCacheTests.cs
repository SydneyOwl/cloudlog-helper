using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Tests;

public class ChartDataCacheTests
{
    private readonly IChartDataCacheService<int> _cache = new ChartDataCacheService<int>();
    
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
        _cache.Add(333);
        _cache.Add(444);
        _cache.Add(555);

        var takeLatestN = _cache.TakeLatestN(10).TakeLast(5);
        Assert.Equal(takeLatestN, new []{111,222,333,444,555});
    }
}