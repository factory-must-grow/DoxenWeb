using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Doxen.Web.Data;

// Хранилище EF Core исключительно для таблиц Identity (AspNetUsers и
// т.д.). Бизнес-таблицы Doxen этим контекстом не владеют и через него
// не читаются — см. Doxen.Data и 01-architecture.md, раздел про
// разделение владения таблицами.
public sealed class AuthDbContext : IdentityDbContext<DoxenUser, IdentityRole<long>, long>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }
}
