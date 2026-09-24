namespace reromanlee.BlockyMesher
{
    /// <summary>How a block is drawn. Every section mesh has one submesh per pass.</summary>
    public enum RenderPass : byte
    {
        /// <summary>Solid: hides the faces it touches, casts ambient occlusion and blocks light. The cheapest pass.</summary>
        Opaque,

        /// <summary>Each pixel is either fully visible or cut away, like leaves or a glass frame.</summary>
        Cutout,

        /// <summary>Blended with what is behind it, like water or stained glass. Drawn last.</summary>
        Transparent,
    }
}
