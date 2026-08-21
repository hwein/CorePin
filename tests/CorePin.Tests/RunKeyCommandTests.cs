using CorePin.Core.Autostart;

namespace CorePin.Tests;

public static class RunKeyCommandTests
{
    public static void Test_Build_WrapsExePathInQuotesAndAppendsTrayFlag()
    {
        Assert.Equal(
            "\"C:\\Program Files\\CorePin\\CorePin.exe\" --tray",
            RunKeyCommand.Build(@"C:\Program Files\CorePin\CorePin.exe"),
            "the path is always quoted, even where the space is the only reason to");
    }

    public static void Test_Build_PreservesUnicodeCharacters()
    {
        Assert.Equal(
            "\"D:\\Käse\\Пример\\corepin.exe\" --tray",
            RunKeyCommand.Build(@"D:\Käse\Пример\corepin.exe"),
            "the exe path may live under a non-ASCII user or folder name");
    }

    public static void Test_ParseExePath_QuotedValueReturnsPathBetweenTheQuotes()
    {
        Assert.Equal(
            @"C:\Program Files\CorePin\CorePin.exe",
            RunKeyCommand.ParseExePath("\"C:\\Program Files\\CorePin\\CorePin.exe\" --tray"),
            "everything after the closing quote, including --tray, is not part of the path");
    }

    public static void Test_ParseExePath_UnquotedValueWithTrayFlagStripsIt()
    {
        Assert.Equal(
            @"C:\CorePin\CorePin.exe",
            RunKeyCommand.ParseExePath(@"C:\CorePin\CorePin.exe --tray"),
            "an unquoted value still carries the flag as a suffix");
    }

    public static void Test_ParseExePath_UnquotedValueWithUppercaseTrayFlagStripsIt()
    {
        Assert.Equal(
            @"C:\CorePin\CorePin.exe",
            RunKeyCommand.ParseExePath(@"C:\CorePin\CorePin.exe --TRAY"),
            "the flag comparison ignores case, the same way Matches does");
    }

    public static void Test_ParseExePath_UnquotedValueWithoutTrayFlagReturnsItTrimmed()
    {
        Assert.Equal(
            @"C:\CorePin\CorePin.exe",
            RunKeyCommand.ParseExePath(@"  C:\CorePin\CorePin.exe  "),
            "a hand-edited value may lack the flag entirely; only surrounding whitespace is removed");
    }

    public static void Test_ParseExePath_UnterminatedQuoteIsUnreadable()
    {
        Assert.True(
            RunKeyCommand.ParseExePath("\"C:\\CorePin\\CorePin.exe --tray") is null,
            "an opening quote without a matching closing quote cannot be split into a path");
    }

    public static void Test_ParseExePath_NullIsUnreadable()
    {
        Assert.True(RunKeyCommand.ParseExePath(null) is null, "a missing run value has no path to parse");
    }

    public static void Test_ParseExePath_EmptyIsUnreadable()
    {
        Assert.True(RunKeyCommand.ParseExePath("") is null, "an empty run value has no path to parse");
    }

    public static void Test_Matches_ExactBuildOutputMatches()
    {
        const string path = @"C:\CorePin\CorePin.exe";
        Assert.True(RunKeyCommand.Matches(RunKeyCommand.Build(path), path), "Build's own output matches itself");
    }

    public static void Test_Matches_CaseDifferenceOnlyStillMatches()
    {
        const string path = @"C:\CorePin\CorePin.exe";
        Assert.True(
            RunKeyCommand.Matches("\"C:\\COREPIN\\COREPIN.EXE\" --TRAY", path),
            "the registry value is compared ordinal-ignore-case, not exact");
    }

    public static void Test_Matches_ValueWithoutTrayFlagDoesNotMatch()
    {
        const string path = @"C:\CorePin\CorePin.exe";
        Assert.True(!RunKeyCommand.Matches("\"C:\\CorePin\\CorePin.exe\"", path), "a value missing the flag is not the value Build writes");
    }

    public static void Test_Matches_UnquotedValueDoesNotMatch()
    {
        const string path = @"C:\CorePin\CorePin.exe";
        Assert.True(!RunKeyCommand.Matches(@"C:\CorePin\CorePin.exe --tray", path), "Build always quotes the path");
    }

    public static void Test_Matches_NullValueDoesNotMatch()
    {
        Assert.True(!RunKeyCommand.Matches(null, @"C:\CorePin\CorePin.exe"), "a missing run value is not a match");
    }

    public static void Test_Matches_EmptyValueDoesNotMatch()
    {
        Assert.True(!RunKeyCommand.Matches("", @"C:\CorePin\CorePin.exe"), "an empty run value is not a match");
    }
}
