using GORT.Security.Models;

namespace GORT.Tests.Integration.Security;

public class NAbilityTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Parse_ThreeAndFourSegments_RoundTrips()
    {
        Assert.Equal("storage:pool:read", NAbility.Parse("storage:pool:read").ToString());
        Assert.Equal("storage:pool:main:read", NAbility.Parse("storage:pool:main:read").ToString());
        Assert.Equal("main", NAbility.Parse("storage:pool:main:read").Scope);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_Wildcards_SupportSingleAndRemainingSegments()
    {
        Assert.True(NAbility.Parse("storage:pool:*:read").Matches(NAbility.Parse("storage:pool:main:read")));
        Assert.True(NAbility.Parse("storage:**").Matches(NAbility.Parse("storage:share:media:write")));
        Assert.True(NAbility.Parse("admin:**").Matches(NAbility.Parse("admin:user:delete")));
        Assert.False(NAbility.Parse("storage:pool:*:read").Matches(NAbility.Parse("storage:pool:main:write")));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("storage")]
    [InlineData("storage::read")]
    [InlineData("storage:**suffix:read")]
    public void Parse_InvalidInput_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => NAbility.Parse(value));
    }
}
