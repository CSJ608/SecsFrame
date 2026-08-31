using SecsFrame.GuidedDemo.Components;
using SecsFrame.GuidedDemo.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<GuidedDemoSession>();

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
