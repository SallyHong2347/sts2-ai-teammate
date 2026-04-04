using MegaCrit.Sts2.Core.Map;

namespace AITeammate.Scripts;

internal sealed class AiTeammateTestActMap : ActMap
{
    private const int GridWidth = 9;
    private const int GridHeight = 1;
    private const int PathColumn = 4;

    protected override MapPoint?[,] Grid { get; }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    public AiTeammateTestActMap(int actIndex)
    {
        Grid = new MapPoint[GridWidth, GridHeight + 1];
        StartingMapPoint = CreateSpecialPoint(PathColumn, 0, MapPointType.Ancient);
        BossMapPoint = CreateSpecialPoint(PathColumn, GridHeight + 1, MapPointType.Boss);

        MapPoint monster = CreatePathPoint(PathColumn, 1, MapPointType.Monster);
        StartingMapPoint.AddChildPoint(monster);
        monster.AddChildPoint(BossMapPoint);
        startMapPoints.Add(monster);
    }

    public static bool IsAromaOfChaosCoord(MapCoord? coord)
    {
        return false;
    }

    public static bool IsDrowningBeaconCoord(MapCoord? coord)
    {
        return false;
    }

    public static bool IsWellspringCoord(MapCoord? coord)
    {
        return false;
    }

    public static bool IsFakeMerchantCoord(MapCoord? coord)
    {
        return false;
    }

    public static bool IsFirstMonsterCoord(MapCoord? coord)
    {
        return coord is { col: PathColumn, row: 1 };
    }

    public static bool IsFirstEliteCoord(MapCoord? coord)
    {
        return false;
    }

    private MapPoint CreatePathPoint(int col, int row, MapPointType pointType)
    {
        MapPoint point = new(col, row)
        {
            PointType = pointType,
            CanBeModified = false
        };
        Grid[col, row] = point;
        return point;
    }

    private static MapPoint CreateSpecialPoint(int col, int row, MapPointType pointType)
    {
        return new MapPoint(col, row)
        {
            PointType = pointType,
            CanBeModified = false
        };
    }
}
