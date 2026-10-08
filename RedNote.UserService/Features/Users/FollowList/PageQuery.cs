using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace RedNote.UserService.Features.Users.FollowList;

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
