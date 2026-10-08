using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.IdentityService.Features.Authentication.ResendConfirmationEmail;

public sealed class ResendConfirmationEmailRequestValidator : RequestValidator<ResendConfirmationEmailEndpoint.ResendConfirmationEmailRequest>
{
    public ResendConfirmationEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").OverridePropertyName("email");
    }
}
