using Stelliberty.Domain.Rules;
namespace Stelliberty.Application.Rules;

public sealed class RuleListLoader(
    IRuleConfigSource source,
    RuleParser parser)
{
    public IReadOnlyList<RuleItem> LoadRules()
    {
        return parser.Parse(source.ReadRuntimeConfig());
    }
}
