namespace Stratum.Modules.Identity.Application.Preferences;

public interface IUserPreferencesService
{
    Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct = default);

    Task<UserPreferences> GetOrDefaultAsync(Guid userId, CancellationToken ct = default);

    Task UpdateAsync(Guid userId, UserPreferences preferences, CancellationToken ct = default);
}
