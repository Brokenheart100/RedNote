using Alba;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Posts.Update;
using RedNote.ContentService.Features.Posts.GetPostsByIds;
using RedNote.IdentityService.Features.Authentication.SessionLogin;
using RedNote.SearchService.Features.Search.History;
using RedNote.UserService.Features.Users.UpdateMe;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Xunit;

namespace RedNote.Backend.Tests.Validation;

// Exercise the generated HTTP middleware, not just validator.Validate(). No database is required.
public sealed class InputValidationHttpTests : IAsyncLifetime
{
    private IAlbaHost _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddWolverineHttp();
        builder.Services.AddSingleton<HandlerCalls>();
        builder.Services.AddSingleton<IValidator<CreatePostRequest>, CreatePostRequestValidator>();
        builder.Services.AddSingleton<IValidator<UpdatePostRequest>, UpdatePostRequestValidator>();
        builder.Services.AddSingleton<IValidator<CreatePostCommentRequest>, CreatePostCommentRequestValidator>();
        builder.Services.AddSingleton<IValidator<SessionLoginEndpoint.SessionLoginRequest>, SessionLoginRequestValidator>();
        builder.Services.AddSingleton<IValidator<UpdateMeRequest>, UpdateMeRequestValidator>();
        builder.Services.AddSingleton<IValidator<RecordSearchHistoryRequest>, RecordSearchHistoryRequestValidator>();
        builder.Services.AddSingleton<IValidator<PageQuery>, PageQueryValidator>();
        builder.Services.AddSingleton<IValidator<PostSearchQuery>, PostSearchQueryValidator>();
        builder.Services.AddSingleton<IValidator<TaggedPageQuery>, TaggedPageQueryValidator>();
        builder.Services.AddSingleton<IValidator<AuthorPostsQuery>, AuthorPostsQueryValidator>();
        builder.Services.AddSingleton<IValidator<GetPostsByIdsEndpoint.GetPostsByIdsRequest>, GetPostsByIdsRequestValidator>();
        builder.Host.UseWolverine(options =>
        {
            options.UseRuntimeCompilation();
            options.ApplicationAssembly = typeof(ValidationProbeEndpoint).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.UseFluentValidation(RegistrationBehavior.ExplicitRegistration);
        });
        _app = await AlbaHost.For(builder, app => app.MapWolverineEndpoints(options => options.UseFluentValidationProblemDetailMiddleware()));
        _client = _app.Server.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/validation/login", "{\"email\":null,\"password\":\" \"}", "email")]
    [InlineData("/validation/post", "{\"title\":\" \" ,\"content\":\"ok\"}", "title")]
    [InlineData("/validation/post", "{\"title\":\"ok\",\"content\":\"ok\",\"tags\":[null]}", "tags")]
    [InlineData("/validation/comment", "{\"content\":\"ok\",\"parentCommentId\":\"00000000-0000-0000-0000-000000000000\"}", "parentCommentId")]
    [InlineData("/validation/history", "{\"keyword\":null}", "keyword")]
    public async Task InvalidBodyReturnsFieldErrorsWithoutEnteringHandler(string path, string json, string field)
    {
        using var payload = JsonDocument.Parse(json);
        var response = await _client.PostAsJsonAsync(path, payload.RootElement);
        await AssertRejected(response, field);
    }

    [Theory]
    [InlineData("/validation/page?page=0&pageSize=20", "page")]
    [InlineData("/validation/page?page=1&pageSize=101", "pageSize")]
    [InlineData("/validation/page", "page")]
    [InlineData("/validation/search?page=1&pageSize=20&q=%20", "q")]
    [InlineData("/validation/search?page=-1&pageSize=20&q=ok", "page")]
    [InlineData("/validation/tags/%20?page=1&pageSize=20", "tagName")]
    [InlineData("/validation/author?page=1&pageSize=20&authorUserId=00000000-0000-0000-0000-000000000000", "authorUserId")]
    public async Task AsParametersValidatesQueryAndRouteMembers(string path, string field) =>
        await AssertRejected(await _client.GetAsync(path), field);

    [Fact]
    public async Task OptionalFieldsAndDistinctTagsKeepExistingSemantics()
    {
        var tags = Enumerable.Repeat(" Nuxt ", 20).Append("nuxt").ToArray();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest("ok", "ok", null, tags))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/validation/update", new UpdatePostRequest("ok", "ok", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/validation/update", new UpdatePostRequest("ok", "ok", []))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/validation/profile", new UpdateMeRequest(null, "/api/media/avatar", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/validation/search?page=1&pageSize=20&q=Nuxt")).StatusCode);
        Assert.Equal(5, _app.Services.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task BoundsAndNullBodiesAreRejected()
    {
        await AssertRejected(await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest(new('x', 101), "ok", null, null)), "title");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest("ok", new('x', 5001), null, null)), "content");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest("ok", "ok", Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray(), null)), "mediaIds");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest("ok", "ok", [Guid.Empty], null)), "mediaIds");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest("ok", "ok", null, [new('x', 31)])), "tags");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/post", new CreatePostRequest("ok", "ok", null, Enumerable.Range(0, 11).Select(i => $"tag{i}").ToArray())), "tags");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/comment", new CreatePostCommentRequest(new('x', 1001), null)), "content");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/profile", new UpdateMeRequest(null, new('x', 2049), null)), "avatarUrl");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/history", new RecordSearchHistoryRequest(new('x', 101))), "keyword");
        await AssertRejected(await _client.PostAsJsonAsync("/validation/batch", new GetPostsByIdsEndpoint.GetPostsByIdsRequest(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray())), "postIds");
        var response = await _client.PostAsync("/validation/post", new StringContent("null", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _app.Services.GetRequiredService<HandlerCalls>().Count);
    }

    private async Task AssertRejected(HttpResponseMessage response, string field)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, text);
        using var problem = JsonDocument.Parse(text);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), text);
        Assert.Equal(0, _app.Services.GetRequiredService<HandlerCalls>().Count);
    }
}

public sealed class HandlerCalls
{
    public int Count { get; private set; }
    public IResult Enter() { Count++; return Results.Ok(); }
}

public static class ValidationProbeEndpoint
{
    [WolverinePost("/validation/login")]
    public static IResult Login(SessionLoginEndpoint.SessionLoginRequest request, HandlerCalls calls) => calls.Enter();
    [WolverinePost("/validation/post")]
    public static IResult Post(CreatePostRequest request, HandlerCalls calls) => calls.Enter();
    [WolverinePost("/validation/update")]
    public static IResult Update(UpdatePostRequest request, HandlerCalls calls) => calls.Enter();
    [WolverinePost("/validation/comment")]
    public static IResult Comment(CreatePostCommentRequest request, HandlerCalls calls) => calls.Enter();
    [WolverinePost("/validation/profile")]
    public static IResult Profile(UpdateMeRequest request, HandlerCalls calls) => calls.Enter();
    [WolverinePost("/validation/history")]
    public static IResult History(RecordSearchHistoryRequest request, HandlerCalls calls) => calls.Enter();
    [WolverineGet("/validation/page")]
    public static IResult Page([AsParameters] PageQuery request, HandlerCalls calls) => calls.Enter();
    [WolverineGet("/validation/search")]
    public static IResult Search([AsParameters] PostSearchQuery request, HandlerCalls calls) => calls.Enter();
    [WolverineGet("/validation/tags/{tagName}")]
    public static IResult Tags([AsParameters] TaggedPageQuery request, HandlerCalls calls) => calls.Enter();
    [WolverineGet("/validation/author")]
    public static IResult Author([AsParameters] AuthorPostsQuery request, HandlerCalls calls) => calls.Enter();
    [WolverinePost("/validation/batch")]
    public static IResult Batch(GetPostsByIdsEndpoint.GetPostsByIdsRequest request, HandlerCalls calls) => calls.Enter();
}
