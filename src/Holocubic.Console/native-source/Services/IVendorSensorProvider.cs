namespace Clocteck.CubicCenter.Services;

internal interface IVendorSensorProvider : IDisposable
{
    string ProviderId { get; }
    bool IsAvailable { get; }
    VendorSensorSnapshot Read();
}

internal sealed record VendorSensorSnapshot(
    bool Available,
    double? CpuTemperatureC,
    double? CpuFanRpm,
    double? GpuFanRpm);
