// Scripts/Runtime/LevelGen/LevelGenerator.Zones.cs

// Stage 2: zones. Each cell takes the first of the region's zones whose ranges it fits, top to
// bottom; cells that fit none take the last. A region with no zones leaves every cell at 0.
public sealed partial class LevelGenerator
{
    void PlaceZones()
    {
        var zones = region.zones;
        if (zones.Count == 0) return;
        int last = zones.Count - 1;

        for (int i = 0; i < cellCount; i++)
        {
            float height01 = plan.GroundHeight[i] / plan.Relief;
            int zone = last;
            for (int z = 0; z < last; z++)
            {
                if (!zones[z].Fits(height01, plan.Slope[i])) continue;
                zone = z;
                break;
            }
            plan.Zone[i] = (byte)zone;
        }
    }
}
