using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;

namespace Stelliberty.Infrastructure.Overrides;

public sealed class FileLocalOverrideFileReader : ILocalOverrideFileReader
{
    public string ReadAllText(string filePath)
    {
        return File.ReadAllText(filePath);
    }
}
