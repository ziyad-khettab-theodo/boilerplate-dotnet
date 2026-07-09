using System.Collections.Immutable;
using Theodo.DotnetBoilerplate.Common.Domain.ValueObjects;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Ports;

namespace Theodo.DotnetBoilerplate.Common.Infra.Adapters;

public sealed class InMemoryUserRepository : IUserRepositoryPort
{
    private static readonly ImmutableList<User> Users =
    [
        new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Username = new Username("user1") },
        new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Username = new Username("user2") }
    ];

    public Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken)
    {
        return Task.FromResult(Users);
    }
}