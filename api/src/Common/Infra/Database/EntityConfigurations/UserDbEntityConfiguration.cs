using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Theodo.DotnetBoilerplate.Common.Infra.Database.Entities;

namespace Theodo.DotnetBoilerplate.Common.Infra.Database.EntityConfigurations;

public sealed class UserDbEntityConfiguration : IEntityTypeConfiguration<UserDbEntity>
{
    public void Configure(EntityTypeBuilder<UserDbEntity> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(50).IsRequired();
        builder.HasIndex(u => u.Id).IsUnique();
    }
}