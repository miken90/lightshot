namespace CompositorProbe;

/// <summary>
/// Frame-number stamp: Bits black/white cells in the source video (and the same layout for the present-sequence
/// strips the compositor draws), so a frame number can be read back from pixels after blur, scale and mask.
/// </summary>
public static class FrameStamp
{
    public const int Bits = 20;
    // Source-pixel layout inside the 2560x1440 clip: top-left, away from the cursor path.
    public const int SrcX0 = 64, SrcY0 = 64, SrcCell = 32, SrcPitch = 36;

    public static (float x, float y) SourceCellCenter(int bit) => (SrcX0 + bit * SrcPitch + SrcCell / 2f, SrcY0 + SrcCell / 2f);

    // Present-sequence strips drawn by the compositor in canvas pixels (unscaled, no zoom), top and bottom rows.
    // Each strip row is StripBits code cells, then a white and a black guard cell (a reader that does not see the guard is not
    // looking at a strip), and a StripRulerW wide cyan ruler runs between the two rows so a reader can find the bottom row in
    // the same captured frame instead of trusting the window rectangle.
    public const int StripBits = 16, StripCellW = 16, StripH = 8, StripCells = StripBits + 2, StripRulerW = 4;
    public static (int x, int y) StripCellCenter(int bit, bool bottom, int canvasH) =>
        (bit * StripCellW + StripCellW / 2, bottom ? canvasH - StripH / 2 : StripH / 2);

    /// <summary>Reads a value from a sampler returning true for a white cell at a given bit index.</summary>
    public static int Decode(Func<int, bool> isWhite, int bits)
    {
        int v = 0;
        for (int i = 0; i < bits; i++) if (isWhite(i)) v |= 1 << i;
        return v;
    }
}
