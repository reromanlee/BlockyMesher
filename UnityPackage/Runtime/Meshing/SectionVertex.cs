using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// One corner of a face in 12 bytes. Every value is a small whole number stored as a normalized
    /// byte; the shader multiplies by 255 to get it back. Unity wants the attributes in
    /// <see cref="VertexAttribute"/> order, hence position, color, then texture coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SectionVertex
    {
        // POSITION: where the corner is in its section (0..16), and which texture corner it is.
        public byte X;
        public byte Y;
        public byte Z;
        public byte Corner;

        // COLOR: block light, its color already scaled by its level.
        public Color32 BlockLight;

        // TEXCOORD0: texture array layer, ambient occlusion case (0..255) and sky light level (0..7).
        public byte Layer;
        public byte Occlusion;
        public byte Sky;
        public byte Unused;

        public static NativeArray<VertexAttributeDescriptor> Attributes(Allocator allocator)
        {
            var attributes = new NativeArray<VertexAttributeDescriptor>(3, allocator, NativeArrayOptions.UninitializedMemory);
            attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.UNorm8, 4);
            attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4);
            attributes[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm8, 4);
            return attributes;
        }
    }
}
