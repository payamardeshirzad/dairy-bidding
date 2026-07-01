namespace DairyBidding.IdentityService.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(string username, string role);
}
