using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared.Nodes;
using BeastsV2.Runtime.State;

namespace BeastsV2.Runtime.Automation;

internal sealed class AutomationHotkeyTracker
{
    private readonly AutomationRuntimeState _state;

    public AutomationHotkeyTracker(AutomationRuntimeState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool TryGetPressedHotkey(HotkeyNodeV2 hotkey, bool isAutomationRunning, out Keys key, out bool usedKeyDownFallback)
    {
        key = hotkey?.Value.Key ?? Keys.None;
        usedKeyDownFallback = false;
        if (key == Keys.None)
        {
            return false;
        }

        var isKeyDown = Input.IsKeyDown((int)key);
        if (!isKeyDown)
        {
            _state.HotkeysCurrentlyDown.Remove(key);
        }

        var wasAlreadyHandledWhileDown = _state.HotkeysCurrentlyDown.Contains(key);

        if (hotkey.PressedOnce())
        {
            if (wasAlreadyHandledWhileDown)
            {
                return false;
            }

            _state.HotkeysCurrentlyDown.Add(key);
            return true;
        }

        if (!isAutomationRunning || !isKeyDown || wasAlreadyHandledWhileDown)
        {
            return false;
        }

        _state.HotkeysCurrentlyDown.Add(key);
        usedKeyDownFallback = true;
        return true;
    }
}