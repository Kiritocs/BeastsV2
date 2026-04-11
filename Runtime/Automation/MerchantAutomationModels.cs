using ExileCore.PoEMemory.Elements.InventoryElements;

namespace BeastsV2;

internal sealed record MerchantListingAttemptResult(
	bool PopupOpened,
	int PreviousCount,
	int CurrentCount,
	long ClickItemMs,
	long PopupWaitMs,
	long EnterPriceMs,
	long PopupReadyMs,
	long SelectAndClearMs,
	long TypeDigitsMs,
	long ConfirmTextMs,
	long SubmitCloseMs,
	long CountChangeMs,
	bool CountChangeFallbackUsed);

internal sealed record MerchantPriceEntryResult(
	long PopupReadyMs,
	long SelectAndClearMs,
	long TypeDigitsMs,
	long ConfirmTextMs,
	long SubmitCloseMs);

internal sealed record MerchantListingCandidate(NormalInventoryItem Item, string BeastName, int ListingPriceChaos);