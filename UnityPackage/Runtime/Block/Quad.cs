namespace reromanlee.BlockyMesher
{
    public readonly struct Quad
    {
        public readonly int vertexA;
        public readonly int vertexB;
        public readonly int vertexC;
        public readonly int vertexD;

        public Quad(int vertexA, int vertexB, int vertexC, int vertexD)
        {
            this.vertexA = vertexA;
            this.vertexB = vertexB;
            this.vertexC = vertexC;
            this.vertexD = vertexD;
        }
    }
}