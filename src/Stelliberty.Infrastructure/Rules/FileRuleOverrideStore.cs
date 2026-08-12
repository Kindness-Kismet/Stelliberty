using System.Text.Json;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Rules;

public sealed class FileRuleOverrideStore(string rootDirectory) : IRuleOverrideStore
{
    private readonly string _directory = Path.Combine(rootDirectory, "rules");
    private readonly string _path = Path.Combine(rootDirectory, "rules", "rule_overrides.json");

    public RuleOverrideSet Load(string subscriptionId)
    {
        var file = JsonFileRecovery.ReadOrRecover<RuleOverrideFile>(_path) ?? new RuleOverrideFile();
        var set = (file.Items ?? []).FirstOrDefault(item => item.SubscriptionId == subscriptionId);
        return set is null
            ? new RuleOverrideSet(subscriptionId, Templates: file.Templates ?? [])
            : new RuleOverrideSet(
                set.SubscriptionId,
                set.CustomRules ?? [],
                (set.DisabledBuiltinRuleKeys ?? []).ToHashSet(StringComparer.Ordinal),
                file.Templates ?? [],
                set.RuleOrder ?? []);
    }

    public void Save(RuleOverrideSet set)
    {
        Directory.CreateDirectory(_directory);
        var file = JsonFileRecovery.ReadOrRecover<RuleOverrideFile>(_path) ?? new RuleOverrideFile();
        var items = (file.Items ?? []).Where(item => !string.Equals(item.SubscriptionId, set.SubscriptionId, StringComparison.Ordinal)).ToList();
        var persisted = new PersistedRuleOverrideSet(set.SubscriptionId, set.CustomRules, set.DisabledBuiltinRuleKeys.ToList(), set.RuleOrder);
        items.Add(persisted);

        var json = JsonSerializer.Serialize(new RuleOverrideFile { Items = items, Templates = file.Templates ?? [] }, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(_path, json);
    }

    public void UpsertTemplate(RuleTemplate template)
    {
        var file = JsonFileRecovery.ReadOrRecover<RuleOverrideFile>(_path) ?? new RuleOverrideFile();
        var templates = (file.Templates ?? []).Where(item => item.Id != template.Id).ToList();
        templates.Add(template);
        SaveFile(file.Items ?? [], templates);
    }

    public void DeleteTemplate(string templateId)
    {
        var file = JsonFileRecovery.ReadOrRecover<RuleOverrideFile>(_path) ?? new RuleOverrideFile();
        var templates = (file.Templates ?? []).Where(item => item.Id != templateId).ToList();
        SaveFile(file.Items ?? [], templates);
    }

    public void Delete(string subscriptionId)
    {
        var file = JsonFileRecovery.ReadOrRecover<RuleOverrideFile>(_path) ?? new RuleOverrideFile();
        var items = (file.Items ?? []).Where(item => !string.Equals(item.SubscriptionId, subscriptionId, StringComparison.Ordinal)).ToList();
        SaveFile(items, file.Templates ?? []);
    }

    private void SaveFile(IReadOnlyList<PersistedRuleOverrideSet> items, IReadOnlyList<RuleTemplate> templates)
    {
        var json = JsonSerializer.Serialize(new RuleOverrideFile { Items = items, Templates = templates }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(_directory);
        AtomicFile.WriteAllText(_path, json);
    }

    private sealed class RuleOverrideFile
    {
        public IReadOnlyList<PersistedRuleOverrideSet>? Items { get; init; } = [];
        public IReadOnlyList<RuleTemplate>? Templates { get; init; } = [];
    }

    private sealed record PersistedRuleOverrideSet(
        string SubscriptionId,
        IReadOnlyList<EditableRule> CustomRules,
        IReadOnlyList<string> DisabledBuiltinRuleKeys,
        IReadOnlyList<string>? RuleOrder = null);
}
