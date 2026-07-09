using System.Collections.Immutable;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;

namespace Theodo.DotnetBoilerplate.Features.Users.Domain.Ports;

public interface IUserRepositoryPort
{
    Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken);
}