using Unity.Mathematics;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// The six sides of a block, named after Unity's <c>Vector3</c> directions.
    /// The order is axis by axis (X, Y, Z), positive side first, so <c>(int)face / 2</c> is the axis.
    /// </summary>
    public enum Face : byte
    {
        Right,
        Left,
        Up,
        Down,
        Forward,
        Back,
    }

    public static class Faces
    {
        public const int Count = 6;

        public static int3 Normal(Face face) => face switch
        {
            Face.Right => new int3(1, 0, 0),
            Face.Left => new int3(-1, 0, 0),
            Face.Up => new int3(0, 1, 0),
            Face.Down => new int3(0, -1, 0),
            Face.Forward => new int3(0, 0, 1),
            _ => new int3(0, 0, -1),
        };
    }
}
