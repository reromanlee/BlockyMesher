using System.Collections.Generic;
using reromanlee.BlockyMesher.Meshing;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher
{
    /// <summary>The GameObject showing one section: its mesh, and its colliders when it has any. Pooled.</summary>
    internal sealed class SectionObject
    {
        /// <summary>Section meshes have no degenerate or duplicate triangles, so PhysX can skip cleaning them up.</summary>
        public const MeshColliderCookingOptions CookingOptions = MeshColliderCookingOptions.UseFastMidphase;

        public readonly GameObject GameObject;
        readonly MeshFilter filter;
        readonly MeshRenderer renderer;
        readonly List<BoxCollider> boxes = new();
        MeshCollider meshCollider;
        Mesh collisionMesh;
        JobHandle cooking;

        public Mesh Mesh { get; private set; }

        public int3 Position { get; private set; }

        public bool HasColliders => (meshCollider != null && meshCollider.enabled) || (boxes.Count > 0 && boxes[0].enabled);

        public long ColliderBytes => meshCollider != null && meshCollider.enabled && collisionMesh != null
            ? collisionMesh.vertexCount * 12L + collisionMesh.GetIndexCount(0) * (collisionMesh.indexFormat == IndexFormat.UInt16 ? 2L : 4L)
            : 0;

        /// <summary>
        /// Never saved: sections are rebuilt from blocks. Outside Play Mode they are hidden too, so they
        /// don't clutter the Hierarchy or show up as changes to a prefab.
        /// </summary>
        public static HideFlags Flags => Application.isPlaying ? HideFlags.DontSave | HideFlags.NotEditable : HideFlags.HideAndDontSave;
        public bool IsCooking { get; private set; }

        public SectionObject(Transform parent)
        {
            GameObject = new GameObject("Section") { hideFlags = Flags, layer = parent.gameObject.layer };
            GameObject.transform.SetParent(parent, false);
            filter = GameObject.AddComponent<MeshFilter>();
            renderer = GameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void Show(int3 section)
        {
            Position = section;
            GameObject.name = $"Section {section.x} {section.y} {section.z}";
            GameObject.transform.localPosition = (float3)(section * Section.Size);
            GameObject.SetActive(true);
        }

        /// <summary>
        /// The mesh to write the next build into. A mesh that dropped its CPU copy can't take new data
        /// in Play Mode or a player, so it is swapped for a fresh one, and the old one leaves the GPU.
        /// </summary>
        public Mesh WritableMesh()
        {
            if (Mesh == null || !Mesh.isReadable)
            {
                DestroyObject(Mesh);
                Mesh = new Mesh { name = "Section", hideFlags = HideFlags.DontSave };
                filter.sharedMesh = Mesh;
            }
            return Mesh;
        }

        public void SetMaterials(Material[] materials) => renderer.sharedMaterials = materials;

        public void Hide()
        {
            FinishCooking(true);
            ClearColliders();
            GameObject.SetActive(false);
        }

        public void ApplyColliders(SectionBuild build)
        {
            if (build.Colliders != ColliderMode.Boxes)
                SetBoxCount(0);
            if (build.Colliders != ColliderMode.Mesh && meshCollider != null)
                meshCollider.enabled = false;

            if (build.Colliders == ColliderMode.Boxes)
            {
                SetBoxCount(build.Boxes.Length);
                for (int i = 0; i < build.Boxes.Length; i++)
                {
                    BoxRange range = build.Boxes[i];
                    boxes[i].center = (float3)(range.Min + range.Max) * 0.5f;
                    boxes[i].size = (float3)(range.Max - range.Min);
                }
            }
            else if (build.Colliders == ColliderMode.Mesh)
            {
                FinishCooking(true);
                if (collisionMesh == null)
                    collisionMesh = new Mesh { name = "Section Collision", hideFlags = HideFlags.DontSave };
                build.ApplyCollision(collisionMesh);
                if (collisionMesh.vertexCount == 0)
                {
                    if (meshCollider != null)
                        meshCollider.enabled = false;
                    return;
                }
                cooking = CookColliderJob.For(collisionMesh, CookingOptions).Schedule();
                IsCooking = true;
            }
        }

        /// <summary>Hands the prepared collision mesh to the collider, once PhysX is done with it.</summary>
        public void FinishCooking(bool wait)
        {
            if (!IsCooking || (!wait && !cooking.IsCompleted))
                return;
            cooking.Complete();
            IsCooking = false;
            if (meshCollider == null)
                meshCollider = GameObject.AddComponent<MeshCollider>();
            meshCollider.cookingOptions = CookingOptions;
            meshCollider.sharedMesh = collisionMesh;
            meshCollider.enabled = true;
        }

        public void Destroy()
        {
            FinishCooking(true);
            DestroyObject(Mesh);
            DestroyObject(collisionMesh);
            DestroyObject(GameObject);
        }

        void ClearColliders()
        {
            SetBoxCount(0);
            if (meshCollider != null)
                meshCollider.enabled = false;
        }

        void SetBoxCount(int count)
        {
            while (boxes.Count < count)
                boxes.Add(GameObject.AddComponent<BoxCollider>());
            for (int i = 0; i < boxes.Count; i++)
                boxes[i].enabled = i < count;
        }

        static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
