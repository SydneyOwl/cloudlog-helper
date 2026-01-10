using System.Globalization;
using Avalonia.Markup.Xaml.MarkupExtensions;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class DXCCTranslationTest
{
    //Amsterdam_&_St._Paul_Is.

    private readonly ITestOutputHelper _testOutputHelper;
    // private readonly IChartDataCacheService<int> _cache = new ChartDataCacheService<int>();

    public DXCCTranslationTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestTranslation_ReturnsCorrectResult()
    {
        I18NExtension.Culture = new CultureInfo("zh-CN");
        Assert.Equal("安达曼和尼科巴岛", TranslationHelper.GetString(DXCCKeys.Andaman_Nicobar_Is));
        Assert.Equal("新胡安岛欧罗巴岛,欧洲", TranslationHelper.GetString(TranslationHelper.ParseToDXCCKey("Juan de Nova, Europa")));
        I18NExtension.Culture = new CultureInfo("aa-ER");
        Assert.Equal("Accept", TranslationHelper.GetString(LangKeys.accept));
    }
}