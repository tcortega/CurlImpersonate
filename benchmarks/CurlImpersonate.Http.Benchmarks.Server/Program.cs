var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenLocalhost(5199, o => o.UseHttps());
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.MapGet("/get", () => new { message = "ok" });

app.MapPost("/post", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    return new { received = body.Length, message = "ok" };
});

app.MapGet("/bytes/{size:int}", (int size) =>
{
    var bytes = new byte[size];
    Random.Shared.NextBytes(bytes);
    return Results.Bytes(bytes, "application/octet-stream");
});

app.Run();
