using SecsFrame.CommunicationDemo.Components;
using SecsFrame.CommunicationDemo.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<CommunicationWorkspace>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/favicon.ico", static () => Results.NoContent());
app.MapGet(
    "/healthz",
    static (IWebHostEnvironment environment) =>
    {
        if (!environment.WebRootFileProvider.GetFileInfo("app.css").Exists)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        return Results.Json(
            new
            {
                name = "SecsFrame.CommunicationDemo",
                status = "Ready",
            });
    });
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
