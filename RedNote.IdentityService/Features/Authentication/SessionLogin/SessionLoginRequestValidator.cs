using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.IdentityService.Features.Authentication.SessionLogin;

public sealed class SessionLoginRequestValidator : RequestValidator<SessionLoginEndpoint.SessionLoginRequest>
{
    public SessionLoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").OverridePropertyName("email");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.").OverridePropertyName("password");
    }
}
