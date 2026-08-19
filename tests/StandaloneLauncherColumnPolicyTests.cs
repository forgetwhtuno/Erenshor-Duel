using System;

namespace ErenshorDuel
{
    internal static class StandaloneLauncherColumnPolicyTests
    {
        private static int _passed;
        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void DefaultXIsRightSideWithSafeMargin()
        {
            Assert(StandaloneLauncherColumnPolicy.DefaultX() > 0.85f && StandaloneLauncherColumnPolicy.DefaultX() <= 1f,
                "default X sits in the right-side column, not the old lower-left default");
            // The previous 0.006f margin was smaller than the 154px launcher width at every
            // realistic screen width, so ResolveAxis's Clamp(normalized*extent, 0, extent-154)
            // always swallowed it, leaving the launcher flush against the screen edge. Prove the
            // resolved margin is now actually non-zero at a common resolution.
            const float launcherWidth = 154f, extent = 1920f;
            float resolvedLeft = Math.Min(StandaloneLauncherColumnPolicy.DefaultX() * extent, extent - launcherWidth);
            float margin = extent - (resolvedLeft + launcherWidth);
            Assert(margin > 0f, "right margin survives the launcher-width clamp at a common resolution instead of resolving flush to the edge");
        }

        private static void SlotsAreNonOverlapping()
        {
            float journal = StandaloneLauncherColumnPolicy.DefaultY(0);
            float follow = StandaloneLauncherColumnPolicy.DefaultY(1);
            float duel = StandaloneLauncherColumnPolicy.DefaultY(2);
            Assert(journal > follow && follow > duel, "successive slots strictly descend so modules never stack on top of each other");
            float gap1 = journal - follow, gap2 = follow - duel;
            Assert(Math.Abs(gap1 - gap2) < 0.0001f, "slot spacing is uniform across the column");
            Assert(gap1 > 0.02f, "slot spacing is wide enough to avoid visual overlap for a ~32px launcher");
        }

        private static void SlotIndexClampsSafely()
        {
            Assert(StandaloneLauncherColumnPolicy.DefaultY(-3) == StandaloneLauncherColumnPolicy.DefaultY(0),
                "a negative slot index cannot push a launcher off the top of the screen");
            float far = StandaloneLauncherColumnPolicy.DefaultY(500);
            Assert(far >= 0f && far <= 1f, "an out-of-range slot index stays clamped inside the visible 0..1 column");
        }

        private static void DuelOwnsSlotOne()
        {
            Assert(StandaloneLauncherColumnPolicy.SlotIndex == 1, "Duel's column slot is 1 (Journal=0, Duel=1, Follow=2)");
        }

        private static void DefaultPanelWorkspaceSitsBelowTheLauncherStack()
        {
            float lowestLauncherSlot = StandaloneLauncherColumnPolicy.DefaultY(StandaloneLauncherColumnPolicy.LauncherSlotCount - 1);
            float panelTop = StandaloneLauncherColumnPolicy.DefaultPanelTopNormalized();
            Assert(panelTop > 0f && panelTop <= 1f, "default panel workspace top stays inside the visible 0..1 column");
            Assert(panelTop < lowestLauncherSlot, "default panel workspace sits below the full launcher stack, not overlapping it");
            Assert(StandaloneLauncherColumnPolicy.DefaultPanelRightNormalized() == StandaloneLauncherColumnPolicy.DefaultX(),
                "default panel workspace shares the same right-side margin as the launcher column");
        }

        public static int Main()
        {
            DefaultXIsRightSideWithSafeMargin();
            SlotsAreNonOverlapping();
            SlotIndexClampsSafely();
            DuelOwnsSlotOne();
            DefaultPanelWorkspaceSitsBelowTheLauncherStack();
            Console.WriteLine("Standalone launcher column policy tests passed: " + _passed);
            return 0;
        }
    }
}
