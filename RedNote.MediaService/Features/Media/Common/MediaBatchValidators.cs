using FluentValidation;
using ServiceDefaults.Validation;
using HttpRequest = RedNote.MediaService.Features.Media.GetMediaBatch.GetMediaBatchRequest;
using GrpcRequest = RedNote.Contracts.Media.GetMediaBatchRequest;

namespace RedNote.MediaService.Features.Media.Common;

public sealed class MediaIdsValidator : AbstractValidator<IReadOnlyList<Guid>>
{
    public MediaIdsValidator()
    {
        RuleFor(ids => ids).Must(ids => ids.Distinct().Count() <= 100)
            .WithMessage("A maximum of 100 media items is allowed.").OverridePropertyName("");
        RuleFor(ids => ids).Must(ids => !ids.Contains(Guid.Empty))
            .WithMessage("Media id cannot be empty.").OverridePropertyName("");
    }
}

public sealed class HttpMediaBatchValidator : RequestValidator<HttpRequest>
{
    public HttpMediaBatchValidator() => RuleFor(x => x.MediaIds)
        .SetValidator(new MediaIdsValidator()!).OverridePropertyName("mediaIds");
}

public sealed class GrpcMediaBatchValidator : RequestValidator<GrpcRequest>
{
    public GrpcMediaBatchValidator() => RuleFor(x => x.MediaIds)
        .SetValidator(new MediaIdsValidator()).OverridePropertyName("mediaIds");
}
