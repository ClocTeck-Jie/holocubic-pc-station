namespace Clocteck.CubicCenter.Models;

// A null value means this sensor exists but has no current measurement.
// IDs come from the provider's hardware and sensor identifiers, never display labels.
public sealed record RawSensorReading(
    string Id,
    string HardwareId,
    string HardwareName,
    string HardwareType,
    string Name,
    string SensorType,
    double? Value,
    string Unit,
    string Source,
    DateTimeOffset Timestamp);
