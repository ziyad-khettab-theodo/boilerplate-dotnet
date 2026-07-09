using Theodo.DotnetBoilerplate.Common.Api.Endpoints;
using Theodo.DotnetBoilerplate.Common.Utils;

namespace Theodo.DotnetBoilerplate.Common.Api.ServiceRegistration;

public static class UseCaseRegistration
{
    public static IServiceCollection AddUseCases(this IServiceCollection serviceCollection)
    {
        var useCases = typeof(AssemblyMarker).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("UseCase"));
        foreach (var useCase in useCases)
        {
            serviceCollection.AddScoped(useCase);
        }

        return serviceCollection;
    }
}