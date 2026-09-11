namespace Clocteck.CubicCenter.Services;

internal interface ISensorProvider
{
    string ProviderId { get; }
    bool IsAvailable { get; }
    string ProviderStatus { get; }
}
