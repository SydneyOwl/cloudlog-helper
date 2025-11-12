using CloudlogHelper.Models;

namespace CloudlogHelper.Tests;

public class HamlibSettingsTests
{
    [Fact]
    public async Task DoHamlibSettingsRaw_ReturnNone()
    {
        var tmp = new HamlibSettings();
        tmp.ReinitRules();
    }
}