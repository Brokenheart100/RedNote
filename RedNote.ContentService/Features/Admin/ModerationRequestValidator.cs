using FluentValidation;
using ServiceDefaults.Validation;

namespace RedNote.ContentService.Features.Admin;

public sealed class ModerationRequestValidator : RequestValidator<ModerationRequest>
{
    public ModerationRequestValidator()
    {
        RuleFor(request => request.Revision).GreaterThan(0).OverridePropertyName("revision");
        RuleFor(request => request.Reason).Cascade(CascadeMode.Stop)
            .NotEmpty().Must(reason => reason.Trim().Length <= 500)
            .WithMessage("Reason must contain at most 500 characters.").OverridePropertyName("reason");
    }
}
