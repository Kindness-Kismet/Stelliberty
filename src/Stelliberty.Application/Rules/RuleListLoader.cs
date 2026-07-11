using Stelliberty.Domain.Rules;
namespace Stelliberty.Application.Rules;

public sealed class RuleListLoader(
    IRuleConfigSource source,
    RuleParser parser,
    Func<bool> isCoreRunning)
{
    public IReadOnlyList<RuleItem> LoadRules()
    {
        if (!isCoreRunning())
        {
            return [];
        }

        return parser.Parse(source.ReadRuntimeConfig());
    }

    public IReadOnlyList<RuleItem> Search(string keyword)
    {
        return new RuleSearch().Filter(LoadRules(), keyword);
    }
}
