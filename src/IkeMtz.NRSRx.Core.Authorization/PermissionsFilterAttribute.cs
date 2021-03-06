using System;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IkeMtz.NRSRx.Core.Authorization
{
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
  public sealed class PermissionsFilterAttribute : BaseActionFilterAttribute
  {

    public PermissionsFilterAttribute(string[] allowedPermissions, bool allowScopes = true, string permissionClaimType = DefaultPermissionClaimType, char permissionClaimSeperator = ',', string scopeClaimType = DefaultScopeClaimType)
      : base(allowedPermissions, allowScopes, permissionClaimType, permissionClaimSeperator, scopeClaimType)
    {
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
      context = context ?? throw new ArgumentNullException(nameof(context));

      if (!HasPermission(context))
      {
        var errMsg = string.Format(CultureInfo.CurrentCulture, "You do not have one of the required permissions {0}.", string.Join(',', AllowedPermissions));
        context.Result = new UnauthorizedObjectResult(errMsg);
      }
    }
  }
}
