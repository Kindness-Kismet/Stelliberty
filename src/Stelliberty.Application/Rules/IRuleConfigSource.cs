using Stelliberty.Domain.Rules;
namespace Stelliberty.Application.Rules;

public interface IRuleConfigSource
{
    string ReadRuntimeConfig();
}
