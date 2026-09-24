namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// Which of the six other blocks around a face corner may light it. A block counts only when
    /// it connects to the block in front of the face through blocks that let light through, so light
    /// never leaks around the edge of a wall.
    ///
    /// Index and result use one bit per block, in this order: front+U, front+V, own+U, own+V,
    /// front+U+V, own+U+V ("front" is the block the face looks into, "own" the face's block, U and V
    /// step toward the corner). The index says which blocks let light through; the result says which
    /// of them are connected. It is a flood fill baked into a table, and the tests check both that and
    /// the hand-made table of the original BlockyMesher prototype.
    /// </summary>
    internal static class LightConnections
    {
        public static readonly byte[] Table =
        {
            0, 1, 2, 3, 0, 5, 2, 7, 0, 1, 10, 11, 0, 5, 10, 15,
            0, 17, 18, 19, 0, 21, 18, 23, 0, 17, 26, 27, 0, 21, 26, 31,
            0, 1, 2, 3, 0, 37, 2, 39, 0, 1, 42, 43, 0, 45, 46, 47,
            0, 49, 50, 51, 0, 53, 54, 55, 0, 57, 58, 59, 0, 61, 62, 63,
        };
    }
}
