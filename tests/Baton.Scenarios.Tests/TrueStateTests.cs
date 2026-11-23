using Baton.Core.News2;

namespace Baton.Scenarios.Tests;

public class TrueStateTests
{
    private static readonly ScenarioCard Card = new(
        Id: "test",
        Title: "Test",
        Diagnosis: "test",
        Scale: SpO2Scale.Scale1,
        Keyframes:
        [
            new Keyframe(2, new VitalSet(16, 97, 0, 120, 80, 37.0m, Consciousness.Alert)),
            new Keyframe(6, new VitalSet(20, 93, 2, 100, 120, 38.0m, Consciousness.Confusion)),
        ],
        Events: [],
        Delay: null);

    [Fact]
    public void AtAKeyframe_ReturnsItsValuesExactly()
    {
        Assert.Equal(new VitalSet(20, 93, 2, 100, 120, 38.0m, Consciousness.Confusion), TrueState.At(Card, 6));
    }

    [Fact]
    public void BetweenKeyframes_NumbersAreLinear()
    {
        var mid = TrueState.At(Card, 4);

        Assert.Equal(18, mid.RespirationRate);
        Assert.Equal(95, mid.SpO2);
        Assert.Equal(110, mid.SystolicBp);
        Assert.Equal(100, mid.PulseRate);
        Assert.Equal(37.5m, mid.TemperatureCelsius);
    }

    [Fact]
    public void BetweenKeyframes_OxygenAndConsciousnessKeepThePreviousValue()
    {
        var justBefore = TrueState.At(Card, 5.9);

        Assert.Equal(0m, justBefore.OxygenLitresPerMinute);
        Assert.Equal(Consciousness.Alert, justBefore.Consciousness);
    }

    [Fact]
    public void BeforeTheFirstAndAfterTheLastKeyframe_ValuesAreHeld()
    {
        Assert.Equal(Card.Keyframes[0].Vitals, TrueState.At(Card, 0));
        Assert.Equal(Card.Keyframes[1].Vitals, TrueState.At(Card, 24));
    }
}
