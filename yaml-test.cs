using System;
using System.IO;
using FlowEngine.Core.Configuration.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var yamlContent = @"
pipeline:
  name: Test
  plugins:
    - name: TestPlugin
      type: DelimitedSource
      config:
        FilePath: test.csv
        HasHeaders: true
        InferSchema: false
        OutputSchema:
          Name: TestSchema
          Columns:
            - Name: id
              Type: Integer
              Index: 0
";

var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .WithTypeConverter(new YamlStringToNumberTypeConverter())
    .WithTypeConverter(new YamlStringToBooleanTypeConverter())
    .IgnoreUnmatchedProperties()
    .Build();

var result = deserializer.Deserialize<YamlPipelineConfiguration>(yamlContent);
Console.WriteLine($"Plugin config type: {result.Pipeline.Plugins[0].Config.GetType()}");
Console.WriteLine($"OutputSchema key exists: {result.Pipeline.Plugins[0].Config.ContainsKey("OutputSchema")}");
if (result.Pipeline.Plugins[0].Config.ContainsKey("OutputSchema"))
{
    var outputSchema = result.Pipeline.Plugins[0].Config["OutputSchema"];
    Console.WriteLine($"OutputSchema type: {outputSchema.GetType()}");
    Console.WriteLine($"OutputSchema value: {outputSchema}");
}