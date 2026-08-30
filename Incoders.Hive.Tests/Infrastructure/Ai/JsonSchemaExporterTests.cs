using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// Guards the JSON Schema generation that backs Claude tool-use structured output. Mirrors the
/// EXACT options + post-processing used by AiCompletionGateway. Claude's tool-use API is strict
/// about the root shape — a regression here breaks every AI call with HTTP 400.
/// </summary>
public sealed class JsonSchemaExporterTests
{
    private static readonly JsonSerializerOptions SchemaExportOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true
    };

    private static string GenerateSchemaFor(Type type)
    {
        var node = JsonSchemaExporter.GetJsonSchemaAsNode(SchemaExportOptions, type, ExporterOptions);
        NormalizeRootTypeForToolUse(node);
        return node.ToJsonString();
    }

    private static void NormalizeRootTypeForToolUse(JsonNode? schema)
    {
        if (schema is not JsonObject root) return;
        if (root["type"] is JsonArray typeArray)
        {
            string? picked = null;
            foreach (var entry in typeArray)
            {
                if (entry is JsonValue v && v.TryGetValue<string>(out var s) && s != "null")
                {
                    picked = s;
                    break;
                }
            }
            root["type"] = picked ?? "object";
        }
        else if (root["type"] is null)
        {
            root["type"] = "object";
        }
    }

    [Fact]
    public void GeneratesSchemaForSingleAnalyzerAnalysis_WithoutThrowing()
    {
        var json = GenerateSchemaFor(typeof(SingleAnalyzerAnalysis));

        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("summary", json);
        Assert.Contains("detectedHooks", json);
        Assert.Contains("derivedIdeas", json);
    }

    [Fact]
    public void RootTypeIsLiteralObjectString_NotArray_AsClaudeToolUseRequires()
    {
        // Reproduces the exact contract that Claude validates: input_schema.type must be the
        // string "object", NOT a JSON array (e.g. ["object","null"]). Anthropic returns
        // HTTP 400 "tools.0.custom.input_schema.type: Input should be 'object'" otherwise.
        var json = GenerateSchemaFor(typeof(SingleAnalyzerAnalysis));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("type", out var typeProp), "root must have a 'type' field");
        Assert.Equal(JsonValueKind.String, typeProp.ValueKind);
        Assert.Equal("object", typeProp.GetString());
    }

    [Fact]
    public void GeneratedSchemaIsValidJson_AndDeserializableByClaudeRequestBuilder()
    {
        var json = GenerateSchemaFor(typeof(SingleAnalyzerAnalysis));

        // ClaudeModelClient does this exact step to inline the schema into the tool definition.
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
    }
}
