using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.IdentityService.Features.Authentication.ConfirmEmail;

public sealed class ConfirmEmailRequestValidator : RequestValidator<ConfirmEmailEndpoint.ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").OverridePropertyName("email");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Confirmation code is required.").OverridePropertyName("code");
    }
}
