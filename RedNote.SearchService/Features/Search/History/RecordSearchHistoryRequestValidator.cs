using ServiceDefaults.Validation;
using FluentValidation;

namespace RedNote.SearchService.Features.Search.History;

public sealed class RecordSearchHistoryRequestValidator : RequestValidator<RecordSearchHistoryRequest>
{
    public RecordSearchHistoryRequestValidator()
    {
        RuleFor(x => x.Keyword).Must(keyword => !string.IsNullOrWhiteSpace(keyword) && keyword.Trim().Length <= 100)
            .WithMessage("Keyword must contain 1 to 100 characters.").OverridePropertyName("keyword");
    }
}
