using Microsoft.EntityFrameworkCore;
using Theodo.DotnetBoilerplate.Common.Api.Endpoints;
using Theodo.DotnetBoilerplate.Common.Api.ServiceRegistration;
using Theodo.DotnetBoilerplate.Common.Infra.Database;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUseCases();
builder.Services.AddAdapters();
builder.Services.AddDbContext<AppDbContext>(
    o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
);

var app = builder.Build();
app.UsePathBase("/api");
app.UseRouting();
app.MapEndpoints();
app.Run();

public partial class Program;