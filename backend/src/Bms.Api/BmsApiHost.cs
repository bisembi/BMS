using Bms.Api.Endpoints;
using Bms.Api.Diagnostics;
using Bms.Api.Hosting;
using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Api.Workers;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Infrastructure;
using Bms.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Bms.Api;

public static class BmsApiHost
{
    public static async Task<WebApplication> BuildAsync(string[] args) =>
        await BuildAsync(args, configureServices: null);

    internal static async Task<WebApplication> BuildAsync(
        string[] args,
        Action<IServiceCollection>? configureServices)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Only a real Windows Service start changes the arguments; a console
        // start passes them through untouched.
        args = WindowsServiceHosting.PrepareArguments(args);
        var builder = WebApplication.CreateBuilder(args);
        var maintenanceSetting = builder.Configuration["BmsWeb:MaintenanceReadOnly"];
        if (maintenanceSetting is not null && !bool.TryParse(maintenanceSetting, out _))
            throw new InvalidOperationException("Invalid read-only maintenance setting.");
        var maintenanceReadOnly = maintenanceSetting is not null && bool.Parse(maintenanceSetting);

        WindowsServiceHosting.AddLifetime(builder.Services);
        builder.Services.AddApiProblemDetails();
        builder.Services.AddSingleton<AttachmentUploadAdmission412>();
        builder.Services.AddExceptionHandler<SafeUnhandledExceptionHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<Bms.Application.Abstractions.IBudgetExecutionTransactionIntent343, Bms.Api.Security.BudgetExecutionHttpIntent343>();
        var windowsAuthentication = WindowsAuthenticationConfiguration.Resolve(builder.Configuration);
        windowsAuthentication.Register(builder.Services);
        var localFrontendRoot = LocalFrontendFiles.ResolveRoot(builder.Configuration, windowsAuthentication);
        builder.Services.AddSingleton(provider => new ClientUiDiagnostics(
            provider.GetRequiredService<ILogger<ClientUiDiagnostics>>(),
            localFrontendRoot is null ? [] : Directory.EnumerateFiles(Path.Combine(localFrontendRoot, "assets"))
                .Select(path => Path.GetFileName(path)), TimeProvider.System));
        if (localFrontendRoot is not null)
        {
            // Factory registration makes the host own and dispose the provider.
            builder.Services.AddSingleton(_ => new LocalFrontendFiles(localFrontendRoot));
        }
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<ActiveRoleSelection>();
        builder.Services.AddScoped<IActiveRoleSelection>(provider =>
        {
            // The existing opt-in local header adapter has no authoritative
            // Windows employee inventory. Keep its legacy testing contract
            // separate; Production always supplies the scoped role selector.
            var context = provider.GetRequiredService<IHttpContextAccessor>().HttpContext;
            if (context is not null && context.User.Identity?.IsAuthenticated != true
                && DevelopmentActorHeaderBoundaryMiddleware.AllowsHeaders(
                    provider.GetRequiredService<IWebHostEnvironment>(), provider.GetRequiredService<IConfiguration>()))
                return null!;
            return provider.GetRequiredService<ActiveRoleSelection>();
        });
        builder.Services.AddScoped<IActiveRoleSelectionInitializer>(provider =>
            provider.GetRequiredService<ActiveRoleSelection>());
        builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
        builder.Services.AddSingleton(new ActiveSessionRegistry(TimeProvider.System));

        // Resolved from the builder so a setting nobody can read stops the
        // host here rather than turning the protection quietly off. An
        // absent setting is the default and keeps the previous behaviour.
        var csrfProtection = CsrfProtectionConfiguration.Resolve(builder.Configuration);
        var allowInsecureHttpCookie =
            CsrfProtectionConfiguration.ResolveAllowInsecureHttpCookie(
                builder.Configuration,
                builder.Environment.EnvironmentName);
        builder.Services.AddSingleton(csrfProtection);
        DataProtectionPersistenceConfiguration.Register(builder.Services, builder.Configuration);
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfProtectionConfiguration.HeaderName;
            // The API client uses the token header for every write. Never let
            // antiforgery parse an arbitrary multipart body on bodyless routes
            // (preview/copy/import) before their own resource guards run.
            options.SuppressReadingTokenFromFormBody = true;
            options.Cookie.Name = CsrfProtectionConfiguration.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = allowInsecureHttpCookie
                ? CookieSecurePolicy.None
                : CookieSecurePolicy.Always;
            options.SuppressXFrameOptionsHeader = true;
        });
        builder.Services.AddBmsInfrastructure(builder.Configuration);
        builder.Services.AddHttpClient<Bms.Infrastructure.Persistence.CurrencyRateDirectoryService>(client =>
            client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        configureServices?.Invoke(builder.Services);
        if (!maintenanceReadOnly) builder.Services.AddHostedService<AccrualNotificationWorker>();
        if (!maintenanceReadOnly) builder.Services.AddHostedService<NationalBankCurrencyRateWorker>();
        if (!maintenanceReadOnly) builder.Services.AddHostedService<AccountingPaymentWorker>();
        if (!maintenanceReadOnly) builder.Services.AddHostedService<TaskMailWorker>();
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>(
                "database",
                tags: ["ready"],
                timeout: DatabaseReadinessHealthCheck.RegistrationTimeout)
            .AddCheck<UsageLogHealthCheck>("usage-log", tags: ["ready"], timeout: TimeSpan.FromSeconds(15))
            .AddCheck<AccrualAnalyticsSchemaHealthCheck>("accrual-analytics", tags: ["ready"], timeout: TimeSpan.FromSeconds(15))
            .AddCheck<CompanyVersionDeletionHealthCheck>("company-version-deletion", tags: ["ready"], timeout: TimeSpan.FromSeconds(15))
            .AddCheck<ActiveComposition454HealthCheck>("active-composition", tags: ["ready"], timeout: TimeSpan.FromSeconds(15))
            .AddCheck<PayrollEvidence455HealthCheck>("payroll-evidence", tags: ["ready"], timeout: TimeSpan.FromSeconds(15))
            .AddCheck<CostCenterPurpose457HealthCheck>("cost-department-purpose", tags: ["ready"], timeout: TimeSpan.FromSeconds(15));

        if(builder.Configuration.GetValue<bool>("Bms:StaffSubstitution471:Enabled"))
            builder.Services.AddHealthChecks().AddCheck<StaffSubstitutionReadiness471>("staff-substitutions471",tags:["ready"]);
        var app = builder.Build();

        ProductionHostFilteringStartupGuard.EnsureConfigured(
            app.Environment,
            app.Configuration);
        await ProductionAuthenticationStartupGuard.EnsureConfiguredAsync(
            app.Environment,
            app.Services.GetRequiredService<
                Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>());
        ProductionIdentityCompositionStartupGuard.EnsureConfigured(
            app.Environment,
            app.Services);

        // Resolved before the pipeline is built so an ambiguous or wildcard
        // trust setting stops the host rather than being discovered on the
        // first request. Null means the deployment did not switch forwarding
        // on, which is the default.
        var forwardedHeaders = ForwardedHeadersTrustConfiguration.Resolve(app.Configuration);

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            SuppressDiagnosticsCallback = context =>
                context.ExceptionHandledBy == ExceptionHandledType.ExceptionHandlerService,
        });
        if (forwardedHeaders is not null)
        {
            // Whatever decides the client address and the scheme has to run
            // before anything that reads them: HSTS, the security headers and
            // every authentication and actor boundary below.
            app.UseForwardedHeaders(forwardedHeaders);
        }
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }
        app.UseMiddleware<ApiResponseSecurityHeadersMiddleware>();
        if (windowsAuthentication.Enabled)
        {
            app.UseMiddleware<WindowsLocalTransportBoundaryMiddleware>();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<DevelopmentActorHeaderBoundaryMiddleware>();
        if (windowsAuthentication.Enabled)
        {
            app.UseMiddleware<WindowsEmployeeBoundaryMiddleware>();
        }
        app.UseMiddleware<ApiActorAuthenticationBoundaryMiddleware>();
        app.UseMiddleware<ActiveBmsRoleBoundaryMiddleware>();
        app.UseMiddleware<ActiveBmsProfileBoundaryMiddleware>();
        app.UseMiddleware<IntegrationAdministratorApiBoundaryMiddleware>();
        app.UseMiddleware<AuditorReadOnlyBoundaryMiddleware>();
        if (maintenanceReadOnly)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsJsonAsync(new { title = "BMS temporarily operates read-only." });
                    return;
                }
                await next(context);
            });
        }
        if (localFrontendRoot is not null)
        {
            app.Services.GetRequiredService<LocalFrontendFiles>().Use(app);
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });
        app.MapExpenseRequestEndpoints();
        app.MapExpenseRequestHistoryEndpoints();
        app.MapMyApprovalTasks355();
        app.MapReturnedRequestNotices377();
        app.MapBudgetEndpoints();
        app.MapTreasuryEndpoints();
        app.MapDatedTreasuryEndpoints();
        app.MapManualDeficitEndpoints();
        app.MapAccrualEndpoints();
        app.MapAccrualNotificationEndpoints();
        app.MapAccrualNotificationAdministrationEndpoints();
        app.MapReferenceDataEndpoints();
        app.MapCurrencyRateEndpoints();
        app.MapCashFlowAccrualSettingsEndpoints();
        app.MapCashFlowCatalogOrderEndpoints();
        app.MapApprovalMatrixEndpoints();
        app.MapSessionEndpoints();
        app.MapStaffSubstitution471();
        app.MapHelpGuideEndpoints();
        app.MapActiveSessionEndpoints();
        app.MapClientUiDiagnostics();
        app.MapIntegrationMonitorEndpoints();
        app.MapBusinessAuditMonitorEndpoints();
        app.MapChangeJournalEndpoints();
        app.MapBudgetExecutionRegister343();
        app.MapExchangeJournalEndpoints();
        app.MapTreasuryPaymentDateEndpoints();
        app.MapBusinessAuditDetailEndpoints();
        app.MapBusinessAuditSearchEndpoints();

        return app;
    }
}
