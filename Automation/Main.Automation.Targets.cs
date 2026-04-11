using System;

namespace BeastsV2;

public partial class Main
{
    #region Automation target metadata

    private static (string Label, string IdSuffix, StashAutomationTargetSettings Target)[] GetAutomationTargets(StashAutomationSettings automation)
    {
        return
        [
            (GetAutomationTargetLabel(automation.Target1, "Slot 1 - Map Slot"), "target1", automation.Target1),
            (GetAutomationTargetLabel(automation.Target2, "Slot 2 - Fragment Slot 1"), "target2", automation.Target2),
            (GetAutomationTargetLabel(automation.Target3, "Slot 3 - Fragment Slot 2"), "target3", automation.Target3),
            (GetAutomationTargetLabel(automation.Target4, "Slot 4 - Fragment Slot 3"), "target4", automation.Target4),
            (GetAutomationTargetLabel(automation.Target5, "Slot 5 - Fragment Slot 4"), "target5", automation.Target5),
            (GetAutomationTargetLabel(automation.Target6, "Slot 6 - Fragment Slot 5"), "target6", automation.Target6)
        ];
    }

    private static string GetAutomationTargetLabel(StashAutomationTargetSettings target, string fallbackLabel)
    {
        var name = target?.ItemName.Value?.Trim();
        return string.IsNullOrWhiteSpace(name) ? fallbackLabel : $"{fallbackLabel} ({name})";
    }

    private static int GetConfiguredTargetQuantity(StashAutomationTargetSettings target)
    {
        return Math.Clamp(target?.Quantity?.Value ?? 0, 0, StashAutomationTargetSettings.MaxQuantity);
    }

    private static bool IsTargetEnabledForAutomation(StashAutomationTargetSettings target)
    {
        return target?.Enabled?.Value == true && GetConfiguredTargetQuantity(target) > 0;
    }

    private static string GetAutomationTargetIdentityKey(StashAutomationTargetSettings target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        var configuredMapTier = TryGetConfiguredMapTier(target);
        if (configuredMapTier.HasValue)
        {
            return $"map-tier:{configuredMapTier.Value}";
        }

        var configuredName = target.ItemName.Value?.Trim();
        return string.IsNullOrWhiteSpace(configuredName)
            ? string.Empty
            : $"item:{configuredName}";
    }

    private static int GetCumulativeConfiguredTargetQuantity(
        StashAutomationSettings automation,
        string currentIdSuffix,
        StashAutomationTargetSettings currentTarget)
    {
        if (automation == null || string.IsNullOrWhiteSpace(currentIdSuffix) || currentTarget == null)
        {
            return 0;
        }

        var currentIdentityKey = GetAutomationTargetIdentityKey(currentTarget);
        if (string.IsNullOrWhiteSpace(currentIdentityKey))
        {
            return 0;
        }

        var cumulativeQuantity = 0;
        foreach (var (_, idSuffix, target) in GetAutomationTargets(automation))
        {
            if (IsTargetEnabledForAutomation(target) && GetAutomationTargetIdentityKey(target).EqualsIgnoreCase(currentIdentityKey))
            {
                cumulativeQuantity += GetConfiguredTargetQuantity(target);
            }

            if (idSuffix.EqualsIgnoreCase(currentIdSuffix))
            {
                break;
            }
        }

        return cumulativeQuantity;
    }

    #endregion
}