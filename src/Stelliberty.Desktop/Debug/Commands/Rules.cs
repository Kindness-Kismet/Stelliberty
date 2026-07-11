#if DEBUG
using Stelliberty.Application.Rules;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static Task<string?> ExecuteRulesCommandAsync(MainWindow window, string command)
    {
        var page = RequireViewModel(window).RulePage;
        var spec = command["rules.".Length..].Trim();
        if (string.Equals(spec, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            page.RefreshRulesCommand.Execute(null);
            return Task.FromResult<string?>(RuleState(page));
        }

        if (spec.StartsWith("filter ", StringComparison.OrdinalIgnoreCase))
        {
            page.SetTypeBucket(ParseRuleTypeBucket(spec["filter ".Length..].Trim()));
            return Task.FromResult<string?>(RuleState(page));
        }

        if (spec.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
        {
            page.SearchKeyword = NormalizeInputValue(spec["search ".Length..].Trim());
            return Task.FromResult<string?>(RuleState(page));
        }

        if (string.Equals(spec, "list", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(string.Join("|", page.FilteredRuleRows.Select(row =>
                $"{row.IndexText}\t{row.Type}\t{row.Payload}\tproxy={row.Proxy}\toptions={row.Options}\tsource={row.SourceText}\tcount={row.RuleCountText}")));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(RuleState(page));
        }

        throw new InvalidOperationException($"Unknown rules command: {command}");
    }

    private static string RuleState(RulePageViewModel page)
    {
        return string.Join(";", [
            $"total={page.Rules.Count}",
            $"filtered={page.FilteredRules.Count}",
            $"bucket={page.TypeBucket}",
            $"search={page.SearchKeyword}",
            $"running={page.IsCoreRunning.ToString().ToLowerInvariant()}",
            $"refresh={page.HasRequestedRefresh.ToString().ToLowerInvariant()}"
        ]);
    }

    private static RuleTypeBucket ParseRuleTypeBucket(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "domain" => RuleTypeBucket.Domain,
            "ip" => RuleTypeBucket.Ip,
            "rule-set" or "ruleset" => RuleTypeBucket.RuleSet,
            "other" => RuleTypeBucket.Other,
            _ => RuleTypeBucket.All
        };
    }
}
#endif
