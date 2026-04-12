using CloudlogHelper.Enums;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Xunit;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class DXCCTranslationTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public DXCCTranslationTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestTranslation_ReturnsCorrectResult()
    {
        TranslationHelper.ApplyCulture(SupportedLanguage.SimplifiedChinese);
        Assert.Equal("新胡安岛欧罗巴岛,欧洲", TranslationHelper.GetString(TranslationHelper.ParseToDXCCKey("Juan de Nova, Europa")));
    }
}
