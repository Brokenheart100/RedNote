using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.IdentityService.Features.Authentication.ResetPassword;

public sealed class ResetPasswordRequestValidator : RequestValidator<ResetPasswordEndpoint.ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").OverridePropertyName("email");
        RuleFor(x => x.ResetCode).NotEmpty().WithMessage("Reset code is required.").OverridePropertyName("resetCode");
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("New password is required.").OverridePropertyName("newPassword");
    }
}
