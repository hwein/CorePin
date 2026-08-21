using System.Xml.Linq;
using CorePin.Core.Autostart;

namespace CorePin.Tests;

public static class AutostartTaskXmlTests
{
    private const string ExePath = @"C:\Program Files\CorePin\CorePin.exe";
    private const string UserName = @"DESKTOP-EXAMPLE\User";
    private const string UserSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static void Test_Build_ProducesElementValuesFromTheTaskSchema()
    {
        XElement? root = XDocument.Parse(AutostartTaskXml.Build(ExePath, UserName, UserSid)).Root;

        Assert.Equal("1.2", (string?)root?.Attribute("version"), "Task carries the schema version");

        XElement? registration = root?.Element(Ns + "RegistrationInfo");
        Assert.Equal("CorePin", registration?.Element(Ns + "Author")?.Value, "Author identifies the tool, not a user");
        Assert.Equal("Starts CorePin at sign-in with administrator rights.",
            registration?.Element(Ns + "Description")?.Value, "Description explains the task's purpose");

        XElement? principal = root?.Element(Ns + "Principals")?.Element(Ns + "Principal");
        Assert.Equal("Author", (string?)principal?.Attribute("id"), "the principal id matches the Actions context");
        Assert.Equal(UserSid, principal?.Element(Ns + "UserId")?.Value, "the principal runs as the given SID");
        Assert.Equal("InteractiveToken", principal?.Element(Ns + "LogonType")?.Value, "no password is stored");
        Assert.Equal("HighestAvailable", principal?.Element(Ns + "RunLevel")?.Value,
            "elevation without a UAC prompt is the point of the admin mode");

        XElement? settings = root?.Element(Ns + "Settings");
        Assert.Equal("false", settings?.Element(Ns + "DisallowStartIfOnBatteries")?.Value,
            "the autostart must not skip battery-powered sign-ins");
        Assert.Equal("false", settings?.Element(Ns + "StopIfGoingOnBatteries")?.Value, "unplugging must not kill CorePin");
        Assert.Equal("PT0S", settings?.Element(Ns + "ExecutionTimeLimit")?.Value, "the default three-day limit would end CorePin");
        Assert.Equal("5", settings?.Element(Ns + "Priority")?.Value, "priority 5 runs like a normal double-click, not below normal");

        XElement? trigger = root?.Element(Ns + "Triggers")?.Element(Ns + "LogonTrigger");
        Assert.Equal(UserName, trigger?.Element(Ns + "UserId")?.Value, "the trigger fires only for this account's own sign-in");

        XElement? actions = root?.Element(Ns + "Actions");
        Assert.Equal("Author", (string?)actions?.Attribute("Context"), "the action runs as the principal, not as SYSTEM");
        XElement? exec = actions?.Element(Ns + "Exec");
        Assert.Equal($"\"{ExePath}\"", exec?.Element(Ns + "Command")?.Value, "the command is quoted like Task Scheduler writes it itself");
        Assert.Equal("--tray", exec?.Element(Ns + "Arguments")?.Value, "the same flag the run key uses");
    }

    public static void Test_Build_AmpersandAndApostropheInPathSurviveTheRoundTrip()
    {
        const string path = @"D:\Spiele\Grüße & Söhne's Software\corepin.exe";
        string xml = AutostartTaskXml.Build(path, UserName, UserSid);

        Assert.True(xml.Contains("&amp;", StringComparison.Ordinal), "XmlWriter escapes & in element content");

        string? command = XDocument.Parse(xml).Root?.Element(Ns + "Actions")?.Element(Ns + "Exec")?.Element(Ns + "Command")?.Value;
        Assert.Equal($"\"{path}\"", command, "the parsed command matches the original path, apostrophe included");
    }

    public static void Test_Build_HasNoXmlDeclaration()
    {
        string xml = AutostartTaskXml.Build(ExePath, UserName, UserSid);

        Assert.True(xml.StartsWith("<Task ", StringComparison.Ordinal),
            "the service takes the XML as a string; a declaration would claim an encoding that never applied");
    }

    public static void Test_Read_FromFixture_ReturnsQuotedCommandAndPrincipalSid()
    {
        string xml = File.ReadAllText(FixturePath());

        var parsed = AutostartTaskXml.Read(xml);
        if (parsed is not { } result) throw new AssertionException("the frozen export must parse");

        Assert.Equal(ExePath, result.ExePath, "the surrounding quotes are stripped");
        Assert.Equal(UserSid, result.PrincipalSid, "the principal SID is read verbatim");
    }

    public static void Test_Read_UnquotedCommand_ReturnsPathUnchanged()
    {
        string xml =
            $"""
            <Task version="1.2" xmlns="{Ns}">
              <Actions Context="Author"><Exec><Command>C:\CorePin\CorePin.exe</Command></Exec></Actions>
            </Task>
            """;

        var parsed = AutostartTaskXml.Read(xml);
        if (parsed is not { } result) throw new AssertionException("a well-formed task must parse");

        Assert.Equal(@"C:\CorePin\CorePin.exe", result.ExePath, "a command without quotes needs no stripping");
    }

    public static void Test_Read_FirstOfTwoExecActionsWins()
    {
        string xml =
            $"""
            <Task version="1.2" xmlns="{Ns}">
              <Actions Context="Author">
                <Exec><Command>C:\First\CorePin.exe</Command></Exec>
                <Exec><Command>C:\Second\CorePin.exe</Command></Exec>
              </Actions>
            </Task>
            """;

        var parsed = AutostartTaskXml.Read(xml);
        if (parsed is not { } result) throw new AssertionException("a well-formed task must parse");

        Assert.Equal(@"C:\First\CorePin.exe", result.ExePath, "the first Exec action wins");
    }

    public static void Test_Read_MissingExec_ReturnsNull()
    {
        string xml =
            $"""
            <Task version="1.2" xmlns="{Ns}">
              <Actions Context="Author" />
            </Task>
            """;

        Assert.True(AutostartTaskXml.Read(xml) is null, "an Actions block without an Exec action carries no command to run");
    }

    public static void Test_Read_GarbageText_ReturnsNull()
    {
        Assert.True(AutostartTaskXml.Read("not xml at all") is null, "unparsable text is reported as absent, not thrown");
    }

    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "data", "autostart", "corepin-autostart.xml");
}
