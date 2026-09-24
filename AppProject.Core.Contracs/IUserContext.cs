using System;

namespace AppProject.Core.Contracs;

public interface IUserContext
{
    public Task<UserInfo> GetSystemAdminUserAsync(CancellationToken cancellationToken = default);
}
