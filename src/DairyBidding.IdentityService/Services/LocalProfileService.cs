using System.Security.Claims;
using DairyBidding.IdentityService.Data;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.IdentityService.Services;

public sealed class LocalProfileService(IdentityDbContext db) : IProfileService
{
    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        var sub = context.Subject.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId)) return;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;

        context.IssuedClaims.AddRange(
        [
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
        ]);
    }

    public async Task IsActiveAsync(IsActiveContext context)
    {
        var sub = context.Subject.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
            context.IsActive = false;
            return;
        }
        context.IsActive = await db.Users.AnyAsync(u => u.Id == userId);
    }
}
