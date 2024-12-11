using FluentAssertions;
using Xunit.Abstractions;

[assembly: CollectionBehavior(MaxParallelThreads = 1)]

namespace AsyncNet9.Tests;

public class AsyncAwait(ITestOutputHelper output)
{
    [Fact]
    public void Test1()
    {
        SynchronizationContext.Current.Should().BeNull();
    }
    
    #region Synchronization Context

    [Fact]
    public async void TestWithSyncContext()
    {
        await Run();
    }

    [Fact]
    public async Task TestWithoutSyncContext()
    {
        await Run();
    }

    async Task Run()
    {
        output.WriteLine($"SynchronizationContext: {SynchronizationContext.Current?.ToString() ?? "null"}");
	        
        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} start");

        var t = DoSomethingAsync();
	        
        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} libero di fare altro nel frattempo!");
	        
        var result = await t;
	        
        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} il risultato è {result}");

        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} end");
    }

    async Task<int> DoSomethingAsync()
    {
        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} start DoSomethingAsync");
	        
        var result = 6;

        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
	        
        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} incremento risultato di 10");
        result += 10;

        output.WriteLine($"Thread {Environment.CurrentManagedThreadId} end DoSomethingAsync");
	        
        return result;
    }

    #endregion
}