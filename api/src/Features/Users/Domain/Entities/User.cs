using Theodo.DotnetBoilerplate.Common.Domain.ValueObjects;

namespace Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;

public sealed record User
{
    public required Guid Id { get; init; }
    public required Username Username { get; init; }
}