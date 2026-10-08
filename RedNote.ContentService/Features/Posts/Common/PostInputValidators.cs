using ServiceDefaults.Validation;
using FluentValidation;
using RedNote.ContentService.Features.Posts.Update;

namespace RedNote.ContentService.Features.Posts.Common;

public sealed class CreatePostRequestValidator : RequestValidator<CreatePostRequest>
{
    public CreatePostRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.")
            .MaximumLength(100).WithMessage("Title cannot exceed 100 characters.").OverridePropertyName("title");
        RuleFor(x => x.Content).NotEmpty().WithMessage("Content is required.")
            .MaximumLength(5000).WithMessage("Content cannot exceed 5000 characters.").OverridePropertyName("content");
        RuleFor(x => x.MediaIds).Must(ids => ids is null || ids.Distinct().Count() <= 9)
            .WithMessage("A post can contain at most 9 media items.").OverridePropertyName("mediaIds");
        RuleFor(x => x.MediaIds).Must(ids => ids is null || !ids.Contains(Guid.Empty))
            .WithMessage("MediaIds cannot contain an empty GUID.").OverridePropertyName("mediaIds");
        RuleFor(x => x.Tags).SetValidator(new PostTagsValidator()).OverridePropertyName("tags");
    }
}

public sealed class UpdatePostRequestValidator : RequestValidator<UpdatePostRequest>
{
    public UpdatePostRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.")
            .MaximumLength(100).WithMessage("Title cannot exceed 100 characters.").OverridePropertyName("title");
        RuleFor(x => x.Content).NotEmpty().WithMessage("Content is required.")
            .MaximumLength(5000).WithMessage("Content cannot exceed 5000 characters.").OverridePropertyName("content");
        RuleFor(x => x.Tags).SetValidator(new PostTagsValidator()).OverridePropertyName("tags");
    }
}

// Validate the list as a whole to retain the API's single `tags` error key.
public sealed class PostTagsValidator : AbstractValidator<IReadOnlyList<string>?>
{
    public PostTagsValidator()
    {
        RuleFor(tags => tags).Cascade(CascadeMode.Stop)
            .Must(tags => tags is null || tags.All(tag => !string.IsNullOrWhiteSpace(tag)))
            .WithMessage("Tags cannot contain empty values.")
            .Must(tags => tags is null || tags.All(tag => tag.Trim().Length <= 30))
            .WithMessage("Each tag cannot exceed 30 characters.")
            .Must(tags => tags is null || PostTags.Normalize(tags).Count <= 10)
            .WithMessage("A post can contain at most 10 tags.").OverridePropertyName("");
    }
}

public static class PostTags
{
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string> tags) =>
        tags.Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed class CreatePostCommentRequestValidator : RequestValidator<CreatePostCommentRequest>
{
    public CreatePostCommentRequestValidator()
    {
        RuleFor(x => x.Content).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Comment content is required.")
            .Must(content => content.Trim().Length <= 1000)
            .WithMessage("Comment content cannot exceed 1000 characters.").OverridePropertyName("content");
        RuleFor(x => x.ParentCommentId).Must(id => id is null || id != Guid.Empty)
            .WithMessage("Parent comment id cannot be empty.").OverridePropertyName("parentCommentId");
    }
}
