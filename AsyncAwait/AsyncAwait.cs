using System.Diagnostics;
using Nito.AsyncEx;

namespace AsyncAwait;

[TestClass]
public sealed class AsyncAwait
{
    #region Synchronization Context

    [TestMethod]
    public void TestWithSyncContext()
    {
        AsyncContext.Run(Run);
    }

    [TestMethod]
    public async Task TestWithoutSyncContext()
    {
        await Run();
    }

    static async Task Run()
    {
        Debug.WriteLine($"SynchronizationContext: {SynchronizationContext.Current?.ToString() ?? "null"}");
	        
        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} start");

        Task<int> t = DoSomethingAsync();
	        
        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} libero di fare altro nel frattempo!");
	        
        var result = await t;
	        
        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} il risultato è {result}");

        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} end");
    }

    static async Task<int> DoSomethingAsync()
    {
        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} enter DoSomethingAsync");
	        
        var result = 6;

        await Task.Delay(TimeSpan.FromSeconds(3));//.ConfigureAwait(false);
	        
        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} incremento risultato di 10");
        result += 10;

        Debug.WriteLine($"Thread {Environment.CurrentManagedThreadId} end DoSomethingAsync");
	        
        return result;
    }

    #endregion
}