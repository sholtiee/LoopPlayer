using LoopPlayer.Core;
using LoopPlayer.UI;
using Xunit;

namespace LoopPlayer.Tests;

public class SpeedControllerTests
{
    [Fact]
    public void StartsAtOneAndFormatsWithComma()
    {
        var speed = new SpeedController();
        Assert.Equal(1.0, speed.Speed, 9);
        Assert.Equal("×1,000", speed.Format());
    }

    [Fact]
    public void StepsByExactly0025WithoutDrift()
    {
        var speed = new SpeedController();
        for (var i = 0; i < 20; i++) speed.Increase();
        Assert.Equal(1.5, speed.Speed, 9);
        Assert.Equal("×1,500", speed.Format());
        speed.Increase();
        Assert.Equal("×1,500", speed.Format());
        Assert.False(speed.CanIncrease);

        for (var i = 0; i < 100; i++) speed.Decrease();
        Assert.Equal("×0,500", speed.Format());
        Assert.False(speed.CanDecrease);
        speed.Increase();
        Assert.Equal("×0,525", speed.Format());
        speed.Increase();
        Assert.Equal("×0,550", speed.Format());
    }
}

public class AbRangeTests
{
    private static AbRange Make(double duration = 100)
    {
        var r = new AbRange();
        r.ResetForDuration(duration);
        return r;
    }

    [Fact]
    public void ResetSetsFullRange()
    {
        var r = Make(120);
        Assert.Equal(0, r.A);
        Assert.Equal(120, r.B);
    }

    [Fact]
    public void SetAPushesBWhenBeyond()
    {
        var r = Make();
        r.SetBFromPosition(30);
        r.SetAFromPosition(50);
        Assert.Equal(50, r.A);
        Assert.Equal(50, r.B);
    }

    [Fact]
    public void SetBPushesAWhenBefore()
    {
        var r = Make();
        r.SetAFromPosition(40);
        r.SetBFromPosition(10);
        Assert.Equal(10, r.A);
        Assert.Equal(10, r.B);
    }

    [Fact]
    public void NudgeClampsToBoundsAndOtherPoint()
    {
        var r = Make();
        r.SetAFromPosition(10);
        r.SetBFromPosition(15);

        r.NudgeA(-20);
        Assert.Equal(0, r.A);
        r.NudgeA(20);
        Assert.Equal(15, r.A); // A ≤ B, B не сдвинулся
        Assert.Equal(15, r.B);

        r.NudgeB(200);
        Assert.Equal(100, r.B);
        r.NudgeB(-200);
        Assert.Equal(15, r.B); // B ≥ A
        Assert.Equal(15, r.A);
    }

    [Fact]
    public void ReleaseBKeepsAAndOpensToEnd()
    {
        var r = Make();
        r.SetAFromPosition(10);
        r.SetBFromPosition(15);
        r.ReleaseB();
        Assert.Equal(10, r.A);
        Assert.Equal(100, r.B);
    }

    [Fact]
    public void NudgeUsesFractionalSteps()
    {
        var r = Make();
        r.SetAFromPosition(10);
        r.NudgeA(AbRange.SmallStep);
        Assert.Equal(10.2, r.A, 9);
        r.NudgeA(-AbRange.LargeStep);
        Assert.Equal(9.2, r.A, 9);
    }

    [Fact]
    public void ChangedFiresOnlyOnRealChange()
    {
        var r = Make();
        var n = 0;
        r.Changed += () => n++;
        r.SetAFromPosition(0);
        Assert.Equal(0, n);
        r.SetAFromPosition(5);
        Assert.Equal(1, n);
        r.NudgeB(10); // B уже на максимуме
        Assert.Equal(1, n);
    }
}

public class RepeatCounterTests
{
    [Fact]
    public void AddsAndResets()
    {
        var c = new RepeatCounter();
        var events = 0;
        c.Changed += () => events++;
        c.Add(2);
        c.Add(0);
        Assert.Equal(2, c.Count);
        Assert.Equal(1, events);
        c.Reset();
        Assert.Equal(0, c.Count);
        c.Reset();
        Assert.Equal(2, events);
    }
}

public class PositionControllerTests
{
    [Theory]
    [InlineData(1.0, -2.0, 0.0)]
    [InlineData(99.5, 2.0, 100.0)]
    [InlineData(10.0, 0.2, 10.2)]
    [InlineData(10.0, -1.0, 9.0)]
    public void SeekByClamps(double pos, double delta, double expected)
    {
        Assert.Equal(expected, PositionController.SeekBy(pos, delta, 100), 9);
    }
}

public class LoopControllerTests
{
    [Fact]
    public void ReadsStraightWhenNoWrapNeeded()
    {
        var plan = LoopController.Plan(0, 100, 0, 1000, 1000);
        Assert.Single(plan.Segments);
        Assert.Equal(new ReadSegment(0, 100), plan.Segments[0]);
        Assert.Equal(100, plan.NewPosition);
        Assert.Equal(0, plan.Wraps);
    }

    [Fact]
    public void WrapsFromBToAInsideOneRead()
    {
        // A=10, B=15, позиция 12, читаем 8 кадров: 12..15 (3) + 10..15 (5)
        var plan = LoopController.Plan(12, 8, 10, 15, 100);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(new ReadSegment(12, 3), plan.Segments[0]);
        Assert.Equal(new ReadSegment(10, 5), plan.Segments[1]);
        Assert.Equal(10, plan.NewPosition); // достигли B — уже перешли на A
        Assert.Equal(2, plan.Wraps);
    }

    [Fact]
    public void StartingExactlyAtBJumpsToAWithoutCounting()
    {
        var plan = LoopController.Plan(15, 3, 10, 15, 100);
        Assert.Single(plan.Segments);
        Assert.Equal(new ReadSegment(10, 3), plan.Segments[0]);
        Assert.Equal(13, plan.NewPosition);
        Assert.Equal(0, plan.Wraps);
    }

    [Fact]
    public void ReachingBAtEndOfReadCountsOnce()
    {
        var plan = LoopController.Plan(10, 5, 10, 15, 100);
        Assert.Equal(1, plan.Wraps);
        Assert.Equal(10, plan.NewPosition);
    }

    [Fact]
    public void CountsMultipleWrapsForTinyFragment()
    {
        var plan = LoopController.Plan(10, 12, 10, 15, 100);
        Assert.Equal(12, plan.Segments.Sum(s => s.Length));
        Assert.Equal(2, plan.Wraps); // 5 + 5 + 2
        Assert.Equal(12, plan.NewPosition);
    }

    [Fact]
    public void WrapsWhenBEqualsTrackEnd()
    {
        var plan = LoopController.Plan(95, 10, 0, 100, 100);
        Assert.Equal(1, plan.Wraps);
        Assert.Equal(5, plan.NewPosition);
        Assert.Equal(10, plan.Segments.Sum(s => s.Length));
    }

    [Fact]
    public void PlaysToEndWhenPositionIsBeyondB()
    {
        var plan = LoopController.Plan(95, 10, 10, 15, 100);
        Assert.Single(plan.Segments);
        Assert.Equal(5, plan.Segments[0].Length);
        Assert.Equal(100, plan.NewPosition);
        Assert.Equal(0, plan.Wraps);

        var atEnd = LoopController.Plan(100, 10, 10, 15, 100);
        Assert.Empty(atEnd.Segments);
    }

    [Fact]
    public void EmptyFragmentDoesNotLoop()
    {
        var plan = LoopController.Plan(0, 50, 10, 10, 100);
        Assert.Equal(0, plan.Wraps);
        Assert.Equal(50, plan.NewPosition);
    }

    [Fact]
    public void PositionBeforeAPlaysIntoFragmentThenLoops()
    {
        var plan = LoopController.Plan(0, 30, 10, 15, 100);
        Assert.Equal(new ReadSegment(0, 15), plan.Segments[0]);
        Assert.Equal(4, plan.Wraps); // 15 + 5 + 5 + 5, последний ровно до B
        Assert.Equal(30, plan.Segments.Sum(s => s.Length));
    }
}

public class TimeFormatterTests
{
    [Theory]
    [InlineData(0, false, "00:00")]
    [InlineData(65.9, false, "01:05")]
    [InlineData(3599.99, false, "59:59")]
    [InlineData(3600, true, "01:00:00")]
    [InlineData(3661, true, "01:01:01")]
    [InlineData(-5, false, "00:00")]
    public void Formats(double seconds, bool hours, string expected)
    {
        Assert.Equal(expected, TimeFormatter.Format(seconds, hours));
    }

    [Fact]
    public void UsesHoursFromSixtyMinutes()
    {
        Assert.False(TimeFormatter.UseHours(3599));
        Assert.True(TimeFormatter.UseHours(3600));
    }
}
