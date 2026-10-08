using FluentValidation;
using ServiceDefaults.Validation;

namespace RedNote.ContentService.Features.Posts.GetPostsByIds;

public sealed class GetPostsByIdsRequestValidator : RequestValidator<GetPostsByIdsEndpoint.GetPostsByIdsRequest>
{
    public GetPostsByIdsRequestValidator()
    {
        RuleFor(x => x.PostIds).Must(ids => ids is null || ids.Where(id => id != Guid.Empty).Distinct().Count() <= 100)
            .WithMessage("A maximum of 100 post IDs is allowed.").OverridePropertyName("postIds");
    }
}
