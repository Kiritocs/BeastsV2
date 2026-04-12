using System;
using System.Collections.Generic;
using System.Threading;
using ExileCore.PoEMemory;
using System.Windows.Forms;

namespace BeastsV2.Runtime.State;

internal sealed class BeastsRuntimeState
{
    public SessionTimelineState Session { get; } = new();
    public MapTrackingState Map { get; } = new();
    public WebServerRuntimeState WebServer { get; } = new();
    public AutomationRuntimeState Automation { get; } = new();
}

internal sealed class SessionTimelineState
{
    public DateTime SessionStartUtc { get; set; }
    public TimeSpan SessionPausedDuration { get; set; }
    public TimeSpan LoadedSessionsDuration { get; set; }
    public DateTime? PauseMenuSessionStartUtc { get; set; }
    public DateTime? CurrentMapStartUtc { get; set; }
    public TimeSpan CurrentMapElapsed { get; set; }
    public TimeSpan CompletedMapsDuration { get; set; }
    public int CompletedMapCount { get; set; }
}

internal sealed class MapTrackingState
{
    public bool IsCurrentAreaTrackable { get; set; }
    public string ActiveMapAreaHash { get; set; } = string.Empty;
    public string ActiveMapAreaName { get; set; } = string.Empty;
    public bool CurrentMapWasComplete { get; set; }
    public int ActiveMapInstanceId { get; set; } = -1;
    public bool MapWasFinalized { get; set; }
}

internal sealed class WebServerRuntimeState
{
    public int Port { get; set; } = -1;
    public bool AllowNetwork { get; set; }
}

internal sealed class AutomationRuntimeState
{
    public string LastStatusMessage { get; set; } = string.Empty;
    public bool IsAutomationRunning { get; set; }
    public bool IsInputLockActive { get; set; }
    public bool IsBestiaryClearRunning { get; set; }
    public bool IsAutomationStopRequested { get; set; }
    public bool? BestiaryDeleteModeOverride { get; set; }
    public bool? BestiaryAutoStashOverride { get; set; }
    public bool BestiaryInventoryFullStop { get; set; }
    public string ActiveBestiarySearchRegex { get; set; } = string.Empty;
    public CancellationTokenSource CancellationTokenSource { get; set; }
    public HashSet<Keys> HotkeysCurrentlyDown { get; } = [];
    public AutomationUiCacheState UiCache { get; } = new();
}

internal sealed class AutomationUiCacheState
{
    public int LastAutomationFragmentScarabTabIndex { get; set; } = -1;
    public int LastAutomationMapStashTierSelection { get; set; } = -1;
    public int LastAutomationMapStashPageNumber { get; set; } = -1;
    public int LastAutomationMapStashUiCacheKey { get; set; } = -1;
    public Element LastAutomationMapStashTierGroupRoot { get; set; }
    public Element LastAutomationMapStashPageTabContainer { get; set; }
    public Dictionary<int, Element> LastAutomationMapStashPageTabsByNumber { get; set; }
    public Element LastAutomationMapStashPageContentRoot { get; set; }
    public string LastAutomationMapStashPageContentLogSignature { get; set; } = string.Empty;
    public string LastAutomationMapStashPageTabsLogSignature { get; set; } = string.Empty;
}