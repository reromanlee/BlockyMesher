using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// How a face sits on its block, seen from outside: <see cref="U"/> points to the texture's right,
    /// <see cref="V"/> to its top. Corners go (0,0) → (0,1) → (1,1) → (1,0), which is clockwise from
    /// the outside, the winding Unity treats as the front.
    /// </summary>
    internal struct FaceFrame
    {
        public int3 Normal;
        public int3 U;
        public int3 V;

        /// <summary>Position of the texture's (0, 0) corner, relative to the block's lowest corner.</summary>
        public int3 Origin;

        public static FaceFrame Of(int face) => face switch
        {
            (int)Face.Right => new FaceFrame { Normal = new int3(1, 0, 0), U = new int3(0, 0, 1), V = new int3(0, 1, 0), Origin = new int3(1, 0, 0) },
            (int)Face.Left => new FaceFrame { Normal = new int3(-1, 0, 0), U = new int3(0, 0, -1), V = new int3(0, 1, 0), Origin = new int3(0, 0, 1) },
            (int)Face.Up => new FaceFrame { Normal = new int3(0, 1, 0), U = new int3(1, 0, 0), V = new int3(0, 0, 1), Origin = new int3(0, 1, 0) },
            (int)Face.Down => new FaceFrame { Normal = new int3(0, -1, 0), U = new int3(-1, 0, 0), V = new int3(0, 0, 1), Origin = new int3(1, 0, 0) },
            (int)Face.Forward => new FaceFrame { Normal = new int3(0, 0, 1), U = new int3(-1, 0, 0), V = new int3(0, 1, 0), Origin = new int3(1, 0, 1) },
            _ => new FaceFrame { Normal = new int3(0, 0, -1), U = new int3(1, 0, 0), V = new int3(0, 1, 0), Origin = new int3(0, 0, 0) },
        };

        /// <summary>Texture coordinates of corner 0..3, in winding order.</summary>
        public static int2 Corner(int corner) => corner switch
        {
            0 => new int2(0, 0),
            1 => new int2(0, 1),
            2 => new int2(1, 1),
            _ => new int2(1, 0),
        };
    }
}
