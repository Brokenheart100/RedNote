using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.IdentityService.Features.Authentication.ForgotPassword;

public sealed class ForgotPasswordRequestValidator : RequestValidator<ForgotPasswordEndpoint.ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").OverridePropertyName("email");
    }
}
