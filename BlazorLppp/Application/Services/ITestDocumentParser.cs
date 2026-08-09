using BlazorLppp.Application.Models;

namespace BlazorLppp.Application.Services;

public interface ITestDocumentParser
{
    bool CanParse(string filePath);

    ParsedTestDocument Parse(string filePath);
}
