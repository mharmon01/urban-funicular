namespace ListingSearch.Api.Search;

/// <summary>
/// Either a value or a set of field-keyed validation errors (the shape ASP.NET Core's
/// ValidationProblem expects), so expected failures don't need exceptions.
/// </summary>
public sealed class Result<T>
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private Result(T? value, IReadOnlyDictionary<string, string[]> errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public bool IsSuccess => Errors.Count == 0;

    public static Result<T> Success(T value) => new(value, NoErrors);

    public static Result<T> Failure(IReadOnlyDictionary<string, string[]> errors) =>
        errors.Count == 0
            ? throw new ArgumentException("A failure needs at least one error.", nameof(errors))
            : new(default, errors);

    public static Result<T> Failure(string field, string message) =>
        Failure(new Dictionary<string, string[]> { [field] = [message] });
}
