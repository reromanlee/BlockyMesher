using System.Collections;
using System.Linq;
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

        /// <summary>
        /// Made in Edit Mode, before the test runner enters Play Mode, so it goes through the same
        /// script reload as a registry in an open scene when someone presses Play.
        /// </summary>
        sealed class RegistryFromEditMode : IPrebuildSetup
        {
            public const string Name = "Registry from Edit Mode";

            public void Setup()
            {
                var registry = ScriptableObject.CreateInstance<BlockRegistry>();
                registry.name = Name;
                registry.hideFlags = HideFlags.DontSave;
            }
        }

        [UnityTest, PrebuildSetup(typeof(RegistryFromEditMode))]
        public IEnumerator MaterialsSurviveEnteringPlayMode()
        {
            BlockRegistry registry = Resources.FindObjectsOfTypeAll<BlockRegistry>().FirstOrDefault(r => r.name == RegistryFromEditMode.Name);
            Assume.That(registry, Is.Not.Null, "The registry only outlives the reload in the editor.");
            try
            {
                Material[] materials = registry.GetMaterials(0b011);
                Assert.AreEqual(2, materials.Length);
                Assert.IsTrue(materials.All(material => material != null));
            }
            finally
            {
                Object.Destroy(registry);
            }
            yield return null;
        }
    }
}
