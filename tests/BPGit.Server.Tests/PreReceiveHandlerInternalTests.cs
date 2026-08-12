using BPGit.Server.GitHttp;
using Xunit;

namespace BPGit.Server.Tests;

public class PreReceiveHandlerInternalTests
{
    [Theory]
    [InlineData("<process name=\"MyProcess\"/>", "MyProcess")]
    [InlineData("<object name=\"Email - POP3/SMTP\"/>", "Email - POP3/SMTP")]
    [InlineData("<process name=\"X\" version=\"1.0\" bpversion=\"7.5\"/>", "X")]
    [InlineData("   <process name=\"WithWhitespace\"/>", "WithWhitespace")]
    public void ExtractProcessName_ReturnsNameFromXml(string xml, string expected)
    {
        Assert.Equal(expected, PreReceiveHandler.ExtractProcessName(xml));
    }

    [Fact]
    public void ExtractProcessName_ReturnsNull_WhenNoNameAttribute()
    {
        Assert.Null(PreReceiveHandler.ExtractProcessName("<process version=\"1.0\"/>"));
    }

    [Fact]
    public void ExtractProcessName_ReturnsNull_WhenEmptyString()
    {
        Assert.Null(PreReceiveHandler.ExtractProcessName(""));
    }

    [Fact]
    public void ExtractProcessName_ReturnsNull_WhenNonBpXml()
    {
        Assert.Null(PreReceiveHandler.ExtractProcessName("<other name=\"X\"/>"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("0000000000000000000000000000000000000000", true)]
    [InlineData("0", true)]
    [InlineData("deadbeef", false)]
    [InlineData("abc123", false)]
    public void IsZeroSha_DetectsZeroAndEmpty(string? sha, bool expected)
    {
        Assert.Equal(expected, PreReceiveHandler.IsZeroSha(sha));
    }
}