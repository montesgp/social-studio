using Microsoft.AspNetCore.Identity;

namespace Incoders.Hive.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string name) : base(name) { }
}
