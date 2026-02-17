namespace backend.Services.Interfaces;

public interface IAuthService
{
    Task<bool> IsSetupCompletedAsync();
    Task SetupUserAsync(string username, string password);
    Task<string?> LoginAsync(string username, string password);
    Task<bool> ChangeCredentialsAsync(string currentPassword, string? newPassword, string? newUsername);
    bool ValidateToken(string token);
}
