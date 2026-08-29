using DScanner.Models;
using System.Reactive.Linq;

namespace DScanner.Reactive;

public static class ControllerInputPipeline
{
    public static IObservable<ControllerInputEvent> DetectInputEvents(
        this IObservable<ControllerSnapshot> source,
        double axisChangeThreshold,
        double axisResetThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (axisChangeThreshold is <= 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(axisChangeThreshold));
        }

        if (axisResetThreshold < 0 || axisResetThreshold >= axisChangeThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(axisResetThreshold));
        }

        return source
            .Scan(
                DetectionStep.Empty,
                (previous, snapshot) => Detect(previous.State, snapshot, axisChangeThreshold, axisResetThreshold))
            .SelectMany(step => step.Events);
    }

    private static DetectionStep Detect(
        DetectionState? previous,
        ControllerSnapshot snapshot,
        double changeThreshold,
        double resetThreshold)
    {
        if (previous is null)
        {
            return new DetectionStep(DetectionState.Create(snapshot), []);
        }

        List<ControllerInputEvent> events = [];

        int buttonCount = Math.Min(previous.Buttons.Length, snapshot.Buttons.Count);
        for (int index = 0; index < buttonCount; index++)
        {
            if (!previous.Buttons[index] && snapshot.Buttons[index])
            {
                events.Add(new ButtonPressedEvent(
                    snapshot.DeviceId,
                    snapshot.DeviceName,
                    snapshot.Timestamp,
                    index));
            }
        }

        int povCount = Math.Min(previous.Povs.Length, snapshot.Povs.Count);
        for (int index = 0; index < povCount; index++)
        {
            if (previous.Povs[index] != snapshot.Povs[index])
            {
                events.Add(new PovChangedEvent(
                    snapshot.DeviceId,
                    snapshot.DeviceName,
                    snapshot.Timestamp,
                    index,
                    snapshot.Povs[index]));
            }
        }

        Dictionary<int, AxisDetectionState> nextAxes = new(previous.Axes.Count);
        foreach (AxisSample axis in snapshot.Axes)
        {
            if (!previous.Axes.TryGetValue(axis.Number, out AxisDetectionState? axisState)
                || axisState is null)
            {
                nextAxes[axis.Number] = new AxisDetectionState(axis.Value, true, 0);
                continue;
            }

            double difference = axis.Value - axisState.Baseline;
            double magnitude = Math.Abs(difference);
            int side = Math.Sign(difference);
            bool shouldLog = false;

            if (axisState.Armed && magnitude >= changeThreshold)
            {
                shouldLog = true;
                axisState = axisState with { Armed = false, ActiveSide = side };
            }
            else if (!axisState.Armed && magnitude < resetThreshold)
            {
                axisState = axisState with { Armed = true, ActiveSide = 0 };
            }
            else if (!axisState.Armed
                && magnitude >= changeThreshold
                && side != 0
                && side != axisState.ActiveSide)
            {
                shouldLog = true;
                axisState = axisState with { ActiveSide = side };
            }

            nextAxes[axis.Number] = axisState;

            if (shouldLog)
            {
                events.Add(new AxisMovedEvent(
                    snapshot.DeviceId,
                    snapshot.DeviceName,
                    snapshot.Timestamp,
                    axis.Number,
                    axis.Name,
                    axis.Value,
                    axisState.Baseline,
                    difference));
            }
        }

        DetectionState nextState = new(
            snapshot.Buttons.ToArray(),
            snapshot.Povs.ToArray(),
            nextAxes);

        return new DetectionStep(nextState, events);
    }

    private sealed record DetectionStep(
        DetectionState? State,
        IReadOnlyList<ControllerInputEvent> Events)
    {
        public static DetectionStep Empty { get; } = new(null, []);
    }

    private sealed record DetectionState(
        bool[] Buttons,
        int[] Povs,
        Dictionary<int, AxisDetectionState> Axes)
    {
        public static DetectionState Create(ControllerSnapshot snapshot) =>
            new(
                snapshot.Buttons.ToArray(),
                snapshot.Povs.ToArray(),
                snapshot.Axes.ToDictionary(
                    axis => axis.Number,
                    axis => new AxisDetectionState(axis.Value, true, 0)));
    }

    private sealed record AxisDetectionState(double Baseline, bool Armed, int ActiveSide);
}
