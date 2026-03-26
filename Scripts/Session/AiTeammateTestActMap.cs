using MegaCrit.Sts2.Core.Map;

namespace AITeammate.Scripts;

internal sealed class AiTeammateTestActMap : ActMap
{
    private const int GridWidth = 7;
    private const int GridHeight = 4;
    private const int StartColumn = 3;
    private const int MonsterColumn = 1;
    private const int EliteColumn = 2;
    private const int TreasureColumn = 4;
    private const int EventColumn = 5;
    private const int SharedRestColumn = 3;

    protected override MapPoint?[,] Grid { get; }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    public AiTeammateTestActMap()
    {
        Grid = new MapPoint[GridWidth, GridHeight];
        StartingMapPoint = CreateSpecialPoint(StartColumn, 0, MapPointType.Ancient);
        BossMapPoint = CreateSpecialPoint(SharedRestColumn, GridHeight, MapPointType.Boss);

        MapPoint smallMonster = CreatePathPoint(MonsterColumn, 1, MapPointType.Monster);
        MapPoint elite = CreatePathPoint(EliteColumn, 1, MapPointType.Elite);
        MapPoint treasure = CreatePathPoint(TreasureColumn, 1, MapPointType.Treasure);
        MapPoint eventPoint = CreatePathPoint(EventColumn, 1, MapPointType.Unknown);
        MapPoint restSite = CreatePathPoint(SharedRestColumn, 2, MapPointType.RestSite);
        MapPoint followUpMonster = CreatePathPoint(SharedRestColumn, 3, MapPointType.Monster);

        StartingMapPoint.AddChildPoint(smallMonster);
        StartingMapPoint.AddChildPoint(elite);
        StartingMapPoint.AddChildPoint(treasure);
        StartingMapPoint.AddChildPoint(eventPoint);

        smallMonster.AddChildPoint(restSite);
        elite.AddChildPoint(restSite);
        treasure.AddChildPoint(restSite);
        eventPoint.AddChildPoint(restSite);
        restSite.AddChildPoint(followUpMonster);
        followUpMonster.AddChildPoint(BossMapPoint);

        startMapPoints.Add(smallMonster);
        startMapPoints.Add(elite);
        startMapPoints.Add(treasure);
        startMapPoints.Add(eventPoint);
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
