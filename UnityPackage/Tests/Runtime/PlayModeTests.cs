using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.BlockyMesher.Tests
{
    /// <summary>What only shows in Play Mode, where Unity holds meshes to the same rules as a player.</summary>
    public class PlayModeTests
    {
        [UnityTest]
        public IEnumerator SectionsRebuildAfterDroppingTheirCpuCopy()
        {
            var stone = ScriptableObject.CreateInstance<BlockData>();
            stone.id = 1;
            var registry = ScriptableObject.CreateInstance<BlockRegistry>();
            registry.blocks = new[] { stone };
            var landscape = new GameObject("Landscape").AddComponent<Landscape>();
            landscape.Registry = registry;
            try
            {
                landscape.SetBlock(new Vector3Int(1, 1, 1), stone);
                landscape.CompleteRebuilds();
                yield return null;
                landscape.SetBlock(new Vector3Int(5, 5, 5), stone);
                landscape.CompleteRebuilds();
                yield return null;

                Mesh mesh = landscape.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.AreEqual(2 * 24, mesh.vertexCount);
                Assert.IsFalse(mesh.isReadable);
            }
            finally
            {
                Object.Destroy(landscape.gameObject);
                Object.Destroy(registry);
                Object.Destroy(stone);
            }
        }
    }
}
