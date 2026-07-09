using Theodo.DotnetBoilerplate.Common.Domain.ValueObjects;
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;

namespace Theodo.DotnetBoilerplate.Common.Infra.Database.Entities;

public sealed class UserDbEntity
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public User ToDomain() => new() { Id = Id, Username = new Username(Username) };
    public static UserDbEntity FromDomain(User user) => new() { Id = user.Id, Username = user.Username.Value };
}