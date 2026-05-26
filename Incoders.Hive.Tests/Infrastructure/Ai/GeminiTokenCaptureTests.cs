using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

public sealed class GeminiTokenCaptureTests
{
    // Fixture mimicking Gemini generateContent response with usageMetadata
    private const string GeminiResponseWithUsage = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "{\"value\": \"test\"}"
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ],
          "usageMetadata": {
            "promptTokenCount": 180,
            "candidatesTokenCount": 75,
            "totalTokenCount": 255
          }
        }
        """;

    // Fixture without usageMetadata
    private const string GeminiResponseWithoutUsage = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "{\"value\": \"test\"}"
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ]
        }
        """;

    [Fact]
    public void GeminiResponse_WithUsageMetadata_MapsPromptTokenCountToInput_CandidatesTokenCountToOutput()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(GeminiResponseWithUsage, "gemini", "gemini-1.5-flash");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Usage);
        Assert.Equal(180, result.Value.Usage!.InputTokens);
        Assert.Equal(75, result.Value.Usage!.OutputTokens);
    }

    [Fact]
    public void GeminiResponse_WithoutUsageMetadata_UsageIsNull()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(GeminiResponseWithoutUsage, "gemini", "gemini-1.5-flash");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Usage);
    }

    [Fact]
    public void GeminiResponse_WithUsageMetadata_ProviderKeyPreserved()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(GeminiResponseWithUsage, "gemini", "gemini-1.5-flash");

        Assert.Equal("gemini", result.Value.ProviderKey);
    }
}
