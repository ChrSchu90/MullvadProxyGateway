namespace GostGen;

/// <summary>
/// Provided proxy that is available for use.
/// </summary>
internal record Proxy
{
    public Proxy() { }

    internal Proxy(bool isPool, MullvadRelay server, ServiceConfig service)
    {
        IsPool = isPool;
        Server = server;
        Service = service;
        LocationCode = $"{server.CountryCode}-{server.CityCode}";
    }

    public bool IsPool { get; init; }

    public string LocationCode { get; init; }

    public MullvadRelay Server { get; init; }

    public ServiceConfig Service { get; init; }
}
