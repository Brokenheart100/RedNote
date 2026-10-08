using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.UserService.Features.Users.UpdateMe;

public sealed class UpdateMeRequestValidator : RequestValidator<UpdateMeRequest>
{
    public UpdateMeRequestValidator()
    {
        RuleFor(x => x.Nickname).MaximumLength(64).WithMessage("Nickname cannot exceed 64 characters.").OverridePropertyName("nickname");
        RuleFor(x => x.AvatarUrl).MaximumLength(2048).WithMessage("Avatar URL cannot exceed 2048 characters.").OverridePropertyName("avatarUrl");
        RuleFor(x => x.Bio).MaximumLength(500).WithMessage("Bio cannot exceed 500 characters.").OverridePropertyName("bio");
    }
}
