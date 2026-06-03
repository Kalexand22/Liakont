using Liakont.Host.Startup;

var builder = WebApplication.CreateBuilder(args);

AppBootstrap.ConfigureServices(builder);

var app = builder.Build();

await AppBootstrap.InitializeDataAsync(app);
AppBootstrap.ConfigureMiddleware(app);

app.Run();

// Required for WebApplicationFactory in integration/smoke tests.
public partial class Program
{
}
