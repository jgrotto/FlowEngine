# FlowEngine Coding Standards

## File Organization

### One Type Per File Rule

**Rule**: Each source file should contain exactly one public type (class, interface, enum, record, or struct).

#### ✅ **Allowed**:
```csharp
// ✅ IArrayRowFactory.cs
public interface IArrayRowFactory
{
    IArrayRow CreateRow(ISchema schema, object[] values);
}

// ✅ ArrayRowFactory.cs  
public class ArrayRowFactory : IArrayRowFactory
{
    public IArrayRow CreateRow(ISchema schema, object[] values) => 
        new ArrayRow(schema, values);
}

// ✅ PluginState.cs
public enum PluginState
{
    Created,
    Initialized,
    Running,
    Stopped,
    Disposed
}
```

#### ❌ **Not Allowed**:
```csharp
// ❌ PluginTypes.cs - Multiple public types
public enum PluginState { /* ... */ }
public enum PluginIsolationLevel { /* ... */ }
public class ValidationResult { /* ... */ }
public class PluginExecutionResult { /* ... */ }
// ... 8 more types
```

#### 🔶 **Exceptions Allowed**:
```csharp
// 🔶 ValidationResult.cs - Tightly coupled helper types
public class ValidationResult
{
    public ValidationError[] Errors { get; set; }
    // ...
}

// Small, tightly coupled helper - allowed in same file
internal class ValidationError
{
    public string Code { get; set; }
    public string Message { get; set; }
}
```

### File Naming Conventions

1. **File name must exactly match the public type name**
   - `IArrayRowFactory` → `IArrayRowFactory.cs`
   - `ArrayRowFactory` → `ArrayRowFactory.cs`
   - `PluginState` → `PluginState.cs`

2. **Use PascalCase for all file names**
   - ✅ `PipelineExecutor.cs`
   - ❌ `pipelineExecutor.cs`
   - ❌ `pipeline_executor.cs`

3. **Interfaces prefixed with 'I'**
   - ✅ `IPluginService.cs`
   - ❌ `PluginServiceInterface.cs`

### Directory Structure

Organize related types into logical folders:

```
src/FlowEngine.Abstractions/
├── Data/
│   ├── IArrayRow.cs
│   ├── IChunk.cs
│   ├── IDataset.cs
│   └── ISchema.cs
├── Plugins/
│   ├── IPlugin.cs
│   ├── IPluginService.cs
│   ├── IPluginValidator.cs
│   └── Results/           # Group result types
│       ├── ValidationResult.cs
│       ├── ExecutionResult.cs
│       └── CompatibilityResult.cs
├── Enums/                 # Group related enums
│   ├── PluginState.cs
│   ├── ValidationSeverity.cs
│   └── IsolationLevel.cs
└── Exceptions/            # Group exception types
    ├── PluginException.cs
    ├── ValidationException.cs
    └── ExecutionException.cs
```

## Implementation Guidelines

### Phase 1: New Code (Immediate)
- **All new types must follow one-type-per-file rule**
- Use the directory structure above for new types
- File templates should enforce this rule

### Phase 2: Existing Code Refactoring
**Priority Order for Refactoring:**
1. `PluginTypes.cs` (12 types) - **Highest Impact**
2. `IPipelineExecutor.cs` (8 types)
3. Configuration files (8-10 types each)
4. Remaining multi-type files

### Phase 3: Enforcement
- Add custom analyzer rules to detect violations
- CI/CD pipeline checks for compliance
- Code review guidelines

## Benefits

### Developer Experience
- **Predictable Navigation**: `IArrayRowFactory` is always in `IArrayRowFactory.cs`
- **Faster Search**: `grep`, IDE search, Go-to-Definition work reliably
- **Better IntelliSense**: IDE can index and suggest types more efficiently

### Code Quality
- **Single Responsibility**: Each file has one clear purpose
- **Easier Refactoring**: Moving types between projects is straightforward
- **Reduced Merge Conflicts**: Changes to different types don't conflict
- **Clearer Dependencies**: File imports reflect actual type usage

### Team Productivity
- **Faster Onboarding**: New developers can navigate intuitively
- **Consistent Codebase**: All projects follow the same organization
- **Better Code Reviews**: Reviewers can focus on single concepts

## Migration Strategy

### For Existing Multi-Type Files

1. **Identify Logical Groupings**
   ```
   PluginTypes.cs (12 types) →
   ├── Enums/PluginState.cs
   ├── Enums/IsolationLevel.cs
   ├── Results/ValidationResult.cs
   ├── Results/ExecutionResult.cs
   └── Exceptions/PluginException.cs
   ```

2. **Create New Files**
   - Extract each type to its own file
   - Maintain namespace consistency
   - Add proper XML documentation

3. **Update References**
   - Add `using` statements where needed
   - Update project files if necessary
   - Test all references resolve correctly

4. **Remove Original File**
   - Only after all references are updated
   - Ensure no breaking changes

### Backward Compatibility
- Use `using` aliases if needed for gradual migration
- Keep public API surface unchanged
- Coordinate with team for large refactoring

## Tools and Automation

### EditorConfig
- Consistent formatting and naming
- File organization preferences
- Already configured in `.editorconfig`

### Custom Analyzers (Future)
```csharp
// Example analyzer rule
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class OneTypePerFileAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        "FE0001",
        "Multiple public types in single file",
        "File '{0}' contains {1} public types. Split into separate files.",
        "Organization",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
```

### File Templates
Create Visual Studio/Rider templates that:
- Generate single-type files
- Use correct naming conventions
- Include proper XML documentation
- Follow namespace patterns

---

**Last Updated**: 2025-01-05  
**Next Review**: When completing Phase 2 refactoring