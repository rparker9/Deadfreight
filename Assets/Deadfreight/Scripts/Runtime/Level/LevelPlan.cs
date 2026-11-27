// Scripts/Runtime/Level/LevelPlan.cs
using System.Collections.Generic;
using UnityEngine;

namespace Deadfreight.Level
{
    /// <summary>
    /// A generated level, as plain data on a grid of cells. LevelGenerator makes it; the preview
    /// reads it and LevelBuilder builds it. Every level is one region.
    ///
    /// Cell (x, y) sits at (x, y) × CellSize metres in the world: x east, y north (the world's z).
    /// A cell's index into the per-cell arrays is y × CellsX + x.
    ///
    /// The whole map is built, but play happens inside PlayArea: the border around it only carries
    /// the land (and the road in) out of sight, so the world never visibly ends.
    /// </summary>
    public sealed class LevelPlan
    {
        public readonly int CellsX, CellsY;
        public readonly float CellSize;             // metres between neighbouring cells
        public float Relief;                        // metres from the lowest ground to the highest
        public RegionProfile Region;

        public readonly float[] GroundHeight;       // metres above the lowest ground (0 to Relief)
        public readonly float[] Slope;              // rise over run
        public readonly byte[] Zone;                // index into Region.zones
        public float WaterHeight = -1f;             // metres: ground below it is under water. Negative = no water.

        public RectInt PlayArea;                    // cells where play happens; the rest is border
        public LevelEntry Entry;                    // where the road comes in, on the map edge
        public Place Start, Site;                   // also in Places
        public readonly List<Place> Places = new();
        public readonly List<Road> Roads = new();   // the main road first, then the branches

        /// <summary>Why generation stopped early, or null if it finished.</summary>
        public string Failure;

        public LevelPlan(int cellsX, int cellsY, float cellSize)
        {
            CellsX = cellsX;
            CellsY = cellsY;
            CellSize = cellSize;
            int count = cellsX * cellsY;
            GroundHeight = new float[count];
            Slope = new float[count];
            Zone = new byte[count];
        }

        public bool HasWater => WaterHeight >= 0f;
        public bool IsWater(int index) => GroundHeight[index] < WaterHeight;

        public int Index(Vector2Int cell) => cell.y * CellsX + cell.x;
        public Vector2Int CellAt(int index) => new(index % CellsX, index / CellsX);
        public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < CellsX && cell.y < CellsY;
        public bool InPlayArea(Vector2Int cell) => PlayArea.Contains(cell);

        /// <summary>World (x, z) of a cell, as a Vector2.</summary>
        public Vector2 WorldXZ(Vector2Int cell) => (Vector2)cell * CellSize;
    }
}
