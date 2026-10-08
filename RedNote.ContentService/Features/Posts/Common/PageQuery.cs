using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace RedNote.ContentService.Features.Posts.Common;

public class PageQuery
{
    [FromQuery(Name = "page")]
    public int Page { get; set; }
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; set; }
}

public sealed class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be greater than or equal to 1.").OverridePropertyName("page");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.").OverridePropertyName("pageSize");
    }
}

public sealed class AuthorPostsQuery : PageQuery
{
    [FromQuery(Name = "authorUserId")]
    public Guid AuthorUserId { get; set; }
}

public sealed class AuthorPostsQueryValidator : AbstractValidator<AuthorPostsQuery>
{
    public AuthorPostsQueryValidator()
    {
        Include(new PageQueryValidator());
        RuleFor(x => x.AuthorUserId).NotEmpty().WithMessage("Author user id cannot be empty.").OverridePropertyName("authorUserId");
    }
}

public sealed class PostSearchQuery : PageQuery
{
    [FromQuery(Name = "q")]
    public string? Q { get; set; }
}

public sealed class PostSearchQueryValidator : AbstractValidator<PostSearchQuery>
{
    public PostSearchQueryValidator()
    {
        Include(new PageQueryValidator());
        RuleFor(x => x.Q).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Search query is required.")
            .Must(q => q!.Trim().Length <= 100).WithMessage("Search query cannot exceed 100 characters.").OverridePropertyName("q");
    }
}

public sealed class TaggedPageQuery : PageQuery
{
    [FromRoute(Name = "tagName")]
    public string? TagName { get; set; }
}

public sealed class TaggedPageQueryValidator : AbstractValidator<TaggedPageQuery>
{
    public TaggedPageQueryValidator()
    {
        Include(new PageQueryValidator());
        RuleFor(x => x.TagName).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Tag name is required.")
            .Must(tag => tag!.Trim().Length <= 30).WithMessage("Tag name cannot exceed 30 characters.").OverridePropertyName("tagName");
    }
}
