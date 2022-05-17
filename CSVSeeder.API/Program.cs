using Microsoft.EntityFrameworkCore;
using CSVSeeder.API;
using CSVSeeder;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<SeedFunContext>(op =>
{
    op.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
}, ServiceLifetime.Scoped);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SeedFunContext>();
    context.Database.Migrate();
    context.AddSeeder(args, app.Logger);
}
app.Run();
