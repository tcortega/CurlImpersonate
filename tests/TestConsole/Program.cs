using CurlImpersonate.Http;

Console.WriteLine("Starting test...");

try
{
    Console.WriteLine("Creating handler...");
    using var handler = new CurlHandler();
    Console.WriteLine("Handler created");

    Console.WriteLine("Creating HttpClient...");
    using var client = new System.Net.Http.HttpClient(handler);
    Console.WriteLine("HttpClient created");

    Console.WriteLine("Sending request...");
    var response = await client.GetAsync("https://httpbin.org/get");
    Console.WriteLine($"Response status: {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Content length: {content.Length}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

Console.WriteLine("Done!");
