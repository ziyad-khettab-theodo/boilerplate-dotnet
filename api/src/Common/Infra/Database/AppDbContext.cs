using Microsoft.EntityFrameworkCore;
using Theodo.DotnetBoilerplate.Common.Infra.Database.Entities;

namespace Theodo.DotnetBoilerplate.Common.Infra.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options): DbContext(options)
{
    public DbSet<UserDbEntity> Users => Set<UserDbEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}