using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Theodo.DotnetBoilerplate.Common.Infra.Database;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Ports;

namespace Theodo.DotnetBoilerplate.Common.Infra.Adapters;

public sealed class UserRepository(AppDbContext dbContext): IUserRepositoryPort
{
    public async Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken)
    {
        return (await dbContext.Users.AsNoTracking().ToListAsync(cancellationToken))
            .Select(entity => entity.ToDomain())
            .ToImmutableList();
    }
}