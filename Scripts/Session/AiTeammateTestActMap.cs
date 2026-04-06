using MegaCrit.Sts2.Core.Map;

namespace AITeammate.Scripts;

internal sealed class AiTeammateTestActMap : ActMap
{
    private const int GridWidth = 9;
    private const int GridHeight = 6;
    private const int CombatPathColumn = 4;
    private const int EventPathColumn = 2;

    protected override MapPoint?[,] Grid { get; }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    public AiTeammateTestActMap(int actIndex)
    {
        Grid = new MapPoint[GridWidth, GridHeight + 1];
        StartingMapPoint = CreateSpecialPoint(CombatPathColumn, 0, MapPointType.Ancient);
        BossMapPoint = CreateSpecialPoint(CombatPathColumn, GridHeight + 1, MapPointType.Boss);

        MapPoint firstMonster = CreatePathPoint(CombatPathColumn, 1, MapPointType.Monster);
        MapPoint secondMonster = CreatePathPoint(CombatPathColumn, 2, MapPointType.Monster);
        MapPoint firstRestSite = CreatePathPoint(CombatPathColumn, 3, MapPointType.RestSite);
        MapPoint thirdMonster = CreatePathPoint(CombatPathColumn, 4, MapPointType.Monster);
        MapPoint fourthMonster = CreatePathPoint(CombatPathColumn, 5, MapPointType.Monster);
        MapPoint secondRestSite = CreatePathPoint(CombatPathColumn, 6, MapPointType.RestSite);
        MapPoint firstEvent = CreatePathPoint(EventPathColumn, 1, MapPointType.Unknown);
        MapPoint secondEvent = CreatePathPoint(EventPathColumn, 2, MapPointType.Unknown);
        MapPoint thirdEvent = CreatePathPoint(EventPathColumn, 3, MapPointType.Unknown);
        MapPoint fourthEvent = CreatePathPoint(EventPathColumn, 4, MapPointType.Unknown);
        MapPoint fifthEvent = CreatePathPoint(EventPathColumn, 5, MapPointType.Unknown);

        StartingMapPoint.AddChildPoint(firstMonster);
        StartingMapPoint.AddChildPoint(firstEvent);
        firstMonster.AddChildPoint(secondMonster);
        secondMonster.AddChildPoint(firstRestSite);
        firstRestSite.AddChildPoint(thirdMonster);
        thirdMonster.AddChildPoint(fourthMonster);
        fourthMonster.AddChildPoint(secondRestSite);
        firstEvent.AddChildPoint(secondEvent);
        secondEvent.AddChildPoint(thirdEvent);
        thirdEvent.AddChildPoint(fourthEvent);
        fourthEvent.AddChildPoint(fifthEvent);
        fifthEvent.AddChildPoint(secondRestSite);
        secondRestSite.AddChildPoint(BossMapPoint);
        startMapPoints.Add(firstMonster);
        startMapPoints.Add(firstEvent);
    }

    public static bool IsAromaOfChaosCoord(MapCoord? coord)
    {
        return coord is { col: EventPathColumn, row: 2 };
    }

    public static bool IsDrowningBeaconCoord(MapCoord? coord)
    {
        return coord is { col: EventPathColumn, row: 3 };
    }

    public static bool IsWellspringCoord(MapCoord? coord)
    {
        return coord is { col: EventPathColumn, row: 4 };
    }

    public static bool IsFakeMerchantCoord(MapCoord? coord)
    {
        return coord is { col: EventPathColumn, row: 5 };
    }

    public static bool IsTabletOfTruthCoord(MapCoord? coord)
    {
        return coord is { col: EventPathColumn, row: 1 };
    }

    public static bool IsFirstMonsterCoord(MapCoord? coord)
    {
        return coord is { col: CombatPathColumn, row: 1 };
    }

    public static bool IsSecondMonsterCoord(MapCoord? coord)
    {
        return coord is { col: CombatPathColumn, row: 2 };
    }

    public static bool IsThirdMonsterCoord(MapCoord? coord)
    {
        return coord is { col: CombatPathColumn, row: 4 };
    }

    public static bool IsFourthMonsterCoord(MapCoord? coord)
    {
        return coord is { col: CombatPathColumn, row: 5 };
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
