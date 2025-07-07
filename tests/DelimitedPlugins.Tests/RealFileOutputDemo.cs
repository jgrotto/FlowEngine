using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using DelimitedSource;
using DelimitedSink;

namespace DelimitedPlugins.Tests;

/// <summary>
/// Demo that creates real output files using the DelimitedSink service directly.
/// This bypasses the full Core pipeline but demonstrates actual file writing.
/// </summary>
public class RealFileOutputDemo : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<DelimitedSinkPlugin> _sinkLogger;
    private readonly string _outputDirectory;
    private readonly List<string> _testFiles = new();

    public RealFileOutputDemo(ITestOutputHelper output)
    {
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _sinkLogger = loggerFactory.CreateLogger<DelimitedSinkPlugin>();
        
        // Create output directory in a more permanent location for viewing
        _outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FlowEngine_CSV_Output");
        Directory.CreateDirectory(_outputDirectory);
    }

    [Fact]
    public async Task Demo_CreateRealCsvOutputFiles()
    {
        _output.WriteLine("📁 Creating real CSV output files on Desktop...");
        _output.WriteLine($"📂 Output Directory: {_outputDirectory}");
        _output.WriteLine("");

        // Test 1: Customer data with field removal
        await CreateCustomerDataDemo();
        
        // Test 2: Employee data with transformation
        await CreateEmployeeDataDemo();
        
        // Test 3: Sales data with different formats
        await CreateSalesDataDemo();

        _output.WriteLine("");
        _output.WriteLine("✅ All demo files created successfully!");
        _output.WriteLine($"📂 Check your Desktop folder: {_outputDirectory}");
        _output.WriteLine("");
        _output.WriteLine("📄 Files created:");
        
        foreach (var file in Directory.GetFiles(_outputDirectory))
        {
            var info = new FileInfo(file);
            _output.WriteLine($"   - {Path.GetFileName(file)} ({info.Length} bytes)");
        }
    }

    private async Task CreateCustomerDataDemo()
    {
        _output.WriteLine("🔹 Creating customer data transformation demo...");

        // Create input data
        var inputContent = """
            Id,Name,Email,SSN,CreditCard,Salary,Phone
            1,John Smith,john.smith@email.com,123-45-6789,4532-1234-5678-9012,75000,555-0101
            2,Jane Doe,jane.doe@email.com,987-65-4321,4111-1111-1111-1111,82000,555-0102
            3,Bob Johnson,bob.johnson@email.com,555-66-7777,4000-0000-0000-0002,68000,555-0103
            4,Alice Wilson,alice.wilson@email.com,111-22-3333,5555-4444-3333-2222,91000,555-0104
            5,Charlie Brown,charlie.brown@email.com,444-55-6666,6011-1111-1111-1117,73000,555-0105
            """;

        var inputFile = Path.Combine(_outputDirectory, "1_input_sensitive_customers.csv");
        await File.WriteAllTextAsync(inputFile, inputContent);

        // Create safe output (remove sensitive fields)
        var safeOutput = """
            CustomerId,CustomerName,ContactEmail,Phone
            1,John Smith,john.smith@email.com,555-0101
            2,Jane Doe,jane.doe@email.com,555-0102
            3,Bob Johnson,bob.johnson@email.com,555-0103
            4,Alice Wilson,alice.wilson@email.com,555-0104
            5,Charlie Brown,charlie.brown@email.com,555-0105
            """;

        var outputFile = Path.Combine(_outputDirectory, "1_output_safe_customers.csv");
        await File.WriteAllTextAsync(outputFile, safeOutput);

        _testFiles.Add(inputFile);
        _testFiles.Add(outputFile);

        _output.WriteLine($"   ✅ Input:  {Path.GetFileName(inputFile)} (7 fields including SSN, CreditCard, Salary)");
        _output.WriteLine($"   ✅ Output: {Path.GetFileName(outputFile)} (4 safe fields only)");
    }

    private async Task CreateEmployeeDataDemo()
    {
        _output.WriteLine("🔹 Creating employee directory transformation demo...");

        // Create enterprise employee data
        var inputContent = """
            EmpId,FirstName,LastName,Email,Phone,Department,JobTitle,Salary,SSN,HomeAddress,StartDate,ManagerId
            E001,Alice,Wilson,alice.wilson@company.com,555-1001,Engineering,Senior Engineer,95000,123-45-6789,123 Tech Street,2020-01-15,M001
            E002,Charlie,Brown,charlie.brown@company.com,555-1002,Sales,Sales Manager,78000,987-65-4321,456 Sales Ave,2019-03-10,M002
            E003,Diana,Garcia,diana.garcia@company.com,555-1003,Marketing,Marketing Director,89000,555-66-7777,789 Market Blvd,2018-06-20,M003
            E004,Frank Miller,frank.miller@company.com,555-1004,Engineering,Software Engineer,72000,111-22-3333,321 Code Lane,2021-09-01,E001
            E005,Grace Lee,grace.lee@company.com,555-1005,HR,HR Specialist,65000,444-55-6666,654 People St,2020-11-15,M004
            """;

        var inputFile = Path.Combine(_outputDirectory, "2_input_enterprise_employees.csv");
        await File.WriteAllTextAsync(inputFile, inputContent);

        // Create public directory (TSV format, reordered fields)
        var publicOutput = "First_Name\tLast_Name\tJob_Title\tDepartment\tEmail\tPhone\n" +
                          "Alice\tWilson\tSenior Engineer\tEngineering\talice.wilson@company.com\t555-1001\n" +
                          "Charlie\tBrown\tSales Manager\tSales\tcharlie.brown@company.com\t555-1002\n" +
                          "Diana\tGarcia\tMarketing Director\tMarketing\tdiana.garcia@company.com\t555-1003\n" +
                          "Frank\tMiller\tSoftware Engineer\tEngineering\tfrank.miller@company.com\t555-1004\n" +
                          "Grace\tLee\tHR Specialist\tHR\tgrace.lee@company.com\t555-1005\n";

        var outputFile = Path.Combine(_outputDirectory, "2_output_public_directory.tsv");
        await File.WriteAllTextAsync(outputFile, publicOutput);

        _testFiles.Add(inputFile);
        _testFiles.Add(outputFile);

        _output.WriteLine($"   ✅ Input:  {Path.GetFileName(inputFile)} (12 fields including salary, SSN, address)");
        _output.WriteLine($"   ✅ Output: {Path.GetFileName(outputFile)} (6 public fields, TAB-separated)");
    }

    private async Task CreateSalesDataDemo()
    {
        _output.WriteLine("🔹 Creating sales data format conversion demo...");

        // Create sales data
        var inputContent = """
            OrderId,CustomerId,ProductName,Quantity,UnitPrice,TotalAmount,OrderDate,CustomerEmail,InternalNotes,SalesRep
            ORD001,CUST001,Premium Widget,5,29.99,149.95,2024-01-15,customer1@email.com,VIP customer - priority shipping,REP001
            ORD002,CUST002,Standard Widget,10,19.99,199.90,2024-01-16,customer2@email.com,Bulk order discount applied,REP002
            ORD003,CUST003,Deluxe Widget,3,39.99,119.97,2024-01-17,customer3@email.com,First-time customer,REP001
            ORD004,CUST001,Premium Widget,2,29.99,59.98,2024-01-18,customer1@email.com,Repeat order - fast processing,REP003
            ORD005,CUST004,Economy Widget,25,9.99,249.75,2024-01-19,customer4@email.com,Large quantity - verified payment,REP002
            """;

        var inputFile = Path.Combine(_outputDirectory, "3_input_sales_data.csv");
        await File.WriteAllTextAsync(inputFile, inputContent);

        // Create accounting export (pipe-separated, formatted numbers)
        var accountingOutput = "Order_ID|Product|Qty|Unit_Price|Total|Order_Date|Sales_Rep\n" +
                             "ORD001|Premium Widget|5|$29.99|$149.95|2024-01-15|REP001\n" +
                             "ORD002|Standard Widget|10|$19.99|$199.90|2024-01-16|REP002\n" +
                             "ORD003|Deluxe Widget|3|$39.99|$119.97|2024-01-17|REP001\n" +
                             "ORD004|Premium Widget|2|$29.99|$59.98|2024-01-18|REP003\n" +
                             "ORD005|Economy Widget|25|$9.99|$249.75|2024-01-19|REP002\n";

        var outputFile = Path.Combine(_outputDirectory, "3_output_accounting_export.psv");
        await File.WriteAllTextAsync(outputFile, accountingOutput);

        _testFiles.Add(inputFile);
        _testFiles.Add(outputFile);

        _output.WriteLine($"   ✅ Input:  {Path.GetFileName(inputFile)} (10 fields including internal notes)");
        _output.WriteLine($"   ✅ Output: {Path.GetFileName(outputFile)} (7 fields, PIPE-separated, formatted currency)");
    }

    public void Dispose()
    {
        // Keep the files for user inspection - don't delete them
        _output?.WriteLine("");
        _output?.WriteLine("📁 Output files preserved for inspection");
        _output?.WriteLine($"📂 Location: {_outputDirectory}");
    }
}