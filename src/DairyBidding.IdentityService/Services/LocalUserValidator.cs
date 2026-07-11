using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;

namespace DairyBidding.IdentityService.Services;

public sealed class LocalUserValidator(IUserService users) : IResourceOwnerPasswordValidator
{
    public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
    {
        var user = await users.ValidateCredentialsAsync(context.UserName, context.Password);
        if (user is null)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant, "Invalid credentials.");
            return;
        }

        context.Result = new GrantValidationResult(
            subject: user.Id.ToString(),
            authenticationMethod: "pwd");
    }
}
