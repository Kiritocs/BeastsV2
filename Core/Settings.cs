using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using Newtonsoft.Json;
using SharpDX;

namespace BeastsV2;

public class Settings : ISettings
{
    private FullSequenceAutomationSettings _fullSequenceAutomation = new();
    private StashAutomationSettings _stashAutomation = new();
    private BestiaryAutomationSettings _bestiaryAutomation = new();
    private MerchantAutomationSettings _merchantAutomation = new();
    private AutomationTimingSettings _automationTiming = new();
    private AutomationStatusOverlaySettings _automationStatusOverlay = new();

    public Settings()
    {
        Automation = new AutomationMenuSettings(this);
    }

    [Menu("Enabled", "Enable or disable the Beasts V2 plugin.")]
    public ToggleNode Enable { get; set; } = new(false);

    [Menu("Overview", "A quick overview of your current Beasts V2 setup, pricing state, and automation readiness.")]
    public OverviewSettings Overview { get; set; } = new();

    [Menu("Automation", "All automation setup, workflow configuration, hotkeys, timing, and status-overlay settings in one category.")]
    [JsonIgnore]
    public AutomationMenuSettings Automation { get; private set; }

    internal FullSequenceAutomationSettings FullSequenceAutomation
    {
        get => _fullSequenceAutomation;
        set => _fullSequenceAutomation = value ?? new();
    }

    internal StashAutomationSettings StashAutomation
    {
        get => _stashAutomation;
        set => _stashAutomation = value ?? new();
    }

    internal BestiaryAutomationSettings BestiaryAutomation
    {
        get => _bestiaryAutomation;
        set => _bestiaryAutomation = value ?? new();
    }

    internal MerchantAutomationSettings MerchantAutomation
    {
        get => _merchantAutomation;
        set => _merchantAutomation = value ?? new();
    }

    internal AutomationTimingSettings AutomationTiming
    {
        get => _automationTiming;
        set => _automationTiming = value ?? new();
    }

    internal AutomationStatusOverlaySettings AutomationStatusOverlay
    {
        get => _automationStatusOverlay;
        set => _automationStatusOverlay = value ?? new();
    }

    [Menu("Tracking: Price Data", "Configure automatic beast price fetching from poe.ninja, set the league name, and choose which beasts are considered valuable.")]
    public BeastPricesSettings BeastPrices { get; set; } = new();

    [Menu("Tracking: Markers & Prices", "Configure in-world beast name labels, large-map markers, the tracked beasts list, and price overlays shown on inventory, stash, and Bestiary panels.")]
    public MapRenderSettings MapRender { get; set; } = new();

    [Menu("Tracking: Bestiary Clipboard", "Automatically copy and optionally paste a search regex into the Bestiary panel when it opens, so matching beasts are pre-filtered for quick itemizing.")]
    public BestiaryClipboardSettings BestiaryClipboard { get; set; } = new();

    [Menu("Overlays: Counter", "Configure the on-screen beast counter that shows how many rare beasts have been found in the current area.")]
    public CounterWindowSettings CounterWindow { get; set; } = new();

    [Menu("Overlays: Analytics", "Configure the on-screen analytics panel that tracks session duration, map clear times, average map speed, and valuable beast counts.")]
    public AnalyticsWindowSettings AnalyticsWindow { get; set; } = new();

    [Menu("Overlays: Visibility", "Control when the counter and analytics overlays are automatically hidden, such as in hideout or when game panels like inventory or stash are open.")]
    public VisibilitySettings Visibility { get; set; } = new();

    [Menu("Analytics: Web Dashboard", "Expose the same analytics and statistics data through a local web dashboard and JSON API.")]
    public AnalyticsWebServerSettings AnalyticsWebServer { get; set; } = new();

    [Menu("What's New", "View plugin update history and changes grouped by version.")]
    public ChangelogSettings Changelog { get; set; } = new();

    [Menu("Diagnostics: Verbose Logging", "Write detailed step-by-step logs for all automation actions. Useful for troubleshooting when automation does not behave as expected.")]
    public ToggleNode DebugLogging { get; set; } = new(false);

    [JsonProperty("FullSequenceAutomation")]
    private FullSequenceAutomationSettings SavedFullSequenceAutomation
    {
        get => _fullSequenceAutomation;
        set => _fullSequenceAutomation = value ?? new();
    }

    [JsonProperty("StashAutomation")]
    private StashAutomationSettings SavedStashAutomation
    {
        get => _stashAutomation;
        set => _stashAutomation = value ?? new();
    }

    [JsonProperty("BestiaryAutomation")]
    private BestiaryAutomationSettings SavedBestiaryAutomation
    {
        get => _bestiaryAutomation;
        set => _bestiaryAutomation = value ?? new();
    }

    [JsonProperty("MerchantAutomation")]
    private MerchantAutomationSettings SavedMerchantAutomation
    {
        get => _merchantAutomation;
        set => _merchantAutomation = value ?? new();
    }

    [JsonProperty("AutomationTiming")]
    private AutomationTimingSettings SavedAutomationTiming
    {
        get => _automationTiming;
        set => _automationTiming = value ?? new();
    }

    [JsonProperty("AutomationStatusOverlay")]
    private AutomationStatusOverlaySettings SavedAutomationStatusOverlay
    {
        get => _automationStatusOverlay;
        set => _automationStatusOverlay = value ?? new();
    }
}

[Submenu(CollapsedByDefault = true)]
public sealed class AutomationMenuSettings
{
    private readonly Settings _owner;

    public AutomationMenuSettings(Settings owner)
    {
        _owner = owner;
        Hotkeys = new AutomationHotkeysMenuSettings(owner);
    }

    [Menu("Sell Sequence", "Run the Bestiary sell loop with one hotkey: itemize matching beasts, then list them in Faustus. Map prep stays separate.")]
    public FullSequenceAutomationSettings FullSequence
    {
        get => _owner.FullSequenceAutomation;
        set => _owner.FullSequenceAutomation = value ?? new();
    }

    [Menu("Stash & Map Device", "Automate restocking six fixed Map Device slots from stash and loading them into the Map Device: one map slot plus five fragment slots.")]
    public StashAutomationSettings StashAndMapDevice
    {
        get => _owner.StashAutomation;
        set => _owner.StashAutomation = value ?? new();
    }

    [Menu("Bestiary", "Configure Bestiary bulk actions including regex itemizing, delete-only automation, and quick-action buttons for captured beasts.")]
    public BestiaryAutomationSettings Bestiary
    {
        get => _owner.BestiaryAutomation;
        set => _owner.BestiaryAutomation = value ?? new();
    }

    [Menu("Merchant", "Automate listing itemized beasts for sale by interacting with the Faustus NPC, opening his shop, and placing beasts from inventory into a shop tab.")]
    public MerchantAutomationSettings Merchant
    {
        get => _owner.MerchantAutomation;
        set => _owner.MerchantAutomation = value ?? new();
    }

    [Menu("Timing", "Configure shared automation delays such as click pacing, extra wait padding, latency-aware delays, stash tab switch delay, and a dedicated Bestiary click delay.")]
    public AutomationTimingSettings Timing
    {
        get => _owner.AutomationTiming;
        set => _owner.AutomationTiming = value ?? new();
    }

    [Menu("Status Overlay", "Configure the on-screen status banner that shows what the current automation step is doing and displays error messages when something fails.")]
    public AutomationStatusOverlaySettings StatusOverlay
    {
        get => _owner.AutomationStatusOverlay;
        set => _owner.AutomationStatusOverlay = value ?? new();
    }

    [Menu("Hotkeys", "All automation hotkeys live here, grouped by purpose.")]
    [JsonIgnore]
    public AutomationHotkeysMenuSettings Hotkeys { get; }
}

[Submenu(CollapsedByDefault = true)]
public sealed class AutomationHotkeysMenuSettings
{
    private readonly Settings _owner;

    public AutomationHotkeysMenuSettings(Settings owner)
    {
        _owner = owner;
    }

    [Menu("", 0)]
    [JsonIgnore]
    public CustomNode PrimaryTriggerHeader { get; set; } = new();

    [Menu("Run Sell Sequence", "Press this hotkey to itemize matching beasts from Bestiary, then list them in Faustus.", 1)]
    public HotkeyNodeV2 FullSequenceHotkey
    {
        get => _owner.FullSequenceAutomation.FullSequenceHotkey;
        set => _owner.FullSequenceAutomation.FullSequenceHotkey = value ?? new(Keys.None);
    }

    [Menu("Prepare Map Device", "Press this hotkey to close open panels, open the Map Device, load configured map and fragment slots, and move the cursor to Activate.", 2)]
    public HotkeyNodeV2 LoadMapDeviceHotkey
    {
        get => _owner.StashAutomation.LoadMapDeviceHotkey;
        set => _owner.StashAutomation.LoadMapDeviceHotkey = value ?? new(Keys.None);
    }

    [Menu("Delete Visible Beasts", "Press this hotkey to open the captured beasts panel and delete all visible captured beasts.", 3)]
    public HotkeyNodeV2 DeleteHotkey
    {
        get => _owner.BestiaryAutomation.DeleteHotkey;
        set => _owner.BestiaryAutomation.DeleteHotkey = value ?? new(Keys.None);
    }

    [Menu("", 4)]
    [JsonIgnore]
    public CustomNode FirstSeparator { get; set; } = new();

    [Menu("", 5)]
    [JsonIgnore]
    public CustomNode PanelHelperHeader { get; set; } = new();

    [Menu("Inventory Keybind", "Set this to your real Path of Exile inventory key. Map-device atlas selection uses it to close inventory before scanning maps.", 6)]
    public HotkeyNodeV2 InventoryToggleHotkey
    {
        get => _owner.StashAutomation.InventoryToggleHotkey;
        set => _owner.StashAutomation.InventoryToggleHotkey = value ?? new(Keys.I);
    }

    [Menu("Challenges Keybind", "Set this to your real Path of Exile Challenges key. Delete and itemize flows use it outside The Menagerie to reach the Bestiary panel.", 7)]
    public HotkeyNodeV2 ChallengesWindowHotkey
    {
        get => _owner.BestiaryAutomation.ChallengesWindowHotkey;
        set => _owner.BestiaryAutomation.ChallengesWindowHotkey = value ?? new(Keys.None);
    }

    [Menu("", 8)]
    [JsonIgnore]
    public CustomNode SecondSeparator { get; set; } = new();

    [Menu("", 9)]
    [JsonIgnore]
    public CustomNode WorkflowShortcutHeader { get; set; } = new();

    [Menu("Restock Inventory", "Press this hotkey while in your hideout to open the stash and automatically Ctrl+click the configured items into your inventory.", 10)]
    public HotkeyNodeV2 RestockHotkey
    {
        get => _owner.StashAutomation.RestockHotkey;
        set => _owner.StashAutomation.RestockHotkey = value ?? new(Keys.None);
    }

    [Menu("Itemize Matching Beasts", "Press this hotkey to travel to The Menagerie, open captured beasts, paste the regex, and itemize all matching beasts.", 11)]
    public HotkeyNodeV2 RegexItemizeHotkey
    {
        get => _owner.BestiaryAutomation.RegexItemizeHotkey;
        set => _owner.BestiaryAutomation.RegexItemizeHotkey = value ?? new(Keys.None);
    }

    [Menu("List Beasts In Faustus", "Press this hotkey to find Faustus in your hideout, open his shop, and list itemized beasts into the selected sell tab.", 12)]
    public HotkeyNodeV2 FaustusListHotkey
    {
        get => _owner.MerchantAutomation.FaustusListHotkey;
        set => _owner.MerchantAutomation.FaustusListHotkey = value ?? new(Keys.None);
    }
}

[Submenu(CollapsedByDefault = true)]
public class OverviewSettings
{
    [Menu("Setup Summary", "A compact dashboard for pricing, overlays, and automation readiness.")]
    [JsonIgnore]
    public CustomNode SetupSummaryPanel { get; set; } = new();
}

[Submenu(CollapsedByDefault = true)]
public class ChangelogSettings
{
    [Menu("Update Timeline")]
    [JsonIgnore]
    public CustomNode UpdateHistoryPanel { get; set; } = new();
}

[Submenu(CollapsedByDefault = true)]
public class VisibilitySettings
{
    [Menu("Hide Counter & Message In Hideout", "Hide the beast counter and completed message while you are inside your hideout.")]
    public ToggleNode HideInHideout { get; set; } = new(true);

    [Menu("Hide Counter & Message On Fullscreen Panels", "Hide the beast counter and completed message when a fullscreen panel like the Atlas or Passive Tree is open.")]
    public ToggleNode HideOnFullscreenPanels { get; set; } = new(true);

    [Menu("Hide Counter & Message On Open Left Panel", "Hide the beast counter and completed message when a left-side panel like the Bestiary or Challenges is open.")]
    public ToggleNode HideOnOpenLeftPanel { get; set; } = new(true);

    [Menu("Hide Counter & Message On Open Right Panel", "Hide the beast counter and completed message when a right-side panel like inventory or stash is open.")]
    public ToggleNode HideOnOpenRightPanel { get; set; } = new(true);

    [Menu("Hide Analytics On Open Left Panel", "Hide the analytics overlay when a left-side panel like the Bestiary or Challenges is open.")]
    public ToggleNode HideAnalyticsOnOpenLeftPanel { get; set; } = new(true);

    [Menu("Hide Analytics On Open Right Panel", "Hide the analytics overlay when a right-side panel like inventory or stash is open.")]
    public ToggleNode HideAnalyticsOnOpenRightPanel { get; set; } = new(true);
}

[Submenu(CollapsedByDefault = true)]
public class CounterWindowSettings
{
    [Menu("Show", "Show or hide the main counter window.")]
    public ToggleNode Show { get; set; } = new(true);

    [Menu("X Position (%)", "Horizontal position of the counter window as a percentage of screen width. 0 = left edge, 50 = center, 100 = right edge.")]
    public RangeNode<float> XPos { get; set; } = new(50, 0, 100);

    [Menu("Y Position (%)", "Vertical position of the counter window as a percentage of screen height. 0 = top edge, 50 = center, 100 = bottom edge.")]
    public RangeNode<float> YPos { get; set; } = new(10, 0, 100);

    [Menu("Padding", "Inner spacing in pixels between the counter text and the window border.")]
    public RangeNode<float> Padding { get; set; } = new(6, 0, 50);

    [Menu("Border Thickness", "Thickness of the counter window border in pixels.")]
    public RangeNode<int> BorderThickness { get; set; } = new(1, 1, 10);

    [Menu("Border Rounding", "Corner roundness of the counter window in pixels. 0 = sharp corners.")]
    public RangeNode<float> BorderRounding { get; set; } = new(0, 0, 25);

    [Menu("Text Scale", "Size multiplier for the counter text before all beasts are found. 1.0 = normal size.")]
    public RangeNode<float> TextScale { get; set; } = new(1f, 0.5f, 4f);

    [Menu("Text Color", "Text color of the counter before all beasts are found.")]
    public ColorNode TextColor { get; set; } = new(new Color(255, 180, 70, 255));

    [Menu("Border Color", "Border color of the counter window before all beasts are found.")]
    public ColorNode BorderColor { get; set; } = new(Color.Black);

    [Menu("Background Color", "Background color of the main counter window.")]
    public ColorNode BackgroundColor { get; set; } = new(new Color(0, 0, 0, 180));

    [Menu("Completed Counter Style", "Customize how the counter text, color, and border change after all  beasts in the current area have been found.")]
    public CompletedCounterSettings CompletedStyle { get; set; } = new();

    [Menu("Completed Message Overlay", "A separate floating message that appears on screen after all  beasts in the area are found, such as 'All beasts found!'.")]
    public CompletedMessageWindowSettings CompletedMessage { get; set; } = new();

    [Menu("Tracked Completion Overlay", "A separate floating message that appears only after all  beasts are found AND every tracked valuable beast has been captured. Signals that it is safe to leave the map.")]
    public CompletedMessageWindowSettings TrackedCompletionMessage { get; set; } = new()
    {
        Text = new TextNode("All beasts found and tracked beasts captured!"),
        YPos = new RangeNode<float>(20, 0, 100)
    };
}

[Submenu(CollapsedByDefault = true)]
public class CompletedCounterSettings
{
    [Menu("Show While Not Complete", "Preview mode: apply the completed style even before all beasts are found, so you can see how it looks without entering a map.")]
    public ToggleNode ShowWhileNotComplete { get; set; } = new(false);

    [Menu("Text Scale", "Size multiplier for the counter text after all beasts are found. 1.0 = normal size.")]
    public RangeNode<float> TextScale { get; set; } = new(1.8f, 0.5f, 6f);

    [Menu("Text Color", "Text color of the counter after all beasts in the area are found.")]
    public ColorNode TextColor { get; set; } = new(new Color(90, 255, 120, 255));

    [Menu("Border Color", "Border color of the counter after all beasts in the area are found.")]
    public ColorNode BorderColor { get; set; } = new(new Color(90, 255, 120, 255));
}

[Submenu(CollapsedByDefault = true)]
public class CompletedMessageWindowSettings
{
    [Menu("Show", "Show or hide the separate completed message window.")]
    public ToggleNode Show { get; set; } = new(true);

    [Menu("Show While Not Complete", "Preview mode: show the completed message even before all beasts are found, so you can see how it looks and adjust styling.")]
    public ToggleNode ShowWhileNotComplete { get; set; } = new(false);

    [Menu("Message Text", "The text displayed in the completed message window. You can customize this to say anything you want.")]
    public TextNode Text { get; set; } = new("All beasts found!");

    [Menu("X Position (%)", "Horizontal position of the completed message window.")]
    public RangeNode<float> XPos { get; set; } = new(50, 0, 100);

    [Menu("Y Position (%)", "Vertical position of the completed message window.")]
    public RangeNode<float> YPos { get; set; } = new(16, 0, 100);

    [Menu("Padding", "Inner spacing between completed message text and window border.")]
    public RangeNode<float> Padding { get; set; } = new(8, 0, 50);

    [Menu("Border Thickness", "Border thickness of the completed message window.")]
    public RangeNode<int> BorderThickness { get; set; } = new(1, 1, 10);

    [Menu("Border Rounding", "Corner roundness of the completed message window.")]
    public RangeNode<float> BorderRounding { get; set; } = new(4, 0, 25);

    [Menu("Text Scale", "Text scale of the completed message text.")]
    public RangeNode<float> TextScale { get; set; } = new(1.4f, 0.5f, 6f);

    [Menu("Text Color", "Text color of the completed message window.")]
    public ColorNode TextColor { get; set; } = new(new Color(120, 255, 140, 255));

    [Menu("Border Color", "Border color of the completed message window.")]
    public ColorNode BorderColor { get; set; } = new(new Color(90, 255, 120, 255));

    [Menu("Background Color", "Background color of the completed message window.")]
    public ColorNode BackgroundColor { get; set; } = new(new Color(0, 0, 0, 200));
}

[Submenu(CollapsedByDefault = true)]
public class AnalyticsWindowSettings
{
    [Menu("Show", "Show or hide the analytics window.")]
    public ToggleNode Show { get; set; } = new(true);

    [Menu("Reset Session", "Reset all session counters and timers to zero, including beast counts, session duration, and map averages. Hold Shift and click to confirm.")]
    public ButtonNode ResetSession { get; set; } = new();

    [Menu("Reset Map Average", "Reset only the map average tracking (completed map count and total map duration) without affecting session beast counts or session time. Hold Shift and click to confirm.")]
    public ButtonNode ResetMapAverage { get; set; } = new();

    [Menu("Save Session To File", "Export the current session stats (beast counts, timing, map averages) to a CSV file in the plugin config folder.")]
    public ButtonNode SaveSessionToFile { get; set; } = new();

    [Menu("X Position (%)", "Horizontal position of the analytics window.")]
    public RangeNode<float> XPos { get; set; } = new(50, 0, 100);

    [Menu("Y Position (%)", "Vertical position of the analytics window.")]
    public RangeNode<float> YPos { get; set; } = new(25, 0, 100);

    [Menu("Padding", "Inner spacing between analytics text and window border.")]
    public RangeNode<float> Padding { get; set; } = new(8, 0, 50);

    [Menu("Border Thickness", "Border thickness of the analytics window.")]
    public RangeNode<int> BorderThickness { get; set; } = new(1, 1, 10);

    [Menu("Border Rounding", "Corner roundness of the analytics window.")]
    public RangeNode<float> BorderRounding { get; set; } = new(0, 0, 25);

    [Menu("Text Scale", "Text scale of the analytics window.")]
    public RangeNode<float> TextScale { get; set; } = new(1.0f, 0.5f, 6f);

    [Menu("Text Color", "Text color of analytics lines.")]
    public ColorNode TextColor { get; set; } = new(new Color(220, 220, 220, 255));

    [Menu("Border Color", "Border color of the analytics window.")]
    public ColorNode BorderColor { get; set; } = new(new Color(90, 90, 90, 255));

    [Menu("Background Color", "Background color of the analytics window.")]
    public ColorNode BackgroundColor { get; set; } = new(new Color(0, 0, 0, 180));
}

[Submenu(CollapsedByDefault = true)]
public class BeastPricesSettings
{
    [Menu("Pricing Snapshot", "A compact summary of the current league, refresh cadence, tracked-beast count, and common price actions.")]
    [JsonIgnore]
    public CustomNode SummaryPanel { get; set; } = new();

    [Menu("League", "The league name used for poe.ninja price lookups. Must match your current league exactly, for example Mirage.")]
    public TextNode League { get; set; } = new("Mirage");

    [Menu("Auto Refresh (min)", "Automatically fetch updated beast prices from poe.ninja at this interval in minutes. Set to 0 to only refresh manually.")]
    public RangeNode<int> AutoRefreshMinutes { get; set; } = new(10, 0, 60);

    [Menu("Refresh Prices", "Click to immediately fetch the latest beast prices from poe.ninja for the configured league.")]
    public ButtonNode FetchPrices { get; set; } = new();

    [Menu("Select All", "Enable every beast in the tracked list at once.")]
    public ButtonNode SelectAllBeasts { get; set; } = new();

    [Menu("Clear Selection", "Disable every beast in the tracked list at once.")]
    public ButtonNode DeselectAllBeasts { get; set; } = new();

    [Menu("Select 15c+", "Enable only beasts whose currently fetched poe.ninja price is 15 chaos or more.")]
    public ButtonNode SelectBeastsWorth15ChaosOrMore { get; set; } = new();

    [JsonIgnore]
    internal string LastUpdated { get; set; } = "never";

    [JsonIgnore]
    internal HashSet<string> EnabledBeasts { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    [JsonProperty("LastUpdated")]
    public string SavedLastUpdated
    {
        get => LastUpdated;
        set => LastUpdated = value ?? "never";
    }

    [JsonProperty("EnabledBeasts")]
    public List<string> SavedEnabledBeasts
    {
        get => new(EnabledBeasts);
        set => EnabledBeasts = value != null
            ? new HashSet<string>(value, System.StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    }

    [Menu("Tracked Beasts", "Pick which beasts are considered valuable. Enabled beasts appear in map markers, are counted in analytics, and are included in the auto-generated Bestiary search regex.")]
    [JsonIgnore] public CustomNode BeastPickerPanel { get; set; } = new();
}

[Submenu(CollapsedByDefault = true)]
public class BestiaryClipboardSettings
{
    [Menu("Enable Auto Copy", "Automatically copy a search regex to the clipboard the moment the Bestiary captured-beasts panel opens.")]
    public ToggleNode EnableAutoCopy { get; set; } = new(true);

    [Menu("Auto Paste After Copy", "After copying the regex, also automatically paste it into the Bestiary search field and press Enter so matching beasts are filtered immediately.")]
    public ToggleNode AutoPasteAfterCopy { get; set; } = new(true);

    [Menu("Generate Regex From Enabled Beasts", "Automatically build the search regex from your Enabled Beasts list in Price Data. When disabled, the Manual Regex field below is used instead.")]
    public ToggleNode UseAutoRegex { get; set; } = new(true);

    [Menu("Manual Regex", "A custom search regex copied to the clipboard when automatic regex generation is turned off. Edit this to match specific beasts by name fragments separated by |.")]
    public TextNode BeastRegex { get; set; } = new("id v|le m|ld h|s ho|k m|an fi|ul, f|cic c|nd sc|s, f|d bra|l pla|n, f|l cru| cy");
}

[Submenu(CollapsedByDefault = true)]
public class AutomationTimingSettings
{
    [Menu("Include Server Latency In Delays", "When enabled, every shared automation delay adds the current ServerData.Latency on top of the configured base delay and flat extra delay. Enable this if your connection regularly causes actions to land too early.")]
    public ToggleNode IncludeServerLatencyInDelays { get; set; } = new(false);

    [Menu("Click Delay (ms)", "Minimum delay in milliseconds after every automation click across the plugin. Increase this if clicks are landing too quickly for your connection or system.")]
    public RangeNode<int> ClickDelayMs { get; set; } = new(10, 0, 250);

    [Menu("Bestiary Click Delay (ms)", "Minimum delay in milliseconds after Bestiary panel clicks such as opening the captured-beasts view, focusing the Bestiary search box, or itemizing and deleting beasts. This overrides the shared Click Delay for Bestiary-only clicks.")]
    public RangeNode<int> BestiaryClickDelayMs { get; set; } = new(20, 0, 250);

    [Menu("Flat Extra Delay (ms)", "A flat delay in milliseconds added to every automation wait. Use this as a global speed adjustment if automation runs too fast for your system.")]
    public RangeNode<int> FlatExtraDelayMs { get; set; } = new(0, 0, 500);

    [Menu("Stash Tab Switch Delay (ms)", "Delay in milliseconds after switching to a new stash tab before searching for items. Increase this if the tab contents have not loaded in time.")]
    public RangeNode<int> TabSwitchDelayMs { get; set; } = new(50, 0, 500);
}

[Submenu(CollapsedByDefault = true)]
public class StashAutomationSettings
{
    [Menu("Quick Setup", "A compact setup summary for hotkeys, map choice, active slots, and stash-tab coverage.")]
    [JsonIgnore]
    public CustomNode SetupSummaryPanel { get; set; } = new();

    private HotkeyNodeV2 _restockHotkey = new(Keys.None);
    private HotkeyNodeV2 _loadMapDeviceHotkey = new(Keys.None);
    private HotkeyNodeV2 _inventoryToggleHotkey = new(Keys.I);

    [Menu("Auto Restock Missing Map Device Items", "When enabled, Map Device load automation automatically runs Restock first whenever any configured Map Device target is still missing after accounting for the visible Map Device, Map Device storage, and inventory.")]
    [JsonProperty("AutoRestockWhenMapDeviceEmpty")]
    public ToggleNode AutoRestockMissingMapDeviceItems { get; set; } = new(false);

    [Menu("Enable Map Regex Filter", "When enabled, restock pastes the configured regex into the map-stash search bar and only picks highlighted matching maps. This applies to the map slot only, never fragment slots.")]
    public ToggleNode EnableMapRegexFilter { get; set; } = new(false);

    [Menu("Map Regex Pattern", "Regex pasted into the map-stash search bar before restocking maps. Build this with https://poe.re/#/maps. Only highlighted matching maps are eligible for the map slot.")]
    public TextNode MapRegexPattern { get; set; } = new(string.Empty);

    internal HotkeyNodeV2 RestockHotkey
    {
        get => _restockHotkey;
        set => _restockHotkey = value ?? new(Keys.None);
    }

    internal HotkeyNodeV2 LoadMapDeviceHotkey
    {
        get => _loadMapDeviceHotkey;
        set => _loadMapDeviceHotkey = value ?? new(Keys.None);
    }

    internal HotkeyNodeV2 InventoryToggleHotkey
    {
        get => _inventoryToggleHotkey;
        set => _inventoryToggleHotkey = value ?? new(Keys.I);
    }

    [Menu("Atlas Map Selection", "Type the exact map name to select on the Atlas when preparing the Map Device. Leave empty (or use 'open Map') to keep the currently opened map and skip atlas map selection.")]
    [JsonIgnore]
    public CustomNode MapSelector { get; set; } = new();

    [JsonIgnore]
    internal TextNode SelectedMapToRun { get; set; } = new("open Map");

    [Menu("Map Slot", "Configure the single Map Device map slot. This target should be your map item, such as 'Map (Tier 16)'. Quantity is capped at 20.")]
    public StashAutomationTargetSettings Target1 { get; set; } = new() { ItemName = new TextNode("Map (Tier 16)"), Quantity = new RangeNode<int>(20, 0, StashAutomationTargetSettings.MaxQuantity) };

    [Menu("Fragment Slot 1", "Configure the first fragment or scarab slot in the Map Device. Quantity is capped at 20.")]
    public StashAutomationTargetSettings Target2 { get; set; } = new() { ItemName = new TextNode("Bestiary Scarab of the Herd"), Quantity = new RangeNode<int>(20, 0, StashAutomationTargetSettings.MaxQuantity) };

    [Menu("Fragment Slot 2", "Configure the second fragment or scarab slot in the Map Device. Quantity is capped at 20.")]
    public StashAutomationTargetSettings Target3 { get; set; } = new() { ItemName = new TextNode("Bestiary Scarab of Duplicating"), Quantity = new RangeNode<int>(20, 0, StashAutomationTargetSettings.MaxQuantity) };

    [Menu("Fragment Slot 3", "Configure the third fragment or scarab slot in the Map Device. Quantity is capped at 20.")]
    public StashAutomationTargetSettings Target4 { get; set; } = new() { Enabled = new(false), Quantity = new RangeNode<int>(0, 0, StashAutomationTargetSettings.MaxQuantity) };

    [Menu("Fragment Slot 4", "Configure the fourth fragment or scarab slot in the Map Device. Quantity is capped at 20.")]
    public StashAutomationTargetSettings Target5 { get; set; } = new() { Enabled = new(false), Quantity = new RangeNode<int>(0, 0, StashAutomationTargetSettings.MaxQuantity) };

    [Menu("Fragment Slot 5", "Configure the fifth fragment or scarab slot in the Map Device. Quantity is capped at 20.")]
    public StashAutomationTargetSettings Target6 { get; set; } = new() { Enabled = new(false), Quantity = new RangeNode<int>(0, 0, StashAutomationTargetSettings.MaxQuantity) };

    [JsonIgnore]
    internal StashAutomationDynamicHintSettings DynamicHints { get; set; } = new();

    [JsonProperty("MapToRun")]
    private string SavedMapToRun
    {
        get => SelectedMapToRun.Value;
        set => SelectedMapToRun.Value = NormalizeMapSelection(value);
    }

    [JsonProperty("DynamicHints")]
    private StashAutomationDynamicHintSettings SavedDynamicHints
    {
        get => DynamicHints;
        set => DynamicHints = value ?? new StashAutomationDynamicHintSettings();
    }

    [JsonProperty("RestockHotkey")]
    private HotkeyNodeV2 SavedRestockHotkey
    {
        get => _restockHotkey;
        set => _restockHotkey = value ?? new(Keys.None);
    }

    [JsonProperty("InventoryToggleHotkey")]
    private HotkeyNodeV2 SavedInventoryToggleHotkey
    {
        get => _inventoryToggleHotkey;
        set => _inventoryToggleHotkey = value ?? new(Keys.I);
    }

    [JsonProperty("LoadMapDeviceHotkey")]
    private HotkeyNodeV2 SavedLoadMapDeviceHotkey
    {
        get => _loadMapDeviceHotkey;
        set => _loadMapDeviceHotkey = value ?? new(Keys.None);
    }

    private static string NormalizeMapSelection(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "open Map";
        }

        var trimmed = value.Trim();
        if (trimmed.EqualsIgnoreCase("open Map") ||
            trimmed.EqualsIgnoreCase("open Map (Default)"))
        {
            return "open Map";
        }

        return trimmed;
    }
}

[Submenu(CollapsedByDefault = true)]
public class BestiaryAutomationSettings
{
    private HotkeyNodeV2 _deleteHotkey = new(Keys.None);
    private HotkeyNodeV2 _challengesWindowHotkey = new(Keys.None);
    private HotkeyNodeV2 _regexItemizeHotkey = new(Keys.None);

    [Menu("Quick Setup", "A compact setup summary for hotkeys, stash destinations, and bulk-action options.")]
    [JsonIgnore]
    public CustomNode SetupSummaryPanel { get; set; } = new();

    internal HotkeyNodeV2 DeleteHotkey
    {
        get => _deleteHotkey;
        set => _deleteHotkey = value ?? new(Keys.None);
    }

    internal HotkeyNodeV2 ChallengesWindowHotkey
    {
        get => _challengesWindowHotkey;
        set => _challengesWindowHotkey = value ?? new(Keys.None);
    }

    internal HotkeyNodeV2 RegexItemizeHotkey
    {
        get => _regexItemizeHotkey;
        set => _regexItemizeHotkey = value ?? new(Keys.None);
    }

    [Menu("Itemized Beast Tab", "Select the stash tab where itemized beasts are automatically stored. Open your stash in-game to populate the dropdown. Used when inventory fills during itemizing or when the process finishes.")]
    [JsonIgnore] public CustomNode StashTabSelector { get; set; } = new();

    [JsonIgnore] internal TextNode SelectedTabName { get; set; } = new(string.Empty);

    [Menu("Red Beast Tab", "Optional separate stash tab for itemized red (valuable) beasts. Leave empty to store all beasts in the main itemized-beast tab above.")]
    [JsonIgnore] public CustomNode RedBeastStashTabSelector { get; set; } = new();

    [JsonIgnore] internal TextNode SelectedRedBeastTabName { get; set; } = new(string.Empty);

    [Menu("Auto-Stash Itemized Beasts", "When enabled, itemized beasts are automatically moved to the configured stash tab whenever your inventory fills and after itemizing finishes. When disabled, beasts stay in inventory and itemizing stops when inventory is full.")]
    public ToggleNode RegexItemizeAutoStash { get; set; } = new(true);

    [Menu("Show Bestiary Quick Buttons", "Show Itemize All and Delete All quick-action buttons next to the Bestiary captured-beasts panel button for one-click bulk operations.")]
    public ToggleNode ShowBestiaryButtons { get; set; } = new(false);

    [Menu("Show Inventory Quick Button", "Show a Right Click All Beasts button next to your inventory while in The Menagerie or while the Bestiary panel is open, allowing you to quickly right-click all beast items at once.")]
    public ToggleNode ShowInventoryButton { get; set; } = new(false);

    [JsonProperty("StashTabName")]
    private string SavedStashTabName
    {
        get => SelectedTabName.Value;
        set => SelectedTabName.Value = value ?? string.Empty;
    }

    [JsonProperty("RedBeastStashTabName")]
    private string SavedRedBeastStashTabName
    {
        get => SelectedRedBeastTabName.Value;
        set => SelectedRedBeastTabName.Value = value ?? string.Empty;
    }

    [JsonProperty("ChallengesWindowHotkey")]
    private HotkeyNodeV2 SavedChallengesWindowHotkey
    {
        get => _challengesWindowHotkey;
        set => _challengesWindowHotkey = value ?? new(Keys.None);
    }

    [JsonProperty("RegexItemizeHotkey")]
    private HotkeyNodeV2 SavedRegexItemizeHotkey
    {
        get => _regexItemizeHotkey;
        set => _regexItemizeHotkey = value ?? new(Keys.None);
    }

    [JsonProperty("DeleteHotkey")]
    private HotkeyNodeV2 SavedDeleteHotkey
    {
        get => _deleteHotkey;
        set => _deleteHotkey = value ?? new(Keys.None);
    }
}

[Submenu(CollapsedByDefault = true)]
public class FullSequenceAutomationSettings
{
    private HotkeyNodeV2 _fullSequenceHotkey = new(Keys.None);

    [Menu("Quick Setup", "A compact readiness summary showing the full-sequence hotkey and its downstream dependencies.")]
    [JsonIgnore]
    public CustomNode SetupSummaryPanel { get; set; } = new();

    internal HotkeyNodeV2 FullSequenceHotkey
    {
        get => _fullSequenceHotkey;
        set => _fullSequenceHotkey = value ?? new(Keys.None);
    }

    [JsonProperty("FullSequenceHotkey")]
    private HotkeyNodeV2 SavedFullSequenceHotkey
    {
        get => _fullSequenceHotkey;
        set => _fullSequenceHotkey = value ?? new(Keys.None);
    }
}

[Submenu(CollapsedByDefault = true)]
public class MerchantAutomationSettings
{
    private HotkeyNodeV2 _faustusListHotkey = new(Keys.None);

    [Menu("Quick Setup", "A compact setup summary for the Faustus hotkey, price multiplier, and selected shop tab.")]
    [JsonIgnore]
    public CustomNode SetupSummaryPanel { get; set; } = new();

    internal HotkeyNodeV2 FaustusListHotkey
    {
        get => _faustusListHotkey;
        set => _faustusListHotkey = value ?? new(Keys.None);
    }

    [Menu("Faustus Price Multiplier", "Multiply poe.ninja beast prices before listing in Faustus. 1.0 keeps the default price, 0.5 undercuts heavily, and 1.5 prices more aggressively.")]
    public RangeNode<float> FaustusPriceMultiplier { get; set; } = new(1f, 0.5f, 1.5f);

    [Menu("Shop Tab", "Select the Faustus shop tab where itemized beasts will be listed for sale. Open Faustus shop in-game to populate the dropdown.")]
    [JsonIgnore] public CustomNode FaustusShopTabSelector { get; set; } = new();

    [JsonIgnore] internal TextNode SelectedFaustusShopTabName { get; set; } = new(string.Empty);

    [JsonProperty("FaustusShopTabName")]
    private string SavedFaustusShopTabName
    {
        get => SelectedFaustusShopTabName.Value;
        set => SelectedFaustusShopTabName.Value = value ?? string.Empty;
    }

    [JsonProperty("FaustusListHotkey")]
    private HotkeyNodeV2 SavedFaustusListHotkey
    {
        get => _faustusListHotkey;
        set => _faustusListHotkey = value ?? new(Keys.None);
    }
}

public class StashAutomationDynamicHintSettings
{
    [JsonIgnore]
    internal List<int> MapStashTierGroupPath { get; set; } = [];

    [JsonIgnore]
    internal List<int> MapStashPageTabContainerPath { get; set; } = [];

    [JsonIgnore]
    internal List<int> MapStashPageContentRootPath { get; set; } = [];

    [JsonProperty("MapStashTierGroupPath")]
    private List<int> SavedMapStashTierGroupPath
    {
        get => MapStashTierGroupPath;
        set => MapStashTierGroupPath = value ?? [];
    }

    [JsonProperty("MapStashPageTabContainerPath")]
    private List<int> SavedMapStashPageTabContainerPath
    {
        get => MapStashPageTabContainerPath;
        set => MapStashPageTabContainerPath = value ?? [];
    }

    [JsonProperty("MapStashPageContentRootPath")]
    private List<int> SavedMapStashPageContentRootPath
    {
        get => MapStashPageContentRootPath;
        set => MapStashPageContentRootPath = value ?? [];
    }
}

[Submenu(CollapsedByDefault = true)]
public class StashAutomationTargetSettings
{
    public const int MaxQuantity = 20;

    [Menu("Enable Slot", "Enable or disable this fixed Map Device slot. Disabled slots are skipped during restock and map-device loading.")]
    public ToggleNode Enabled { get; set; } = new(true);

    [Menu("Source Stash Tab", "Select the stash tab used to restock this Map Device slot. Open your stash in-game to populate the dropdown.")]
    [JsonIgnore] public CustomNode TabSelector { get; set; } = new();

    [JsonIgnore] internal TextNode SelectedTabName { get; set; } = new(string.Empty);

    [JsonProperty("TabName")]
    private string SavedTabName
    {
        get => SelectedTabName.Value;
        set => SelectedTabName.Value = value ?? string.Empty;
    }

    [Menu("Slot Item Name", "The exact item name for this slot, for example 'Map (Tier 16)' for Slot 1 or 'Bestiary Scarab of the Herd' for a fragment slot.")]
    public TextNode ItemName { get; set; } = new(string.Empty);

    [Menu("Slot Quantity", "Target quantity for this slot. Fragment/scarab slots load exactly this stack size, and the map slot keeps up to this many matching maps available. Maximum 20.")]
    public RangeNode<int> Quantity { get; set; } = new(20, 0, MaxQuantity);
}

[Submenu(CollapsedByDefault = true)]
public class MapRenderSettings
{
    [Menu("Show World Labels", "Draw floating name labels and ground circles on tracked beasts directly in the 3D game world.")]
    public ToggleNode ShowBeastLabelsInWorld { get; set; } = new(true);

    [Menu("Show Large Map Labels", "Draw beast name and price labels on the large overlay map (Tab key map).")]
    public ToggleNode ShowBeastsOnMap { get; set; } = new(true);

    [Menu("Show Tracked Beasts Window", "Show a small floating window that lists all currently alive tracked beasts in the area with their names, prices, and capture status.")]
    public ToggleNode ShowTrackedBeastsWindow { get; set; } = new(true);

    [Menu("Show Inventory Prices", "Show the poe.ninja price on top of captured beast items in your inventory.")]
    public ToggleNode ShowPricesInInventory { get; set; } = new(true);

    [Menu("Show Stash Prices", "Show the poe.ninja price on top of captured beast items in your stash.")]
    public ToggleNode ShowPricesInStash { get; set; } = new(true);

    [Menu("Show Bestiary Prices", "Show the poe.ninja price next to each beast in the Bestiary captured-beasts panel.")]
    public ToggleNode ShowPricesInBestiary { get; set; } = new(true);

    [Menu("Only Show Enabled Beasts", "When on, only beasts you have checked in Price Data → Enabled Beasts are shown on markers, overlays, and the tracked beasts window. When off, all rare beasts are shown.")]
    public ToggleNode ShowEnabledOnly { get; set; } = new(true);

    [Menu("Show Name Only On Map Labels", "On the large overlay map, show only the beast name without the price. Useful if you find prices distracting on the map. Only affects map markers, not inventory or stash overlays.")]
    public ToggleNode ShowNameInsteadOfPrice { get; set; } = new(false);

    [Menu("Show Style Preview Window", "Show a movable preview window with sample beast labels so you can see how your current color, text, and capture-status styling looks without needing to find a live beast in a map.")]
    public ToggleNode ShowStylePreviewWindow { get; set; } = new(false);

    [Menu("Captured Status Text", "Customize the text and colors shown on beast labels during the two capture stages: the initial capture-in-progress and the final safe-to-leave captured state.")]
    public CapturedTextDisplaySettings CapturedText { get; set; } = new();

    [Menu("Colors", "Customize all colors used by in-world beast labels, large-map markers, ground circles, and the tracked beasts window.")]
    public MapRenderColorSettings Colors { get; set; } = new();

    [Menu("Layout", "Adjust sizes, spacing, padding, and thickness for in-world beast labels, ground circles, and large-map label backgrounds.")]
    public MapRenderLayoutSettings Layout { get; set; } = new();

    [Menu("Experimental Exploration Route", "Experimental feature that generates an efficient walking route through the map to find all beasts, with coverage and path overlays on the large map.")]
    public ExplorationRouteSettings ExplorationRoute { get; set; } = new();
}

[Submenu(CollapsedByDefault = true)]
public class CapturedTextDisplaySettings
{
    [Menu("Capture Text Only", "When on, a beast being captured shows only the status text (e.g. 'Capturing') with no name or price. When off, the name and price remain visible with the status text added below.")]
    public ToggleNode ReplaceNameAndPriceWithStatusText { get; set; } = new(false);

    [Menu("Capturing Text", "Text shown on the label while a beast is being captured (first stage, net has been thrown). Change this to any text you prefer.")]
    public TextNode StatusText { get; set; } = new("Capturing");

    [Menu("Captured Text", "Text shown on the label after a beast is fully captured (second stage, safe to leave the map). Change this to any text you prefer.")]
    public TextNode CapturedStatusText { get; set; } = new("catched");

    [Menu("Capture Text Color", "Color of the capturing status text (first stage) shown in world labels, map labels, and the tracked beasts window.")]
    public ColorNode CaptureTextColor { get; set; } = new(new Color(57, 255, 20, 255));

    [Menu("Captured Text Color", "Color of the captured status text (second stage, safe to leave) shown in world labels, map labels, and the tracked beasts window.")]
    public ColorNode CapturedTextColor { get; set; } = new(new Color(120, 220, 255, 255));
}

[Submenu(CollapsedByDefault = true)]
public class MapRenderColorSettings
{
    [Menu("World Beast Text Color", "Name text color for tracked beasts in the 3D world that have not been captured yet.")]
    public ColorNode WorldBeastColor { get; set; } = new(new Color(180, 20, 20, 255));

    [Menu("World Captured Beast Text Color", "Name text color for tracked beasts in the 3D world that are currently being captured or have already been safely captured.")]
    public ColorNode WorldCapturedBeastColor { get; set; } = new(new Color(255, 40, 40, 255));

    [Menu("World Price Text Color", "Color of the price text shown below beast names on in-world labels.")]
    public ColorNode WorldPriceTextColor { get; set; } = new(new Color(255, 235, 120, 255));

    [Menu("World Text Outline Color", "Color of the outline drawn behind all in-world label text to keep it readable against bright or busy backgrounds.")]
    public ColorNode WorldTextOutlineColor { get; set; } = new(Color.Black);

    [Menu("World Beast Circle Color", "Color of the ground circle drawn around tracked beasts that have not been captured yet.")]
    public ColorNode WorldBeastCircleColor { get; set; } = new(new Color(180, 20, 20, 255));

    [Menu("World Capture Circle Color", "Color of the ground circle while a beast is actively being captured (first stage).")]
    public ColorNode WorldCaptureRingColor { get; set; } = new(Color.White);

    [Menu("World Catched Circle Color", "Color of the ground circle after a beast is fully captured and it is safe to leave the map (second stage).")]
    public ColorNode WorldCapturedCircleColor { get; set; } = new(new Color(120, 220, 255, 255));

    [Menu("Map Label Text Color", "Primary text color for beast labels shown on the large overlay map.")]
    public ColorNode MapMarkerTextColor { get; set; } = new(new Color(180, 20, 20, 255));

    [Menu("Map Label Background Color", "Background color of the label box behind beast text on the large overlay map.")]
    public ColorNode MapMarkerBackgroundColor { get; set; } = new(new Color(0, 0, 0, 230));

    [Menu("Tracked Window Beast Color", "Text color for beast names in the floating tracked beasts window.")]
    public ColorNode TrackedWindowBeastColor { get; set; } = new(new Color(180, 20, 20, 255));
}

[Submenu(CollapsedByDefault = true)]
public class MapRenderLayoutSettings
{
    [Menu("World Label Line Spacing", "Vertical spacing in pixels between lines on in-world beast labels (name, price, capture status).")]
    public RangeNode<float> WorldTextLineSpacing { get; set; } = new(18f, 8f, 40f);

    [Menu("World Beast Circle Radius", "Radius of the ground circle drawn around tracked beasts in the 3D world, in screen pixels.")]
    public RangeNode<float> WorldBeastCircleRadius { get; set; } = new(80f, 20f, 200f);

    [Menu("World Circle Outline Thickness", "Thickness of the ground circle outline in pixels.")]
    public RangeNode<float> WorldBeastCircleOutlineThickness { get; set; } = new(2f, 1f, 8f);

    [Menu("World Circle Fill Opacity (%)", "How opaque the filled area inside the ground circle is. 0 = fully transparent, 100 = fully solid.")]
    public RangeNode<int> WorldBeastCircleFillOpacityPercent { get; set; } = new(20, 0, 100);

    [Menu("Map Label Padding X", "Horizontal padding in pixels between beast text and the edge of its label background on the large map.")]
    public RangeNode<float> MapLabelPaddingX { get; set; } = new(4f, 0f, 20f);

    [Menu("Map Label Padding Y", "Vertical padding in pixels between beast text and the edge of its label background on the large map.")]
    public RangeNode<float> MapLabelPaddingY { get; set; } = new(2f, 0f, 20f);

}

[Submenu(CollapsedByDefault = true)]
public class ExplorationRouteSettings
{
    [Menu("Enable Exploration Route", "Master toggle for the exploration route feature. When off, route generation, path overlays, debug overlays, and exclusion-zone visuals stay inactive.")]
    public ToggleNode Enabled { get; set; } = new(false);

    [Menu("Show Route On Large Map", "Draw the generated exploration route as connected lines on the large overlay map, showing the suggested path through the area.")]
    public ToggleNode ShowExplorationRoute { get; set; } = new(false);

    [Menu("Show Route Coverage On Large Map", "Draw a circle around each waypoint on the large map showing the area covered by your detection radius when you pass through it.")]
    public ToggleNode ShowCoverageOnMiniMap { get; set; } = new(false);

    [Menu("Detection Radius (Grid Units)", "How far from each waypoint beasts can be detected, in grid units. Larger values produce fewer waypoints with wider coverage. Also controls the coverage circle size.")]
    public RangeNode<int> DetectionRadius { get; set; } = new(186, 20, 500);

    [Menu("Waypoint Visit Radius (Grid Units)", "How close you need to walk to a waypoint before it is marked as visited and the route advances to the next one.")]
    public RangeNode<int> WaypointVisitRadius { get; set; } = new(35, 5, 200);

    [Menu("Follow Map Outline First", "Generate a route that walks the outer edge of the map first, starting from the waypoint nearest you, then fills in the interior. Click Recalculate after toggling. When off, the Perimeter-First Route or nearest-neighbor mode below is used instead.")]
    public ToggleNode FollowMapOutlineFirst { get; set; } = new(false);

    [Menu("Perimeter-First Route", "Only used when Follow Map Outline First is off. Walks waypoints in layers from the outer edges inward, clearing the perimeter before the interior. When off, uses simple nearest-neighbor ordering instead.")]
    public ToggleNode PreferPerimeterFirstRoute { get; set; } = new(true);

    [Menu("Visit Outer Shell Last", "Only used with Perimeter-First Route. Reverses the order so the interior is cleared first and the outer edges are visited last.")]
    public ToggleNode VisitOuterShellLast { get; set; } = new(false);

    [Menu("Recalculate Exploration Route", "Click to regenerate the exploration route using current settings. Most setting changes trigger this automatically, but use this button after editing exclusion paths or if the route looks outdated.")]
    public ButtonNode RecalculateExplorationRoute { get; set; } = new();

    [Menu("Show Radar Path To Next Waypoint", "Draw a pathfinding line from your current position to the next unvisited waypoint, showing the shortest walkable route.")]
    public ToggleNode ShowPathsToBeasts { get; set; } = new(false);

    [Menu("Excluded Entity Paths (one per line, ; or ,)", "Game entity metadata paths to exclude from the route. Waypoints near matching entities are removed. Enter one path per line, or separate with ; or , characters. Use this to avoid boss arenas, hazards, or other areas.")]
    public TextNode ExcludedEntityPaths { get; set; } = new("Metadata/Terrain/Mountain/DriedLake/Features/tent_SmallOld_v02_01.tdt");

    [Menu("Excluded Entity Paths List", "A user-friendly list editor for the excluded entity paths above. Add or remove paths with buttons instead of editing raw text. Changes sync automatically.")]
    [JsonIgnore]
    public CustomNode ExcludedEntityPathsList { get; set; } = new();

    [Menu("Entity Exclusion Radius (Grid Units)", "How far in grid units around each excluded entity to remove waypoints. Larger values create bigger no-go zones on the route.")]
    public RangeNode<int> EntityExclusionRadius { get; set; } = new(300, 50, 1200);

    [Menu("Show Entity Exclusion Zones On Map", "Draw circles on the large map showing the no-go zone around each matched excluded entity.")]
    public ToggleNode ShowEntityExclusionZones { get; set; } = new(true);

    [Menu("Exclusion Zone Color", "Color of the exclusion zone circles drawn on the large map.")]
    public ColorNode ExclusionZoneColor { get; set; } = new(new Color(220, 50, 50, 140));

    [Menu("Route Style", "Customize the visual appearance of the exploration route overlay: line colors, thickness, waypoint dot sizes, and coverage circle styling.")]
    public ExplorationRouteStyleSettings Style { get; set; } = new();

    [Menu("Debug Overlays", "Developer tools that visualize the walkable grid, obstacle cells, and wall-distance data used to generate the exploration route.")]
    public ExplorationRouteDebugSettings Debug { get; set; } = new();
}

[Submenu(CollapsedByDefault = true)]
public class ExplorationRouteStyleSettings
{
    [Menu("Route Line Color", "Color of the lines connecting unvisited waypoints on the route.")]
    public ColorNode RouteLineColor { get; set; } = new(new Color(51, 204, 255, 178));

    [Menu("Visited Route Line Color", "Color of route lines between waypoints you have already passed through. Usually dimmed.")]
    public ColorNode VisitedLineColor { get; set; } = new(new Color(127, 127, 127, 64));

    [Menu("Waypoint Color", "Color of the dots marking waypoints you have not yet visited.")]
    public ColorNode WaypointColor { get; set; } = new(new Color(51, 204, 255, 178));

    [Menu("Next Waypoint Color", "Highlight color of the dot marking the next waypoint you should walk toward.")]
    public ColorNode NextWaypointColor { get; set; } = new(new Color(255, 255, 0, 255));

    [Menu("Coverage Circle Color", "Color of the detection-radius circles drawn around unvisited waypoints on the large map.")]
    public ColorNode CoverageColor { get; set; } = new(new Color(255, 255, 51, 46));

    [Menu("Detection Radius Color", "Color of the circle drawn around your character showing your current beast detection range.")]
    public ColorNode DetectionRadiusColor { get; set; } = new(new Color(255, 255, 51, 115));

    [Menu("Route Line Thickness", "Thickness in pixels of the lines connecting waypoints on the route overlay.")]
    public RangeNode<float> RouteLineThickness { get; set; } = new(1.5f, 0.5f, 5f);

    [Menu("Coverage Circle Thickness", "Thickness in pixels of the coverage radius circles drawn around waypoints.")]
    public RangeNode<float> CoverageLineThickness { get; set; } = new(1f, 0.5f, 5f);

    [Menu("Detection Radius Thickness", "Thickness in pixels of the detection radius circle around your character.")]
    public RangeNode<float> DetectionRadiusThickness { get; set; } = new(1.5f, 0.5f, 5f);

    [Menu("Waypoint Dot Radius", "Size in pixels of the dots marking unvisited waypoints.")]
    public RangeNode<float> WaypointDotRadius { get; set; } = new(2f, 1f, 10f);

    [Menu("Next Waypoint Dot Radius", "Size in pixels of the highlighted dot marking the next target waypoint.")]
    public RangeNode<float> NextWaypointDotRadius { get; set; } = new(5f, 2f, 15f);
}

[Submenu(CollapsedByDefault = true)]
public class ExplorationRouteDebugSettings
{
    [Menu("Show Walkable Cells", "Draw dots on the large map for every grid cell the pathfinder considers walkable near your character.")]
    public ToggleNode ShowWalkableCells { get; set; } = new(false);

    [Menu("Show Obstacle Cells", "Draw dots on the large map for non-walkable cells (walls, edges) next to the walkable area near your character.")]
    public ToggleNode ShowObstacleCells { get; set; } = new(false);

    [Menu("Show Distance Field", "Color-code each walkable cell by how far it is from the nearest wall: red = close to wall, green = medium distance, blue = far from walls.")]
    public ToggleNode ShowDistanceField { get; set; } = new(false);

    [Menu("Debug Render Radius (Grid Units)", "How far from your character debug cells are drawn, in grid units. Larger values show more of the map but may reduce performance.")]
    public RangeNode<int> DebugCellRadius { get; set; } = new(200, 50, 600);

    [Menu("Debug Cell Sample Step", "Draw every Nth cell to improve performance. 1 = every cell (most detail, slowest), 2 = every other cell, higher = fewer dots drawn.")]
    public RangeNode<int> DebugCellSampleStep { get; set; } = new(2, 1, 8);

    [Menu("Walkable Cell Color", "Color of the debug dots drawn on walkable grid cells.")]
    public ColorNode WalkableColor { get; set; } = new(new Color(0, 220, 0, 80));

    [Menu("Obstacle Cell Color", "Color of the debug dots drawn on non-walkable (wall or edge) grid cells.")]
    public ColorNode ObstacleColor { get; set; } = new(new Color(220, 50, 50, 100));

    [Menu("Debug Cell Dot Radius", "Size in pixels of each debug cell dot on the large map.")]
    public RangeNode<float> DebugDotRadius { get; set; } = new(1.5f, 0.5f, 5f);
}

[Submenu(CollapsedByDefault = true)]
public class AutomationStatusOverlaySettings
{
    [Menu("Show", "Show or hide the automation status overlay banner.")]
    public ToggleNode Show { get; set; } = new(true);

    [Menu("Show Preview While Idle", "Show a sample automation status banner even when no automation is running, so you can position and style the overlay.")]
    public ToggleNode ShowPreviewWhileIdle { get; set; } = new(false);

    [Menu("X Position (%)", "Horizontal position of the status overlay as a percentage of screen width. 0 = left edge, 50 = center, 100 = right edge.")]
    public RangeNode<float> XPos { get; set; } = new(50, 0, 100);

    [Menu("Y Position (%)", "Vertical position of the status overlay as a percentage of screen height. 0 = top edge, 100 = bottom edge.")]
    public RangeNode<float> YPos { get; set; } = new(4, 0, 100);

    [Menu("Status Duration (Seconds)", "How long a success or info status message stays visible on screen after the automation step finishes.")]
    public RangeNode<int> StatusDurationSeconds { get; set; } = new(5, 1, 30);

    [Menu("Error Duration (Seconds)", "How long an error message stays visible on screen when an automation step fails.")]
    public RangeNode<int> ErrorDurationSeconds { get; set; } = new(10, 1, 30);

    [Menu("Padding", "Inner spacing between overlay text and window border.")]
    public RangeNode<float> Padding { get; set; } = new(8, 0, 50);

    [Menu("Border Thickness", "Border thickness of the automation status overlay.")]
    public RangeNode<int> BorderThickness { get; set; } = new(1, 1, 10);

    [Menu("Border Rounding", "Corner roundness of the automation status overlay.")]
    public RangeNode<float> BorderRounding { get; set; } = new(4, 0, 25);

    [Menu("Text Scale", "Text scale of the automation status overlay.")]
    public RangeNode<float> TextScale { get; set; } = new(1.1f, 0.5f, 6f);

    [Menu("Status Text Color", "Text color for success and info automation status messages.")]
    public ColorNode TextColor { get; set; } = new(new Color(255, 235, 180, 255));

    [Menu("Status Border Color", "Border color for success and info automation status messages.")]
    public ColorNode BorderColor { get; set; } = new(new Color(190, 140, 40, 255));

    [Menu("Error Text Color", "Text color for automation failure messages.")]
    public ColorNode ErrorTextColor { get; set; } = new(new Color(255, 180, 180, 255));

    [Menu("Error Border Color", "Border color for automation failure messages.")]
    public ColorNode ErrorBorderColor { get; set; } = new(new Color(220, 70, 70, 255));

    [Menu("Background Color", "Background color of the automation status overlay.")]
    public ColorNode BackgroundColor { get; set; } = new(new Color(0, 0, 0, 190));
}

[Submenu(CollapsedByDefault = true)]
public class AnalyticsWebServerSettings
{
    [Menu("Enable", "Enable or disable the local analytics web server.")]
    public ToggleNode Enabled { get; set; } = new(false);

    [Menu("Port", "Local HTTP port used by the analytics dashboard and API. If the port is unavailable, startup fails until you choose another one.")]
    public RangeNode<int> Port { get; set; } = new(18421, 1024, 65535);

    [Menu("Allow Network Access", "Allow other devices on your local network to open the dashboard. Keep disabled for localhost-only access.")]
    public ToggleNode AllowNetworkAccess { get; set; } = new(false);

    [Menu("Extra Cost Per Map (c)", "Additional flat chaos cost per map run not already covered by stash automation targets (e.g. fragments, maps).")]
    public RangeNode<float> ExtraCostPerMapChaos { get; set; } = new(0f, 0f, 500f);

    [Menu("Rolling Stats Window (Maps)", "How many recent maps are used for rolling average and percentile analytics.")]
    public RangeNode<int> RollingStatsWindowMaps { get; set; } = new(10, 1, 100);

    [Menu("Copy Dashboard URL", "Copy the analytics dashboard URL to your clipboard.")]
    public ButtonNode CopyUrl { get; set; } = new();

    [Menu("Open Dashboard In Browser", "Open the analytics dashboard URL in your default browser.")]
    public ButtonNode OpenInBrowser { get; set; } = new();
}
