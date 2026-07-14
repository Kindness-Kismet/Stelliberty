using Stelliberty.Infrastructure.Platform;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class AutoStartEntryBuilderTests
{
    [Fact(DisplayName = "Windows scheduled task XML starts after one second without elevating the app")]
    public void WindowsScheduledTaskXmlStartsAfterOneSecondWithoutElevatingTheApp()
    {
        var xml = AutoStartEntryBuilder.WindowsScheduledTaskXml(
            @"C:\Program Files\Stelliberty\stelliberty.exe",
            isSilentStartEnabled: true,
            userId: "S-1-5-21-test");

        Assert.Contains("<LogonTrigger>", xml);
        Assert.Contains("<Delay>PT1S</Delay>", xml);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml);
        Assert.Contains("<UserId>S-1-5-21-test</UserId>", xml);
        Assert.DoesNotContain("<Arguments>", xml);
    }

    [Fact(DisplayName = "Windows scheduled task XML escapes path and omits silent argument")]
    public void WindowsScheduledTaskXmlEscapesPathAndOmitsSilentArgument()
    {
        var xml = AutoStartEntryBuilder.WindowsScheduledTaskXml(
            @"C:\A&B\stelliberty.exe",
            isSilentStartEnabled: false);

        Assert.Contains(@"<Command>C:\A&amp;B\stelliberty.exe</Command>", xml);
        Assert.DoesNotContain("<Arguments>", xml);
    }
}
