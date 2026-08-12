using BPGit.Server.Services;
using Xunit;

namespace BPGit.Server.Tests;

public class WorktreeSyncServiceTests
{
    [Theory]
    [InlineData("MyProcess", "MyProcess")]
    [InlineData("My Process", "My Process")]
    [InlineData("Email - POP3/SMTP/IMAP", "Email - POP3_SMTP_IMAP")]
    [InlineData(@"a/b\c:d*e?f""g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("trailing...", "trailing")]
    [InlineData("trailing   ", "trailing")]
    public void SanitizeFilename_ReplacesInvalidCharsAndTrims(string input, string expected)
    {
        var result = WorktreeSyncService.SanitizeFilename(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeFilename_EmptyOrWhitespace_ReturnsUnderscore()
    {
        Assert.Equal("_", WorktreeSyncService.SanitizeFilename(""));
        Assert.Equal("_", WorktreeSyncService.SanitizeFilename("   "));
    }

    [Fact]
    public void StripLeadingXmlComments_RemovesAllLeadingComments()
    {
        var withLeading = "<!-- c1 --><!-- c2 -->\n<process name=\"X\"/>";
        var stripped = WorktreeSyncService.StripLeadingXmlComments(withLeading);
        Assert.StartsWith("<process", stripped);
    }

    [Fact]
    public void StripLeadingXmlComments_PreservesMiddleComments()
    {
        var withMiddle = "<process><!-- middle --></process>";
        var stripped = WorktreeSyncService.StripLeadingXmlComments(withMiddle);
        Assert.Equal(withMiddle, stripped);
    }

    [Fact]
    public void StripLeadingXmlComments_NoComments_ReturnsUnchanged()
    {
        var noComments = "<process name=\"X\"/>";
        Assert.Equal(noComments, WorktreeSyncService.StripLeadingXmlComments(noComments));
    }

    [Fact]
    public void StripLeadingXmlComments_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", WorktreeSyncService.StripLeadingXmlComments(""));
    }
}