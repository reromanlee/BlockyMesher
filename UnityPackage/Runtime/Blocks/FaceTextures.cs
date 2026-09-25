using System;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>The texture array layer shown on each face of a block.</summary>
    [Serializable]
    public struct FaceTextures
    {
        [Min(0)] public int right;
        [Min(0)] public int left;
        [Min(0)] public int up;
        [Min(0)] public int down;
        [Min(0)] public int forward;
        [Min(0)] public int back;

        public FaceTextures(int all) : this(all, all, all) { }

        public FaceTextures(int sides, int top, int bottom)
        {
            right = left = forward = back = sides;
            up = top;
            down = bottom;
        }

        public int this[Face face] => face switch
        {
            Face.Right => right,
            Face.Left => left,
            Face.Up => up,
            Face.Down => down,
            Face.Forward => forward,
            _ => back,
        };
    }
}
