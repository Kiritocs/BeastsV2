# BeastsV2

Bestiary farming automation and analytics plugin for ExileApi.

Version: 0.1.0
Compatibility: Windows, Path of Exile 1, ExileApi, net10.0-windows
Repository: https://github.com/Kiritocs/BeastsV2

Quick links:

- [INSTALL.md](INSTALL.md)

Beasts V2 tracks valuable beasts in real time, estimates value using poe.ninja data, records detailed session analytics, and can automate most of the hideout-side workflow: itemizing beasts, listing them with Faustus, and preparing the next map by restocking missing items and loading the Map Device.

This README is written as a practical user guide. It explains what the plugin can do, how to configure it correctly, and how to run each workflow without fighting the UI.

The settings menu is grouped by workflow and feature area. The main top-level sections are `Overview`, `Automation`, `Tracking: ...`, `Overlays: ...`, `Analytics: Web Dashboard`, `What's New`, and `Diagnostics: Verbose Logging`.

## Requirements

- Windows.
- Path of Exile 1.
- ExileApi.
- .NET 10 SDK only if you want to build the plugin manually.

For install and update steps, see [INSTALL.md](INSTALL.md).

## What the Plugin Can Do

### Tracking and overlays

- Count rare beasts found in the current area.
- Show a completion message when all beasts in the area are found.
- Show a second completion message when all tracked valuable beasts are safely captured.
- Draw beast labels in the world and on the large map.
- Show tracked beast prices in the world, on the large map, in inventory, in stash, and in the Bestiary panel.
- Show a floating tracked-beasts window for valuable alive beasts in the current area.
- Auto-hide overlays when you are in hideout or when blocking UI panels are open.

### Price and value tracking

- Fetch beast prices from poe.ninja for your configured league.
- Fetch market prices for common map-running costs such as scarabs, maps and other fragments.
- Let you choose which beasts count as valuable for overlays, analytics, and Bestiary regex generation.
- Calculate per-map cost, estimated value, captured value, and net profit.

### Analytics

- Track current map duration, session duration, completed map count, and average clear time.
- Record per-map beast encounters, captured vs missed beasts, and value history.
- Save session snapshots to disk.
- Provide a local web dashboard with sortable history, exports, saved sessions, and A/B comparisons.
- Show family-level profit breakdowns, spawn-rate tables, clear-speed analysis, and recent performance trends.

### Automation

- Open and load the Map Device from your configured inventory loadout.
- Optionally auto-restock missing maps and scarabs before loading the device.
- Optionally filter map restock with a stash-search regex so only highlighted matching maps are eligible for the map slot.
- Optionally select a specific Atlas map by name before loading the device.
- Clear captured beasts by itemizing them in The Menagerie.
- Delete captured beasts with a dedicated hotkey when you want cleanup instead of selling.
- Auto-stash itemized beasts into a normal tab and optionally a separate red-beast tab.
- List itemized beasts in a Faustus shop tab using poe.ninja prices and a configurable multiplier.
- Run a one-key sell sequence: itemize -> list with Faustus.
- Optionally suppress user mouse and keyboard input while automation is active so manual interference does not break runs.

### Quality-of-life extras

- Auto-copy and auto-paste a Bestiary search regex when the captured-beasts panel opens.
- Show quick Itemize All / Delete All buttons beside the Bestiary panel.
- Show a Right Click All Beasts button beside inventory while in The Menagerie.
- Draw an experimental exploration route overlay on the large map.

## First-Time Setup

Do this once before relying on automation.

### 1. Enable the plugin and fetch price data

1. Open `Beasts V2` settings.
2. Open `Tracking: Price Data`.
3. Set `League` to your exact current league name.
4. Press `Refresh Prices`.
5. Confirm the `Last Updated` time changes.

If the league name is wrong, price-based overlays and Faustus listing will be incomplete or unusable.

### 2. Choose which beasts are valuable

1. Open `Tracking: Price Data -> Tracked Beasts`.
2. Tick the beasts you care about.
3. Leave `Only Show Enabled Beasts` on if you want cleaner overlays.

The tracked-beast list drives three major behaviors:

- Which beasts are emphasized in overlays.
- Which beasts count as tracked value in analytics.
- Which beasts are used to auto-generate the Bestiary regex.

### 3. Configure your hotkeys correctly

Set every hotkey you plan to use. The important ones are:

- `Automation -> Hotkeys -> Run Sell Sequence`
- `Automation -> Hotkeys -> Prepare Map Device`
- `Automation -> Hotkeys -> Delete Visible Beasts`

Also verify the helper hotkeys match your in-game keybinds:

- `Automation -> Hotkeys -> Inventory Keybind` must match your real Path of Exile inventory key.
- `Automation -> Hotkeys -> Challenges Keybind` must match your real Path of Exile Challenges key if you want delete mode.

If these do not match the game, automation will look broken even though the plugin is doing exactly what it was configured to do.

### 4. Configure stash automation targets

1. Open your stash in game.
2. In `Automation -> Stash & Map Device`, choose a stash tab for each enabled target.
3. Enter the exact item name to pull.
4. Enter the desired quantity.

The default targets are set up for a typical Bestiary loop:

- `Map (Tier 16)`
- `Bestiary Scarab of the Herd`
- `Bestiary Scarab of Duplicating`

Important rules:

- The item name match should be exact.
- The selected stash tabs only populate when the stash is open.
- Turn on `Auto Restock Missing Map Device Items` if you want `Prepare Map Device` to refill missing maps and scarabs automatically before loading the device.
- Turn on `Enable Map Regex Filter` and set `Map Regex Pattern` if you want map restock to paste a stash-search regex and only pull highlighted matching maps for the map slot.
- If automation skips items, increase `Flat Extra Delay (ms)` by 5.

If you use map regex filtering, build the regex first with a tool such as `https://poe.re/#/maps`. Fragment and scarab slots are never filtered by this regex.

### 5. Configure Bestiary stash tabs

1. Open your stash in game.
2. In `Automation -> Bestiary`, pick `Itemized Beast Tab`.
3. Optionally pick `Red Beast Tab`.

If you leave the red tab empty, red beasts use the normal itemized-beasts tab.

### 6. Configure Faustus selling

1. Open Faustus shop in game.
2. In `Automation -> Merchant`, choose `Shop Tab`.
3. Set your `Faustus Price Multiplier`.

Use `1.0` to list at raw poe.ninja value. Lower values undercut more aggressively. Higher values list more optimistically.

### 7. Configure map selection behavior

In `Automation -> Stash & Map Device -> Atlas Map Selection`:

- Set a specific map name if you want Atlas selection automation.
- Leave it empty or use `open Map` if you want the plugin to keep the current map selection and only load the device.

### 8. Optional: enable the analytics web dashboard

The analytics dashboard and session handling are both still in a very, very early stage. Expect rough edges, incomplete workflows, and occasional data inconsistencies.

1. Turn on `Analytics: Web Dashboard -> Enable`.
2. Keep the default port unless it conflicts with something else.
3. Use `Open Dashboard In Browser` or `Copy Dashboard URL`.

Default local URL:

```text
http://localhost:18421/
```

Keep `Allow Network Access` off unless you intentionally want other devices on your network to reach the dashboard.

If you want analytics visible in hideout, leave `Overlays -> Visibility -> Hide Analytics In Hideout` off. Counter and completion-message hideout visibility is controlled separately by `Hide Counter & Message In Hideout`.

### 9. Optional: reduce accidental automation interference

If you tend to bump the mouse, scroll stash, or press unrelated keys during automation, enable `Automation -> Timing -> Lock User Input During Automation`.

Behavior:

- Normal user mouse movement, clicks, scrolls, and unrelated key presses are suppressed while automation is running.
- Beasts V2 still lets its own internal inputs through.
- Automation trigger hotkeys still pass through so you can stop or replace an active run.

## How to Use the Plugin Correctly

### 1. While mapping

During a map, the plugin is mostly passive.

What you should expect:

- The counter tracks rare beasts found in the area.
- Valuable beasts can be highlighted in the world and on the large map.
- The tracked-beasts window can show which valuable beasts are still alive.
- When all beasts are found, the completed overlay can appear.
- When all tracked valuable beasts are captured, the tracked-completion overlay can appear.

This is the basic intended map loop:

1. Enter a runnable map area.
2. Watch the overlays for valuable beast spawns.
3. Capture what you want.
4. Use the completion overlays as your signal for whether the map is effectively done.
5. Leave the map and let analytics record the run.
6. Press `Prepare Map Device` to go into the next map.

### 2. Bestiary regex workflow

This is the fastest way to filter desirable captured beasts in the Bestiary panel.

How it works:

- When the captured-beasts panel opens, the plugin can copy a regex to clipboard.
- If auto-paste is enabled, it also pastes the regex into the search box and applies it.
- The regex is either auto-generated from your tracked-beast selection or taken from the manual regex field.

Recommended usage:

1. Keep auto-generated regex enabled unless you have a very specific custom filter.
2. Maintain your valuable beast list in `Tracking: Price Data -> Tracked Beasts` instead of constantly editing regex by hand.
3. Use manual regex only for temporary niche farming or cleanup passes.

### 3. Itemizing beasts in The Menagerie

Use this when you want tradable beast items instead of leaving captures in the Bestiary.

### Standard itemize run

1. Press `Itemize Matching Beasts`.
2. The plugin travels to The Menagerie.
3. It finds Einhar and opens the Bestiary interface.
4. It opens the captured-beasts tab.
5. It applies the active regex.
6. It itemizes all matching beasts.
7. If `Auto-Stash Itemized Beasts` is enabled and inventory fills, it stashes itemized beasts and continues.

### Delete mode

Use this only if you intentionally want to destroy captured beasts.

1. Make sure `Automation -> Hotkeys -> Challenges Keybind` matches your game keybind if you use delete outside The Menagerie.
2. Press `Delete Visible Beasts`.

Delete mode is permanent. There is no recovery step in the plugin.

### Quick buttons

If enabled, the plugin can show:

- `Itemize All` and `Delete All` buttons next to the Bestiary captured-beasts panel.
- `Right Click All Beasts` beside inventory while in The Menagerie.

These are convenience tools, not a different system. They follow the same automation rules.

### 4. Listing beasts with Faustus

Use this after you have itemized beasts and want to list them for sale.

Workflow:

1. Press `List Beasts In Faustus`.
2. The plugin travels to hideout if needed.
3. It finds Faustus and opens the merchant panel.
4. It switches to the configured shop inventory.
5. It selects your configured `Shop Tab`.
6. It Ctrl-clicks itemized beasts from inventory.
7. It fills the price popup using poe.ninja price times your configured `Faustus Price Multiplier`.

Important behavior:

- Beasts without price data are skipped.
- If the selected `Shop Tab` is full, automation stops with an error.
- If no `Shop Tab` is configured, listing cannot start.

### 5. Optional: restocking from stash

Use this in hideout only if you want to refill inventory manually without immediately loading the Map Device.

Workflow:

1. Press `Restock Inventory`.
2. It finds the Stash and opens it.
3. The plugin reads each enabled target in order.
4. It switches to the configured stash tab.
5. It Ctrl-clicks items until the requested quantity is reached, stash runs out, or inventory is full.

Correct usage rules:

- If the plugin is loading fewer items than expected, first verify exact item names, then raise timing delays.
- If `Enable Map Regex Filter` is on, only stash-highlighted maps that match `Map Regex Pattern` are eligible for the map slot.
- If `Enable Map Regex Filter` is on but `Map Regex Pattern` is empty, restock stops with an error instead of guessing.

### 6. Loading the Map Device

This is the main map-prep hotkey. Use it to prepare the Map Device. If `Auto Restock Missing Map Device Items` is enabled, the plugin first checks the current Map Device, storage, and inventory state, and only runs restock when required items are actually missing.

Workflow:

1. Press `Prepare Map Device`.
2. The plugin closes blocking UI unless Atlas is already open and reusable.
3. It walks to the Map Device if necessary and opens it.
4. If a specific map is configured, it opens or validates Atlas selection and chooses that map.
5. It checks whether the current Map Device state, Map Device storage, and inventory already satisfy the configured loadout.
6. If auto-restock is enabled and required items are missing, it closes the Map Device UI, opens stash, and runs the restock step.
7. After restocking, it reopens the Map Device and re-applies the configured map selection if needed.
8. It loads the map and configured fragments/scarabs from inventory.
9. It verifies the device contents and moves the mouse to the Activate button.

Important behavior:

- If the device already contains a clean subset of your configured loadout, the plugin only tops up missing items.
- If `Auto Restock Missing Map Device Items` is enabled, missing configured items are pulled from stash automatically before the device is loaded.
- If `Enable Map Regex Filter` is enabled, the stash search is applied before map restock and only highlighted maps are considered for the map slot.
- If `Atlas Map Selection` is `open Map`, Atlas map selection is skipped.
- The `Inventory Keybind` setting is used to close inventory before Atlas scanning when needed.

Best practice:

1. Turn on `Auto Restock Missing Map Device Items` if you want one-button map prep.
2. Press `Prepare Map Device`.
3. Let it refill missing items and load the device.
4. Activate the device manually once the cursor is moved to the button.

### 7. Sell sequence automation

This is the one-key Bestiary selling loop.

Order of operations:

1. Itemize beasts from Bestiary using the active regex.
2. List those itemized beasts with Faustus.

Use it when you want the plugin to handle clearing Bestiary and selling the resulting beasts, but not your map-prep steps.

Things to know:

- If the regex is empty, the sell sequence stops immediately.
- If zero beasts were itemized, the Faustus step is skipped.
- During sell sequence, the itemize step keeps newly itemized beasts in inventory instead of auto-stashing them, so the Faustus step can list them next.
- If inventory becomes full during the sell-sequence itemize step, the sequence continues with whatever was already itemized.
- Sell sequence is only as reliable as your individual setup for Bestiary and Faustus automation.

Map preparation stays separate:

1. Use `Prepare Map Device` for normal map prep.
2. Use `Restock Inventory` only if you still want a manual refill step without loading the Map Device.

If you are setting the plugin up for the first time, get each individual workflow stable before relying on the sell-sequence hotkey.

### 8. Analytics overlay and dashboard

### In-game analytics overlay

The overlay can show:

- Session duration.
- Current map duration.
- Completed map count.
- Average map time.

You can also:

- Reset the full session.
- Reset the map-average calculation only.
- Save the current session to file.

Hideout visibility is split:

- `Overlays -> Visibility -> Hide Counter & Message In Hideout` controls the beast counter and completion message.
- `Overlays -> Visibility -> Hide Analytics In Hideout` controls the analytics overlay.

### Web dashboard

The web dashboard is useful when you want deeper review than the in-game overlay.

It is still in a very, very early stage, and the same applies to session handling and saved-session workflows.

It can show:

- Current session summary.
- Map history.
- Captured vs missed beast data.
- Cost, EV, captured profit, and net profit.
- Area-by-area clear-speed analysis.
- Saved sessions and load/unload behavior.
- A/B comparisons using named session saves and tags.
- JSON and CSV export.

Recommended usage:

1. Enable `Analytics: Web Dashboard -> Enable`.
2. Run maps normally.
3. Open the dashboard between maps or after a farming session.
4. Save named sessions for different strategies.
5. Use compare tools only after you have enough sample size to make the results meaningful.

### 9. Exploration route overlay

This feature is experimental.

It can generate a route over the large map to help you sweep a map efficiently for beasts.

Suggested way to use it:

1. Turn on `Show Route On Large Map`.
2. Adjust `Detection Radius` and `Waypoint Visit Radius` conservatively first.
3. Press `Recalculate Exploration Route` after changing route-related settings.
4. Add excluded entity paths if the route keeps sending you into bad or irrelevant spaces.

If you do not actively want route assistance, leave this entire section off.

## Common Mistakes

These are the biggest setup problems users run into.

### Wrong league name

If price data looks empty or outdated, check the exact league string first.

### Hotkeys do not match the game

Two settings must match your real Path of Exile keybinds:

- `Automation -> Hotkeys -> Inventory Keybind`
- `Automation -> Hotkeys -> Challenges Keybind`

Mismatch here breaks Atlas selection or delete mode.

### Stash or Faustus tabs are not configured

The dropdowns only populate when the relevant UI is open in game.

- Open stash before setting stash tabs.
- Open Faustus shop before setting the Faustus tab.

### Automation is too fast for your machine

If clicks are skipped or UI lookups fail, increase `Flat Extra Delay (ms)` by 5 and try again. Repeat until it works.

### Using sell sequence before the parts work individually

Do not debug sell sequence first. Make these work separately first:

1. Bestiary itemize
2. Faustus list
3. Load Map Device
4. Manual Restock, only if you plan to use it separately

### Delete hotkey used by accident

Double-check before pressing `Delete Visible Beasts`.

## Troubleshooting

If you hit an error or automation behaves unexpectedly, send the `BeastsV2.log` file from the plugin config folder when reporting the issue.
You do not need to enable `Diagnostics: Verbose Logging` first. Normal info/error logs still go to `BeastsV2.log`; that toggle only controls whether detailed step-by-step automation diagnostics are also mirrored into ExileApi logs.

### Itemize stops because inventory is full

Either:

1. Turn on `Auto-Stash Itemized Beasts`, or
2. Free inventory space before running the itemize automation.

### Faustus listing skips beasts

That usually means the skipped beasts do not have usable price data.

Check:

1. Your league name.
2. Whether poe.ninja currently has data for those beasts.
3. Whether the itemized beast is one of your priced tracked beasts.

### Map selection behaves inconsistently

Check:

1. `Inventory Keybind` matches the game.
2. `Atlas Map Selection` is correct.
3. The Atlas is not being obscured by UI.
4. Your automation delays are not too aggressive.

### Travel commands seem to fail

Blocking UI can interfere with travel-related automation. Close extra panels and try again. The plugin already attempts to close blocking UI, but bad timing or manual interference can still cause misses.

### A workflow seems stalled

You can stop a running automation by requesting another automation run or by using the quick `Stop` button where available.

## Safe Operating Habits

- **Clean up your inventory before running any automation.** This is very important to prevent items from being lost or misplaced.
- Do not click around the UI while automation is actively running unless you intentionally rely on `Lock User Input During Automation` to suppress your own inputs.
- Set up every stash or merchant tab with the relevant UI open, not from memory.
- Test new automation settings with cheap items before trusting them with a full farming loop.
- Treat delete mode as destructive.
- Keep network access off for the web server unless you intentionally need it.
