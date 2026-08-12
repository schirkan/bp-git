using BPGit.Server.Services;
using Xunit;

namespace BPGit.Server.Tests;

public class BpSyncServiceInternalHelperTests
{
    [Fact]
    public void StripLeadingXmlComments_RemovesAllLeadingComments()
    {
        var withLeading = "<!-- c1 --><!-- c2 -->\n<process name=\"X\"/>";
        var stripped = BpSyncService.StripLeadingXmlComments(withLeading);
        Assert.StartsWith("<process", stripped);
        Assert.DoesNotContain("<!--", stripped);
    }

    [Fact]
    public void StripLeadingXmlComments_PreservesNonLeadingComments()
    {
        var withMiddle = "<process><!-- middle --></process>";
        Assert.Equal(withMiddle, BpSyncService.StripLeadingXmlComments(withMiddle));
    }

    [Fact]
    public void StripLeadingXmlComments_NoComments_ReturnsUnchanged()
    {
        var noComments = "<process name=\"X\"/>";
        Assert.Equal(noComments, BpSyncService.StripLeadingXmlComments(noComments));
    }

    [Fact]
    public void StripLeadingXmlComments_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", BpSyncService.StripLeadingXmlComments(""));
    }

    [Theory]
    [InlineData("<!-- hi -->\n<object name=\"O\"/>", "<object")]
    [InlineData("   <!-- ws-prefix -->\n<process name=\"P\"/>", "<process")]
    [InlineData("<!--a--><!--b-->\n<!--c-->  <process name=\"P\"/>", "<process")]
    public void StripLeadingXmlComments_VariousLeadingShapes_AllStripped(string input, string expectedPrefix)
    {
        var result = BpSyncService.StripLeadingXmlComments(input);
        Assert.StartsWith(expectedPrefix, result);
    }
}