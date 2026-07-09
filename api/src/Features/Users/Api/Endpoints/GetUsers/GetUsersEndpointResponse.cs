using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;

namespace Theodo.DotnetBoilerplate.Features.Users.Api.Endpoints.GetUsers;

public sealed record GetUsersEndpointResponse
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }

    public static GetUsersEndpointResponse From(User user)
    => new () { Id = user.Id, Username = user.Username.Value };
}