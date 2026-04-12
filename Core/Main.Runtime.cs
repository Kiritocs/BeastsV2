using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BeastsV2.Runtime.Analytics;
using BeastsV2.Runtime;
using BeastsV2.Runtime.Automation;
using BeastsV2.Runtime.Features;
using GameOffsets.Native;
using ExileCore.Shared.Enums;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2;

public partial class Main
{
    private BeastsRuntime _runtime;
    private AutomationRunCoordinator _automationRunCoordinator;
    private AutomationHotkeyTracker _automationHotkeyTracker;
    private AutomationInputLockService _automationInputLockService;
    private BestiaryAutomationWorkflow _bestiaryAutomationWorkflow;
    private BestiaryUiOpenService _bestiaryUiOpenService;
    private BestiaryClearService _bestiaryClearService;
    private BestiaryCapturedMonsterStashService _bestiaryCapturedMonsterStashService;
    private MapStashUiService _mapStashUiService;
    private RestockTransferBatchService _restockTransferBatchService;
    private RestockTransferConfirmationService _restockTransferConfirmationService;
    private RestockTransferPlannerService _restockTransferPlannerService;
    private MapDeviceAutomationWorkflow _mapDeviceAutomationWorkflow;
    private MapDeviceLoadPlanService _mapDeviceLoadPlanService;
    private MapDeviceVerificationService _mapDeviceVerificationService;
    private MerchantAutomationWorkflow _merchantAutomationWorkflow;
    private FullSequenceAutomationWorkflow _fullSequenceAutomationWorkflow;
    private ExplorationRouteState _explorationRouteState;
    private MapRenderState _mapRenderState;
    private AnalyticsPersistenceState _analyticsPersistenceState;
    private AnalyticsSessionPersistenceService _analyticsSessionPersistenceService;
    private AnalyticsSnapshotService _analyticsSnapshotService;
    private AnalyticsWebRuntimeState _analyticsWebRuntimeState;
    private AnalyticsWebServerCoordinator _analyticsWebServerCoordinator;
    private AnalyticsReplayEventTrackerService _analyticsReplayEventTrackerService;
    private AnalyticsMapCostTrackingService _analyticsMapCostTrackingService;
    private AnalyticsSessionAggregationService _analyticsSessionAggregationService;
    private BestiaryCapturedBeastsViewService _bestiaryCapturedBeastsViewService;
    private BeastLookupService _beastLookupService;
    private MapRenderPresentationService _mapRenderPresentationService;
    private MapRenderImGuiOverlayService _mapRenderImGuiOverlayService;
    private MapRenderBeastOverlayService _mapRenderBeastOverlayService;
    private MapRenderLabelService _mapRenderLabelService;
    private MapRenderLargeMapOverlayService _mapRenderLargeMapOverlayService;
    private MapRenderDrawingPrimitivesService _mapRenderDrawingPrimitivesService;
    private MapRenderPathOverlayService _mapRenderPathOverlayService;
    private ExplorationRouteRefreshService _explorationRouteRefreshService;
    private ExplorationRoutePlanningService _explorationRoutePlanningService;
    private MapRenderPanelOverlayService _mapRenderPanelOverlayService;

    private BeastsRuntime Runtime => _runtime ??= new BeastsRuntime(this);

    private AutomationRunCoordinator AutomationRuns => _automationRunCoordinator ??= new AutomationRunCoordinator(
        Runtime.State.Automation,
        new AutomationRunCoordinatorCallbacks(
            BeginAutomationOverlaySession,
            EndAutomationOverlaySession,
            ResetAutomationState,
            EnableAutomationInputLock,
            DisableAutomationInputLock,
            ReleaseAutomationModifierKeys,
            ShowAutomationError,
            (message, forceLog) => UpdateAutomationStatus(message, forceLog),
            (message, ex) => LogError(message, ex),
            (debugContext, options) => PrepareAutomationUiAsync(debugContext, options)));

    private AutomationHotkeyTracker AutomationHotkeys => _automationHotkeyTracker ??= new AutomationHotkeyTracker(Runtime.State.Automation);

    private AutomationInputLockService AutomationInputLock => _automationInputLockService ??= new AutomationInputLockService(
        Runtime.State.Automation,
        () => Settings?.AutomationTiming?.LockUserInputDuringAutomation?.Value == true,
        LogDebug);

    private BestiaryUiOpenService BestiaryUi => _bestiaryUiOpenService ??= new BestiaryUiOpenService(
        new BestiaryUiOpenCallbacks(
            () => CloseBlockingUiWithSpaceAsync(IsBestiaryWorldUiOpen, "bestiary world UI"),
            ThrowIfAutomationStopRequested,
            IsBestiaryChallengePanelOpen,
            IsBestiaryCapturedBeastsTabVisible,
            IsBestiaryCapturedBeastsWindowOpen,
            TryGetBestiaryChallengesBestiaryButton,
            TryGetBestiaryCapturedBeastsButton,
            LogBestiaryUiState,
            (message, forceLog) => UpdateAutomationStatus(message, forceLog),
            LogDebug,
            () => new BestiaryUiTiming(
                AutomationTiming.KeyTapDelayMs,
                AutomationTiming.FastPollDelayMs,
                AutomationTiming.UiClickPreDelayMs,
                AutomationTiming.MinTabClickPostDelayMs,
                AutomationTiming.StashOpenPollDelayMs,
                AutomationTiming.StashInteractionDistance,
                Settings?.AutomationTiming?.TabSwitchDelayMs.Value ?? 0),
            () => Settings?.BestiaryAutomation?.ChallengesWindowHotkey?.Value.Key ?? Keys.None,
            TapKeyAsync,
            WaitForBestiaryConditionAsync,
            RetryBestiaryOpenAsync,
            (element, preClickDelayMs, postClickDelayMs, waitForConfirmationAsync, afterClickUiDelayMs) => ClickElementAndConfirmAsync(
                element,
                preClickDelayMs,
                postClickDelayMs,
                waitForConfirmationAsync,
                afterClickUiDelayMs,
                GetConfiguredBestiaryClickDelayMs()),
            WaitForBestiaryCapturedBeastsButtonAsync,
            WaitForBestiaryCapturedBeastsDisplayAsync,
            () => DescribePath(BestiaryChallengesEntriesRootPath),
            DescribeElement,
            WaitForMenagerieEinharAsync,
            GetPlayerDistanceToEntity,
            DescribeEntity,
            CtrlClickWorldEntityAsync,
            EnsurePollingAutomationOpenAsync));

    private BestiaryClearService BestiaryClear => _bestiaryClearService ??= new BestiaryClearService(
        new BestiaryClearCallbacks(
            () => BestiaryWorkflow.ShouldDeleteBeasts(),
            ReleaseAutomationModifierKeys,
            () => SetAutomationKeysDown(Keys.LControlKey),
            () => ReleaseAutomationKeys(Keys.LControlKey),
            ThrowIfAutomationStopRequested,
            EnsureBestiaryCapturedBeastsTabVisible,
            GetPlayerInventoryFreeCellCount,
            () => BestiaryWorkflow.ShouldAutoStashItemizedBeasts(),
            (message, forceLog) =>
            {
                _bestiaryInventoryFullStop = true;
                UpdateAutomationStatus(message, forceLog);
            },
            LogDebug,
            StashCapturedMonstersAndReturnToBestiaryAsync,
            GetVisibleBestiaryCapturedBeasts,
            WaitForBestiaryCapturedBeastsToPopulateAsync,
            GetBestiaryTotalCapturedBeastCount,
            CanClickBestiaryBeast,
            DelayAutomationAsync,
            () => BestiaryReleaseTimeoutMs,
            () => AutomationTiming.BestiaryReleasePollDelayMs,
            ClickBestiaryBeastAsync,
            WaitForBestiaryReleaseVisibleCountAsync));

    private MapStashUiService MapStashUi => _mapStashUiService ??= new MapStashUiService(
        Runtime.State.Automation.UiCache,
        new MapStashUiCallbacks(
            () => GameController?.IngameState?.IngameUi?.StashElement,
            () => GameController?.IngameState?.IngameUi?.OpenLeftPanel,
            () => Settings?.StashAutomation,
            () => Settings?.StashAutomation?.DynamicHints,
            () => new MapStashUiTiming(AutomationTiming.UiClickPreDelayMs, AutomationTiming.MinTabClickPostDelayMs, GetConfiguredTabSwitchDelayMs()),
            ThrowIfAutomationStopRequested,
            LogDebug,
            DescribeTarget,
            DescribeStash,
            DescribeElement,
            DescribeRect,
            DescribePath,
            DescribePathLookup,
            DescribePageTabs,
            element => DescribeChildren(element),
            ClickElementAsync,
            DelayAutomationAsync,
            TrySaveSettingsSnapshot,
            IsMapStashTarget,
            TryGetConfiguredMapTier,
            IsMatchingMapStashItem,
            MapStashVisiblePageContainsMatch,
            MapStashTierOneToNineTabPath,
            MapStashTierTenToSixteenTabPath,
            MapStashPageTabPath,
            MapStashPageNumberPath,
            MapStashPageContentPath));

    private RestockTransferBatchService RestockTransferBatches => _restockTransferBatchService ??= new RestockTransferBatchService(
        new RestockTransferBatchCallbacks(
            GetVisibleMapStashPageMatchingQuantity,
            GetVisibleMatchingItemQuantity,
            TryTransferNextMatchingItemAsync,
            LogDebug,
            UpdateRestockLoadingStatus,
            EnsureSpecialStashSubTabSelectedAsync,
            GetConfiguredTabSwitchDelayMs,
            DelayAutomationAsync));

    private RestockTransferConfirmationService RestockTransferConfirmation => _restockTransferConfirmationService ??= new RestockTransferConfirmationService(
        new RestockTransferConfirmationCallbacks(
            LogDebug,
            DescribePlayerInventoryCells,
            WaitForObservedQuantityToSettleAsync,
            TryGetPlayerInventorySlotFillCount,
            () => DelayForUiCheckAsync(),
            TryGetVisiblePlayerInventoryMatchingQuantity));

    private RestockTransferPlannerService RestockTransferPlanning => _restockTransferPlannerService ??= new RestockTransferPlannerService(
        new RestockTransferPlannerCallbacks(
            GetVisibleMapStashPageItems,
            (items, metadata) => GetMatchingMapStashPageItems(items, metadata),
            (items, metadata) => FindNextMatchingMapStashPageItem(items, metadata),
            (items, metadata) => CountMatchingMapStashPageItems(items, metadata),
            WaitForNextMatchingMapStashPageItemAsync,
            EnsureMapStashPageWithItemSelectedAsync,
            GetVisibleMapStashPageMatchingQuantity,
            GetVisibleMatchingItemQuantity,
            TryGetVisiblePlayerInventoryMatchingQuantity,
            GetVisibleStashItems,
            GetVisibleInventoryItemStackQuantity,
            (items, metadata) => GetKnownFullStackSize(items, metadata),
            GetPlayerInventoryNextFreeCells,
            CountOccupiedPlayerInventoryCells,
            LogDebug,
            DescribePlayerInventoryCells));

    private MapDeviceAutomationWorkflow MapDeviceWorkflow => _mapDeviceAutomationWorkflow ??= new MapDeviceAutomationWorkflow(
        new MapDeviceAutomationWorkflowCallbacks(
            (message, forceLog) => UpdateAutomationStatus(message, forceLog),
            () => GameController?.IngameState?.IngameUi?.Atlas?.IsVisible == true,
            CloseMapDeviceBlockingUiAsync,
            EnsureMapDeviceWindowOpenAsync,
            SelectConfiguredMapOnAtlasIfNeededAsync,
            () => DelayForUiCheckAsync(AutomationTiming.UiCheckInitialSettleDelayMs),
            TryRestockMissingMapDeviceItemsAsync,
            LoadConfiguredMapDevicePlanAsync,
            CapturePreparedMapCostBreakdownFromMapDeviceWindow,
            MoveCursorToMapDeviceActivateButton));

    private MapDeviceLoadPlanService MapDeviceLoadPlans => _mapDeviceLoadPlanService ??= new MapDeviceLoadPlanService(
        new MapDeviceLoadPlanCallbacks(
            ResolveConfiguredMapDeviceRequestedSlots,
            ResolveConfiguredMapDeviceInventoryTotals,
            GetCurrentMapDeviceRequestedItemQuantity,
            GetVisibleCombinedRequestedItemQuantity,
            IsRequestedItemCurrentlyLoadedInExpectedSlot,
            LogDebug));

    private MapDeviceVerificationService MapDeviceVerification => _mapDeviceVerificationService ??= new MapDeviceVerificationService(
        new MapDeviceVerificationCallbacks(
            () => GetVisibleMapDeviceItems().Count > 0,
            slotIndex =>
            {
                var item = GetVisibleMapDeviceItemInSlot(slotIndex);
                return item?.Item == null
                    ? new MapDeviceVisibleSlotState(slotIndex, null, 0)
                    : new MapDeviceVisibleSlotState(slotIndex, item.Item.Metadata, GetVisibleInventoryItemStackQuantity(item));
            },
            () => GetVisibleMapDeviceSlotItems()
                .Select((item, index) => item?.Item == null
                    ? new MapDeviceVisibleSlotState(index, null, 0)
                    : new MapDeviceVisibleSlotState(index, item.Item.Metadata, GetVisibleInventoryItemStackQuantity(item)))
                .ToList(),
            TryGetVisiblePlayerInventoryMatchingQuantity,
            GetVisibleMapDeviceMatchingQuantity,
            GetVisibleLoadedMapDeviceMatchingQuantity,
            GetVisibleMapDeviceStorageMatchingQuantity,
            GetVisibleMapDeviceQuantities,
            GetVisibleMapDeviceItemMetadata,
            WaitForBestiaryConditionAsync,
            () => AutomationTiming.FastPollDelayMs,
            MapDeviceFragmentSlotCount,
            MapDeviceTransferTimeoutMs,
            LogDebug));

    private MerchantAutomationWorkflow MerchantWorkflow => _merchantAutomationWorkflow ??= new MerchantAutomationWorkflow(
        new MerchantAutomationWorkflowCallbacks(
            ReleaseAutomationTriggerKeys,
            EnsureTravelToHideoutAsync,
            EnsureFaustusMerchantPanelOpenAsync,
            EnsureOfflineMerchantShopInventorySelectedAsync,
            ResolveConfiguredFaustusShopTabName,
            SelectOfflineMerchantTabAsync,
            EnsureOfflineMerchantTabCanFitSellableCapturedMonsters,
            () => GetOfflineMerchantPanel()?.IsVisible == true,
            EnsureFaustusListingContextReadyAsync,
            IsCurrentOfflineMerchantTabFull,
            GetNextSellableCapturedMonsterInventoryItem,
            (message, forceLog) => UpdateAutomationStatus(message, forceLog),
            TryListCapturedMonsterAndConfirmAsync,
            () => GetVisibleCapturedMonsterInventoryItems().Count,
            GetConfiguredClickDelayMs,
            DelayAutomationAsync,
            message => LogDebug(message)));

    private FullSequenceAutomationWorkflow FullSequenceWorkflow => _fullSequenceAutomationWorkflow ??= new FullSequenceAutomationWorkflow(
        new FullSequenceAutomationWorkflowCallbacks(
            RunBestiaryRegexItemizeForFullSequenceAsync,
            RunFaustusListBodyAsync,
            (message, forceLog) => UpdateAutomationStatus(message, forceLog)));

    private ExplorationRouteState ExplorationRouteRuntime => _explorationRouteState ??= new ExplorationRouteState();

    private MapRenderState MapRenderRuntime => _mapRenderState ??= new MapRenderState();

    private AnalyticsPersistenceState AnalyticsPersistenceRuntime => _analyticsPersistenceState ??= new AnalyticsPersistenceState();

    private AnalyticsWebRuntimeState AnalyticsWebRuntime => _analyticsWebRuntimeState ??= new AnalyticsWebRuntimeState();

    private AnalyticsSessionPersistenceService AnalyticsSessions => _analyticsSessionPersistenceService ??= new AnalyticsSessionPersistenceService(
        new AnalyticsSessionPersistenceCallbacks(
            BuildSavedSessionData,
            BuildCurrentLiveSessionDataForDuplicateCheck,
            () => _sessionStore,
            () => _autoSaveSessionStore,
            () => _loadedSaveIds,
            () => _loadedSaveCacheById,
            ApplyLoadedSessionAnalytics,
            RemoveLoadedSessionAnalytics));

    private AnalyticsSnapshotService AnalyticsSnapshots => _analyticsSnapshotService ??= new AnalyticsSnapshotService(
        new AnalyticsSnapshotCallbacks(
            () => BuildAnalyticsLines(_analyticsLineBuffer),
            () => _completedMapCount,
            () => _completedMapsDuration,
            () => _mapHistory,
            ComputeCurrentMapCapturedChaos,
            ComputePerMapCostChaos,
            ComputePerMapCostBreakdown,
            BuildCurrentMapReplayEvents,
            GetCurrentMapTime,
            GetTotalSessionTime,
            BuildSessionTotals,
            () => _isCurrentAreaTrackable,
            () => _pauseMenuSessionStartUtc.HasValue,
            () => _activeMapAreaHash,
            () => _activeMapAreaName
                  ?? BeastsV2Helpers.TryGetAreaNameText(GameController?.Area?.CurrentArea)
                  ?? string.Empty,
            () => _sessionBeastsFound,
            () => _totalRedBeastsSession,
            () => _currentMapBeastsFound,
            () => _currentMapRedBeastsFound,
            () => _currentMapUsedDuplicatingScarab,
            () => _currentMapCostBreakdown,
            () => _currentMapFirstRedSeenSeconds,
            IsDuplicatingScarabItemName,
            () => Settings.AnalyticsWebServer.RollingStatsWindowMaps.Value,
            () => Settings.BeastPrices.EnabledBeasts.ToArray(),
            CloneMapRecord,
            MaxMapHistoryEntries));

    private AnalyticsReplayEventTrackerService AnalyticsReplayEvents => _analyticsReplayEventTrackerService ??= new AnalyticsReplayEventTrackerService(
        new AnalyticsReplayEventTrackerCallbacks(
            () => _isCurrentAreaTrackable,
            GetCurrentMapReplayOffsetSeconds,
            GetTrackedBeastUnitPriceChaos,
            () => _currentMapBeastEncounters,
            () => _currentMapReplayEvents,
            () => _currentMapFirstRedSeenSeconds,
            value => _currentMapFirstRedSeenSeconds = value,
            () => _currentMapElapsed,
            GetReplayEventSortOrder));

    private AnalyticsMapCostTrackingService AnalyticsMapCosts => _analyticsMapCostTrackingService ??= new AnalyticsMapCostTrackingService(
        new AnalyticsMapCostTrackingCallbacks(
            () => _preparedMapCostBreakdown,
            () => _currentMapCostBreakdown,
            () => _preparedMapUsedDuplicatingScarab,
            value => _preparedMapUsedDuplicatingScarab = value,
            () => _currentMapUsedDuplicatingScarab,
            value => _currentMapUsedDuplicatingScarab = value,
            () => Settings.AnalyticsWebServer.ExtraCostPerMapChaos.Value,
            IsDuplicatingScarabItemName));

    private AnalyticsSessionAggregationService AnalyticsSessionAggregation => _analyticsSessionAggregationService ??= new AnalyticsSessionAggregationService(
        new AnalyticsSessionAggregationCallbacks(
            () => _mapHistory,
            CloneMapRecord,
            ComputeCurrentMapCapturedChaos,
            () => _isCurrentAreaTrackable,
            ComputePerMapCostChaos,
            BuildSessionTotals,
            () => _currentAnalyticsSessionId,
            GetTotalSessionTime,
            () => _completedMapCount,
            () => _sessionBeastsFound,
            () => _totalRedBeastsSession,
            () => _preparedMapCostBreakdown,
            () => _currentMapElapsed,
            () => _currentMapBeastsFound,
            () => _currentMapRedBeastsFound,
            () => _currentMapValuableBeastCounts,
            () => _currentMapValuableBeastCapturedCounts,
            () => _currentMapCostBreakdown,
            () => _beastPrices,
            () => _currentMapFirstRedSeenSeconds,
            () => BuildCurrentMapReplayEvents(true),
            () => _currentMapUsedDuplicatingScarab,
            IsDuplicatingScarabItemName,
            record => AnalyticsEngineV2.ApplyMapRecord(_mapHistory, record, MaxMapHistoryEntries),
            ResetCurrentMapAnalytics,
            NormalizeTag));

    private MapRenderPresentationService MapRenderPresentation => _mapRenderPresentationService ??= new MapRenderPresentationService(
        new MapRenderPresentationCallbacks(
            () => Settings.MapRender.CapturedText.ReplaceNameAndPriceWithStatusText.Value,
            () => Settings.MapRender.CapturedText.StatusText.Value,
            () => Settings.MapRender.CapturedText.CapturedStatusText.Value,
            () => Settings.MapRender.CapturedText.CaptureTextColor.Value,
            () => Settings.MapRender.CapturedText.CapturedTextColor.Value,
            () => Settings.MapRender.ShowNameInsteadOfPrice.Value,
            beastName => BeastLookup.TryGetBeastPriceText(beastName, out var priceText) ? priceText : null,
            () => Settings.MapRender.Colors.WorldCapturedBeastColor.Value,
            () => Settings.MapRender.Colors.WorldBeastColor.Value,
            () => Settings.MapRender.Colors.WorldCapturedCircleColor.Value,
            () => Settings.MapRender.Colors.WorldCaptureRingColor.Value,
            () => Settings.MapRender.Colors.WorldBeastCircleColor.Value,
            () => Settings.MapRender.Colors.TrackedWindowBeastColor.Value));

    private BestiaryCapturedBeastsViewService BestiaryCapturedBeastsView => _bestiaryCapturedBeastsViewService ??= new BestiaryCapturedBeastsViewService(
        new BestiaryCapturedBeastsViewCallbacks(
            TryGetBestiaryPanel,
            TryGetBestiaryCapturedBeastsTab,
            beastElement => GetElementTextRecursive(beastElement, 2),
            beastElement => EnumerateDescendants(beastElement)));

    private BeastLookupService BeastLookup => _beastLookupService ??= new BeastLookupService(
        new BeastLookupCallbacks(
            beastName => _beastPriceTexts.TryGetValue(beastName, out var priceText) ? priceText : null,
            () => CaptureMonsterCapturedBuffName,
            () => CaptureMonsterTrappedBuffName));

    private ExplorationRouteRefreshService ExplorationRouteRefresh => _explorationRouteRefreshService ??= new ExplorationRouteRefreshService(
        new ExplorationRouteRefreshCallbacks(
            () => _routeNeedsRegen,
            value => _routeNeedsRegen = value,
            () => _lastExplorationRouteEnabled,
            value => _lastExplorationRouteEnabled = value,
            () => _lastRouteDetectionRadius,
            value => _lastRouteDetectionRadius = value,
            () => _lastPreferPerimeterFirstRoute,
            value => _lastPreferPerimeterFirstRoute = value,
            () => _lastVisitOuterShellLast,
            value => _lastVisitOuterShellLast = value,
            () => _lastFollowMapOutlineFirst,
            value => _lastFollowMapOutlineFirst = value,
            () => _lastExcludedEntityPathsSnapshot,
            value => _lastExcludedEntityPathsSnapshot = value,
            () => _lastEntityExclusionRadius,
            value => _lastEntityExclusionRadius = value,
            () => Settings.MapRender.ExplorationRoute.DetectionRadius.Value,
            IsExplorationRouteEnabled,
            () => Settings.MapRender.ExplorationRoute.PreferPerimeterFirstRoute.Value,
            () => Settings.MapRender.ExplorationRoute.VisitOuterShellLast.Value,
            () => Settings.MapRender.ExplorationRoute.FollowMapOutlineFirst.Value,
            () => Settings.MapRender.ExplorationRoute.ExcludedEntityPaths.Value,
            value => Settings.MapRender.ExplorationRoute.ExcludedEntityPaths.Value = value,
            () => Settings.MapRender.ExplorationRoute.EntityExclusionRadius.Value,
            CancelBeastPaths,
            ClearExplorationRouteState,
            GenerateExplorationRoute));

    private ExplorationRoutePlanningService ExplorationRoutePlanning => _explorationRoutePlanningService ??= new ExplorationRoutePlanningService();

    private MapRenderPanelOverlayService MapRenderPanelOverlays => _mapRenderPanelOverlayService ??= new MapRenderPanelOverlayService(
        new MapRenderPanelOverlayCallbacks(
            beastName => _beastPrices.TryGetValue(beastName, out var price) ? price : null,
            (rect, color) => Graphics.DrawBox(rect, color),
            (rect, color, thickness) => Graphics.DrawFrame(rect, color, thickness),
            MapRenderDrawingPrimitives.DrawCenteredText,
            () => TryGetBestiaryCapturedBeastsDisplay(out _, out var visibleRect) ? visibleRect : null,
            GetVisibleBestiaryCapturedBeasts,
            GetBestiaryBeastLabel));

    private MapRenderImGuiOverlayService MapRenderImGuiOverlays => _mapRenderImGuiOverlayService ??= new MapRenderImGuiOverlayService(
        new MapRenderImGuiOverlayCallbacks(
            DrawPreviewWorldLabel,
            DrawPreviewMapLabel,
            DrawTrackedBeastPreviewRow,
            DrawPreviewCircles,
            GetTrackedWindowBeastColor,
            beastName => BeastLookup.TryGetBeastPriceText(beastName, out var priceText) ? priceText : null,
            GetDisplayedCaptureStatusColor,
            GetDisplayedCaptureStatusText));

    private MapRenderBeastOverlayService MapRenderBeastOverlays => _mapRenderBeastOverlayService ??= new MapRenderBeastOverlayService(
        new MapRenderBeastOverlayCallbacks(
            positioned => GameController.IngameState.Data.ToWorldWithTerrainHeight(positioned.GridPosition),
            worldPosition => GameController.IngameState.Camera.WorldToScreen(worldPosition),
            () => Settings.MapRender.Layout.WorldTextLineSpacing.Value,
            () => Settings.MapRender.CapturedText.ReplaceNameAndPriceWithStatusText.Value,
            () => Settings.MapRender.Colors.WorldPriceTextColor.Value,
            beastName => BeastLookup.TryGetBeastPriceText(beastName, out var priceText) ? priceText : null,
            GetWorldBeastColor,
            GetDisplayedCaptureStatusText,
            GetDisplayedCaptureStatusColor,
            GetWorldBeastCircleColor,
            () => Settings.MapRender.Layout.WorldBeastCircleRadius.Value,
            MapRenderDrawingPrimitives.DrawOutlinedText,
            MapRenderDrawingPrimitives.DrawFilledCircleInWorld,
            TranslateGridDeltaToMapDelta,
            DrawMapMarker));

    private MapRenderLabelService MapRenderLabels => _mapRenderLabelService ??= new MapRenderLabelService(
        new MapRenderLabelCallbacks(
            () => Settings.MapRender.Layout.WorldTextLineSpacing.Value,
            () => Settings.MapRender.Colors.WorldPriceTextColor.Value,
            () => Settings.MapRender.Colors.MapMarkerBackgroundColor.Value,
            () => Settings.MapRender.Colors.MapMarkerTextColor.Value,
            () => Settings.MapRender.Layout.MapLabelPaddingX.Value,
            () => Settings.MapRender.Layout.MapLabelPaddingY.Value,
            () => Settings.MapRender.CapturedText.ReplaceNameAndPriceWithStatusText.Value,
            GetWorldBeastColor,
            GetDisplayedCaptureStatusText,
            GetDisplayedCaptureStatusColor,
            GetWorldBeastCircleColor,
            GetTrackedWindowBeastColor,
            (beastName, captureState) =>
            {
                BuildPreviewMapMarkerTexts(beastName, captureState, out var primaryText, out var secondaryText);
                return (primaryText, secondaryText);
            },
            (beastName, captureState) =>
            {
                BuildMapMarkerTexts(beastName, captureState, out var primaryText, out var secondaryText);
                return (primaryText, secondaryText);
            },
            () => Settings.MapRender.Layout.WorldBeastCircleRadius.Value,
            () => Settings.MapRender.Layout.WorldBeastCircleFillOpacityPercent.Value,
            () => Settings.MapRender.Layout.WorldBeastCircleOutlineThickness.Value,
            () => Settings.MapRender.Colors.WorldTextOutlineColor.Value));

    private MapRenderLargeMapOverlayService MapRenderLargeMapOverlay => _mapRenderLargeMapOverlayService ??= new MapRenderLargeMapOverlayService(
        new MapRenderLargeMapOverlayCallbacks(
            () => GameController.Window.GetWindowRectangle() with { Location = SharpDX.Vector2.Zero },
            () => GameController.IngameState.IngameUi.OpenRightPanel.IsVisible,
            () => GameController.IngameState.IngameUi.OpenRightPanel.GetClientRectCache.Left,
            () => GameController.IngameState.IngameUi.OpenLeftPanel.IsVisible,
            () => GameController.IngameState.IngameUi.OpenLeftPanel.GetClientRectCache.Right,
            () =>
            {
                var largeMap = GameController.IngameState.IngameUi.Map.LargeMap;
                return new MapRenderLargeMapState(largeMap.IsVisible, largeMap.MapScale, largeMap.MapCenter);
            },
            value => _mapRect = value,
            value => _mapScale = value,
            value => _mapDrawList = value,
            () => Settings.MapRender.ShowBeastsOnMap.Value,
            () => IsExplorationRouteEnabled() && Settings.MapRender.ExplorationRoute.ShowPathsToBeasts.Value,
            () => IsExplorationRouteEnabled() && (Settings.MapRender.ExplorationRoute.ShowExplorationRoute.Value || Settings.MapRender.ExplorationRoute.ShowCoverageOnMiniMap.Value),
            DrawBeastMarkersOnMap,
            DrawPathsToBeasts,
            DrawExplorationRouteOnMap,
            DrawEntityExclusionZones,
            DrawExplorationDebugOnMap));

    private MapRenderDrawingPrimitivesService MapRenderDrawingPrimitives => _mapRenderDrawingPrimitivesService ??= new MapRenderDrawingPrimitivesService(
        new MapRenderDrawingPrimitivesCallbacks(
            worldPosition => GameController.Game.IngameState.Camera.WorldToScreen(worldPosition),
            () => Settings.MapRender.Layout.WorldBeastCircleFillOpacityPercent.Value,
            () => Settings.MapRender.Layout.WorldBeastCircleOutlineThickness.Value,
            () => Settings.MapRender.Colors.WorldTextOutlineColor.Value,
            () => _worldCircleScreenPoints,
            WorldCirclePoints,
            (text, position, color) => Graphics.DrawText(text, position, color, FontAlign.Center),
            (points, color) => Graphics.DrawConvexPolyFilled(points, color),
            (points, color, thickness) => Graphics.DrawPolyLine(points, color, thickness)));

    private MapRenderPathOverlayService MapRenderPathOverlay => _mapRenderPathOverlayService ??= new MapRenderPathOverlayService(
        new MapRenderPathOverlayCallbacks(
            () => GameController?.Game?.IngameState?.Data?.LocalPlayer,
            () => GameController?.IngameState?.Data?.RawTerrainHeightData,
            () => GameController.PluginBridge.GetMethod<Func<Vector2, Action<List<Vector2i>>, CancellationToken, Task>>("Radar.LookForRoute"),
            () => _pathFindingCts,
            value => _pathFindingCts = value,
            () => _explorationPath,
            value => _explorationPath = value,
            () => _explorationPathForIdx,
            value => _explorationPathForIdx = value,
            IsExplorationRouteEnabled,
            () => _explorationRoute,
            () => _visitedWaypointIndices,
            UpdateVisitedWaypoints,
            GetNextWaypointIndex,
            EnsureExplorationRouteIsCurrent,
            () => _mapDrawList,
            TranslateGridDeltaToMapDelta));

    private AnalyticsWebServerCoordinator AnalyticsWeb => _analyticsWebServerCoordinator ??= new AnalyticsWebServerCoordinator(
        new AnalyticsWebServerCoordinatorCallbacks(
            BuildAnalyticsWebSnapshot,
            () => _analyticsWebServer,
            server => _analyticsWebServer = server,
            () => _latestAnalyticsSnapshot,
            snapshot => _latestAnalyticsSnapshot = snapshot ?? new SessionCurrentResponseV2(),
            () => Settings.AnalyticsWebServer.Enabled.Value,
            () => Settings.AnalyticsWebServer.Port.Value,
            () => Settings.AnalyticsWebServer.AllowNetworkAccess.Value,
            (_, _) => new AnalyticsWebServer(
                () => _latestAnalyticsSnapshot,
                (offset, limit) => BuildMapListResponseV2(offset, limit),
                () => ListSavedSessionsV2(),
                request => SaveSessionSnapshotToFileV2(request),
                sessionId => GetSavedSessionDataV2(sessionId),
                sessionId => LoadSavedSessionV2(sessionId),
                sessionId => UnloadSavedSessionV2(sessionId),
                sessionId => DeleteSavedSessionV2(sessionId),
                request => CompareSavedSessionsV2(request),
                msg => LogDebug(msg)),
            () => _analyticsWebServerPort,
            value => _analyticsWebServerPort = value,
            () => _analyticsWebServerAllowNetwork,
            value => _analyticsWebServerAllowNetwork = value,
            LogDebug,
            LogError));

    private AnalyticsWebServer _analyticsWebServer
    {
        get => AnalyticsWebRuntime.Server;
        set => AnalyticsWebRuntime.Server = value;
    }

    private SessionCurrentResponseV2 _latestAnalyticsSnapshot
    {
        get => AnalyticsWebRuntime.LatestSnapshot;
        set => AnalyticsWebRuntime.LatestSnapshot = value ?? new SessionCurrentResponseV2();
    }

    private BestiaryAutomationWorkflow BestiaryWorkflow => _bestiaryAutomationWorkflow ??= new BestiaryAutomationWorkflow(
        Runtime.State.Automation,
        () => Settings?.BestiaryAutomation,
        new BestiaryAutomationWorkflowCallbacks(
            RequestAutomationStop,
            LaunchBestiaryClearAutomationAsync,
            openViaChallengesHotkey => BestiaryUi.EnsureCapturedBeastsWindowOpenAsync(openViaChallengesHotkey),
            EnsureTravelToMenagerieAsync,
            EnsureBestiaryCapturedBeastsTabVisible,
            GetBestiaryTotalCapturedBeastCount,
            EnsureFullSequenceCanStartItemizing,
            isFullSequence => EnsureBestiaryItemizingCapacityAsync(isFullSequence),
            () => BestiaryClear.ClearAsync(),
            StashCapturedMonstersAndCloseUiAsync,
            (message, forceLog) => UpdateAutomationStatus(message, forceLog),
            LogDebug,
            ApplyBestiarySearchRegexAsync,
            GetPlayerInventoryFreeCellCount));

    private BestiaryCapturedMonsterStashService BestiaryCapturedMonsterStash => _bestiaryCapturedMonsterStashService ??= new BestiaryCapturedMonsterStashService(
        Runtime.State.Automation,
        new BestiaryCapturedMonsterStashCallbacks(
            () => BestiaryUi.CloseWorldUiAsync(),
            EnsureStashOpenForAutomationAsync,
            ResolveBestiaryCapturedMonsterStashTabIndex,
            ResolveConfiguredTabIndex,
            GetVisibleCapturedMonsterInventoryItems,
            BuildVisibleBestiaryStashCapacityFailureMessage,
            (message, forceLog) => UpdateAutomationStatus(message, forceLog),
            ThrowIfAutomationStopRequested,
            SelectStashTabAsync,
            CtrlClickInventoryItemAsync,
            WaitForCapturedMonsterInventoryItemCountToChangeAsync,
            DelayForUiCheckAsync,
            DelayAutomationAsync,
            () => GameController?.IngameState?.IngameUi?.StashElement?.IsVisible == true,
            openViaChallengesHotkey => BestiaryUi.EnsureCapturedBeastsWindowOpenAsync(openViaChallengesHotkey),
            ApplyBestiarySearchRegexAsync,
            IsRedCapturedMonsterInventoryItem,
            () => Settings?.BestiaryAutomation,
            GetConfiguredTabSwitchDelayMs,
            GetConfiguredClickDelayMs));

    private DateTime _sessionStartUtc
    {
        get => Runtime.State.Session.SessionStartUtc;
        set => Runtime.State.Session.SessionStartUtc = value;
    }

    private TimeSpan _sessionPausedDuration
    {
        get => Runtime.State.Session.SessionPausedDuration;
        set => Runtime.State.Session.SessionPausedDuration = value;
    }

    private TimeSpan _loadedSessionsDuration
    {
        get => Runtime.State.Session.LoadedSessionsDuration;
        set => Runtime.State.Session.LoadedSessionsDuration = value;
    }

    private DateTime? _pauseMenuSessionStartUtc
    {
        get => Runtime.State.Session.PauseMenuSessionStartUtc;
        set => Runtime.State.Session.PauseMenuSessionStartUtc = value;
    }

    private DateTime? _currentMapStartUtc
    {
        get => Runtime.State.Session.CurrentMapStartUtc;
        set => Runtime.State.Session.CurrentMapStartUtc = value;
    }

    private TimeSpan _currentMapElapsed
    {
        get => Runtime.State.Session.CurrentMapElapsed;
        set => Runtime.State.Session.CurrentMapElapsed = value;
    }

    private TimeSpan _completedMapsDuration
    {
        get => Runtime.State.Session.CompletedMapsDuration;
        set => Runtime.State.Session.CompletedMapsDuration = value;
    }

    private int _completedMapCount
    {
        get => Runtime.State.Session.CompletedMapCount;
        set => Runtime.State.Session.CompletedMapCount = value;
    }

    private bool _isCurrentAreaTrackable
    {
        get => Runtime.State.Map.IsCurrentAreaTrackable;
        set => Runtime.State.Map.IsCurrentAreaTrackable = value;
    }

    private string _activeMapAreaHash
    {
        get => Runtime.State.Map.ActiveMapAreaHash;
        set => Runtime.State.Map.ActiveMapAreaHash = value ?? string.Empty;
    }

    private string _activeMapAreaName
    {
        get => Runtime.State.Map.ActiveMapAreaName;
        set => Runtime.State.Map.ActiveMapAreaName = value ?? string.Empty;
    }

    private bool _currentMapWasComplete
    {
        get => Runtime.State.Map.CurrentMapWasComplete;
        set => Runtime.State.Map.CurrentMapWasComplete = value;
    }

    private int _activeMapInstanceId
    {
        get => Runtime.State.Map.ActiveMapInstanceId;
        set => Runtime.State.Map.ActiveMapInstanceId = value;
    }

    private bool _mapWasFinalized
    {
        get => Runtime.State.Map.MapWasFinalized;
        set => Runtime.State.Map.MapWasFinalized = value;
    }

    private bool _shouldRenderFinalizedMapCompletionOverlay
    {
        get => Runtime.State.Map.ShouldRenderFinalizedMapCompletionOverlay;
        set => Runtime.State.Map.ShouldRenderFinalizedMapCompletionOverlay = value;
    }

    private int _analyticsWebServerPort
    {
        get => Runtime.State.WebServer.Port;
        set => Runtime.State.WebServer.Port = value;
    }

    private bool _analyticsWebServerAllowNetwork
    {
        get => Runtime.State.WebServer.AllowNetwork;
        set => Runtime.State.WebServer.AllowNetwork = value;
    }

    private string _lastAutomationStatusMessage
    {
        get => Runtime.State.Automation.LastStatusMessage;
        set => Runtime.State.Automation.LastStatusMessage = value ?? string.Empty;
    }

    private bool _isAutomationRunning
    {
        get => Runtime.State.Automation.IsAutomationRunning;
        set => Runtime.State.Automation.IsAutomationRunning = value;
    }

    private bool _isAutomationInputLocked
    {
        get => Runtime.State.Automation.IsInputLockActive;
        set => Runtime.State.Automation.IsInputLockActive = value;
    }

    private bool _isBestiaryClearRunning
    {
        get => Runtime.State.Automation.IsBestiaryClearRunning;
        set => Runtime.State.Automation.IsBestiaryClearRunning = value;
    }

    private bool _isAutomationStopRequested
    {
        get => Runtime.State.Automation.IsAutomationStopRequested;
        set => Runtime.State.Automation.IsAutomationStopRequested = value;
    }

    private bool? _bestiaryDeleteModeOverride
    {
        get => Runtime.State.Automation.BestiaryDeleteModeOverride;
        set => Runtime.State.Automation.BestiaryDeleteModeOverride = value;
    }

    private bool? _bestiaryAutoStashOverride
    {
        get => Runtime.State.Automation.BestiaryAutoStashOverride;
        set => Runtime.State.Automation.BestiaryAutoStashOverride = value;
    }

    private bool _bestiaryInventoryFullStop
    {
        get => Runtime.State.Automation.BestiaryInventoryFullStop;
        set => Runtime.State.Automation.BestiaryInventoryFullStop = value;
    }

    private string _activeBestiarySearchRegex
    {
        get => Runtime.State.Automation.ActiveBestiarySearchRegex;
        set => Runtime.State.Automation.ActiveBestiarySearchRegex = value ?? string.Empty;
    }

    private CancellationTokenSource _automationCancellationTokenSource
    {
        get => Runtime.State.Automation.CancellationTokenSource;
        set => Runtime.State.Automation.CancellationTokenSource = value;
    }

    private MainSettingsBindingTargets CreateSettingsBindingTargets()
    {
        return new MainSettingsBindingTargets(
            ResetSessionAnalytics,
            SaveSessionSnapshotToFile,
            ResetMapAverageAnalytics,
            CopyAnalyticsWebServerUrlToClipboard,
            OpenAnalyticsWebServerInBrowser,
            DrawSettingsOverviewPanel,
            DrawChangelogPanel,
            QueuePriceFetch,
            SelectAllPriceDataBeasts,
            DeselectAllPriceDataBeasts,
            SelectPriceDataBeastsWorth15ChaosOrMore,
            DrawBeastPricesSummaryPanel,
            DrawBeastPickerPanel,
            DrawStashAutomationSummaryPanel,
            DrawBestiaryAutomationSummaryPanel,
            DrawMerchantAutomationSummaryPanel,
            DrawFullSequenceAutomationSummaryPanel,
            DrawExcludedEntityPathsListPanel,
            RequestExplorationRouteRegen,
            () =>
            {
                InitializeAutomationSettingsUi(Settings.StashAutomation);
                InitializeBestiaryAutomationSettingsUi(Settings.BestiaryAutomation);
                InitializeMerchantAutomationSettingsUi(Settings.MerchantAutomation);
            });
    }
}