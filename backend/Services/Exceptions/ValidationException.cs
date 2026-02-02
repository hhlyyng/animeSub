namespace backend.Services.Exceptions;

/// <summary>
/// Exception thrown when request validation fails
/// </summary>
public class ValidationException : ApiException
{
    public Dictionary<string, string[]> ValidationErrors { get; }

    public ValidationException(
        Dictionary<string, string[]> validationErrors,
        string message = "One or more validation errors occurred")
        : base(
            message,
            "VALIDATION_ERROR",
            400, // Bad Request
            validationErrors)
    {
        ValidationErrors = validationErrors;
    }

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = new[] { error } })
    {
    }
}
