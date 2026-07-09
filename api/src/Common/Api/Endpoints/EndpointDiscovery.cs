using Theodo.DotnetBoilerplate.Common.Utils;

namespace Theodo.DotnetBoilerplate.Common.Api.Endpoints;

public static class EndpointDiscovery
{
    public static void MapEndpoints(this IEndpointRouteBuilder app)
    {
        var endpointTypes = typeof(AssemblyMarker).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IEndpoint)));
        foreach (var type in endpointTypes)
        {
            type.GetMethod(nameof(IEndpoint.Map))!.Invoke(null, [app]);
        }
    }
}