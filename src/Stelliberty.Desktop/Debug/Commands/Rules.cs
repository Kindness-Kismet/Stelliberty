#if DEBUG
using Stelliberty.Application.Rules;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteRulesCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var page = viewModel.RulePage;
        var spec = command["rules.".Length..].Trim();
        if (string.Equals(spec, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            page.RefreshRulesCommand.Execute(null);
            return RuleState(page);
        }

        if (string.Equals(spec, "reset-order", StringComparison.OrdinalIgnoreCase))
        {
            page.ResetRuleOrderCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return RuleOrder(page);
        }

        if (spec.StartsWith("filter ", StringComparison.OrdinalIgnoreCase))
        {
            page.SetTypeBucket(ParseRuleTypeBucket(spec["filter ".Length..].Trim()));
            return RuleState(page);
        }

        if (spec.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
        {
            page.SearchKeyword = NormalizeInputValue(spec["search ".Length..].Trim());
            return RuleState(page);
        }

        if (spec.StartsWith("move up ", StringComparison.OrdinalIgnoreCase))
        {
            MoveRule(page, spec["move up ".Length..].Trim(), -1);
            await WaitRuntimeRefreshAsync(viewModel);
            return RuleOrder(page);
        }

        if (spec.StartsWith("move down ", StringComparison.OrdinalIgnoreCase))
        {
            MoveRule(page, spec["move down ".Length..].Trim(), 1);
            await WaitRuntimeRefreshAsync(viewModel);
            return RuleOrder(page);
        }

        if (spec.StartsWith("list ", StringComparison.OrdinalIgnoreCase))
        {
            return ListRules(page, spec["list ".Length..].Trim());
        }

        if (spec.StartsWith("add custom ", StringComparison.OrdinalIgnoreCase))
        {
            SaveCustomRule(page, null, spec["add custom ".Length..]);
            await WaitRuntimeRefreshAsync(viewModel);
            return ListRules(page, "custom");
        }

        if (spec.StartsWith("edit custom ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["edit custom ".Length..]);
            if (tokens.Count < 4)
            {
                throw new InvalidOperationException("rules.edit custom usage: rules.edit custom <id> <type> <payload> <outbound> [--no-resolve]");
            }

            var row = page.CustomRules.FirstOrDefault(item => string.Equals(item.Id, tokens[0], StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Custom rule not found: {tokens[0]}");
            SaveCustomRule(page, row, tokens.Skip(1).ToList());
            await WaitRuntimeRefreshAsync(viewModel);
            return ListRules(page, "custom");
        }

        if (spec.StartsWith("remove custom ", StringComparison.OrdinalIgnoreCase))
        {
            var id = FirstCommandToken(spec["remove custom ".Length..]);
            var row = page.CustomRules.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Custom rule not found: {id}");
            page.DeleteRuleCommand.Execute(row);
            page.ConfirmDeleteRuleCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return ListRules(page, "custom");
        }

        if (spec.StartsWith("toggle ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["toggle ".Length..]);
            if (tokens.Count != 2 || tokens[0] is not ("builtin" or "custom"))
            {
                throw new InvalidOperationException("rules.toggle usage: rules.toggle <builtin|custom> <id>");
            }

            var rows = tokens[0] == "builtin" ? page.BuiltinRules : page.CustomRules;
            var row = rows.FirstOrDefault(item => string.Equals(item.Id, tokens[1], StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"{tokens[0]} rule not found: {tokens[1]}");
            row.IsEnabled = !row.IsEnabled;
            await WaitRuntimeRefreshAsync(viewModel);
            return ListRules(page, tokens[0]);
        }

        if (string.Equals(spec, "list", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", page.FilteredRuleRows.Select(row =>
                $"{row.IndexText}\t{row.Type}\t{row.Payload}\tproxy={row.Proxy}\toptions={row.Options}\tsource={row.SourceText}\tcount={row.RuleCountText}"));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return RuleState(page);
        }

        throw new InvalidOperationException($"Unknown rules command: {command}");
    }

    private static void SaveCustomRule(RulePageViewModel page, RuleEditorRowViewModel? existing, string spec)
        => SaveCustomRule(page, existing, SplitCommandTokens(spec));

    private static void SaveCustomRule(RulePageViewModel page, RuleEditorRowViewModel? existing, IReadOnlyList<string> tokens)
    {
        if (tokens.Count is < 3 or > 4 || (tokens.Count == 4 && !string.Equals(tokens[3], "--no-resolve", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("rules.add custom usage: rules.add custom <type> <payload> <outbound> [--no-resolve]");
        }

        var type = tokens[0].ToUpperInvariant();
        var options = tokens.Count == 4 ? "no-resolve" : string.Empty;
        var ruleType = page.RuleTypes.FirstOrDefault(item => string.Equals(item.Type, type, StringComparison.Ordinal)
            && string.Equals(item.Options, options, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Unsupported rule type or options: {tokens[0]}");
        var outbound = page.OutboundTargets.FirstOrDefault(item => string.Equals(item.Value, tokens[2], StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Outbound target not found: {tokens[2]}");

        if (existing is null)
        {
            page.AddRuleCommand.Execute(null);
        }
        else
        {
            page.EditRuleCommand.Execute(existing);
        }

        page.SelectedRuleType = ruleType;
        page.Payload = tokens[1];
        page.SelectedOutboundTarget = outbound;
        page.SaveRuleCommand.Execute(null);
        if (page.IsEditorDialogVisible)
        {
            page.CancelEditorCommand.Execute(null);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(page.PayloadError)
                ? "Custom rule could not be saved"
                : page.PayloadError);
        }
    }

    private static string ListRules(RulePageViewModel page, string source)
    {
        var rows = source.ToLowerInvariant() switch
        {
            "builtin" => page.BuiltinRules,
            "custom" => page.CustomRules,
            _ => throw new InvalidOperationException("rules.list usage: rules.list <builtin|custom>")
        };
        return string.Join("|", rows.Select(row =>
            $"{row.Id}\t{row.Type}\t{row.Payload}\tproxy={row.Proxy}\toptions={row.Options}\tenabled={row.IsEnabled.ToString().ToLowerInvariant()}"));
    }

    private static string RuleState(RulePageViewModel page)
    {
        return string.Join(";", [
            $"total={page.Rules.Count}",
            $"filtered={page.FilteredRules.Count}",
            $"bucket={page.TypeBucket}",
            $"search={page.SearchKeyword}",
            $"running={page.IsCoreRunning.ToString().ToLowerInvariant()}",
            $"refresh={page.HasRequestedRefresh.ToString().ToLowerInvariant()}",
            $"can-reset-order={page.CanResetRuleOrder.ToString().ToLowerInvariant()}"
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
