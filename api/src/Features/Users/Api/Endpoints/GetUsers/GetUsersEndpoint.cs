using System.Collections.Immutable;
using Microsoft.AspNetCore.Http.HttpResults;
using Theodo.DotnetBoilerplate.Common.Api.Endpoints;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;
using Theodo.DotnetBoilerplate.Features.Users.Domain.UseCases.GetUsers;

namespace Theodo.DotnetBoilerplate.Features.Users.Api.Endpoints.GetUsers;

public sealed class GetUsersEndpoint: IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/users", Handle);

    private static async Task<Ok<ImmutableList<GetUsersEndpointResponse>>> Handle(GetUsersUseCase getUsersUseCase, CancellationToken cancellationToken)
    {
        ImmutableList<User> users = await getUsersUseCase.Handle(new GetUsersQuery(), cancellationToken);
        return TypedResults.Ok(users.Select(GetUsersEndpointResponse.From).ToImmutableList());
    }
}