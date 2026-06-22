namespace RevitCliBridge.Models
{
    internal class WallEntry
    {
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public int LevelId { get; set; }
        public double Height { get; set; }
    }
}
