using System.Drawing;

namespace League_Data_Recorder;

// A stable dock owns its portraits and labels. It makes no assumptions about
// League's portrait coordinates, HUD scale, side panels, or spectator ordering.
internal static class ReplayCommsDock
{
    internal static Rectangle Card(int slot, int people, Size viewport)
    {
        int count = Math.Max(1, people), columns = Math.Min(count, viewport.Width < 900 ? 2 : 3);
        int rows = (count + columns - 1) / columns;
        float scale = Math.Clamp(viewport.Height / 1080f, .55f, 1.8f);
        int gap = Math.Max(4, (int)(8 * scale));
        int width = Math.Max(1, Math.Min((int)(340 * scale), (int)(viewport.Width * .78 / columns) - gap));
        int height = Math.Max(1, Math.Min((int)(112 * scale), (int)(viewport.Height * .46 / rows) - gap));
        int dockWidth = columns * width + (columns - 1) * gap;
        int dockHeight = rows * height + (rows - 1) * gap;
        int left = (viewport.Width - dockWidth) / 2;
        int top = Math.Max(0, viewport.Height - (int)(viewport.Height * .15) - dockHeight);
        return new(left + slot % columns * (width + gap), top + slot / columns * (height + gap), width, height);
    }
}
