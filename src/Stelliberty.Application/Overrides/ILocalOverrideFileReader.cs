using Stelliberty.Domain.Overrides;
namespace Stelliberty.Application.Overrides;

public interface ILocalOverrideFileReader
{
    string ReadAllText(string filePath);
}
