using Stelliberty.Domain.Rules;

namespace Stelliberty.Application.Rules;

public interface IRuleOverrideStore
{
    RuleOverrideSet Load(string subscriptionId);

    void Save(RuleOverrideSet set);

    void UpsertTemplate(RuleTemplate template);

    void DeleteTemplate(string templateId);

    void Delete(string subscriptionId);
}
