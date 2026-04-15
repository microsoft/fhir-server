using SqlOnFhirDemo.Components;
using SqlOnFhirDemo.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure FHIR server connection
string fhirBaseUrl = builder.Configuration.GetValue<string>("FhirServer:BaseUrl") ?? "http://localhost:44348";
builder.Services.AddHttpClient<FhirDemoService>(client =>
{
    client.BaseAddress = new Uri(fhirBaseUrl);
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/fhir+json"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
