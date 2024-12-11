using System.Text.Json;

namespace AsyncNet9.Tests;

public class AsyncAwaitStateMachine
{
    static async Task ReadObjects()
    {
        var client = new HttpClient();
        var response = await client.GetAsync("https://api.restful-api.dev/objects");
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        var objs = await JsonSerializer.DeserializeAsync<List<dynamic>>(stream);
    }
}