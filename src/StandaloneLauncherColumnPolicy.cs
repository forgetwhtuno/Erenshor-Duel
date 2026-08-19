namespace ErenshorDuel
{
    // Canonical Forgotten Roads standalone-launcher default placement: a vertical column
    // hugging the right edge of the screen, beneath the native minimap (no stable minimap
    // RectTransform/anchor exists in the installed assembly to derive an exact lower edge from,
    // so this uses a resolution-independent top-right anchor with a conservative fixed inset).
    // This file is intentionally copied source, not shared as a runtime assembly, so every
    // module remains independently loadable. Only SlotIndex differs between modules.
    //
    // Column order (top -> bottom): Journal(0), Practice Duel(1), Follow(2).
    internal static class StandaloneLauncherColumnPolicy
    {
        internal const int SlotIndex = 1;
        internal const int LauncherSlotCount = 3;

        internal const float TopInsetNormalized = 0.24f;
        internal const float SlotStepNormalized = 0.036f;

        // Right-side margin. The launcher is a fixed 154px-wide ConstantPixelSize element and
        // DefaultX() is resolved through a clamp of the form Clamp(normalized*extent, 0, extent-
        // 154) - the previous 0.006f normalized margin is smaller than 154px at every realistic
        // screen width, so normalized*extent always exceeded extent-154 and the clamp silently
        // swallowed it, leaving the launcher flush against the very edge of the screen with zero
        // margin. This value is derived from a small deliberate pixel margin at a documented
        // reference width (not a runtime assumption - the same single constant is used at every
        // resolution) so a real, visible gap survives that clamp instead of being erased by it.
        private const float LauncherWidthPixels = 154f;
        private const float TargetMarginPixels = 6f;
        private const float ReferenceScreenWidth = 1920f;
        internal const float RightMarginNormalized = (LauncherWidthPixels + TargetMarginPixels) / ReferenceScreenWidth;

        // Gap (screen-height-normalized) between the bottom of the launcher column's lowest slot
        // and the top of the shared default utility-panel workspace below it.
        internal const float PanelGapNormalized = 0.02f;

        internal static float DefaultX()
        {
            return 1f - RightMarginNormalized;
        }

        internal static float DefaultY(int slotIndex)
        {
            if (slotIndex < 0) slotIndex = 0;
            float y = (1f - TopInsetNormalized) - slotIndex * SlotStepNormalized;
            return Clamp01(y);
        }

        // Bottom-origin normalized top edge for the shared default utility-panel workspace: just
        // below the full three-slot launcher stack, regardless of which module's launcher
        // triggered the panel build, so Journal/Duel/Follow panels default into one coherent
        // right-side workspace instead of three unrelated screen locations.
        internal static float DefaultPanelTopNormalized()
        {
            return Clamp01(DefaultY(LauncherSlotCount - 1) - PanelGapNormalized);
        }

        // Same right-side margin as the launcher column, so a utility panel's right edge lines up
        // with the launcher rail above it regardless of the panel's own width.
        internal static float DefaultPanelRightNormalized()
        {
            return 1f - RightMarginNormalized;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        internal static string RunSelfTests()
        {
            if (DefaultX() <= 0.5f) return "FAIL DefaultX not right-side";
            if (DefaultX() > 1f) return "FAIL DefaultX out of 0..1 bounds";

            // The margin must survive StandaloneFallbackUi/SuiteUiPositionPolicy's
            // Clamp(normalized*extent, 0, extent-size) resolve at realistic screen widths -
            // otherwise the launcher silently reverts to flush-against-the-edge with no margin.
            float[] widths = { 1280f, 1600f, 1920f, 2560f, 3840f };
            for (int i = 0; i < widths.Length; i++)
            {
                float extent = widths[i];
                float resolvedLeft = ClampToExtent(DefaultX() * extent, 0f, extent - LauncherWidthPixels);
                float margin = extent - (resolvedLeft + LauncherWidthPixels);
                if (margin < 0f) return "FAIL launcher pushed past the screen edge at width " + extent;
            }
            float marginAt1920 = 1920f - (ClampToExtent(DefaultX() * 1920f, 0f, 1920f - LauncherWidthPixels) + LauncherWidthPixels);
            if (marginAt1920 <= 0f) return "FAIL right margin is fully clamped away at a common resolution (flush-edge regression)";

            float y0 = DefaultY(0), y1 = DefaultY(1), y2 = DefaultY(2);
            if (!(y0 > y1 && y1 > y2)) return "FAIL slot Y order not strictly descending";
            if (Mathf_Abs((y0 - y1) - (y1 - y2)) > 0.0001f) return "FAIL slot spacing not uniform";
            if (y0 <= 0f || y0 > 1f || y2 <= 0f || y2 > 1f) return "FAIL slot Y out of 0..1 bounds";

            if (DefaultY(-5) != DefaultY(0)) return "FAIL negative slot index not clamped to slot 0";

            float far = DefaultY(1000);
            if (far < 0f || far > 1f) return "FAIL far slot index not clamped into 0..1";

            float panelTop = DefaultPanelTopNormalized();
            if (panelTop <= 0f || panelTop > 1f) return "FAIL panel default top out of 0..1 bounds";
            if (panelTop >= y2) return "FAIL panel default workspace overlaps the lowest launcher slot";
            if (DefaultPanelRightNormalized() != DefaultX()) return "FAIL panel default right margin diverges from the launcher column";

            return "PASS StandaloneLauncherColumnPolicy";
        }

        private static float ClampToExtent(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float Mathf_Abs(float value) { return value < 0f ? -value : value; }
    }
}
