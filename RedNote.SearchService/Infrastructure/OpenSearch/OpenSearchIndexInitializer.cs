using OpenSearch.Client;
using RedNote.SearchService.Domain.Posts;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace RedNote.SearchService.Infrastructure.OpenSearch;

public sealed partial class OpenSearchIndexInitializer(
    IOpenSearchClient openSearchClient,
    ILogger<OpenSearchIndexInitializer> logger)
{
    public const string PostIndexName = "posts-v1";

    private const int MaxAttempts = 10;

    private static readonly TimeSpan RetryDelay =
        TimeSpan.FromSeconds(2);

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        LogWaitingForOpenSearch(logger);

        var existsResponse =
            await WaitForOpenSearchAsync(
                cancellationToken);

        if (existsResponse.Exists)
        {
            LogIndexAlreadyExists(
                logger,
                PostIndexName);

            return;
        }

        LogCreatingIndex(
            logger,
            PostIndexName);

        var createResponse =
            await openSearchClient.Indices.CreateAsync(
                PostIndexName,
                descriptor => descriptor
                    .Settings(settings => settings
                        .NumberOfShards(1)
                        .NumberOfReplicas(0))
                    .Map<PostSearchDocument>(
                        mapping => mapping
                            .Properties(properties => properties

                                .Keyword(keyword => keyword
                                    .Name(document =>
                                        document.Id))

                                .Keyword(keyword => keyword
                                    .Name(document =>
                                        document.AuthorUserId))

                                .Text(text => text
                                    .Name(document =>
                                        document.Title))

                                .Text(text => text
                                    .Name(document =>
                                        document.Content))

                                .Keyword(keyword => keyword
                                    .Name(document =>
                                        document.Tags))

                                .Number(number => number
                                    .Name(document =>
                                        document.LikeCount)
                                    .Type(NumberType.Integer))

                                .Number(number => number
                                    .Name(document =>
                                        document.CommentCount)
                                    .Type(NumberType.Integer))

                                .Date(date => date
                                    .Name(document =>
                                        document.CreatedAtUtc))

                                .Date(date => date
                                    .Name(document =>
                                        document.UpdatedAtUtc)))),
                cancellationToken);

        if (!createResponse.IsValid)
        {
            throw new InvalidOperationException(
                $"Failed to create OpenSearch index '{PostIndexName}'. " +
                $"{createResponse.DebugInformation}");
        }

        if (!createResponse.Acknowledged)
        {
            throw new InvalidOperationException(
                $"OpenSearch did not acknowledge creation of " +
                $"index '{PostIndexName}'.");
        }

        LogIndexCreated(
            logger,
            PostIndexName);
    }

private async Task<ExistsResponse> WaitForOpenSearchAsync(
    CancellationToken cancellationToken)
{
    ExistsResponse? lastResponse = null;

    for (var attempt = 1;
         attempt <= MaxAttempts;
         attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lastResponse =
            await openSearchClient.Indices.ExistsAsync(
                PostIndexName,
                descriptor => descriptor,
                cancellationToken);

        var statusCode =
            lastResponse.ApiCall?.HttpStatusCode;

        if (statusCode is 200 or 404)
        {
            LogOpenSearchReady(
                logger,
                attempt);

            return lastResponse;
        }

        LogOpenSearchNotReady(
            logger,
            attempt,
            MaxAttempts);

        if (attempt < MaxAttempts)
        {
            await Task.Delay(
                RetryDelay,
                cancellationToken);
        }
    }

    throw new InvalidOperationException(
        $"OpenSearch was not ready after " +
        $"{MaxAttempts} attempts. " +
        $"{lastResponse?.DebugInformation}");
}

    [LoggerMessage(
        EventId = 1000,
        Level = MsLogLevel.Information,
        Message = "Waiting for OpenSearch to become ready.")]
    private static partial void LogWaitingForOpenSearch(
        ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = MsLogLevel.Information,
        Message =
            "OpenSearch became ready on attempt {Attempt}.")]
    private static partial void LogOpenSearchReady(
        ILogger logger,
        int attempt);

    [LoggerMessage(
        EventId = 1002,
        Level = MsLogLevel.Warning,
        Message =
            "OpenSearch is not ready. Attempt " +
            "{Attempt}/{MaxAttempts}.")]
    private static partial void LogOpenSearchNotReady(
        ILogger logger,
        int attempt,
        int maxAttempts);

    [LoggerMessage(
        EventId = 1003,
        Level = MsLogLevel.Information,
        Message =
            "OpenSearch index {IndexName} already exists.")]
    private static partial void LogIndexAlreadyExists(
        ILogger logger,
        string indexName);

    [LoggerMessage(
        EventId = 1004,
        Level = MsLogLevel.Information,
        Message =
            "Creating OpenSearch index {IndexName}.")]
    private static partial void LogCreatingIndex(
        ILogger logger,
        string indexName);

    [LoggerMessage(
        EventId = 1005,
        Level = MsLogLevel.Information,
        Message =
            "OpenSearch index {IndexName} created successfully.")]
    private static partial void LogIndexCreated(
        ILogger logger,
        string indexName);
}