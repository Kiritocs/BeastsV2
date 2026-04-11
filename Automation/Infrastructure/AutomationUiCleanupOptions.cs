namespace BeastsV2;

internal sealed record AutomationUiCleanupOptions(
    bool SkipUiCleanup = false,
    bool KeepInventory = false,
    bool KeepStash = false,
    bool KeepMerchant = false,
    bool KeepBestiary = false,
    bool KeepAtlas = false,
    bool KeepMapDeviceWindow = false);