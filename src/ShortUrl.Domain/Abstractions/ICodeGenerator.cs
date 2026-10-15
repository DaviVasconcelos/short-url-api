namespace ShortUrl.Domain.Abstractions;

public interface ICodeGenerator
{
    string Generate(int length);
}
