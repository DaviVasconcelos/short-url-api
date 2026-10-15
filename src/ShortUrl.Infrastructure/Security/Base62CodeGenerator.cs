using System.Security.Cryptography;
using ShortUrl.Domain.Abstractions;

namespace ShortUrl.Infrastructure.Security;

public sealed class Base62CodeGenerator : ICodeGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public string Generate(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");

        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}
