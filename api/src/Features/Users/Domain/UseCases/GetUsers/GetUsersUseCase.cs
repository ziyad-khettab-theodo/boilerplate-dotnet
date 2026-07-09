using System.Collections.Immutable;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Ports;

namespace Theodo.DotnetBoilerplate.Features.Users.Domain.UseCases.GetUsers;

public sealed class GetUsersUseCase(IUserRepositoryPort userRepositoryPort)
{
    public Task<ImmutableList<User>> Handle(GetUsersQuery getUsersQuery, CancellationToken cancellationToken) 
        => userRepositoryPort.FindAll(cancellationToken);
}