using System.Text.Json;
using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Tests.Infrastructure.Ai;

public sealed class ClaudeTokenCaptureTests
{
    // Fixture that mirrors a real Claude tool-use response with usage block
    private const string ClaudeResponseWithUsage = """
        {
          "id": "msg_01",
          "type": "message",
          "role": "assistant",
          "content": [
            {
              "type": "tool_use",
              "id": "toolu_01",
              "name": "submit_response",
              "input": {"value": "test"}
            }
          ],
          "model": "claude-sonnet-4-6",
          "stop_reason": "tool_use",
          "usage": {
            "input_tokens": 350,
            "output_tokens": 120
          }
        }
        """;

    // Fixture with no usage block
    private const string ClaudeResponseWithoutUsage = """
        {
          "id": "msg_02",
          "type": "message",
          "role": "assistant",
          "content": [
            {
              "type": "tool_use",
              "id": "toolu_02",
              "name": "submit_response",
              "input": {"value": "test"}
            }
          ],
          "model": "claude-sonnet-4-6",
          "stop_reason": "tool_use"
        }
        """;

    [Fact]
    public void ClaudeResponse_WithUsageBlock_MapsInputAndOutputTokens()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(ClaudeResponseWithUsage, "claude", "claude-sonnet-4-6");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Usage);
        Assert.Equal(350, result.Value.Usage!.InputTokens);
        Assert.Equal(120, result.Value.Usage!.OutputTokens);
    }

    [Fact]
    public void ClaudeResponse_WithoutUsageBlock_UsageIsNull()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(ClaudeResponseWithoutUsage, "claude", "claude-sonnet-4-6");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Usage);
    }

    [Fact]
    public void ClaudeResponse_WithUsageBlock_ProviderKeyPreserved()
    {
        var result = ProviderHttpResponseMapper.MapAiSuccess(ClaudeResponseWithUsage, "claude", "claude-sonnet-4-6");

        Assert.True(result.IsSuccess);
        Assert.Equal("claude", result.Value.ProviderKey);
    }
}
