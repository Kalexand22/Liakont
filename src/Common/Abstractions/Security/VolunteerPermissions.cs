namespace Stratum.Common.Abstractions.Security;

/// <summary>
/// Defines the limited set of permissions available to users with the stratum-volunteer role.
/// Volunteers can read schedules and manage their own attendance -- nothing else.
/// </summary>
public static class VolunteerPermissions
{
    /// <summary>
    /// Named policy requiring the volunteer role. Use on endpoints/pages that
    /// volunteers are allowed to access. Permission-level restriction is enforced
    /// separately by <c>VolunteerAuthorizationHandler</c>.
    /// </summary>
    public const string PolicyName = "VolunteerPolicy";

    public const string ScheduleRead = "schedule.read";
    public const string AttendanceWrite = "attendance.write";

    /// <summary>
    /// All permissions granted to volunteers. Used by the authorization handler
    /// to restrict volunteer access to only these operations.
    /// </summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ScheduleRead,
        AttendanceWrite,
    };

    public static bool IsAllowed(string permission) => Allowed.Contains(permission);
}
