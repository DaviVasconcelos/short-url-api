using System.Text.RegularExpressions;
using ShortUrl.Infrastructure.Security;

namespace ShortUrl.Tests.Generators;

public sealed class Base62CodeGeneratorTests
{
    private static readonly Regex Base62 = new("^[A-Za-z0-9]+$", RegexOptions.Compiled);

    private readonly Base62CodeGenerator _generator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void Generate_ReturnsRequestedLength(int length)
    {
        var code = _generator.Generate(length);

        Assert.Equal(length, code.Length);
    }

    [Fact]
    public void Generate_ReturnsOnlyBase62Characters()
    {
        var code = _generator.Generate(256);

        Assert.Matches(Base62, code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Generate_WithNonPositiveLength_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(length));
    }
}
