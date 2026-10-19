using Microsoft.AspNetCore.Identity;

namespace Finance_API.Persistence;

/// <summary>
/// ApplicationUser extends IdentityUser and is the representation of a user
/// account stored in the Identity tables. Add additional profile fields here
/// as needed (e.g., DisplayName, TimeZone, etc.).
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
}
