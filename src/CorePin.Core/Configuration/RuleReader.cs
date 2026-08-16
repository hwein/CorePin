using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CorePin.Core.Diagnostics;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;

namespace CorePin.Core.Configuration;

/// A rejected rule never fails the load; it is logged and handed back as a raw element.
internal sealed class RuleReader(ILog log)
{
    private const int MaxThreadIndex = 63;

    public (RuleSet Rules, List<JsonElement> SkippedRaw) Parse(JsonElement[]? raw)
    {
        var result = new List<Rule>();
        var skippedRaw = new List<JsonElement>();
        var idToExeName = new Dictionary<Guid, string>();
        var exeNameToFirstId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (raw is null) return (RuleSet.Empty, skippedRaw);

        for (int i = 0; i < raw.Length; i++)
        {
            var element = raw[i];
            RuleJson? dto;
            try { dto = element.Deserialize(ConfigJsonContext.Default.RuleJson); }
            catch (JsonException)
            {
                log.Warning("config", $"rule at index {i} skipped: malformed entry");
                skippedRaw.Add(element);
                continue;
            }

            if (!TryValidate(dto, out var rule, out string reason))
            {
                log.Warning("config", $"rule at index {i} skipped: {reason}");
                skippedRaw.Add(element);
                continue;
            }

            // File order decides: on a duplicate id or exeName the FIRST rule wins.
            if (idToExeName.ContainsKey(rule.Id))
            {
                log.Warning("config", $"rule '{rule.ExeName}' skipped: duplicate id {rule.Id}");
                skippedRaw.Add(element);
                continue;
            }
            if (exeNameToFirstId.TryGetValue(rule.ExeName, out var firstId))
            {
                log.Warning("config",
                    $"rule '{rule.ExeName}' skipped: duplicate exeName, rule {firstId} already covers this program");
                skippedRaw.Add(element);
                continue;
            }

            idToExeName[rule.Id] = rule.ExeName;
            exeNameToFirstId[rule.ExeName] = rule.Id;
            result.Add(rule);
        }

        return (new RuleSet(result), skippedRaw);
    }

    private bool TryValidate(RuleJson? dto, [NotNullWhen(true)] out Rule? rule, out string reason)
    {
        rule = null;

        if (dto is null) { reason = "malformed entry"; return false; }

        if (!Guid.TryParse(dto.Id, out var id)) { reason = "missing/invalid id"; return false; }

        string exeName = (dto.ExeName ?? string.Empty).Trim();
        // `malformed entry` is the only one of the four skip reasons that fits an empty name.
        if (exeName.Length == 0) { reason = "malformed entry"; return false; }

        bool enabled;
        switch (dto.Enabled.ValueKind)
        {
            case JsonValueKind.Undefined: enabled = true; break;
            case JsonValueKind.True: enabled = true; break;
            case JsonValueKind.False: enabled = false; break;
            default: reason = "invalid enabled"; return false;
        }

        if (dto.Threads is not { Length: > 0 }) { reason = "empty threads"; return false; }

        var kept = new List<int>();
        foreach (int thread in dto.Threads)
        {
            if ((uint)thread > MaxThreadIndex)
            {
                log.Warning("config", $"rule '{exeName}': thread index {thread} out of range, dropped");
                continue;
            }
            kept.Add(thread);
        }
        if (kept.Count == 0) { reason = "empty threads"; return false; }

        rule = new Rule
        {
            Id = id,
            ExeName = exeName,
            LastKnownPath = dto.LastKnownPath,
            Threads = AffinityMask.FromThreads(kept),     // duplicates collapse into one mask
            Enabled = enabled,
        };
        reason = string.Empty;
        return true;
    }
}
