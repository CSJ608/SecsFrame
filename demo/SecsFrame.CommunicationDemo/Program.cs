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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
