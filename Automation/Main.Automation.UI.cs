using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2;

public partial class Main
{
    #region Settings UI

    private void DrawTargetTabSelectorPanel(string label, string idSuffix, StashAutomationTargetSettings target)
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        if (stash?.IsVisible != true)
        {
            var selectedTabName = target?.SelectedTabName.Value?.Trim();
            ImGui.Text($"{label} tab");
            ImGui.SameLine();
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(selectedTabName) ? "Select tab" : selectedTabName);
            ImGui.TextDisabled("Open stash to change the selected stash tab.");
            return;
        }

        var stashTabNames = GetAvailableStashTabNames(stash);
        if (stashTabNames.Count <= 0)
        {
            ImGui.TextDisabled("No stash tabs available.");
            return;
        }

        DrawTargetTabSelector(label, idSuffix, target, stashTabNames);
    }

    private void DrawBestiaryStashTabSelectorPanel(BestiaryAutomationSettings automation)
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        if (stash?.IsVisible != true)
        {
            var selectedTabName = automation?.SelectedTabName.Value?.Trim();
            ImGui.Text("Itemized beasts stash tab");
            ImGui.SameLine();
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(selectedTabName) ? "Select tab" : selectedTabName);
            var selectedRedTabName = automation?.SelectedRedBeastTabName.Value?.Trim();
            ImGui.Text("Red beasts stash tab");
            ImGui.SameLine();
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(selectedRedTabName) ? "Use itemized beasts tab" : selectedRedTabName);
            ImGui.TextDisabled("Open stash to change the selected stash tab.");
            return;
        }

        var stashTabNames = GetAvailableStashTabNames(stash);
        if (stashTabNames.Count <= 0)
        {
            ImGui.TextDisabled("No stash tabs available.");
            return;
        }

        DrawBestiaryStashTabSelector("Itemized beasts", "bestiary", automation, stashTabNames);
        DrawBestiaryStashTabSelector("Red beasts", "bestiaryRed", automation.RedBeastStashTabSelector, automation.SelectedRedBeastTabName, stashTabNames, "Use itemized beasts tab");
    }

    private void InitializeAutomationSettingsUi(StashAutomationSettings automation)
    {
        foreach (var (label, idSuffix, target) in GetAutomationTargets(automation))
        {
            target.TabSelector.DrawDelegate = () => DrawTargetTabSelectorPanel(label, idSuffix, target);
        }

        automation.MapSelector.DrawDelegate = () => DrawMapSelectionSelectorPanel(automation);

        var hotkeys = Settings?.Automation?.Hotkeys;
        if (hotkeys != null)
        {
            hotkeys.PrimaryTriggerHeader.DrawDelegate = () => DrawAutomationHotkeyHeader("Main Actions", "Direct automation runs.");
            hotkeys.FirstSeparator.DrawDelegate = DrawAutomationHotkeySeparator;
            hotkeys.PanelHelperHeader.DrawDelegate = () => DrawAutomationHotkeyHeader("Game Keybinds", "Must match Path of Exile settings.");
            hotkeys.SecondSeparator.DrawDelegate = DrawAutomationHotkeySeparator;
            hotkeys.WorkflowShortcutHeader.DrawDelegate = () => DrawAutomationHotkeyHeader("Utility Actions", "Manual helper workflows.");
        }
    }

    private void InitializeBestiaryAutomationSettingsUi(BestiaryAutomationSettings automation)
    {
        automation.StashTabSelector.DrawDelegate = () => DrawBestiaryStashTabSelectorPanel(automation);
    }

    private void InitializeMerchantAutomationSettingsUi(MerchantAutomationSettings automation)
    {
        automation.FaustusShopTabSelector.DrawDelegate = () => DrawFaustusShopTabSelectorPanel(automation);
    }

    private void DrawMenagerieInventoryQuickButton()
    {
        if (Settings?.BestiaryAutomation?.ShowInventoryButton?.Value != true || !CanUseInventoryBeastQuickAction()) return;

        var inventoryPanel = GameController?.IngameState?.IngameUi?.InventoryPanel[InventoryIndex.PlayerInventory];
        if (inventoryPanel?.IsVisible != true) return;

        var rect = inventoryPanel.GetClientRect();
        if (rect.Width <= 0 || rect.Height <= 0) return;

        ImGui.SetNextWindowPos(new Vector2(rect.Left - 8f, rect.Top), ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.SetNextWindowBgAlpha(0.9f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                       ImGuiWindowFlags.AlwaysAutoResize |
                                       ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoFocusOnAppearing |
                                       ImGuiWindowFlags.NoNav;

        if (!ImGui.Begin("##BeastsV2InventoryAutomationButton", flags))
        {
            ImGui.End();
            return;
        }

        if (_isAutomationRunning)
        {
            ImGui.TextDisabled("Automation running...");
            if (ImGui.Button("Stop##BeastsV2InventoryAutomation"))
            {
                RequestAutomationStop();
            }
        }
        else if (ImGui.Button("Right Click All Beasts##BeastsV2InventoryAutomation"))
        {
            _ = RunRightClickCapturedMonstersInInventoryAsync();
        }

        ImGui.End();
    }

    private void DrawBestiaryAutomationQuickButtons()
    {
        if (Settings?.BestiaryAutomation?.ShowBestiaryButtons?.Value != true || !IsBestiaryCapturedBeastsTabVisible()) return;

        var buttonContainer = TryGetBestiaryCapturedBeastsButtonContainer();
        if (buttonContainer?.IsVisible != true) return;

        var rect = buttonContainer.GetClientRect();
        if (rect.Width <= 0 || rect.Height <= 0) return;

        ImGui.SetNextWindowPos(new Vector2(rect.Right + 8f, rect.Top), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.9f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                       ImGuiWindowFlags.AlwaysAutoResize |
                                       ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoFocusOnAppearing |
                                       ImGuiWindowFlags.NoNav;

        if (!ImGui.Begin("##BeastsV2BestiaryAutomationButtons", flags))
        {
            ImGui.End();
            return;
        }

        if (_isAutomationRunning)
        {
            ImGui.TextDisabled("Automation running...");
            if (ImGui.Button("Stop##BeastsV2BestiaryAutomation"))
            {
                RequestAutomationStop();
            }
        }
        else
        {
            if (ImGui.Button("Itemize All##BeastsV2BestiaryAutomation"))
            {
                StartBestiaryClearAutomation(deleteBeasts: false, "button");
            }

            if (ImGui.Button("Delete All##BeastsV2BestiaryAutomation"))
            {
                StartBestiaryClearAutomation(deleteBeasts: true, "button");
            }
        }

        ImGui.End();
    }

    private void StartBestiaryClearAutomation(bool deleteBeasts, string triggerSource)
    {
        _ = BestiaryWorkflow.TriggerClearAsync(deleteBeasts, triggerSource, _isAutomationRunning);
    }

    private async Task RunRightClickCapturedMonstersInInventoryAsync()
    {
        await RunQueuedAutomationAsync(
            async ct =>
            {
                if (!CanUseInventoryBeastQuickAction())
                {
                    UpdateAutomationStatus("This action is only available in The Menagerie or while the Bestiary panel is open.", forceLog: true);
                    return;
                }

                var clickedCount = 0;
                var consecutiveFailures = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var capturedMonsterItems = GetVisibleCapturedMonsterInventoryItems();
                    if (capturedMonsterItems.Count <= 0)
                    {
                        UpdateAutomationStatus(clickedCount > 0
                            ? $"Right-clicked {clickedCount} {BeastLabel(clickedCount)} from inventory."
                            : "No captured beasts were found in player inventory.", forceLog: true);
                        return;
                    }

                    var nextItem = capturedMonsterItems[0];
                    var previousCount = capturedMonsterItems.Count;
                    UpdateAutomationStatus($"Right-clicking beasts in inventory... {clickedCount}/{previousCount}");
                    await RightClickInventoryItemAsync(nextItem);
                    var currentCount = await WaitForCapturedMonsterInventoryItemCountToChangeAsync(previousCount);
                    if (currentCount >= previousCount)
                    {
                        await DelayForUiCheckAsync(250);
                        currentCount = GetVisibleCapturedMonsterInventoryItems().Count;
                    }

                    if (currentCount >= previousCount)
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures >= 3)
                        {
                            throw new InvalidOperationException("Right-clicking captured beasts in inventory stalled.");
                        }

                        await DelayAutomationAsync(AutomationTiming.FastPollDelayMs);
                        continue;
                    }

                    consecutiveFailures = 0;
                    clickedCount += previousCount - currentCount;
                }
            },
            "Right-click inventory beasts",
            cancelledStatus: "Right-click inventory beasts cancelled.",
            uiCleanupOptions: new AutomationUiCleanupOptions(SkipUiCleanup: true, KeepInventory: true, KeepBestiary: true));
    }

    private void DrawTargetTabSelector(string label, string idSuffix, StashAutomationTargetSettings target, IReadOnlyList<string> stashTabNames)
    {
        var previewText = string.IsNullOrWhiteSpace(target.SelectedTabName.Value) ? "Select tab" : target.SelectedTabName.Value;
        ImGui.Text($"{label} tab");
        ImGui.SameLine();

        if (ImGui.BeginCombo($"##BeastsV2StashTab{idSuffix}", previewText))
        {
            for (var i = 0; i < stashTabNames.Count; i++)
            {
                var tabName = stashTabNames[i];
                var isSelected = target.SelectedTabName.Value.EqualsIgnoreCase(tabName);
                if (ImGui.Selectable($"{i}: {tabName}", isSelected))
                {
                    target.SelectedTabName.Value = tabName;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawMapSelectionSelectorPanel(StashAutomationSettings automation)
    {
        var selectedMapNode = automation?.SelectedMapToRun;
        if (selectedMapNode == null)
        {
            ImGui.TextDisabled("Map selection is unavailable.");
            return;
        }

        var normalizedSelection = NormalizeMapSelectionValue(selectedMapNode.Value);
        if (!string.Equals(selectedMapNode.Value, normalizedSelection, StringComparison.Ordinal))
        {
            selectedMapNode.Value = normalizedSelection;
        }

        var mapNameInput = normalizedSelection.EqualsIgnoreCase(OpenMapSelectionValue)
            ? string.Empty
            : normalizedSelection;

        ImGui.Text("Map Name (exact)");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputText("##BeastsV2MapToRun", ref mapNameInput, 128))
        {
            selectedMapNode.Value = NormalizeMapSelectionValue(mapNameInput);
        }

        ImGui.TextDisabled("Leave empty to keep the currently opened map.");
    }

    private void DrawBestiaryStashTabSelector(string label, string idSuffix, BestiaryAutomationSettings automation, IReadOnlyList<string> stashTabNames)
    {
        DrawBestiaryStashTabSelector(label, idSuffix, automation?.StashTabSelector, automation?.SelectedTabName, stashTabNames, "Select tab");
    }

    private void DrawBestiaryStashTabSelector(string label, string idSuffix, CustomNode _, TextNode selectedTabName, IReadOnlyList<string> stashTabNames, string defaultPreviewText)
    {
        var previewText = string.IsNullOrWhiteSpace(selectedTabName?.Value) ? defaultPreviewText : selectedTabName.Value;
        ImGui.Text($"{label} stash tab");
        ImGui.SameLine();

        if (ImGui.BeginCombo($"##BeastsV2BestiaryStashTab{idSuffix}", previewText))
        {
            if (!string.IsNullOrWhiteSpace(defaultPreviewText) && defaultPreviewText != "Select tab")
            {
                var useDefault = string.IsNullOrWhiteSpace(selectedTabName?.Value);
                if (ImGui.Selectable(defaultPreviewText, useDefault))
                {
                    selectedTabName.Value = string.Empty;
                }

                if (useDefault)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            for (var i = 0; i < stashTabNames.Count; i++)
            {
                var tabName = stashTabNames[i];
                var isSelected = selectedTabName?.Value.EqualsIgnoreCase(tabName) == true;
                if (ImGui.Selectable($"{i}: {tabName}", isSelected))
                {
                    selectedTabName.Value = tabName;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    #endregion
}

