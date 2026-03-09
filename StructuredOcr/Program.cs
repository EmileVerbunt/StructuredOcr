using StructuredOcr.Services;
using StructuredOcr.Components;
using BlazorBlueprint.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register BlazorBlueprint services
builder.Services.AddBlazorBlueprintComponents();

// Config store (in-memory, seeded from appsettings.json)
var configStore = new ConfigStore();
configStore.SeedFromConfiguration(builder.Configuration);
builder.Services.AddSingleton(configStore);

// Schema service
builder.Services.AddSingleton<SchemaService>();

// Cost estimator
builder.Services.AddSingleton<CostEstimator>();

// OCR services
builder.Services.AddHttpClient<MistralOcrService>();
builder.Services.AddHttpClient<LlmVisionOcrService>();
builder.Services.AddSingleton<IOcrService, ContentUnderstandingOcrService>();
builder.Services.AddSingleton<IOcrService>(sp =>
    sp.GetRequiredService<MistralOcrService>());
builder.Services.AddSingleton<IOcrService>(sp =>
    sp.GetRequiredService<LlmVisionOcrService>());

// Service registry
builder.Services.AddSingleton<OcrServiceRegistry>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();