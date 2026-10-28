using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;

namespace Bms.Api.Security;

/// <summary>Explicit compiled frontend for the direct localhost Windows adapter.</summary>
public sealed class LocalFrontendFiles : IDisposable
{
    internal const string RootKey = "BmsWeb:LocalFrontendRoot";
    internal const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; " +
        "base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    private readonly PhysicalFileProvider files;

    public LocalFrontendFiles(string root) => files = new PhysicalFileProvider(root);

    public static string? ResolveRoot(IConfiguration configuration, WindowsAuthenticationConfiguration authentication)
    {
        var root = configuration[RootKey];
        if (root is null) return null;
        if (!authentication.Enabled)
            throw new InvalidOperationException("Local frontend hosting requires the localhost Windows authentication adapter.");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
            || root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Local frontend hosting requires an absolute local build folder.");
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot) || !File.Exists(Path.Combine(fullRoot, "index.html"))
            || !Directory.Exists(Path.Combine(fullRoot, "assets")))
            throw new InvalidOperationException("The selected frontend build must contain index.html and assets.");
        return fullRoot;
    }

    public void Use(WebApplication app)
    {
        // Reserve the entire API/health namespaces, including unknown routes.
        // There is no SPA fallback: BMS currently selects views in page state.
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/health"), branch =>
        {
            var defaults = new DefaultFilesOptions { FileProvider = files };
            defaults.DefaultFileNames.Clear();
            defaults.DefaultFileNames.Add("index.html");
            branch.UseDefaultFiles(defaults);
            var types = new FileExtensionContentTypeProvider();
            // Staff guides are served by the authenticated help API, never by
            // the anonymous bootstrap/static branch (including retained assets).
            types.Mappings.Remove(".pdf");
            foreach (var extension in new[] { ".bcmap", ".pfb", ".icc" })
                types.Mappings[extension] = "application/octet-stream";
            branch.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                ContentTypeProvider = types,
                OnPrepareResponse = context =>
                {
                    var headers = context.Context.Response.Headers;
                    headers.CacheControl = "no-cache";
                    // The isolated, locally bundled PDF worker decodes scanned
                    // images using WebAssembly. This does not allow JavaScript
                    // eval or external script/network sources in the app.
                    headers["Content-Security-Policy"] = context.File.Name.StartsWith("pdf.worker.", StringComparison.Ordinal)
                        ? ContentSecurityPolicy.Replace("script-src 'self';", "script-src 'self' 'wasm-unsafe-eval';", StringComparison.Ordinal)
                        : ContentSecurityPolicy;
                    headers["X-Frame-Options"] = "DENY";
                    headers["X-Content-Type-Options"] = "nosniff";
                    headers["Referrer-Policy"] = "no-referrer";
                }
            });
        });
    }

    public void Dispose() => files.Dispose();
}
