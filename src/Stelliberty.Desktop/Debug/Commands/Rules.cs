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

        if (spec.StartsWith("move up ", StringComparison.OrdinalIgnoreCase))
        {
            MoveRule(page, spec["move up ".Length..].Trim(), -1);
            return Task.FromResult<string?>(RuleOrder(page));
        }

        if (spec.StartsWith("move down ", StringComparison.OrdinalIgnoreCase))
        {
            MoveRule(page, spec["move down ".Length..].Trim(), 1);
            return Task.FromResult<string?>(RuleOrder(page));
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

    private static void MoveRule(RulePageViewModel page, string ruleId, int offset)
    {
        if (!string.IsNullOrWhiteSpace(page.SearchKeyword) || page.TypeBucket != RuleTypeBucket.All)
        {
            throw new InvalidOperationException("rules.move up/down requires the all-rules view without search");
        }

        var row = page.VisibleRules.FirstOrDefault(item => item.Id == ruleId || item.OrderId == ruleId);
        if (row is null)
        {
            return;
        }

        var sourceIndex = page.VisibleRules.IndexOf(row);
        page.MoveRuleCommand.Execute(new RuleMoveRequest(row.OrderId, sourceIndex + offset));
    }

    private static string RuleOrder(RulePageViewModel page)
        => $"order={string.Join(',', page.VisibleRules.Select(row => row.Id))}";

    private static RuleTypeBucket ParseRuleTypeBucket(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "domain" => RuleTypeBucket.Domain,
            "ip" => RuleTypeBucket.Ip,
            "rule-set" => RuleTypeBucket.RuleSet,
            "other" => RuleTypeBucket.Other,
            _ => RuleTypeBucket.All
        };
    }
}
#endif
