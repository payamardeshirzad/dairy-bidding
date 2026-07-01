using DairyBidding.IdentityService.Data;

namespace DairyBidding.IdentityService.Services;

public interface IUserService
{
    Task<User?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default);
}
