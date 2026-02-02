namespace backend.Services.Exceptions;

/// <summary>
/// Exception thrown when API credentials are invalid or missing
/// </summary>
public class InvalidCredentialsException : ApiException
{
    public string CredentialType { get; }

    public InvalidCredentialsException(
        string credentialType,
        string message = "Invalid or missing credentials")
        : base(
            message,
            "INVALID_CREDENTIALS",
            401, // Unauthorized
            new { CredentialType = credentialType })
    {
        CredentialType = credentialType;
    }
}
