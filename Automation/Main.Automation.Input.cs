using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ImGuiNET;
using SharpDX;

namespace BeastsV2;

public partial class Main
{
    #region Shared input and timing

    private async Task DelayAutomationAsync(int baseDelayMs)
    {
        ThrowIfAutomationStopRequested();
        AutomationInputLock.EnforceCursorPosition();

        var adjustedDelayMs = GetAutomationDelayMs(baseDelayMs);
        var remainingDelayMs = adjustedDelayMs;
        while (remainingDelayMs > 0)
        {
            var sliceDelayMs = Math.Min(remainingDelayMs, 50);
            await Task.Delay(sliceDelayMs);
            remainingDelayMs -= sliceDelayMs;
            AutomationInputLock.EnforceCursorPosition();
            ThrowIfAutomationStopRequested();
        }
    }

    private int GetAutomationDelayMs(int baseDelayMs)
    {
        var normalizedBaseDelayMs = Math.Max(0, baseDelayMs);

        var automation = Settings?.AutomationTiming;
        if (automation == null)
        {
            return normalizedBaseDelayMs;
        }

        return Math.Max(0, normalizedBaseDelayMs + automation.FlatExtraDelayMs.Value + GetConfiguredAutomationDelayLatencyMs());
    }

    private int GetAutomationTimeoutMs(int baseDelayMs)
    {
        return Math.Max(0, GetAutomationDelayMs(baseDelayMs) + GetServerLatencyMs());
    }

    private int GetConfiguredClickDelayMs()
    {
        return Math.Max(0, Settings?.AutomationTiming?.ClickDelayMs?.Value ?? 0);
    }

    private int GetConfiguredTabSwitchDelayMs()
    {
        return Math.Max(0, Settings?.AutomationTiming?.TabSwitchDelayMs?.Value ?? 0);
    }

    private static int NormalizeConfiguredClickDelayOverrideMs(int? configuredClickDelayOverrideMs)
    {
        return Math.Max(0, configuredClickDelayOverrideMs ?? 0);
    }

    private int GetServerLatencyMs()
    {
        return Math.Max(0, GameController?.Game?.IngameState?.ServerData?.Latency ?? 0);
    }

    private int GetConfiguredAutomationDelayLatencyMs()
    {
        return Settings?.AutomationTiming?.IncludeServerLatencyInDelays?.Value == true
            ? GetServerLatencyMs()
            : 0;
    }

    private bool IsAutomationBlockingUiOpen()
    {
        return IsAutomationBlockingUiOpen(null);
    }

    private bool IsAutomationBlockingUiOpen(AutomationUiCleanupOptions options)
    {
        var ui = GameController?.IngameState?.IngameUi;
        if (ui == null)
        {
            return false;
        }

        options ??= new AutomationUiCleanupOptions();
        var keepBestiaryUi = options.KeepBestiary && ui.ChallengesPanel?.IsVisible == true;
        var keepAtlasUi = options.KeepAtlas && ui.Atlas?.IsVisible == true;
        var keepStashUi = options.KeepStash && ui.StashElement?.IsVisible == true;
        var keepMerchantUi = options.KeepMerchant && ui.OfflineMerchantPanel?.IsVisible == true;
        var keepInventoryUi = options.KeepInventory && ui.InventoryPanel?.IsVisible == true;
        var keepMapDeviceUi = options.KeepMapDeviceWindow && ui.MapDeviceWindow?.IsVisible == true;
        var keepLeftPanelUi = keepBestiaryUi || keepAtlasUi || keepStashUi || keepMerchantUi || keepMapDeviceUi;
        var keepRightPanelUi = keepInventoryUi;

        return (!keepStashUi && ui.StashElement?.IsVisible == true) ||
               ui.NpcDialog?.IsVisible == true ||
               ui.SellWindow?.IsVisible == true ||
               ui.PurchaseWindow?.IsVisible == true ||
               (!keepInventoryUi && ui.InventoryPanel?.IsVisible == true) ||
               ui.TreePanel?.IsVisible == true ||
               (!keepAtlasUi && ui.Atlas?.IsVisible == true) ||
               ui.AtlasTreePanel?.IsVisible == true ||
               ui.RitualWindow?.IsVisible == true ||
               (!keepLeftPanelUi && ui.OpenLeftPanel?.IsVisible == true) ||
               (!keepRightPanelUi && ui.OpenRightPanel?.IsVisible == true) ||
               ui.TradeWindow?.IsVisible == true ||
               (!keepBestiaryUi && ui.ChallengesPanel?.IsVisible == true) ||
               ui.CraftBench?.IsVisible == true ||
               ui.DelveWindow?.IsVisible == true ||
               ui.ExpeditionWindow?.IsVisible == true ||
               ui.BanditDialog?.IsVisible == true ||
               ui.MetamorphWindow?.IsVisible == true ||
               ui.SyndicatePanel?.IsVisible == true ||
               ui.SyndicateTree?.IsVisible == true ||
               ui.QuestRewardWindow?.IsVisible == true ||
               (!keepMapDeviceUi && ui.MapDeviceWindow?.IsVisible == true) ||
               ui.SettingsPanel?.IsVisible == true ||
               ui.PopUpWindow?.IsVisible == true;
    }

    private async Task PrepareAutomationUiAsync(string debugContext, AutomationUiCleanupOptions options = null)
    {
        if (options?.SkipUiCleanup == true)
        {
            return;
        }

        await CloseBlockingUiWithSpaceAsync(
            () => IsAutomationBlockingUiOpen(options),
            debugContext,
            maxAttempts: MapDeviceCloseUiMaxAttempts,
            throwOnFailure: true);
    }

    private async Task CloseBlockingUiWithSpaceAsync(Func<bool> isBlockingUiOpen, string debugContext, int maxAttempts = 1, bool throwOnFailure = false)
    {
        if (isBlockingUiOpen == null || !isBlockingUiOpen())
        {
            return;
        }

        var timing = AutomationTiming;
        for (var attempt = 1; attempt <= Math.Max(1, maxAttempts) && isBlockingUiOpen(); attempt++)
        {
            ThrowIfAutomationStopRequested();
            UpdateAutomationStatus("Closing UI...");
            LogDebug($"Closing blocking UI for {debugContext}. attempt={attempt}/{Math.Max(1, maxAttempts)}");
            await TapKeyAsync(Keys.Space, timing.KeyTapDelayMs, timing.FastPollDelayMs);
            await DelayForUiCheckAsync(150);
        }

        if (throwOnFailure && isBlockingUiOpen())
        {
            throw new InvalidOperationException("Could not close all open UI panels.");
        }
    }

    #endregion
    #region Click and keyboard primitives

    private async Task ClickInventoryItemWithModifiersAsync(
        NormalInventoryItem item,
        MouseButtons button,
        int preClickDelayMs,
        int postClickDelayMs,
        params Keys[] modifierKeys)
    {
        await HoverInventoryItemAsync(item);
        await ClickCurrentCursorWithModifiersAsync(
            button,
            preClickDelayMs,
            postClickDelayMs,
            modifierKeys: modifierKeys);
    }

    private async Task HoverInventoryItemAsync(NormalInventoryItem item)
    {
        if (item == null)
        {
            throw new InvalidOperationException("Cannot hover a null inventory item.");
        }

        var timing = AutomationTiming;
        var hoverPollDelayMs = Math.Max(5, timing.FastPollDelayMs);
        var hoverTimeoutMs = Math.Max(90, timing.UiClickPreDelayMs + hoverPollDelayMs * 4);
        var hovered = await PollAutomationValueAsync(
            () => IsHoveringInventoryItem(item),
            isHovered => isHovered,
            hoverTimeoutMs,
            hoverPollDelayMs,
            onPendingAsync: _ =>
            {
                SetAutomationCursorPosition(item.GetClientRect().Center);
                return Task.CompletedTask;
            });

        if (hovered)
        {
            return;
        }

        SetAutomationCursorPosition(item.GetClientRect().Center);
        await DelayAutomationAsync(Math.Max(timing.UiClickPreDelayMs, hoverPollDelayMs));
        LogDebug($"Inventory hover did not confirm before click. proceeding with fallback click. itemAddress={item.Address}, metadata='{item.Item?.Metadata ?? "<null>"}'");
    }

    private bool IsHoveringInventoryItem(NormalInventoryItem item)
    {
        var hoverElement = GameController?.IngameState?.UIHoverElement;
        if (item == null || hoverElement == null)
        {
            return false;
        }

        var itemAddress = item.Address;
        if (itemAddress == 0)
        {
            return false;
        }

        return hoverElement.Address == itemAddress || hoverElement.Parent?.Address == itemAddress;
    }

    private async Task CtrlClickInventoryItemAsync(NormalInventoryItem item)
    {
        var timing = AutomationTiming;
        await ClickInventoryItemWithModifiersAsync(
            item,
            MouseButtons.Left,
            timing.CtrlClickPreDelayMs,
            timing.CtrlClickPostDelayMs,
            Keys.LControlKey);
    }

    private async Task CtrlRightClickInventoryItemAsync(NormalInventoryItem item)
    {
        var timing = AutomationTiming;
        await ClickInventoryItemWithModifiersAsync(
            item,
            MouseButtons.Right,
            timing.CtrlClickPreDelayMs,
            timing.CtrlClickPostDelayMs,
            Keys.LControlKey);
    }

    private async Task ShiftClickInventoryItemAsync(NormalInventoryItem item)
    {
        var timing = AutomationTiming;
        await ClickInventoryItemWithModifiersAsync(
            item,
            MouseButtons.Left,
            timing.CtrlClickPreDelayMs,
            timing.CtrlClickPostDelayMs,
            Keys.LShiftKey);
    }

    private async Task RightClickInventoryItemAsync(NormalInventoryItem item)
    {
        var timing = AutomationTiming;
        await ClickInventoryItemWithModifiersAsync(
            item,
            MouseButtons.Right,
            timing.UiClickPreDelayMs,
            timing.CtrlClickPostDelayMs);
    }

    private async Task CtrlClickElementAsync(Element element)
    {
        var timing = AutomationTiming;
        await ClickElementWithModifiersAsync(
            element,
            MouseButtons.Left,
            timing.CtrlClickPreDelayMs,
            timing.CtrlClickPostDelayMs,
            modifierKeys: [Keys.LControlKey]);
    }

    private async Task ClickElementAsync(Element element, int preClickDelayMs, int postClickDelayMs)
    {
        await ClickElementWithModifiersAsync(element, MouseButtons.Left, preClickDelayMs, postClickDelayMs);
    }

    private async Task ClickElementAsync(Element element, int preClickDelayMs, int postClickDelayMs, int? configuredClickDelayOverrideMs)
    {
        await ClickElementWithModifiersAsync(
            element,
            MouseButtons.Left,
            preClickDelayMs,
            postClickDelayMs,
            configuredClickDelayOverrideMs: configuredClickDelayOverrideMs);
    }

    private async Task ClickElementWithModifiersAsync(
        Element element,
        MouseButtons button,
        int preClickDelayMs,
        int postClickDelayMs,
        int modifierSettleDelayMs = 0,
        int? configuredClickDelayOverrideMs = null,
        params Keys[] modifierKeys)
    {
        if (element == null)
        {
            throw new InvalidOperationException("Cannot click a null UI element.");
        }

        await ClickAtWithModifiersAsync(
            element.GetClientRect().Center,
            button,
            preClickDelayMs,
            postClickDelayMs,
            modifierSettleDelayMs,
            configuredClickDelayOverrideMs,
            modifierKeys);
    }

    private async Task<bool> ClickElementAndConfirmAsync(
        Element element,
        int preClickDelayMs,
        int postClickDelayMs,
        Func<Task<bool>> waitForConfirmationAsync,
        int afterClickUiDelayMs = 0,
        int? configuredClickDelayOverrideMs = null)
    {
        await ClickElementAsync(element, preClickDelayMs, postClickDelayMs, configuredClickDelayOverrideMs);

        if (afterClickUiDelayMs > 0)
        {
            await DelayForUiCheckAsync(afterClickUiDelayMs);
        }

        return waitForConfirmationAsync != null && await waitForConfirmationAsync();
    }

    private void SetAutomationCursorPosition(SharpDX.Vector2 position)
    {
        var clampedPosition = ClampCursorPositionToGameWindow(position);
        AutomationInputLock.TrackAutomationCursorPosition(clampedPosition.X, clampedPosition.Y);
        AutomationInputLock.AllowAutomationMouseInput();
        Input.SetCursorPos(ToInputVector(clampedPosition));
        Input.MouseMove();
    }

    private async Task ClickAtWithModifiersAsync(
        SharpDX.Vector2 position,
        MouseButtons button,
        int preClickDelayMs,
        int postClickDelayMs,
        int modifierSettleDelayMs = 0,
        int? configuredClickDelayOverrideMs = null,
        params Keys[] modifierKeys)
    {
        SetAutomationCursorPosition(position);
        await DelayAutomationAsync(preClickDelayMs);
        await ClickCurrentCursorWithModifiersAsync(
            button,
            0,
            postClickDelayMs,
            modifierSettleDelayMs,
            configuredClickDelayOverrideMs,
            modifierKeys);
    }

    private async Task ClickCurrentCursorWithModifiersAsync(
        MouseButtons button,
        int preClickDelayMs,
        int postClickDelayMs,
        int modifierSettleDelayMs = 0,
        int? configuredClickDelayOverrideMs = null,
        params Keys[] modifierKeys)
    {
        await DelayAutomationAsync(preClickDelayMs);

        var keys = modifierKeys ?? [];
        AutomationInputLock.AllowAutomationKeys(keys);
        foreach (var key in keys)
        {
            if (key == Keys.None)
            {
                continue;
            }

            Input.KeyDown(key);
        }

        if (modifierSettleDelayMs > 0)
        {
            await DelayAutomationAsync(modifierSettleDelayMs);
        }

        AutomationInputLock.AllowAutomationMouseInput();
        Input.Click(button);

        AutomationInputLock.AllowAutomationKeys(keys);
        for (var index = keys.Length - 1; index >= 0; index--)
        {
            var key = keys[index];
            if (key == Keys.None)
            {
                continue;
            }

            Input.KeyUp(key);
        }

        var configuredClickDelayMs = configuredClickDelayOverrideMs.HasValue
            ? NormalizeConfiguredClickDelayOverrideMs(configuredClickDelayOverrideMs)
            : GetConfiguredClickDelayMs();
        await DelayAutomationAsync(Math.Max(postClickDelayMs, configuredClickDelayMs));
    }

    private async Task ClickAtAsync(SharpDX.Vector2 position, bool holdCtrl, int preClickDelayMs, int postClickDelayMs)
    {
        await ClickAtWithModifiersAsync(
            position,
            MouseButtons.Left,
            preClickDelayMs,
            postClickDelayMs,
            modifierKeys: holdCtrl ? [Keys.LControlKey] : []);
    }

    private async Task ClickCurrentCursorAsync(bool holdCtrl, int preClickDelayMs, int postClickDelayMs)
    {
        await ClickCurrentCursorWithModifiersAsync(
            MouseButtons.Left,
            preClickDelayMs,
            postClickDelayMs,
            modifierKeys: holdCtrl ? [Keys.LControlKey] : []);
    }

    private async Task DragLeftCursorAsync(
        SharpDX.Vector2 startPosition,
        SharpDX.Vector2 endPosition,
        int preDragDelayMs,
        int holdSettleDelayMs,
        int endSettleDelayMs,
        int releaseSettleDelayMs)
    {
        var leftDown = false;
        try
        {
            SetAutomationCursorPosition(startPosition);
            await DelayAutomationAsync(preDragDelayMs);

            AutomationInputLock.AllowAutomationMouseInput();
            Input.LeftDown();
            leftDown = true;
            await DelayAutomationAsync(holdSettleDelayMs);

            SetAutomationCursorPosition(endPosition);
            await DelayAutomationAsync(endSettleDelayMs);
        }
        finally
        {
            if (leftDown)
            {
                AutomationInputLock.AllowAutomationMouseInput();
                Input.LeftUp();
                await DelayAutomationAsync(releaseSettleDelayMs);
            }
        }
    }

    private void SetAutomationKeysDown(params Keys[] keys)
    {
        if (keys == null)
        {
            return;
        }

        AutomationInputLock.AllowAutomationKeys(keys);
        foreach (var key in keys)
        {
            if (key == Keys.None)
            {
                continue;
            }

            Input.KeyDown(key);
        }
    }

    private void ReleaseAutomationKeys(params Keys[] keys)
    {
        if (keys == null)
        {
            return;
        }

        AutomationInputLock.AllowAutomationKeys(keys);
        foreach (var key in keys)
        {
            if (key == Keys.None)
            {
                continue;
            }

            Input.KeyUp(key);
        }
    }

    private async Task TapKeyWithModifiersAsync(
        Keys key,
        int holdDelayMs,
        int postDelayMs,
        int modifierSettleDelayMs = 0,
        params Keys[] modifierKeys)
    {
        SetAutomationKeysDown(modifierKeys);

        if (modifierSettleDelayMs > 0)
        {
            await DelayAutomationAsync(modifierSettleDelayMs);
        }

        AutomationInputLock.AllowAutomationKeys(key);
        Input.KeyDown(key);
        await DelayAutomationAsync(Math.Max(1, holdDelayMs));
        AutomationInputLock.AllowAutomationKeys(key);
        Input.KeyUp(key);
        ReleaseAutomationKeys(modifierKeys);
        await DelayAutomationAsync(postDelayMs);
    }

    private async Task SendChatCommandAsync(string command)
    {
        ThrowIfAutomationStopRequested();

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        ReleaseAutomationModifierKeys();
        await CloseBlockingUiWithSpaceAsync(
            IsAutomationBlockingUiOpen,
            $"sending chat command '{command}'",
            maxAttempts: 2,
            throwOnFailure: true);

        var timing = AutomationTiming;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            ThrowIfAutomationStopRequested();

            if (IsChatTitlePanelVisible() && !IsChatInputVisible())
            {
                await TapKeyAsync(Keys.Escape, timing.KeyTapDelayMs, timing.FastPollDelayMs);
                await DelayForUiCheckAsync(100);
            }

            if (!IsChatInputVisible())
            {
                await TapKeyAsync(Keys.Enter, timing.KeyTapDelayMs, timing.FastPollDelayMs);
            }

            var chatOpened = await WaitForBestiaryConditionAsync(
                IsChatInputVisible,
                GetAutomationTimeoutMs(750),
                Math.Max(timing.FastPollDelayMs, 25));
            if (!chatOpened)
            {
                LogDebug($"Failed to open chat input for command '{command}'. attempt={attempt}/2");
                await DelayForUiCheckAsync(100);
                continue;
            }

            ImGui.SetClipboardText(command);
            await DelayForUiCheckAsync(100);
            await PasteClipboardAsync();

            var commandPasted = await WaitForBestiaryConditionAsync(
                () => string.Equals(GetChatInputText(), command, StringComparison.Ordinal),
                GetAutomationTimeoutMs(750),
                Math.Max(timing.FastPollDelayMs, 25));
            if (!commandPasted)
            {
                LogDebug($"Chat command paste mismatch. expected='{command}', observed='{GetChatInputText() ?? "<null>"}', attempt={attempt}/2, inputVisible={IsChatInputVisible()}, titleVisible={IsChatTitlePanelVisible()}");
                if (IsChatInputVisible())
                {
                    await TapKeyAsync(Keys.Escape, timing.KeyTapDelayMs, timing.FastPollDelayMs);
                    await DelayForUiCheckAsync(100);
                }

                continue;
            }

            await TapKeyAsync(Keys.Enter, timing.KeyTapDelayMs, timing.FastPollDelayMs);
            return;
        }

        throw new InvalidOperationException($"Failed to issue chat command '{command}'.");
    }

    private bool IsChatInputVisible()
    {
        return GameController?.IngameState?.IngameUi?.ChatPanel?.ChatInputElement?.IsVisible == true;
    }

    private bool IsChatTitlePanelVisible()
    {
        return GameController?.IngameState?.IngameUi?.ChatPanel?.ChatTitlePanel?.IsVisible == true;
    }

    private string GetChatInputText()
    {
        return GameController?.IngameState?.IngameUi?.ChatPanel?.InputText;
    }

    private async Task PasteClipboardAsync()
    {
        var timing = AutomationTiming;
        await TapKeyWithModifiersAsync(
            Keys.V,
            timing.KeyTapDelayMs,
            0,
            timing.KeyTapDelayMs,
            Keys.LControlKey);
    }

    private async Task CtrlTapKeyAsync(Keys key, int holdDelayMs, int postDelayMs)
    {
        await TapKeyWithModifiersAsync(
            key,
            holdDelayMs,
            postDelayMs,
            Math.Max(1, holdDelayMs),
            Keys.LControlKey);
    }

    private async Task TapKeyAsync(Keys key, int holdDelayMs, int postDelayMs)
    {
        await TapKeyWithModifiersAsync(key, holdDelayMs, postDelayMs);
    }

    private async Task DelayForUiCheckAsync(int minimumDelayMs = 125)
    {
        var timing = AutomationTiming;
        var latencyDelayMs = Settings?.AutomationTiming?.IncludeServerLatencyInDelays?.Value == true
            ? 0
            : GetServerLatencyMs();
        var uiDelayMs = Math.Max(minimumDelayMs, latencyDelayMs > 0 ? Math.Min(300, latencyDelayMs) : 0);
        uiDelayMs = Math.Max(uiDelayMs, timing.UiCheckInitialSettleDelayMs);
        uiDelayMs = Math.Max(uiDelayMs, timing.FastPollDelayMs + 25);

        await DelayAutomationAsync(uiDelayMs);
    }

    #endregion
    #region World entity interaction

    private async Task<bool> TryInteractWithWorldEntityAsync(
        Entity entity,
        string hoverLabel,
        string statusMessage,
        string missingPositionStatus,
        string hoverFailureStatus,
        MouseButtons button,
        int preClickDelayMs,
        int postClickDelayMs,
        int modifierSettleDelayMs = 0,
        params Keys[] modifierKeys)
    {
        if (entity?.GetComponent<Render>() == null)
        {
            if (!string.IsNullOrWhiteSpace(missingPositionStatus))
            {
                UpdateAutomationStatus(missingPositionStatus, forceLog: true);
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            UpdateAutomationStatus(statusMessage);
        }

        if (!await HoverWorldEntityAsync(entity, hoverLabel))
        {
            if (!string.IsNullOrWhiteSpace(hoverFailureStatus))
            {
                UpdateAutomationStatus(hoverFailureStatus, forceLog: true);
            }

            return false;
        }

        await ClickCurrentCursorWithModifiersAsync(
            button,
            preClickDelayMs,
            postClickDelayMs,
            modifierSettleDelayMs,
            configuredClickDelayOverrideMs: null,
            modifierKeys);
        return true;
    }

    #endregion
    #region Entity hover

    private async Task<bool> HoverWorldEntityAsync(Entity entity, string label)
    {
        var timing = AutomationTiming;
        DateTime lastNavigationAttemptUtc = DateTime.MinValue;
        var hovered = await PollAutomationValueAsync(
            () => IsHoveringEntityLabel(entity),
            isHovered => isHovered,
            5000,
            Math.Max(10, timing.FastPollDelayMs),
            onPendingAsync: async _ =>
            {
                var labelCenter = TryGetWorldEntityLabelCenter(entity);
                if (labelCenter.HasValue)
                {
                    SetAutomationCursorPosition(labelCenter.Value);

                    if (await WaitForBestiaryConditionAsync(
                            () => IsHoveringEntityLabel(entity),
                            Math.Max(40, timing.UiClickPreDelayMs + timing.FastPollDelayMs),
                            Math.Max(10, timing.FastPollDelayMs)))
                    {
                        return;
                    }
                }

                if ((DateTime.UtcNow - lastNavigationAttemptUtc).TotalMilliseconds >= 250)
                {
                    lastNavigationAttemptUtc = DateTime.UtcNow;
                    await NavigateTowardsEntityAsync(entity, label, $"Moving to {label}...");
                }
            });
        if (hovered)
        {
            return true;
        }

        LogDebug($"Failed to hover world entity '{label}'. entity={DescribeEntity(entity)}");
        return false;
    }

    private static System.Numerics.Vector2 ToInputVector(SharpDX.Vector2 vector) =>
        new(vector.X, vector.Y);

    private SharpDX.Vector2 ClampCursorPositionToGameWindow(SharpDX.Vector2 position)
    {
        var window = GameController?.Window;
        if (window == null)
        {
            return position;
        }

        var rect = window.GetWindowRectangle();
        var left = rect.Left + 10f;
        var top = rect.Top + 10f;
        var right = Math.Max(left, rect.Right - 20f);
        var bottom = Math.Max(top, rect.Bottom - 130f);

        return new SharpDX.Vector2(
            Math.Clamp(position.X, left, right),
            Math.Clamp(position.Y, top, bottom));
    }

    private SharpDX.Vector2? TryGetWorldEntityLabelCenter(Entity entity)
    {
        var labelsOnGround = GameController?.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible ??
                            GameController?.IngameState?.IngameUi?.ItemsOnGroundLabelElement?.LabelsOnGround;
        if (labelsOnGround == null || entity == null)
        {
            return null;
        }

        var label = labelsOnGround.FirstOrDefault(x =>
            x?.ItemOnGround != null &&
            x.Label?.IsVisible == true &&
            (x.ItemOnGround.Address != 0 && entity.Address != 0
                ? x.ItemOnGround.Address == entity.Address
                : x.ItemOnGround.Id == entity.Id ||
                                    x.ItemOnGround.Path.EqualsIgnoreCase(entity.Path) ||
                                    x.ItemOnGround.Metadata.EqualsIgnoreCase(entity.Metadata)));

        return label?.Label?.GetClientRect().Center;
    }

    private bool IsHoveringEntityLabel(Entity entity)
    {
        var itemsOnGroundLabelElement = GameController?.IngameState?.IngameUi?.ItemsOnGroundLabelElement;
        var hoverPath = itemsOnGroundLabelElement?.ItemOnHoverPath;
        var hoveredLabel = itemsOnGroundLabelElement?.LabelOnHover;
        if (entity == null || hoveredLabel == null || string.IsNullOrWhiteSpace(hoverPath))
        {
            return false;
        }

         return hoverPath.EqualsIgnoreCase(entity.Path) ||
             hoverPath.EqualsIgnoreCase(entity.Metadata);
    }

    #endregion
    #region Reflection utilities

    private static string TryGetPropertyValueAsString(object instance, string propertyName)
    {
        try
        {
            return instance?.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static T TryGetPropertyValue<T>(object instance, string propertyName) where T : class
    {
        try
        {
            return instance?.GetType().GetProperty(propertyName)?.GetValue(instance) as T;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
