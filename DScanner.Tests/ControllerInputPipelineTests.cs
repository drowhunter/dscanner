using DScanner.Models;
using DScanner.Reactive;
using System.Reactive.Linq;

namespace DScanner.Tests;

public sealed class ControllerInputPipelineTests
{
    [Fact]
    public void Axis_UsesFirstValueAsBaselineAndFiltersInsignificantChanges()
    {
        ControllerInputEvent[] events = new[]
            {
                Snapshot(axis: 1.00),
                Snapshot(axis: 0.99),
                Snapshot(axis: 0.75)
            }
            .ToObservable()
            .DetectInputEvents(0.25, 0.20)
            .ToArray()
            .Wait();

        AxisMovedEvent axis = Assert.IsType<AxisMovedEvent>(Assert.Single(events));
        Assert.Equal(1.00, axis.Baseline, 10);
        Assert.Equal(0.75, axis.Value, 10);
        Assert.Equal(-0.25, axis.Difference, 10);
    }

    [Fact]
    public void Axis_LogsOnceUntilItReturnsInsideResetThreshold()
    {
        ControllerInputEvent[] events = new[]
            {
                Snapshot(axis: 0),
                Snapshot(axis: 0.25),
                Snapshot(axis: 0.50),
                Snapshot(axis: 0.10),
                Snapshot(axis: 0.30)
            }
            .ToObservable()
            .DetectInputEvents(0.25, 0.20)
            .ToArray()
            .Wait();

        AxisMovedEvent[] axes = events.OfType<AxisMovedEvent>().ToArray();
        Assert.Equal(2, axes.Length);
        Assert.Equal(0.25, axes[0].Value, 10);
        Assert.Equal(0.30, axes[1].Value, 10);
    }

    [Fact]
    public void Axis_LogsDirectionReversalWithoutNeutralSample()
    {
        ControllerInputEvent[] events = new[]
            {
                Snapshot(axis: 0),
                Snapshot(axis: 0.30),
                Snapshot(axis: -0.30)
            }
            .ToObservable()
            .DetectInputEvents(0.25, 0.20)
            .ToArray()
            .Wait();

        AxisMovedEvent[] axes = events.OfType<AxisMovedEvent>().ToArray();
        Assert.Equal(2, axes.Length);
        Assert.Equal(0.30, axes[0].Value, 10);
        Assert.Equal(-0.30, axes[1].Value, 10);
    }

    [Fact]
    public void Button_LogsOnlyReleasedToPressedTransition()
    {
        ControllerInputEvent[] events = new[]
            {
                Snapshot(button: false),
                Snapshot(button: true),
                Snapshot(button: true),
                Snapshot(button: false)
            }
            .ToObservable()
            .DetectInputEvents(0.25, 0.20)
            .ToArray()
            .Wait();

        ButtonPressedEvent button = Assert.IsType<ButtonPressedEvent>(Assert.Single(events));
        Assert.Equal(0, button.ButtonNumber);
    }

    [Fact]
    public void Pov_ConvertsHundredthsOfDegreesAndPreservesDepressedValue()
    {
        ControllerInputEvent[] events = new[]
            {
                Snapshot(pov: -1),
                Snapshot(pov: 4500),
                Snapshot(pov: -1)
            }
            .ToObservable()
            .DetectInputEvents(0.25, 0.20)
            .ToArray()
            .Wait();

        PovChangedEvent[] povs = events.OfType<PovChangedEvent>().ToArray();
        Assert.Equal(2, povs.Length);
        Assert.Equal(45, povs[0].Degrees);
        Assert.Equal(-1, povs[1].Degrees);
    }

    private static ControllerSnapshot Snapshot(
        double axis = 0,
        bool button = false,
        int pov = -1) =>
        new(
            Guid.Parse("31A184C3-91F8-4D39-8F81-EF48C662129C"),
            "Test controller",
            DateTimeOffset.UtcNow,
            [button],
            [new AxisSample(0, "X", axis)],
            [pov]);
}
