using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.IdentityService.Features.Authentication.Register;

public sealed class RegisterRequestValidator : RequestValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").OverridePropertyName("email");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.").OverridePropertyName("password");
    }
}
