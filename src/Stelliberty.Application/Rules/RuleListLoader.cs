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

    public IReadOnlyList<RuleItem> Search(string keyword)
    {
        return new RuleSearch().Filter(LoadRules(), keyword);
    }
}
