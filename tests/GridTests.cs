using System.Numerics;

namespace Yorehold.Rules.Tests;

/// <summary>Cells and distances, line of sight, flanking and cover.</summary>
public class GridTests
{
    private static readonly Cell Origin = new(0, 0);

    [Fact]
    public void CellsAndCentres()
    {
        var square = new Grid(GridType.Square, 10);
        Assert.Equal(new Cell(-1, -1), square.CellAt(new Vector2(-1, -1)));
        Assert.Equal(new Cell(1, 2), square.CellAt(new Vector2(19.9f, 20)));
        Assert.Equal(new Vector2(15, 15), square.Snap(new Vector2(17, 13)));
        Assert.Equal(new Vector2(35, 5), square.Center(new Cell(3, 0)));
        Assert.Equal(8, square.Neighbours(Origin).Distinct().Count());
        Assert.DoesNotContain(Origin, square.Neighbours(Origin));

        var hex = new Grid(GridType.Hex, 10);
        for (int q = -4; q <= 4; q++)
        {
            for (int r = -4; r <= 4; r++)
            {
                Assert.Equal(new Cell(q, r), hex.CellAt(hex.Center(new Cell(q, r))));
            }
        }
        Assert.Equal(6, hex.Neighbours(new Cell(3, 4)).Count);
        Assert.All(hex.Neighbours(new Cell(3, 4)), cell => Assert.Equal(1f, hex.Distance(new Cell(3, 4), cell)));

        var free = new Grid(GridType.Gridless, 10);
        Assert.Equal(new Vector2(17, 13), free.Snap(new Vector2(17, 13)));
        Assert.Equal(new Cell(1, 1), free.CellAt(new Vector2(17, 13)));
    }

    [Fact]
    public void DistanceFollowsTheDiagonalRule()
    {
        var grid = new Grid(GridType.Square, 10);
        Cell far = new(5, 3); // three diagonals and two straight
        Assert.Equal(5f, grid.Distance(Origin, far));
        Assert.Equal(5f, grid.Distance(far, Origin));
        Assert.Equal(0f, grid.Distance(far, far));
        grid.Diagonals = DiagonalRule.Alternating;
        Assert.Equal(6f, grid.Distance(Origin, far));
        Assert.Equal(1f, grid.Distance(Origin, new Cell(1, 1)));
        Assert.Equal(3f, grid.Distance(Origin, new Cell(2, 2)));
        grid.Diagonals = DiagonalRule.Double;
        Assert.Equal(8f, grid.Distance(Origin, far));
        grid.Diagonals = DiagonalRule.Euclidean;
        Assert.Equal(2f + 3f * 1.41421356f, grid.Distance(Origin, far));

        Assert.Equal(5f, new Grid(GridType.Gridless, 10).Distance(Origin, new Cell(3, 4)));
        var hex = new Grid(GridType.Hex, 10);
        Assert.Equal(3f, hex.Distance(Origin, new Cell(3, 0)));
        Assert.Equal(3f, hex.Distance(Origin, new Cell(3, -3)));
        Assert.Equal(4f, hex.Distance(Origin, new Cell(2, 2)));
    }

    [Fact]
    public void AStepCostsWhatTheRuleSays()
    {
        var grid = new Grid(GridType.Square, 10);
        Cell straight = new(1, 0);
        Cell diagonal = new(1, 1);
        Assert.Equal((1f, 1f), (grid.StepCost(Origin, straight, 0), grid.StepCost(Origin, diagonal, 3)));
        grid.Diagonals = DiagonalRule.Alternating;
        Assert.Equal((1f, 2f, 1f, 1f), (grid.StepCost(Origin, diagonal, 0), grid.StepCost(Origin, diagonal, 1), grid.StepCost(Origin, diagonal, 2), grid.StepCost(Origin, straight, 1)));
        grid.Diagonals = DiagonalRule.Double;
        Assert.Equal(2f, grid.StepCost(Origin, diagonal, 0));
        grid.Diagonals = DiagonalRule.Euclidean;
        Assert.Equal(1.41421356f, grid.StepCost(Origin, diagonal, 0));
        Assert.Equal(1.41421356f, new Grid(GridType.Gridless, 10).StepCost(Origin, diagonal, 0));
        Assert.Equal(1f, new Grid(GridType.Hex, 10).StepCost(Origin, new Cell(1, -1), 0));
    }

    [Fact]
    public void ReachCountsCells()
    {
        var grid = new Grid(GridType.Square, 10);
        Assert.True(grid.InReach(Origin, new Cell(1, 1), 1)); // a diagonal neighbour is adjacent
        Assert.True(grid.InReach(Origin, Origin, 1));
        Assert.False(grid.InReach(Origin, new Cell(2, 0), 1));
        Assert.True(grid.InReach(Origin, new Cell(2, 2), 2));
        Assert.True(grid.InReach(Origin, new Cell(6, 3), 6));
        Assert.False(grid.InReach(Origin, new Cell(7, 3), 6));
        grid.Diagonals = DiagonalRule.Alternating;
        Assert.False(grid.InReach(Origin, new Cell(2, 2), 2));
        var free = new Grid(GridType.Gridless, 10);
        Assert.False(free.InReach(Origin, new Cell(1, 1), 1));
        Assert.True(free.InReach(Origin, new Cell(3, 4), 5));
    }

    [Fact]
    public void WallsStopSight()
    {
        Wall[] walls = { new(new Vector2(5, -10), new Vector2(5, 10)) };
        Assert.False(Sight.LineOfSight(new Vector2(0, 0), new Vector2(10, 0), walls));
        Assert.True(Sight.LineOfSight(new Vector2(0, 0), new Vector2(4, 0), walls));
        Assert.True(Sight.LineOfSight(new Vector2(0, 0), new Vector2(0, 0), walls));
        Assert.True(Sight.LineOfSight(new Vector2(0, 11), new Vector2(10, 11), walls)); // past its end
        Assert.True(Sight.LineOfSight(new Vector2(0, 0), new Vector2(10, 0), Array.Empty<Wall>()));
        // A wall lying along the line is in the way too.
        Wall[] collinear = { new(new Vector2(2, 0), new Vector2(4, 0)) };
        Assert.False(Sight.LineOfSight(new Vector2(0, 0), new Vector2(10, 0), collinear));
        Assert.True(Sight.LineOfSight(new Vector2(0, 1), new Vector2(10, 1), collinear));
    }

    [Fact]
    public void FlankingNeedsFoesOnOppositeSides()
    {
        var grid = new Grid(GridType.Square, 10);
        Assert.True(Positioning.IsFlanked(grid, Origin, new Cell[] { new(-1, 0), new(1, 0) }));
        Assert.True(Positioning.IsFlanked(grid, Origin, new Cell[] { new(-1, -1), new(1, 1) }));
        Assert.False(Positioning.IsFlanked(grid, Origin, new Cell[] { new(-1, 0), new(0, 1) }));
        Assert.False(Positioning.IsFlanked(grid, Origin, new Cell[] { new(-1, 0) }));
        Cell[] apart = { new(-2, 0), new(2, 0) };
        Assert.False(Positioning.IsFlanked(grid, Origin, apart));
        Assert.True(Positioning.IsFlanked(grid, Origin, apart, 2));
        Assert.False(Positioning.IsFlanked(grid, Origin, apart, 2, (a, _) => a.X < 0)); // one of them can't see the target
        // Someone in the target's own cell, or the same foe twice, flanks nothing.
        Assert.False(Positioning.IsFlanked(grid, Origin, new Cell[] { new(0, 0), new(1, 0), new(1, 0) }));

        Cell[] slanted = { new(-1, 1), new(1, -1) };
        Assert.True(Positioning.IsFlanked(new Grid(GridType.Hex, 10), Origin, slanted));
        Assert.True(Positioning.IsFlanked(new Grid(GridType.Gridless, 10), Origin, slanted, 2));
    }

    [Fact]
    public void CoverCountsBlockedCorners()
    {
        var grid = new Grid(GridType.Square, 10);
        Cover Behind(float top)
        {
            Wall[] wall = { new(new Vector2(35, top), new Vector2(35, 20)) };
            return Positioning.CoverBetween(grid, Origin, new Cell(4, 0), (a, b) => !Sight.LineOfSight(a, b, wall));
        }
        Assert.Equal(Cover.None, Behind(15));
        Assert.Equal(Cover.Half, Behind(5));
        Assert.Equal(Cover.ThreeQuarters, Behind(2));
        Assert.Equal(Cover.Full, Behind(0));
        Assert.Equal(Cover.None, Positioning.CoverBetween(grid, Origin, Origin, (_, _) => true));
        Assert.Equal(Cover.None, Positioning.CoverBetween(grid, Origin, new Cell(4, 0), null));
        Assert.Equal(Cover.Full, Positioning.CoverBetween(new Grid(GridType.Hex, 10), Origin, new Cell(4, 0), (_, _) => true));
    }

    [Fact]
    public void TheShippedRulesGiveFlankingAndCoverTheirNumbers()
    {
        PositioningRules rules = RulesFolder.Load(TestContent.Shipped()).Positioning;
        var grid = new Grid(GridType.Square, 64);
        Cell target = new(4, 0);

        // Flanking reaches one cell and puts the off-guard condition on.
        Assert.Equal(("off-guard", 1.0), (rules.FlankingCondition, rules.FlankingReach));
        Assert.True(Positioning.IsFlanked(grid, target, new Cell[] { new(3, 0), new(5, 0) }, (float)rules.FlankingReach));
        Assert.False(Positioning.IsFlanked(grid, target, new Cell[] { new(2, 0), new(6, 0) }, (float)rules.FlankingReach));

        // +2 for half cover and +4 for three-quarters against a shot; a melee attack ignores cover.
        Assert.Equal((0, 2, 4, 0), (rules.CoverArmorClass(Cover.None, true), rules.CoverArmorClass(Cover.Half, true),
            rules.CoverArmorClass(Cover.ThreeQuarters, true), rules.CoverArmorClass(Cover.Full, true)));
        Assert.Equal((0, 0), (rules.CoverArmorClass(Cover.Half, false), rules.CoverArmorClass(Cover.ThreeQuarters, false)));
        Assert.Equal(0, new PositioningRules().CoverArmorClass(Cover.Half, true)); // no positioning.json, no cover

        // Walls give what they block; a creature standing in the way gives half cover and no more.
        Wall[] none = Array.Empty<Wall>();
        Wall[] pillar = { new(new Vector2(200, 0), new Vector2(200, 64)) };
        Cell[] crowd = { Origin, new(2, 0), target };
        Assert.Equal(Cover.None, Positioning.CoverFrom(grid, Origin, target, none, new[] { Origin, target }, rules));
        Assert.Equal(Cover.Half, Positioning.CoverFrom(grid, Origin, target, none, crowd, rules));
        Assert.Equal(Cover.None, Positioning.CoverFrom(grid, Origin, target, none, new[] { new Cell(2, 3) }, rules));
        Assert.Equal(Cover.Full, Positioning.CoverFrom(grid, Origin, target, pillar, crowd, rules));
        Assert.Equal(Cover.None, Positioning.CoverFrom(grid, Origin, target, pillar, crowd, new PositioningRules()));
        PositioningRules bodiless = PositioningRules.Read(TestContent.Json("{\"creaturesProvideCover\": false}"));
        Assert.Equal(Cover.None, Positioning.CoverFrom(grid, Origin, target, none, crowd, bodiless));
    }
}
