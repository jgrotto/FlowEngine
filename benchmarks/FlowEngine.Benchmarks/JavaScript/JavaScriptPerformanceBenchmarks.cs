using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using FlowEngine.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Benchmarks.JavaScript;

/// <summary>
/// Benchmarks for JavaScript engine performance with Jint 4.3.0 optimizations
/// Tests compilation and caching efficiency
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class JavaScriptPerformanceBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithId("Jint 4.3.0 Optimized"));
        }
    }

    private JintScriptEngineService? _scriptEngine;

    private const string SimpleJavaScript = @"
        function process(context) {
            var customer = context.input.current();
            var fullName = customer.first_name + ' ' + customer.last_name;
            context.output.setField('full_name', fullName);
            return true;
        }";

    private const string ComplexJavaScript = @"
        function process(context) {
            try {
                var customer = context.input.current();
                
                if (!customer.first_name || !customer.last_name) {
                    return false;
                }
                
                var fullName = customer.first_name + ' ' + customer.last_name;
                var age = parseInt(customer.age);
                var ageCategory = 'Unknown';
                
                if (age < 18) ageCategory = 'Minor';
                else if (age < 25) ageCategory = 'Young Adult';
                else if (age < 35) ageCategory = 'Adult';
                else if (age < 45) ageCategory = 'Mid Adult';
                else if (age < 55) ageCategory = 'Mature Adult';
                else if (age < 65) ageCategory = 'Pre-Senior';
                else ageCategory = 'Senior';
                
                var emailDomain = 'unknown';
                if (customer.email && customer.email.indexOf('@') > 0) {
                    var parts = customer.email.split('@');
                    emailDomain = parts[1].toLowerCase();
                }
                
                var accountValue = parseFloat(customer.account_value || 0);
                var valueCategory = 'Basic';
                if (accountValue >= 500000) valueCategory = 'Platinum';
                else if (accountValue >= 250000) valueCategory = 'Diamond';
                else if (accountValue >= 100000) valueCategory = 'Premium';
                else if (accountValue >= 50000) valueCategory = 'Gold';
                else if (accountValue >= 25000) valueCategory = 'Silver';
                else if (accountValue >= 10000) valueCategory = 'Bronze';
                
                var priorityScore = 0;
                if (valueCategory === 'Platinum') priorityScore += 100;
                else if (valueCategory === 'Diamond') priorityScore += 80;
                else if (valueCategory === 'Premium') priorityScore += 60;
                else if (valueCategory === 'Gold') priorityScore += 40;
                else if (valueCategory === 'Silver') priorityScore += 25;
                else if (valueCategory === 'Bronze') priorityScore += 15;
                else priorityScore += 5;
                
                if (ageCategory === 'Adult' || ageCategory === 'Mid Adult' || ageCategory === 'Mature Adult') {
                    priorityScore += 30;
                } else if (ageCategory === 'Young Adult' || ageCategory === 'Pre-Senior') {
                    priorityScore += 20;
                } else {
                    priorityScore += 10;
                }
                
                var businessDomains = ['gmail.com', 'yahoo.com', 'outlook.com', 'hotmail.com'];
                var enterpriseDomains = ['enterprise.com', 'company.com', 'corp.com'];
                
                if (enterpriseDomains.indexOf(emailDomain) >= 0) {
                    priorityScore += 25;
                } else if (businessDomains.indexOf(emailDomain) >= 0) {
                    priorityScore += 15;
                } else {
                    priorityScore += 5;
                }
                
                var hashInput = fullName + age + emailDomain + accountValue + valueCategory;
                var complexityHash = '';
                for (var i = 0; i < hashInput.length; i++) {
                    var char = hashInput.charCodeAt(i);
                    complexityHash += (char * 17 + i * 3).toString(16);
                }
                complexityHash = complexityHash.substring(0, 16).toUpperCase();
                
                context.output.setField('customer_id', customer.customer_id);
                context.output.setField('full_name', fullName);
                context.output.setField('first_name', customer.first_name);
                context.output.setField('last_name', customer.last_name);
                context.output.setField('age', age);
                context.output.setField('age_category', ageCategory);
                context.output.setField('email', customer.email);
                context.output.setField('email_domain', emailDomain);
                context.output.setField('account_value', accountValue);
                context.output.setField('value_category', valueCategory);
                context.output.setField('priority_score', priorityScore);
                context.output.setField('processed_date', new Date().toISOString());
                context.output.setField('complexity_hash', complexityHash);
                
                return true;
            } catch (error) {
                context.utils.log('error', 'Transform error: ' + error.message);
                return false;
            }
        }";

    [GlobalSetup]
    public void Setup()
    {
        var logger = NullLogger<JintScriptEngineService>.Instance;
        _scriptEngine = new JintScriptEngineService(logger);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scriptEngine?.Dispose();
    }

    [Benchmark(Description = "Script Compilation - Simple JavaScript")]
    public async Task<CompiledScript> CompileSimpleScript()
    {
        var options = new ScriptOptions { EnableCaching = false }; // Force recompilation
        return await _scriptEngine!.CompileAsync(SimpleJavaScript, options);
    }

    [Benchmark(Description = "Script Compilation - Complex JavaScript")]
    public async Task<CompiledScript> CompileComplexScript()
    {
        var options = new ScriptOptions { EnableCaching = false }; // Force recompilation
        return await _scriptEngine!.CompileAsync(ComplexJavaScript, options);
    }

    [Benchmark(Description = "Script Compilation - Simple with Caching")]
    public async Task<CompiledScript> CompileSimpleScriptWithCaching()
    {
        var options = new ScriptOptions { EnableCaching = true };
        return await _scriptEngine!.CompileAsync(SimpleJavaScript, options);
    }

    [Benchmark(Description = "Script Compilation - Complex with Caching")]
    public async Task<CompiledScript> CompileComplexScriptWithCaching()
    {
        var options = new ScriptOptions { EnableCaching = true };
        return await _scriptEngine!.CompileAsync(ComplexJavaScript, options);
    }

    [Params(1, 10, 100)]
    public int CompilationBatch { get; set; }

    [Benchmark(Description = "Batch Script Compilations")]
    public async Task BatchScriptCompilations()
    {
        var options = new ScriptOptions { EnableCaching = true };
        
        for (int i = 0; i < CompilationBatch; i++)
        {
            var script = await _scriptEngine!.CompileAsync(ComplexJavaScript, options);
            script.Dispose();
        }
    }

    [Benchmark(Description = "Engine Statistics Collection")]
    public ScriptEngineStats GetEngineStatistics()
    {
        return _scriptEngine!.GetStats();
    }
}

/// <summary>
/// Performance test focused on Jint 4.3.0 optimization validation
/// </summary>
[Config(typeof(ValidationConfig))]
[MemoryDiagnoser]
public class JavaScriptOptimizationValidation
{
    private class ValidationConfig : ManualConfig
    {
        public ValidationConfig()
        {
            AddJob(Job.Default.WithId("Jint 4.3.0 Validation"));
        }
    }

    private JintScriptEngineService? _scriptEngine;
    private List<CompiledScript> _cachedScripts = new();

    [GlobalSetup]
    public async Task Setup()
    {
        var logger = NullLogger<JintScriptEngineService>.Instance;
        _scriptEngine = new JintScriptEngineService(logger);

        // Pre-compile scripts for cache testing
        var options = new ScriptOptions { EnableCaching = true };
        var testScript = @"
            function process(context) {
                var x = Math.random() * 100;
                var y = Math.floor(x);
                return y > 50;
            }";

        // Create multiple scripts to fill cache
        for (int i = 0; i < 50; i++)
        {
            var script = testScript.Replace("100", (100 + i).ToString());
            var compiled = await _scriptEngine.CompileAsync(script, options);
            _cachedScripts.Add(compiled);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var script in _cachedScripts)
        {
            script.Dispose();
        }
        _cachedScripts.Clear();
        _scriptEngine?.Dispose();
    }

    [Benchmark(Description = "Cache Hit Rate Validation")]
    public async Task<CompiledScript> CacheHitRateTest()
    {
        var options = new ScriptOptions { EnableCaching = true };
        var cachedScript = @"
            function process(context) {
                var x = Math.random() * 100;
                var y = Math.floor(x);
                return y > 50;
            }";
        
        // This should hit cache after first compilation
        return await _scriptEngine!.CompileAsync(cachedScript, options);
    }

    [Benchmark(Description = "Script Engine Statistics Overhead")]
    public (ScriptEngineStats stats, int cacheHits, double utilizationRate) StatsCollectionOverhead()
    {
        var stats = _scriptEngine!.GetStats();
        return (stats, stats.CacheHits, stats.AstUtilizationRate);
    }
}