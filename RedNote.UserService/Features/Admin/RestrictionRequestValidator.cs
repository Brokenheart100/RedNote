using FluentValidation;
using ServiceDefaults.Validation;

namespace RedNote.UserService.Features.Admin;

public sealed class RestrictionRequestValidator : RequestValidator<RestrictionRequest>
{
    public RestrictionRequestValidator()
    {
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0).OverridePropertyName("revision");
        RuleFor(request => request.Reason).Cascade(CascadeMode.Stop)
            .NotEmpty().Must(reason => reason.Trim().Length <= 500)
            .WithMessage("Reason must contain at most 500 characters.").OverridePropertyName("reason");
        RuleFor(request => request.ExpiresAtUtc).Must(expiry => expiry is null || expiry > DateTimeOffset.UtcNow)
            .WithMessage("Expiry must be in the future.").OverridePropertyName("expiresAtUtc");
    }
}
