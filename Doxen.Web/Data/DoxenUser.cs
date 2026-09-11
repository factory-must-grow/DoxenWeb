using Microsoft.AspNetCore.Identity;

namespace Doxen.Web.Data;

// Учётная запись Identity. Бизнес-данные пользователя (тариф, дата
// регистрации) — в таблице user_profiles (Doxen.Data), не здесь, чтобы
// не смешивать их с таблицами Identity. Дополнительных свойств нет.
public sealed class DoxenUser : IdentityUser<long>
{
}
