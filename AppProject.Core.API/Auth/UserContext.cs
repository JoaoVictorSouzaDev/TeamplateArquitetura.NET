using System;
using AppProject.Core.Contracs;
using AppProject.Core.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Core.API.Auth;

public class UserContext(
    ApplicationDbContext applicationDbContext)
    : IUserContext
{
    public async Task<UserInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = await applicationDbContext.Users.FirstOrDefaultAsync(u => u.IsSystemAdmin, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException("System admin user not found ");
        }

        return new UserInfo
        {
            UserId = user.Id,
            UserName = user.Name,
            Email = user.Email,
            IsSystemAdmin = true
        };
    }

    public Task<UserInfo> GetSystemAdminUserAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
