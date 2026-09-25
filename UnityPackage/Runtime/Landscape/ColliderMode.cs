namespace reromanlee.BlockyMesher
{
    /// <summary>What kind of colliders a <see cref="Landscape"/> gets.</summary>
    public enum ColliderMode
    {
        /// <summary>No colliders, for decoration or when blocks are only raycast against.</summary>
        None,

        /// <summary>
        /// A mesh collider per section, with neighboring faces merged so PhysX has few triangles to
        /// prepare. For landscapes that stay put, like a terrain.
        /// </summary>
        Mesh,

        /// <summary>
        /// Box colliders covering the blocks. For landscapes that move, like a raft on a Rigidbody:
        /// Unity doesn't allow concave mesh colliders on a moving Rigidbody.
        /// </summary>
        Boxes,
    }
}
