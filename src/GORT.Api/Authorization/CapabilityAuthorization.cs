using GORT.Core;
using GORT.Security.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace GORT.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresCapabilityAttribute : Attribute, IFilterMetadata
{
    public RequiresCapabilityAttribute(string capability, NasDataLevel dataLevel = NasDataLevel.System) { Capability = capability; DataLevel = dataLevel; }
    public string Capability { get; }
    public NasDataLevel DataLevel { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class BootstrapOnlyAttribute : Attribute, IFilterMetadata { }

/// <summary>Places an explicit default requirement on every non-public MVC action.</summary>
public sealed class CapabilityConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        foreach (var action in controller.Actions)
        {
            if (action.Attributes.OfType<AllowAnonymousAttribute>().Any() || action.Attributes.OfType<BootstrapOnlyAttribute>().Any()) continue;
            var requirement = action.Attributes.OfType<RequiresCapabilityAttribute>().FirstOrDefault()
                ?? controller.Attributes.OfType<RequiresCapabilityAttribute>().FirstOrDefault()
                ?? new RequiresCapabilityAttribute("admin:**");
            action.Filters.Add(requirement);
        }
    }
}

/// <summary>Central authorization filter; controllers do not inspect token payloads.</summary>
public sealed class CapabilityAuthorizationFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.Filters.OfType<BootstrapOnlyAttribute>().Any())
        {
            var bootstrapPayload = context.HttpContext.Items["NasTokenPayload"] as NasTokenPayload;
            if (bootstrapPayload is null || bootstrapPayload.Capabilities.Satisfies("admin:**") || bootstrapPayload.Capabilities.Satisfies("admin:user:create")) return;
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }
        if (context.ActionDescriptor is ControllerActionDescriptor descriptor && descriptor.MethodInfo.GetCustomAttributes(inherit: true).Any(attribute => attribute.GetType().Name == "AllowAnonymousAttribute")) return;
        var requirement = context.Filters.OfType<RequiresCapabilityAttribute>().LastOrDefault();
        if (requirement is null) { context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden); return; }
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var requireAuth = configuration.GetValue("security:require_auth", true);
        if (!requireAuth) return; // Auth disabled — allow all requests.

        var token = context.HttpContext.Request.Headers.Authorization.ToString();
        token = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token[7..].Trim() : context.HttpContext.Request.Headers["X-Nas-Token"].ToString();
        var payload = context.HttpContext.Items["NasTokenPayload"] as NasTokenPayload;
        if (string.IsNullOrWhiteSpace(token) || payload is null) { context.Result = new UnauthorizedResult(); return; }
        if (payload.Capabilities.Satisfies("admin:**")) return;
        var engine = context.HttpContext.RequestServices.GetRequiredService<IPermissionEngine>();
        var resource = context.HttpContext.Request.Query["path"].FirstOrDefault();
        var decision = await engine.CheckPermissionAsync(token, requirement.Capability, resource, requirement.DataLevel, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!decision.Granted) context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}

public static class EndpointCapabilityValidation
{
    public static void Validate(WebApplication app)
    {
        var descriptors = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        var missing = descriptors.Where(d => d.AttributeRouteInfo?.Template?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(d => !d.MethodInfo.GetCustomAttributes(inherit: true).Any(attribute => attribute.GetType().Name == "AllowAnonymousAttribute") && !d.MethodInfo.GetCustomAttributes(inherit: true).OfType<BootstrapOnlyAttribute>().Any() && !d.FilterDescriptors.Any(f => f.Filter is RequiresCapabilityAttribute))
            .Select(d => d.DisplayName).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("REST endpoints missing capability metadata: " + string.Join(", ", missing));
    }
}


