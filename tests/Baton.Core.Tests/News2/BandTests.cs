using System.Globalization;
using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

// Every expected value below is read from the RCP NEWS2 2017 chart, one row per band boundary.
public class BandTests
{
    [Theory]
    [InlineData(8, 3)]
    [InlineData(9, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 0)]
    [InlineData(20, 0)]
    [InlineData(21, 2)]
    [InlineData(24, 2)]
    [InlineData(25, 3)]
    public void Respiration_ReturnsRcpBandScore(int rate, int expected) =>
        Assert.Equal(expected, News2Bands.Respiration(rate));

    [Theory]
    [InlineData(91, 3)]
    [InlineData(92, 2)]
    [InlineData(93, 2)]
    [InlineData(94, 1)]
    [InlineData(95, 1)]
    [InlineData(96, 0)]
    [InlineData(100, 0)]
    public void SpO2Scale1_ReturnsRcpBandScore(int spo2, int expected) =>
        Assert.Equal(expected, News2Bands.SpO2Scale1(spo2));

    [Theory]
    [InlineData(83, false, 3)]
    [InlineData(84, false, 2)]
    [InlineData(85, false, 2)]
    [InlineData(86, false, 1)]
    [InlineData(87, false, 1)]
    [InlineData(88, false, 0)]
    [InlineData(92, false, 0)]
    [InlineData(93, false, 0)]
    [InlineData(100, false, 0)]
    [InlineData(88, true, 0)]
    [InlineData(92, true, 0)]
    [InlineData(93, true, 1)]
    [InlineData(94, true, 1)]
    [InlineData(95, true, 2)]
    [InlineData(96, true, 2)]
    [InlineData(97, true, 3)]
    public void SpO2Scale2_ReturnsRcpBandScore(int spo2, bool onOxygen, int expected) =>
        Assert.Equal(expected, News2Bands.SpO2Scale2(spo2, onOxygen));

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void SupplementalOxygen_ReturnsRcpScore(bool onOxygen, int expected) =>
        Assert.Equal(expected, News2Bands.SupplementalOxygen(onOxygen));

    [Theory]
    [InlineData(90, 3)]
    [InlineData(91, 2)]
    [InlineData(100, 2)]
    [InlineData(101, 1)]
    [InlineData(110, 1)]
    [InlineData(111, 0)]
    [InlineData(219, 0)]
    [InlineData(220, 3)]
    public void Systolic_ReturnsRcpBandScore(int mmHg, int expected) =>
        Assert.Equal(expected, News2Bands.Systolic(mmHg));

    [Theory]
    [InlineData(40, 3)]
    [InlineData(41, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 0)]
    [InlineData(90, 0)]
    [InlineData(91, 1)]
    [InlineData(110, 1)]
    [InlineData(111, 2)]
    [InlineData(130, 2)]
    [InlineData(131, 3)]
    public void Pulse_ReturnsRcpBandScore(int rate, int expected) =>
        Assert.Equal(expected, News2Bands.Pulse(rate));

    [Theory]
    [InlineData("35.0", 3)]
    [InlineData("35.1", 1)]
    [InlineData("36.0", 1)]
    [InlineData("36.1", 0)]
    [InlineData("38.0", 0)]
    [InlineData("38.1", 1)]
    [InlineData("39.0", 1)]
    [InlineData("39.1", 2)]
    public void Temperature_ReturnsRcpBandScore(string celsius, int expected) =>
        Assert.Equal(expected, News2Bands.Temperature(decimal.Parse(celsius, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(Consciousness.Alert, ConfusionOnset.Unknown, 0)]
    [InlineData(Consciousness.Alert, ConfusionOnset.New, 0)]
    [InlineData(Consciousness.Confusion, ConfusionOnset.New, 3)]
    [InlineData(Consciousness.Confusion, ConfusionOnset.Unknown, 3)]
    [InlineData(Consciousness.Confusion, ConfusionOnset.Chronic, 0)]
    [InlineData(Consciousness.Voice, ConfusionOnset.Unknown, 3)]
    [InlineData(Consciousness.Pain, ConfusionOnset.Unknown, 3)]
    [InlineData(Consciousness.Unresponsive, ConfusionOnset.Unknown, 3)]
    public void Consciousness_ReturnsRcpScore(Consciousness level, ConfusionOnset onset, int expected) =>
        Assert.Equal(expected, News2Bands.Consciousness(level, onset));
}
