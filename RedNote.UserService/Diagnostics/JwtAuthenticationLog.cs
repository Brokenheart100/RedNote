namespace RedNote.UserService.Diagnostics;

internal static partial class JwtAuthenticationLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "JWT authentication failed.")]
    public static partial void AuthenticationFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "JWT token validated. Subject: {Subject}")]
    public static partial void TokenValidated(
        ILogger logger,
        string? subject);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "JWT challenge. Error: {Error}. Description: {Description}")]
    public static partial void Challenge(
        ILogger logger,
        string? error,
        string? description);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "JWT claim. Type: {Type}, Value: {Value}")]
    public static partial void Claim(
        ILogger logger,
        string type,
        string value);
}