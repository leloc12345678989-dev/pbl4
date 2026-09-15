using pbl4.OSIntegration;
using Xunit;

namespace pbl4.OSIntegration.Tests;

public sealed class SegmentPlannerTests
{
    [Fact]
    public void Plan_WithEvenSize_CoversWholeFile()
    {
        var planner = new SegmentPlanner();

        var segments = planner.Plan(1024, 4);

        Assert.Equal(4, segments.Count);
        Assert.Equal(new PlannedSegment(0, 0, 255), segments[0]);
        Assert.Equal(new PlannedSegment(3, 768, 1023), segments[3]);
    }

    [Fact]
    public void Plan_WithRemainder_DistributesFirstSegments()
    {
        var planner = new SegmentPlanner();

        var segments = planner.Plan(10, 3);

        Assert.Equal(new PlannedSegment(0, 0, 3), segments[0]);
        Assert.Equal(new PlannedSegment(1, 4, 6), segments[1]);
        Assert.Equal(new PlannedSegment(2, 7, 9), segments[2]);
    }

    [Fact]
    public void Plan_WithMoreSegmentsThanBytes_UsesOneByteSegments()
    {
        var planner = new SegmentPlanner();

        var segments = planner.Plan(3, 8);

        Assert.Equal(3, segments.Count);
        Assert.All(segments, segment => Assert.Equal(1, segment.Length));
    }
}

