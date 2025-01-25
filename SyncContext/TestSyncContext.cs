using System.Collections.Concurrent;
using System.Diagnostics;

namespace SyncContext;

[TestClass]
    public class TestSyncContext
    {
        //SynchronizationContext and TaskScheduler are abstractions that represent a “scheduler”,
        //something that you give some work to, and it determines when and where to run that work
        
        [TestMethod]
        public async Task AsyncAwait()
        {
            var value = await DoSomethingAsync();
            RestOfTheMethod(value);
        }
        static async Task<int> DoSomethingAsync()
        {
            const int result = 6;

            await Task.Delay(TimeSpan.FromSeconds(2));

            return result;
        }
        static void RestOfTheMethod(int value)
        {
            Debug.WriteLine(value);
        }
        
        [TestMethod]
        public void UsingSyncContext()
        {
            var task = DoSomethingAsync();
            var scheduler = SynchronizationContext.Current != null 
                ? TaskScheduler.FromCurrentSynchronizationContext() 
                : TaskScheduler.Default;
            task.ContinueWith(t =>
            {
                RestOfTheMethod(t.Result);
            }, scheduler);
        }
        
        [TestMethod]
        public void UsingSyncContextDirectly()
        {
            var task = DoSomethingAsync();
            var currentSyncContext = SynchronizationContext.Current;
            task.ContinueWith(t =>
            {
                if (currentSyncContext is null)
                    RestOfTheMethod(t.Result);
                else
                    currentSyncContext.Post(delegate { RestOfTheMethod(t.Result); }, null);
            }, TaskScheduler.Default);
        }

        [TestMethod]
        public void DoWorkWithoutContext()
        {
            DoWork().Wait();
        }

        [TestMethod]
        public void DoWorkWithContext()
        {
            AsyncPump.Run(async () => await DoWork());
        }
        
        [TestMethod]
        public void PassSchedulerToStartNew()
        {
            var cesp = new ConcurrentExclusiveSchedulerPair();
            Task.Factory.StartNew(() =>
            {
                Console.WriteLine(TaskScheduler.Current == cesp.ExclusiveScheduler);
            }, CancellationToken.None, TaskCreationOptions.None, cesp.ExclusiveScheduler).Wait();
        }
        
        static async Task DoWork()
        {
            var d = new Dictionary<int, int>();
            for (var i = 0; i < 10000; i++)
            {
                var id = Environment.CurrentManagedThreadId;
                d[id] = d.TryGetValue(id, out var count) ? count+1 : 1;
 
                await Task.Yield();
            }
            foreach (var pair in d)
                Console.WriteLine(pair);
        }
        
        //use ThreadPool and then post back to the prev context
        public void DoWork(Action work, Action completion)
        {
            var sc = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    work();
                }
                finally
                {
                    sc?.Post(_ => completion(), null);
                }
            });
        }
        
    }
    
    #region SyncContext
    
    //SynchronizationContext --> Post --> later execute the delegate

    //Provides a pump that supports running asynchronous methods on the current thread
    public static class AsyncPump
    {
        public static void Run(Func<Task> func)
        {
            ArgumentNullException.ThrowIfNull(func);

            var prevCtx = SynchronizationContext.Current;
            try
            {
                var syncCtx = new SingleThreadSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(syncCtx);

                // Invoke the function and alert the context when it completes
                var t = func();
                if (t == null)
                    throw new InvalidOperationException("No task provided.");

                t.ContinueWith(delegate
                {
                    syncCtx.Complete();
                }, TaskScheduler.Default);

                // Pump continuations and propagate any exceptions
                syncCtx.RunOnCurrentThread();
                t.GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prevCtx);
            }
        }

        public static void Run(Action action)
        {
            var prevCtx = SynchronizationContext.Current; 
            try 
            { 
                var syncCtx = new SingleThreadSynchronizationContext(true); 
                SynchronizationContext.SetSynchronizationContext(syncCtx);

                syncCtx.OperationStarted(); 
                action(); 
                syncCtx.OperationCompleted();

                syncCtx.RunOnCurrentThread(); 
            } 
            finally 
            { 
                SynchronizationContext.SetSynchronizationContext(prevCtx); 
            } 
        }
    }
    
    //SynchronizationContext that is single-threaded
    internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> queue = new();

        readonly bool trackOperations;
        int operationCount;
        
        internal SingleThreadSynchronizationContext(bool trackOperations = false)
        {
            this.trackOperations = trackOperations;
        }
        
        //Dispatches an asynchronous message to the synchronization context
        //"work" is the delegate to call
        //"state" is the object passed to the delegate
        public override void Post(SendOrPostCallback work, object state)
        {
            ArgumentNullException.ThrowIfNull(work);
            queue.Add(new KeyValuePair<SendOrPostCallback, object>(work, state));
        }
        
        public override void Send(SendOrPostCallback d, object state)
        {
            throw new NotSupportedException("Synchronously sending is not supported.");
        }

        //Runs an loop to process all queued work items
        public void RunOnCurrentThread()
        {
            foreach (var workItem in queue.GetConsumingEnumerable())
                workItem.Key(workItem.Value);
        }

        //Notifies the context that no more work will arrive
        public void Complete()
        {
            queue.CompleteAdding();
        }
        public override void OperationStarted() 
        { 
            if(trackOperations)
                Interlocked.Increment(ref operationCount); 
        }
        public override void OperationCompleted() 
        { 
            if (trackOperations && Interlocked.Decrement(ref operationCount) == 0) 
                Complete(); 
        }
    }
    
    internal sealed class MaxConcurrencySynchronizationContext(int maxConcurrencyLevel) : SynchronizationContext
    {
        readonly SemaphoreSlim semaphore = new(maxConcurrencyLevel);

        public override void Post(SendOrPostCallback d, object state) =>
            semaphore.WaitAsync()
                .ContinueWith(delegate
                {
                    try
                    {
                        d(state);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        public override void Send(SendOrPostCallback d, object state)
        {
            semaphore.Wait();
            try
            {
                d(state);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }

    public static class SyncContextExtensions
    {
        public static Task SendAsync(this SynchronizationContext context, SendOrPostCallback d, object state) 
        { 
            var tcs = new TaskCompletionSource<bool>(); 
            context.Post(delegate {
                try
                {
                    d(state);
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                } 
            }, null); 
            
            return tcs.Task; 
        }
    }
    
    #endregion
    
    #region TaskScheduler
    
    //TaskScheduler --> QueueTask --> later invoke the task with the method ExecuteTask
    //TaskScheduler.Default --> ThreadPool
    //TaskScheduler.Current --> the task scheduler associated to the Task, it decides where and when the Task will bi executed
    //TaskScheduler.FromCurrentSynchronizationContext --> creates a new task schduler that queues Tasks to run on SynchronizationContext.Current, using the Post method

    internal sealed class TestScheduler : TaskScheduler
    {
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            throw new NotImplementedException();
        }

        protected override void QueueTask(Task task)
        {
            throw new NotImplementedException();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            throw new NotImplementedException();
        }
    }

    #endregion