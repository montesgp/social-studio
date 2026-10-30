namespace SocialStudio.Tests.ApiService;

public sealed class GlobalExceptionHandlerContractTests
{
    [Fact]
    public void DataStoreOutageMapsToServiceUnavailableInsteadOfInternalServerError()
    {
        var source = ReadHandlerSource();

        Assert.Contains("Status503ServiceUnavailable", source);
        Assert.Contains("server.data_store_unavailable", source);
    }

    [Fact]
    public void DataStoreOutageIsDetectedWithoutProviderSpecificTypes()
    {
        var source = ReadHandlerSource();

        Assert.Contains("DbException { IsTransient: true } or SocketException", source);
        Assert.DoesNotContain("Npgsql", source);
    }

    [Fact]
    public void DataStoreOutageDetailIsSanitizedEvenInDevelopment()
    {
        var source = ReadHandlerSource();
        var sanitizedProblem = ExtractMethodBody(source, "ProblemDetails BuildDataStoreUnavailableProblem");

        // Connection failures carry host, port and credentials in their message, so the
        // sanitized branch must never consult the environment or echo the exception.
        Assert.DoesNotContain("IsDevelopment", sanitizedProblem);
        Assert.DoesNotContain("exception", sanitizedProblem);
        Assert.Contains("The service is temporarily unavailable.", sanitizedProblem);
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var start = source.IndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method '{methodName}' was not found in the handler source.");

        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"Method '{methodName}' has no body.");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Unbalanced braces while reading '{methodName}'.");
    }

    private static string ReadHandlerSource() => ReadRepositoryFile(
        "SocialStudio.ApiService",
        "Endpoints",
        "Common",
        "GlobalExceptionHandler.cs");

    private static string ReadRepositoryFile(params string[] relativePathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{Path.Combine(relativePathParts)}' walking up from {AppContext.BaseDirectory}.");
    }
}
