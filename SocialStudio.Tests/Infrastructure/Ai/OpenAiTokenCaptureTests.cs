using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Tests.Infrastructure.Ai;

public sealed class OpenAiTokenCaptureTests
{
    // Fixture mimicking OpenAI chat completions response with usage
    private const string OpenAiResponseWithUsage = """
        {
          "id": "chatcmpl-01",
          "object": "chat.completion",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": "{\"value\": \"test\"}"
              },
              "finish_reason": "stop"
            }
          ],
          "usage": {
            "prompt_tokens": 250,
            "completion_tokens": 90,
            "total_tokens": 340
          }
        }
        """;

    // Fixture without usage block
    private const string OpenAiResponseWithoutUsage = """
        {
          "id": "chatcmpl-02",
          "object": "chat.completion",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": "{\"value\": \"test\"}"
              },
              "finish_reason": "stop"
            }
          ]
        }
        """;

    [Fact]
    public void OpenAiResponse_WithUsageBlock_MapsPromptTokensToInput_CompletionTokensToOutput()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(OpenAiResponseWithUsage, "openai", "gpt-4o");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Usage);
        Assert.Equal(250, result.Value.Usage!.InputTokens);
        Assert.Equal(90, result.Value.Usage!.OutputTokens);
    }

    [Fact]
    public void OpenAiResponse_WithoutUsageBlock_UsageIsNull()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(OpenAiResponseWithoutUsage, "openai", "gpt-4o");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Usage);
    }

    [Fact]
    public void OpenAiResponse_WithUsageBlock_ModelPreserved()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(OpenAiResponseWithUsage, "openai", "gpt-4o");

        Assert.Equal("gpt-4o", result.Value.Model);
    }
}
