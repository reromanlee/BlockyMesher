using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// The section being built plus an 8-block border on every side: a 32³ box. Light fades out
    /// within 7 blocks, so nothing outside the box can change how the section looks.
    /// Same layout as a section: x changes fastest, then z, then y.
    /// </summary>
    internal static class Neighborhood
    {
        public const int Border = 8;
        public const int Size = Section.Size + 2 * Border;
        public const int Area = Size * Size;
        public const int Volume = Area * Size;

        public const int StepX = 1;
        public const int StepZ = Size;
        public const int StepY = Area;

        public static int Index(int x, int y, int z) => x + z * StepZ + y * StepY;

        public static int Offset(int3 direction) => direction.x * StepX + direction.y * StepY + direction.z * StepZ;

        public static int3 Position(int index) => new(index & (Size - 1), index / Area, (index / Size) & (Size - 1));
    }
}
