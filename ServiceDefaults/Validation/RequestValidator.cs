using FluentValidation;
using FluentValidation.Results;

namespace ServiceDefaults.Validation;

// Wolverine 6.38 passes JSON `null` to the validator. FluentValidation otherwise throws
// for a null root model before running rules. Use its supported PreValidate hook.
public abstract class RequestValidator<T> : AbstractValidator<T>
{
    protected override bool PreValidate(ValidationContext<T> context, ValidationResult result)
    {
        if (context.InstanceToValidate is not null) return base.PreValidate(context, result);
        result.Errors.Add(new ValidationFailure("body", "Request body is required."));
        return false;
    }
}
