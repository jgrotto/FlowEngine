using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;
using FlowEngine.Benchmarks.DataStructures;

namespace FlowEngine.Benchmarks.Memory;

/// <summary>
/// Simple object for pooling performance tests
/// </summary>
public class SimpleTestObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    
    public void Reset()
    {
        Id = 0;
        Name = "";
        CreatedAt = default;
        Data.Clear();
    }
    
    public void Initialize(int id, string name)
    {
        Id = id;
        Name = name;
        CreatedAt = DateTime.UtcNow;
        Data["initialized"] = true;
    }
}

/// <summary>
/// Object pool policy for SimpleTestObject
/// </summary>
public class SimpleTestObjectPolicy : IPooledObjectPolicy<SimpleTestObject>
{
    public SimpleTestObject Create() => new SimpleTestObject();
    
    public bool Return(SimpleTestObject obj)
    {
        obj.Reset();
        return true;
    }
}

/// <summary>
/// Custom object pool implementation for comparison with Microsoft.Extensions.ObjectPool
/// </summary>
public class CustomObjectPool<T> where T : class, new()
{
    private readonly ConcurrentBag<T> _objects = new();
    private readonly Func<T, bool>? _resetPolicy;
    private int _currentCount;
    private readonly int _maxSize;
    
    public CustomObjectPool(int maxSize = 100, Func<T, bool>? resetPolicy = null)
    {
        _maxSize = maxSize;
        _resetPolicy = resetPolicy;
    }
    
    public T Get()
    {
        if (_objects.TryTake(out var item))
        {
            Interlocked.Decrement(ref _currentCount);
            return item;
        }
        
        return new T();
    }
    
    public void Return(T item)
    {
        if (item == null) return;
        
        // Apply reset policy if provided
        if (_resetPolicy != null && !_resetPolicy(item))
            return;
        
        // Don't exceed max size
        if (_currentCount < _maxSize)
        {
            _objects.Add(item);
            Interlocked.Increment(ref _currentCount);
        }
    }
    
    public int CurrentCount => _currentCount;
}

/// <summary>
/// Thread-safe simple object pool using a stack
/// </summary>
public class StackObjectPool<T> where T : class, new()
{
    private readonly ConcurrentStack<T> _stack = new();
    private readonly Action<T>? _resetAction;
    private readonly int _maxSize;
    private int _currentCount;
    
    public StackObjectPool(int maxSize = 100, Action<T>? resetAction = null)
    {
        _maxSize = maxSize;
        _resetAction = resetAction;
    }
    
    public T Get()
    {
        if (_stack.TryPop(out var item))
        {
            Interlocked.Decrement(ref _currentCount);
            return item;
        }
        
        return new T();
    }
    
    public void Return(T item)
    {
        if (item == null) return;
        
        _resetAction?.Invoke(item);
        
        if (_currentCount < _maxSize)
        {
            _stack.Push(item);
            Interlocked.Increment(ref _currentCount);
        }
    }
    
    public int CurrentCount => _currentCount;
}

/// <summary>
/// Benchmarks comparing different object pooling strategies
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class PoolingBenchmarks
{
    private ObjectPool<SimpleTestObject> _microsoftPool = null!;
    private CustomObjectPool<SimpleTestObject> _customPool = null!;
    private StackObjectPool<SimpleTestObject> _stackPool = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        var provider = new DefaultObjectPoolProvider();
        _microsoftPool = provider.Create(new SimpleTestObjectPolicy());
        
        _customPool = new CustomObjectPool<SimpleTestObject>(
            maxSize: 100,
            resetPolicy: obj => { obj.Reset(); return true; }
        );
        
        _stackPool = new StackObjectPool<SimpleTestObject>(
            maxSize: 100,
            resetAction: obj => obj.Reset()
        );
    }

    [Benchmark(Baseline = true)]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public void NoPooling(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            var obj = new SimpleTestObject();
            obj.Initialize(i, $"object-{i}");
            
            // Simulate some work
            obj.Data["processed"] = DateTime.UtcNow;
            
            // Object goes out of scope and gets GC'd
        }
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public void MicrosoftObjectPool(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            var obj = _microsoftPool.Get();
            try
            {
                obj.Initialize(i, $"object-{i}");
                
                // Simulate some work
                obj.Data["processed"] = DateTime.UtcNow;
            }
            finally
            {
                _microsoftPool.Return(obj);
            }
        }
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public void CustomBagPool(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            var obj = _customPool.Get();
            try
            {
                obj.Initialize(i, $"object-{i}");
                
                // Simulate some work
                obj.Data["processed"] = DateTime.UtcNow;
            }
            finally
            {
                _customPool.Return(obj);
            }
        }
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public void CustomStackPool(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            var obj = _stackPool.Get();
            try
            {
                obj.Initialize(i, $"object-{i}");
                
                // Simulate some work
                obj.Data["processed"] = DateTime.UtcNow;
            }
            finally
            {
                _stackPool.Return(obj);
            }
        }
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public void PooledRowLifecycle(int iterations)
    {
        // Test our actual PooledRow implementation
        for (int i = 0; i < iterations; i++)
        {
            var data = new[]
            {
                new KeyValuePair<string, object?>("id", i),
                new KeyValuePair<string, object?>("name", $"test-{i}"),
                new KeyValuePair<string, object?>("processed", DateTime.UtcNow)
            };
            
            using var row = PooledRow.Create(data);
            
            // Simulate some work
            var id = row["id"];
            var name = row["name"];
            
            // Row is automatically disposed when going out of scope
        }
    }
}

/// <summary>
/// Concurrent pooling benchmarks to test thread safety and contention
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class ConcurrentPoolingBenchmarks
{
    private ObjectPool<SimpleTestObject> _microsoftPool = null!;
    private CustomObjectPool<SimpleTestObject> _customPool = null!;
    private StackObjectPool<SimpleTestObject> _stackPool = null!;
    
    [Params(2, 4, 8)]
    public int ThreadCount { get; set; }
    
    [Params(1000, 10000)]
    public int OperationsPerThread { get; set; }
    
    [GlobalSetup]
    public void Setup()
    {
        var provider = new DefaultObjectPoolProvider();
        _microsoftPool = provider.Create(new SimpleTestObjectPolicy());
        
        _customPool = new CustomObjectPool<SimpleTestObject>(
            maxSize: ThreadCount * 10, // Larger pool for concurrent access
            resetPolicy: obj => { obj.Reset(); return true; }
        );
        
        _stackPool = new StackObjectPool<SimpleTestObject>(
            maxSize: ThreadCount * 10,
            resetAction: obj => obj.Reset()
        );
    }

    [Benchmark(Baseline = true)]
    public async Task ConcurrentNoPooling()
    {
        var tasks = new Task[ThreadCount];
        
        for (int t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var obj = new SimpleTestObject();
                    obj.Initialize(i, $"thread-object-{i}");
                    obj.Data["processed"] = DateTime.UtcNow;
                }
            });
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task ConcurrentMicrosoftPool()
    {
        var tasks = new Task[ThreadCount];
        
        for (int t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var obj = _microsoftPool.Get();
                    try
                    {
                        obj.Initialize(i, $"thread-object-{i}");
                        obj.Data["processed"] = DateTime.UtcNow;
                    }
                    finally
                    {
                        _microsoftPool.Return(obj);
                    }
                }
            });
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task ConcurrentCustomBagPool()
    {
        var tasks = new Task[ThreadCount];
        
        for (int t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var obj = _customPool.Get();
                    try
                    {
                        obj.Initialize(i, $"thread-object-{i}");
                        obj.Data["processed"] = DateTime.UtcNow;
                    }
                    finally
                    {
                        _customPool.Return(obj);
                    }
                }
            });
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task ConcurrentStackPool()
    {
        var tasks = new Task[ThreadCount];
        
        for (int t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var obj = _stackPool.Get();
                    try
                    {
                        obj.Initialize(i, $"thread-object-{i}");
                        obj.Data["processed"] = DateTime.UtcNow;
                    }
                    finally
                    {
                        _stackPool.Return(obj);
                    }
                }
            });
        }
        
        await Task.WhenAll(tasks);
    }
}

/// <summary>
/// Benchmarks for different pooling scenarios specific to FlowEngine
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class FlowEnginePoolingBenchmarks
{
    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)]
    public void ArrayPoolingPattern(int iterations)
    {
        // Test ArrayPool pattern for value type arrays
        var pool = System.Buffers.ArrayPool<int>.Shared;
        
        for (int i = 0; i < iterations; i++)
        {
            var array = pool.Rent(100);
            try
            {
                // Simulate processing
                for (int j = 0; j < 100; j++)
                {
                    array[j] = j * i;
                }
                
                // Simulate some work with the array
                var sum = array.Take(100).Sum();
            }
            finally
            {
                pool.Return(array, clearArray: true);
            }
        }
    }

    [Benchmark]  
    [Arguments(1000)]
    [Arguments(10000)]
    public void StringBuilderPooling(int iterations)
    {
        // Test StringBuilder pooling (common pattern for string operations)
        var pool = new DefaultObjectPoolProvider().CreateStringBuilderPool();
        
        for (int i = 0; i < iterations; i++)
        {
            var sb = pool.Get();
            try
            {
                sb.Append("Processing item ");
                sb.Append(i);
                sb.Append(" with data: ");
                sb.Append(DateTime.UtcNow.Ticks);
                
                var result = sb.ToString();
            }
            finally
            {
                pool.Return(sb);
            }
        }
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)] 
    public void DictionaryPooling(int iterations)
    {
        // Test pooling of Dictionary objects (relevant for Row implementations)
        var pool = new CustomObjectPool<Dictionary<string, object?>>(
            maxSize: 50,
            resetPolicy: dict => { dict.Clear(); return true; }
        );
        
        for (int i = 0; i < iterations; i++)
        {
            var dict = pool.Get();
            try
            {
                dict["id"] = i;
                dict["name"] = $"item-{i}";
                dict["timestamp"] = DateTime.UtcNow;
                dict["data"] = new string('x', 100);
                
                // Simulate access
                var id = dict["id"];
                var name = dict["name"];
            }
            finally
            {
                pool.Return(dict);
            }
        }
    }
}