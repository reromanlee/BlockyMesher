using NUnit.Framework;
using reromanlee.BlockyMesher.Meshing;
using UnityEngine;
using static reromanlee.BlockyMesher.Tests.TestBlocks;

namespace reromanlee.BlockyMesher.Tests
{
    public class MemoryTests
    {
        [Test]
        public void AMeshCanDropItsCpuCopyAndStillBeRebuilt()
        {
            using var world = new BuildHarness();
            var mesh = new Mesh();
            try
            {
                world.Set(1, 1, 1, Stone);
                world.Run();
                world.Build.Apply(mesh);
                mesh.UploadMeshData(true);
                Assert.IsFalse(mesh.isReadable);

                world.Set(5, 5, 5, Stone);
                world.Run();
                world.Build.Apply(mesh);
                Assert.AreEqual(2 * 24, mesh.vertexCount);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
