using FlowEngine.Core.Configuration;
using System.Text.Json;
using Xunit;

namespace FlowEngine.Core.Tests.Configuration;

public class GlobalVariablesTests
{
    [Fact]
    public void SetVariable_WithValidInput_StoresValue()
    {
        // Arrange
        var variables = new GlobalVariables();
        const string name = "TestVar";
        const string value = "TestValue";

        // Act
        variables.SetVariable(name, value);

        // Assert
        Assert.Equal(value, variables.GetVariable<string>(name));
    }

    [Fact]
    public void SetVariable_WithDifferentTypes_StoresCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act
        variables.SetVariable("StringVar", "test");
        variables.SetVariable("IntVar", 42);
        variables.SetVariable("BoolVar", true);
        variables.SetVariable("DateVar", new DateTime(2025, 1, 17));

        // Assert
        Assert.Equal("test", variables.GetVariable<string>("StringVar"));
        Assert.Equal(42, variables.GetVariable<int>("IntVar"));
        Assert.True(variables.GetVariable<bool>("BoolVar"));
        Assert.Equal(new DateTime(2025, 1, 17), variables.GetVariable<DateTime>("DateVar"));
    }

    [Fact]
    public void SetVariable_WhenReadOnly_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.MakeReadOnly();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            variables.SetVariable("Test", "Value"));
        
        Assert.Contains("read-only", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123InvalidStart")]
    [InlineData("Invalid-Name")]
    [InlineData("Invalid Name")]
    public void SetVariable_WithInvalidName_ThrowsException(string invalidName)
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            variables.SetVariable(invalidName, "value"));
    }

    [Theory]
    [InlineData("ValidName")]
    [InlineData("_ValidName")]
    [InlineData("Valid_Name_123")]
    [InlineData("VALID_NAME")]
    public void SetVariable_WithValidName_Succeeds(string validName)
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert (should not throw)
        variables.SetVariable(validName, "value");
        Assert.Equal("value", variables.GetVariable<string>(validName));
    }

    [Fact]
    public void GetVariable_WithExistingVariable_ReturnsValue()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Test", 42);

        // Act
        var result = variables.GetVariable<int>("Test");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetVariable_WithTypeConversion_ConvertsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("IntAsString", 42);

        // Act
        var result = variables.GetVariable<string>("IntAsString");

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public void GetVariable_WithNonExistentVariable_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(() =>
            variables.GetVariable<string>("NonExistent"));
        
        Assert.Contains("NonExistent", exception.Message);
    }

    [Fact]
    public void GetVariable_WithInvalidTypeConversion_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("StringVar", "NotANumber");

        // Act & Assert
        Assert.Throws<InvalidCastException>(() =>
            variables.GetVariable<int>("StringVar"));
    }

    [Fact]
    public void SetComputedVariable_EvaluatesLazily()
    {
        // Arrange
        var variables = new GlobalVariables();
        var callCount = 0;
        variables.SetComputedVariable("LazyVar", () =>
        {
            callCount++;
            return "computed";
        });

        // Act - function should not be called yet
        Assert.Equal(0, callCount);

        // Act - function should be called when accessed
        var result = variables.GetVariable<string>("LazyVar");

        // Assert
        Assert.Equal(1, callCount);
        Assert.Equal("computed", result);
    }

    [Fact]
    public void SetComputedVariable_EvaluatesEachTime()
    {
        // Arrange
        var variables = new GlobalVariables();
        var counter = 0;
        variables.SetComputedVariable("Counter", () => ++counter);

        // Act
        var result1 = variables.GetVariable<int>("Counter");
        var result2 = variables.GetVariable<int>("Counter");

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
    }

    [Fact]
    public void SetComputedVariable_WhenReadOnly_StillAllowed()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.MakeReadOnly();

        // Act & Assert (should not throw)
        variables.SetComputedVariable("ComputedVar", () => "value");
        Assert.Equal("value", variables.GetVariable<string>("ComputedVar"));
    }

    [Fact]
    public void SetComputedVariable_WithNullFactory_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            variables.SetComputedVariable("Test", null!));
    }

    [Fact]
    public void HasVariable_WithExistingStaticVariable_ReturnsTrue()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Test", "value");

        // Act & Assert
        Assert.True(variables.HasVariable("Test"));
    }

    [Fact]
    public void HasVariable_WithExistingComputedVariable_ReturnsTrue()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetComputedVariable("Test", () => "value");

        // Act & Assert
        Assert.True(variables.HasVariable("Test"));
    }

    [Fact]
    public void HasVariable_WithNonExistentVariable_ReturnsFalse()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        Assert.False(variables.HasVariable("NonExistent"));
    }

    [Fact]
    public void GetAllVariables_ReturnsAllStaticAndComputedVariables()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Static1", "value1");
        variables.SetVariable("Static2", 42);
        variables.SetComputedVariable("Computed1", () => "computed");

        // Act
        var allVariables = variables.GetAllVariables();

        // Assert
        Assert.Equal(3, allVariables.Count);
        Assert.Equal("value1", allVariables["Static1"]);
        Assert.Equal(42, allVariables["Static2"]);
        Assert.Equal("computed", allVariables["Computed1"]);
    }

    [Fact]
    public void GetAllVariables_WithComputedVariableError_IncludesErrorMessage()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetComputedVariable("ErrorVar", () => throw new Exception("Test error"));

        // Act
        var allVariables = variables.GetAllVariables();

        // Assert
        Assert.Single(allVariables);
        Assert.Contains("Error: Test error", allVariables["ErrorVar"].ToString());
    }

    [Theory]
    [InlineData("Name=Value", "Name", "Value")]
    [InlineData("IntVar=42", "IntVar", 42)]
    [InlineData("BoolVar=true", "BoolVar", true)]
    [InlineData("FloatVar=3.14", "FloatVar", 3.14)]
    public void SetVariableFromString_WithValidFormat_ParsesCorrectly(string assignment, string expectedName, object expectedValue)
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act
        variables.SetVariableFromString(assignment);

        // Assert
        var actualValue = variables.GetVariableValue(expectedName);
        Assert.Equal(expectedValue, actualValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoEquals")]
    [InlineData("=NoName")]
    [InlineData("NoValue=")]
    public void SetVariableFromString_WithInvalidFormat_ThrowsException(string invalidAssignment)
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            variables.SetVariableFromString(invalidAssignment));
    }

    [Fact]
    public void SetVariableFromString_WithComplexString_HandlesCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act
        variables.SetVariableFromString("Path=C:\\Program Files\\App");

        // Assert
        Assert.Equal("C:\\Program Files\\App", variables.GetVariable<string>("Path"));
    }

    [Fact]
    public void SetVariableFromString_WithDateTime_ParsesCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act
        variables.SetVariableFromString("Date=2025-01-17");

        // Assert
        var result = variables.GetVariable<DateTime>("Date");
        Assert.Equal(new DateTime(2025, 1, 17), result);
    }

    [Fact]
    public void SetVariableFromEnvironment_WithExistingVariable_SetsValue()
    {
        // Arrange
        var variables = new GlobalVariables();
        Environment.SetEnvironmentVariable("TEST_VAR", "TestValue");

        try
        {
            // Act
            variables.SetVariableFromEnvironment("TEST_VAR");

            // Assert
            Assert.Equal("TestValue", variables.GetVariable<string>("TEST_VAR"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_VAR", null);
        }
    }

    [Fact]
    public void SetVariableFromEnvironment_WithNonExistentVariable_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            variables.SetVariableFromEnvironment("NON_EXISTENT_VAR"));
        
        Assert.Contains("NON_EXISTENT_VAR", exception.Message);
    }

    [Fact]
    public void LoadFromFile_WithValidJson_LoadsVariables()
    {
        // Arrange
        var variables = new GlobalVariables();
        var tempFilePath = Path.GetTempFileName();
        var json = """
            {
                "StringVar": "test",
                "IntVar": 42,
                "BoolVar": true,
                "ArrayVar": [1, 2, 3]
            }
            """;
        File.WriteAllText(tempFilePath, json);

        try
        {
            // Act
            variables.LoadFromFile(tempFilePath);

            // Assert
            Assert.Equal("test", variables.GetVariable<string>("StringVar"));
            Assert.Equal(42, variables.GetVariable<int>("IntVar"));
            Assert.True(variables.GetVariable<bool>("BoolVar"));
            
            var arrayVar = variables.GetVariableValue("ArrayVar") as object[];
            Assert.NotNull(arrayVar);
            Assert.Equal(3, arrayVar.Length);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void LoadFromFile_WithNonExistentFile_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() =>
            variables.LoadFromFile("NonExistentFile.json"));
    }

    [Fact]
    public void LoadFromFile_WithInvalidJson_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();
        var tempFilePath = Path.GetTempFileName();
        File.WriteAllText(tempFilePath, "{ invalid json");

        try
        {
            // Act & Assert
            Assert.Throws<JsonException>(() =>
                variables.LoadFromFile(tempFilePath));
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void LoadFromFile_WithEmptyJson_DoesNotThrow()
    {
        // Arrange
        var variables = new GlobalVariables();
        var tempFilePath = Path.GetTempFileName();
        File.WriteAllText(tempFilePath, "{}");

        try
        {
            // Act & Assert (should not throw)
            variables.LoadFromFile(tempFilePath);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void MakeReadOnly_SetsReadOnlyFlag()
    {
        // Arrange
        var variables = new GlobalVariables();
        Assert.False(variables.IsReadOnly);

        // Act
        variables.MakeReadOnly();

        // Assert
        Assert.True(variables.IsReadOnly);
    }

    [Fact]
    public void IsReadOnly_InitiallyFalse()
    {
        // Arrange & Act
        var variables = new GlobalVariables();

        // Assert
        Assert.False(variables.IsReadOnly);
    }
}

public class VariableSubstitutionEngineTests
{
    private readonly GlobalVariables _variables;

    public VariableSubstitutionEngineTests()
    {
        _variables = new GlobalVariables();
        _variables.SetVariable("Name", "John");
        _variables.SetVariable("Age", 25);
        _variables.SetVariable("Date", new DateTime(2025, 1, 17));
        _variables.SetVariable("FilePath", @"C:\Data\file.csv");
        _variables.SetVariable("Amount", 1234.56);
    }

    [Fact]
    public void Substitute_WithSimpleVariable_ReturnsSubstituted()
    {
        // Act
        var result = VariableSubstitutionEngine.Substitute("Hello {Name}", _variables);

        // Assert
        Assert.Equal("Hello John", result);
    }

    [Fact]
    public void Substitute_WithMultipleVariables_SubstitutesAll()
    {
        // Act
        var result = VariableSubstitutionEngine.Substitute("Hello {Name}, you are {Age} years old", _variables);

        // Assert
        Assert.Equal("Hello John, you are 25 years old", result);
    }

    [Fact]
    public void Substitute_WithDateTimeFormat_FormatsCorrectly()
    {
        // Act
        var result = VariableSubstitutionEngine.Substitute("Date: {Date:yyyy-MM-dd}", _variables);

        // Assert
        Assert.Equal("Date: 2025-01-17", result);
    }

    [Fact]
    public void Substitute_WithNumericFormat_FormatsCorrectly()
    {
        // Act
        var result = VariableSubstitutionEngine.Substitute("Amount: {Amount:F2}", _variables);

        // Assert
        Assert.Equal("Amount: 1234.56", result);
    }

    [Theory]
    [InlineData("filename", "file.csv")]
    [InlineData("filenamewithoutext", "file")]
    [InlineData("extension", ".csv")]
    [InlineData("upper", "C:\\DATA\\FILE.CSV")]
    [InlineData("lower", "c:\\data\\file.csv")]
    public void Substitute_WithStringFormat_FormatsCorrectly(string format, string expected)
    {
        // Act
        var result = VariableSubstitutionEngine.Substitute($"{{FilePath:{format}}}", _variables);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Substitute_WithUndefinedVariable_ThrowsException()
    {
        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(() =>
            VariableSubstitutionEngine.Substitute("Hello {UndefinedVar}", _variables));
        
        Assert.Contains("UndefinedVar", exception.Message);
    }

    [Fact]
    public void Substitute_WithNoVariables_ReturnsOriginal()
    {
        // Arrange
        const string input = "No variables here";

        // Act
        var result = VariableSubstitutionEngine.Substitute(input, _variables);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void Substitute_WithEmptyString_ReturnsEmpty()
    {
        // Act
        var result = VariableSubstitutionEngine.Substitute("", _variables);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Substitute_WithNestedBraces_HandlesCorrectly()
    {
        // Arrange
        _variables.SetVariable("JsonData", "{\"key\": \"value\"}");

        // Act
        var result = VariableSubstitutionEngine.Substitute("Data: {JsonData}", _variables);

        // Assert
        Assert.Equal("Data: {\"key\": \"value\"}", result);
    }

    [Fact]
    public void Substitute_WithComplexString_SubstitutesCorrectly()
    {
        // Act
        var input = "Processing file {FilePath:filename} for {Name} on {Date:yyyy-MM-dd} with amount {Amount:F2}";
        var result = VariableSubstitutionEngine.Substitute(input, _variables);

        // Assert
        var expected = "Processing file file.csv for John on 2025-01-17 with amount 1234.56";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Substitute_WithGlobalVariablesSubstituteVariables_WorksCorrectly()
    {
        // Act
        var result = _variables.SubstituteVariables("Hello {Name}, today is {Date:yyyy-MM-dd}");

        // Assert
        Assert.Equal("Hello John, today is 2025-01-17", result);
    }
}