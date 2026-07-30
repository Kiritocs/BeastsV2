using System;
using SharpDX;

namespace BeastsV2.Runtime.Features;

internal sealed record MapRenderPresentationCallbacks(
    Func<bool> GetReplaceNameAndPriceWithStatusText,
    Func<string> GetCapturingStatusText,
    Func<string> GetCapturedStatusText,
    Func<Color> GetCaptureTextColor,
    Func<Color> GetCapturedTextColor,
    Func<bool> GetShowNameInsteadOfPrice,
    Func<string, string> GetBeastPriceTextOrNull,
    Func<Color> GetWorldCapturedBeastColor,
    Func<Color> GetWorldBeastColor,
    Func<Color> GetWorldTalismanOnlyBeastColor,
    Func<Color> GetWorldCapturedCircleColor,
    Func<Color> GetWorldCaptureRingColor,
    Func<Color> GetWorldBeastCircleColor,
    Func<Color> GetWorldTalismanOnlyCircleColor,
    Func<Color> GetTrackedWindowBeastColor,
    Func<Color> GetTrackedWindowTalismanOnlyBeastColor);

internal sealed class MapRenderPresentationService
{
    private readonly MapRenderPresentationCallbacks _callbacks;

    public MapRenderPresentationService(MapRenderPresentationCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public string GetDisplayedCaptureStatusText(BeastCaptureState captureState)
    {
        var (setting, fallback) = captureState == BeastCaptureState.Captured
            ? (_callbacks.GetCapturedStatusText(), "catched")
            : (_callbacks.GetCapturingStatusText(), "Captured");
        return string.IsNullOrWhiteSpace(setting) ? fallback : setting;
    }

    public Color GetDisplayedCaptureStatusColor(BeastCaptureState captureState)
    {
        return captureState == BeastCaptureState.Captured
            ? _callbacks.GetCapturedTextColor()
            : _callbacks.GetCaptureTextColor();
    }

    public void BuildMarkerTexts(string label, BeastCaptureState captureState, out string primaryText, out string secondaryText)
    {
        if (captureState == BeastCaptureState.None)
        {
            primaryText = label;
            secondaryText = null;
            return;
        }

        if (_callbacks.GetReplaceNameAndPriceWithStatusText())
        {
            primaryText = GetDisplayedCaptureStatusText(captureState);
            secondaryText = null;
            return;
        }

        primaryText = label;
        secondaryText = GetDisplayedCaptureStatusText(captureState);
    }

    public void BuildPreviewMapMarkerTexts(string beastName, BeastCaptureState captureState, out string primaryText, out string secondaryText)
    {
        var label = _callbacks.GetShowNameInsteadOfPrice() ? beastName : $"{beastName} 1c";
        BuildMarkerTexts(label, captureState, out primaryText, out secondaryText);
    }

    public void BuildMapMarkerTexts(string beastName, BeastCaptureState captureState, out string primaryText, out string secondaryText)
    {
        var priceText = _callbacks.GetBeastPriceTextOrNull(beastName);
        var label = _callbacks.GetShowNameInsteadOfPrice()
            ? beastName
            : !string.IsNullOrEmpty(priceText) ? $"{beastName} {priceText}" : beastName;

        BuildMarkerTexts(label, captureState, out primaryText, out secondaryText);
    }

    // Capture state wins over the talisman-only color wherever both apply: once a beast is being
    // captured, that state is the more urgent thing to read off the label.
    public Color GetWorldBeastColor(BeastCaptureState captureState, bool isTalismanOnly)
    {
        if (captureState != BeastCaptureState.None)
        {
            return _callbacks.GetWorldCapturedBeastColor();
        }

        return isTalismanOnly ? _callbacks.GetWorldTalismanOnlyBeastColor() : _callbacks.GetWorldBeastColor();
    }

    public Color GetWorldBeastCircleColor(BeastCaptureState captureState, bool isTalismanOnly)
    {
        return captureState switch
        {
            BeastCaptureState.Captured => _callbacks.GetWorldCapturedCircleColor(),
            BeastCaptureState.Capturing => _callbacks.GetWorldCaptureRingColor(),
            _ when isTalismanOnly => _callbacks.GetWorldTalismanOnlyCircleColor(),
            _ => _callbacks.GetWorldBeastCircleColor(),
        };
    }

    public Color GetTrackedWindowBeastColor(bool isTalismanOnly)
    {
        return isTalismanOnly
            ? _callbacks.GetTrackedWindowTalismanOnlyBeastColor()
            : _callbacks.GetTrackedWindowBeastColor();
    }
}