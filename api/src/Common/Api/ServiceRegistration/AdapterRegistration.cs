using Theodo.DotnetBoilerplate.Common.Infra.Adapters;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Ports;

namespace Theodo.DotnetBoilerplate.Common.Api.ServiceRegistration;

public static class AdapterRegistration
{
    public static IServiceCollection AddAdapters(this IServiceCollection serviceCollection) 
        => serviceCollection.AddScoped<IUserRepositoryPort, UserRepository>();
}