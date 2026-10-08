using RedNote.Authentication;
using RedNote.ContentService.Domain.Posts;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class ModerationTests
{
    [Fact]
    public void RestoringAParentPreservesAReplysOwnModeration()
    {
        var reply = new PostComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "reply", Guid.NewGuid());
        reply.SetHidden(true);
        reply.SetParentHidden(true);
        reply.SetParentHidden(false);
        Assert.True(reply.IsHidden);
        Assert.False(reply.IsParentHidden);
        Assert.Equal(4, reply.Revision);
        reply.Delete();
        Assert.Throws<InvalidOperationException>(() => reply.SetHidden(false));
    }

    [Fact]
    public void DeletedPostCannotBeRestoredByModeration()
    {
        var post = new Post(Guid.NewGuid(), Guid.NewGuid(), "title", "body");
        post.SetHidden(true);
        post.Delete();
        Assert.Throws<InvalidOperationException>(() => post.SetHidden(false));
        Assert.Equal(PostStatus.Deleted, post.Status);
    }

    [Fact]
    public void FixedRolesGrantOnlyTheirOwnPermissions()
    {
        Assert.Equal([AdminAuthorization.Moderate], AdminAuthorization.Permissions(["ContentModerator"]));
        Assert.Equal([AdminAuthorization.Users], AdminAuthorization.Permissions(["UserAdministrator"]));
        Assert.Equal([AdminAuthorization.Audit], AdminAuthorization.Permissions(["AuditReader"]));
        Assert.Empty(AdminAuthorization.Permissions(["Administrator", "unknown"]));
    }
}
