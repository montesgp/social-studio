using System.Text.Json;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// WU-2 RED tests (task 2.1): <c>ComfyUiImageModelClient.SubstituteTemplate</c> /
/// <c>BuildWorkflowGraph</c> perform placeholder substitution, JSON-escape scalar values, and
/// validate the substituted graph via <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>.
/// Refs: CUI-01-c; design §3.3.
/// </summary>
public sealed class ComfyUiTemplateSubstitutionTests
{
    private const string Template = """
        {
          "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "{{CHECKPOINT_NAME}}" } },
          "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "{{POSITIVE_PROMPT}}", "clip": ["1", 1] } },
          "3": { "class_type": "CLIPTextEncode", "inputs": { "text": "{{NEGATIVE_PROMPT}}", "clip": ["1", 1] } },
          "4": { "class_type": "LoadImage", "inputs": { "image": "{{REFERENCE_IMAGE}}" } },
          "5": { "class_type": "IPAdapterUnifiedLoader", "inputs": { "preset": "{{IPADAPTER_MODEL}}", "model": ["1", 0] } },
          "6": { "class_type": "IPAdapterAdvanced", "inputs": { "model": ["5", 0], "image": ["4", 0], "weight": 0.8 } },
          "7": { "class_type": "EmptyLatentImage", "inputs": { "width": {{WIDTH}}, "height": {{HEIGHT}}, "batch_size": 1 } },
          "8": { "class_type": "KSampler", "inputs": { "model": ["6", 0], "positive": ["2", 0], "negative": ["3", 0], "latent_image": ["7", 0], "seed": {{SEED}}, "steps": {{STEPS}}, "cfg": {{CFG}} } },
          "vae": { "class_type": "VAEDecode", "inputs": { "samples": ["8", 0], "vae": ["1", 2] } },
          "9": { "class_type": "SaveImage", "inputs": { "images": ["vae", 0] } }
        }
        """;

    [Fact]
    public void SubstituteTemplate_AllTokensReplaced_ProducesValidJson()
    {
        var result = ComfyUiImageModelClient.SubstituteTemplate(
            Template,
            checkpoint: "v1-5-pruned-emaonly.safetensors",
            positivePrompt: "a vivid botanical background",
            negativePrompt: "blurry, low quality",
            referenceImageName: "uploaded_ref.png",
            ipAdapterModel: "ip-adapter_sd15.bin",
            width: 1024,
            height: 1024,
            seed: 42,
            steps: 20,
            cfg: 7.0);

        Assert.DoesNotContain("{{", result, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("v1-5-pruned-emaonly.safetensors", doc.RootElement.GetProperty("1").GetProperty("inputs").GetProperty("ckpt_name").GetString());
        Assert.Equal("uploaded_ref.png", doc.RootElement.GetProperty("4").GetProperty("inputs").GetProperty("image").GetString());
        Assert.Equal(1024, doc.RootElement.GetProperty("7").GetProperty("inputs").GetProperty("width").GetInt32());
    }

    [Fact]
    public void SubstituteTemplate_PromptWithJsonSpecialChars_IsCorrectlyEscaped()
    {
        const string tricky = "a \"stylized\" background\\with a newline\nand a tab\t";

        var result = ComfyUiImageModelClient.SubstituteTemplate(
            Template,
            checkpoint: "v1-5-pruned-emaonly.safetensors",
            positivePrompt: tricky,
            negativePrompt: "none",
            referenceImageName: "ref.png",
            ipAdapterModel: "ip-adapter.bin",
            width: 512,
            height: 512,
            seed: 1,
            steps: 20,
            cfg: 7.0);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(tricky, doc.RootElement.GetProperty("2").GetProperty("inputs").GetProperty("text").GetString());
    }

    [Fact]
    public void BuildWorkflowGraph_InvalidTemplateAfterSubstitution_ReturnsAiJsonInvalid()
    {
        const string brokenTemplate = """{ "1": { "ckpt_name": "{{CHECKPOINT_NAME}}" MISSING_COLON }""";

        var result = ComfyUiImageModelClient.BuildWorkflowGraph(
            brokenTemplate,
            checkpoint: "checkpoint.safetensors",
            positivePrompt: "prompt",
            negativePrompt: "",
            referenceImageName: null,
            ipAdapterModel: null,
            width: 512,
            height: 512,
            seed: 1,
            steps: 20,
            cfg: 7.0);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiJsonInvalid.Code, result.Error.Code);
    }

    [Fact]
    public void SubstituteTemplate_NoReferenceImage_BypassesIpAdapterChainWithPassThroughModelEdge()
    {
        var result = ComfyUiImageModelClient.SubstituteTemplate(
            Template,
            checkpoint: "v1-5-pruned-emaonly.safetensors",
            positivePrompt: "text-only prompt",
            negativePrompt: "",
            referenceImageName: null,
            ipAdapterModel: null,
            width: 512,
            height: 512,
            seed: 1,
            steps: 20,
            cfg: 7.0);

        using var doc = JsonDocument.Parse(result);
        var ksamplerModelEdge = doc.RootElement.GetProperty("8").GetProperty("inputs").GetProperty("model");
        Assert.Equal("1", ksamplerModelEdge[0].GetString());
        Assert.Equal(0, ksamplerModelEdge[1].GetInt32());
    }

    [Fact]
    public void BuildWorkflowGraph_NoReferenceImage_StillProducesValidJson()
    {
        var result = ComfyUiImageModelClient.BuildWorkflowGraph(
            Template,
            checkpoint: "v1-5-pruned-emaonly.safetensors",
            positivePrompt: "text-only prompt",
            negativePrompt: "",
            referenceImageName: null,
            ipAdapterModel: null,
            width: 512,
            height: 512,
            seed: 1,
            steps: 20,
            cfg: 7.0);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.Value);
        Assert.True(doc.RootElement.TryGetProperty("9", out _));
    }
}
