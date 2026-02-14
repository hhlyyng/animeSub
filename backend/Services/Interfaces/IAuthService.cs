namespace backend.Services.Interfaces;

public interface IAuthService
{
    Task<bool> IsSetupCompletedAsync();
    Task SetupUserAsync(string username, string password);
    Task<string?> LoginAsync(string username, string password);
    bool ValidateToken(string token);
}
