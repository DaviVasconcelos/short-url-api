using ShortUrl.Tests.Support;
using Domain = ShortUrl.Domain;

namespace ShortUrl.Tests.Persistence;

public sealed class SqliteOwnershipTests
{
    [Fact]
    public void Short_url_can_reference_a_user_and_keep_a_null_owner()
    {
        using var context = new SqliteTestContext();

        var user = new Domain.User
        {
            Email = "owner@example.com",
            PasswordHash = "hash"
        };

        context.Db.Users.Add(user);
        context.Db.SaveChanges();

        context.Db.ShortUrls.AddRange(
            new Domain.ShortUrl
            {
                ShortCode = "owned01",
                OriginalUrl = "https://example.com/owned",
                OwnerId = user.Id
            },
            new Domain.ShortUrl
            {
                ShortCode = "anon001",
                OriginalUrl = "https://example.com/anon"
            });

        context.Db.SaveChanges();

        var owned = context.Db.ShortUrls.Single(x => x.ShortCode == "owned01");
        var anonymous = context.Db.ShortUrls.Single(x => x.ShortCode == "anon001");

        Assert.Equal(user.Id, owned.OwnerId);
        Assert.Null(anonymous.OwnerId);
    }
}
