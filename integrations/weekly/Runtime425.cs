using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Bms.WeeklyPrimary425;

internal static class Runtime425
{
    internal const string Owned = @"D:\BMS\artifacts\bms-weekly-primary425-20261025\runner425";
    internal const string Release = @"D:\BMS\releases\catalog485-20261028";
    internal const string Runtime = @"D:\BMS\runtimes\attachments-10.0.13-20261014-220819";
    internal const string ReleaseSha = "1943688D3A6E6C33C42CC186862633D2FA0A349CF373A3266D3CA8CB02B22724";
    internal static bool IsSystem() => OperatingSystem.IsWindows() && WindowsIdentity.GetCurrent().User?.Value == "S-1-5-18";
    internal static void Ordinary(string path)
    {
        for (var node = path; node is not null; node = Path.GetDirectoryName(node))
            Wire425.Require((File.GetAttributes(node) & FileAttributes.ReparsePoint) == 0, "REPARSE_PATH");
    }
    internal static void PrivateAcl(string path, bool directory, bool protectedRequired = false)
    {
        Wire425.Require(OperatingSystem.IsWindows(), "WINDOWS_REQUIRED");
        if (!OperatingSystem.IsWindows()) return;
        Ordinary(path);
        FileSystemSecurity acl = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        Wire425.Require(acl.GetOwner(typeof(SecurityIdentifier))?.Value is "S-1-5-18" or "S-1-5-32-544", "PRIVATE_OWNER_REQUIRED");
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Wire425.Require(rules.Length == 2 && (!protectedRequired || acl.AreAccessRulesProtected), "PRIVATE_ACL_REQUIRED");
        foreach (var r in rules) Wire425.Require(r.AccessControlType == AccessControlType.Allow
            && (r.IdentityReference.Value is "S-1-5-18" or "S-1-5-32-544")
            && (r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl, "PRIVATE_ACL_SCOPE");
        var ancestor = directory ? new DirectoryInfo(path).Parent : new FileInfo(path).Directory;
        var replacementRights = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        for (; ancestor is not null; ancestor = ancestor.Parent)
            foreach (FileSystemAccessRule rule in ancestor.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                    && rule.IdentityReference.Value is not ("S-1-5-18" or "S-1-5-32-544")
                    && (rule.FileSystemRights & replacementRights) != 0) throw new Guard425("UNTRUSTED_ANCESTOR_REPLACEMENT_RIGHTS");
    }
    internal static Config425 ReadConfig()
    {
        var path = Path.Combine(Owned, "runner-config.json"); PrivateAcl(path, false);
        Wire425.Require(new FileInfo(path).Length is > 0 and <= 16384, "CONFIG_BOUND");
        var c = Wire425.Parse<Config425>(File.ReadAllText(path, Wire425.Utf8));
        Wire425.Require(c.SchemaVersion == 1 && c.DeploymentId != Guid.Empty && c.LedgerId != Guid.Empty
            && c.Approval == Wire425.Approval && (c.LdapServer is "directory.example.invalid" or "directory.example.invalid")
            && c.PackageManifestSha256.Length == 64 && c.PackageManifestSha256.All(char.IsAsciiHexDigit), "CONFIG_INVALID");
        return c;
    }
    internal static Config425 Verify()
    {
        Wire425.Require(OperatingSystem.IsWindows() && Environment.MachineName == "BMS" && Environment.Version == new Version(10, 0, 13)
            && Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) == Owned
            && typeof(object).Assembly.Location == Runtime + @"\shared\Microsoft.NETCore.App\10.0.13\System.Private.CoreLib.dll", "RUNTIME_REQUIRED");
        PrivateAcl(Owned, true, true); PrivateAcl(Path.GetDirectoryName(Owned)!, true, true);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Qyzylorda Standard Time");
        Wire425.Require(TimeZoneInfo.Local.Id == zone.Id && zone.BaseUtcOffset == TimeSpan.FromHours(5), "TIMEZONE_DRIFT");
        Wire425.Require(Wire425.FileHash(Path.Combine(Release, "manifest.json")) == ReleaseSha, "ACTIVE_RELEASE_DRIFT");
        var c = ReadConfig(); var manifestPath = Path.Combine(Owned, "package-manifest425.json"); PrivateAcl(manifestPath, false);
        Wire425.Require(Wire425.FileHash(manifestPath) == c.PackageManifestSha256, "PACKAGE_MANIFEST_DRIFT");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath, Wire425.Utf8));
        var files = manifest.RootElement.GetProperty("Files");
        Wire425.Require(files.GetArrayLength() is >= 38 and <= 100, "PACKAGE_MANIFEST_BOUND");
        foreach (var f in files.EnumerateArray())
        {
            var relative = f.GetProperty("Path").GetString()!;
            Wire425.Require(relative.Length is > 0 and < 180 && !Path.IsPathFullyQualified(relative) && !relative.Contains("..", StringComparison.Ordinal)
                && !relative.Contains(':') && !relative.Any(char.IsControl), "PACKAGE_PATH_INVALID");
            var path = Path.GetFullPath(Path.Combine(Owned, relative));
            Wire425.Require(path.StartsWith(Owned + "\\", StringComparison.Ordinal), "PACKAGE_PATH_ESCAPE");
            PrivateAcl(path, false); Wire425.Require(Wire425.FileHash(path) == f.GetProperty("Sha256").GetString(), "PACKAGE_FILE_DRIFT");
        }
        using var referencePins = JsonDocument.Parse(File.ReadAllText(Path.Combine(Owned, "reference-pins425.json"), Wire425.Utf8));
        Wire425.Require(referencePins.RootElement.GetArrayLength() == 35, "REFERENCE_BOUND");
        using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(Release, "manifest.json"), Wire425.Utf8));
        foreach (var pin in referencePins.RootElement.EnumerateArray())
        {
            var name = pin.GetProperty("Name").GetString()!; var source = pin.GetProperty("SourcePath").GetString()!; var hash = pin.GetProperty("Sha256").GetString();
            Wire425.Require(name.EndsWith(".dll", StringComparison.Ordinal) && !name.Contains('\\') && !name.Contains('/')
                && new[] { Release + @"\service\" + name, Runtime + @"\shared\Microsoft.NETCore.App\10.0.13\" + name, Runtime + @"\shared\Microsoft.AspNetCore.App\10.0.13\" + name }.Contains(source, StringComparer.Ordinal)
                && Wire425.FileHash(source) == hash && Wire425.FileHash(Path.Combine(Owned, name)) == hash, "REFERENCE_ACTIVE_DRIFT");
            if (pin.GetProperty("SourceKind").GetString() == "release-manifest")
                Wire425.Require(release.RootElement.GetProperty("Files").EnumerateArray().Single(x => x.GetProperty("Path").GetString()!.Replace('\\', '/') == "service/" + name).GetProperty("Sha256").GetString() == hash, "REFERENCE_MANIFEST_DRIFT");
        }
        var state = Path.Combine(Owned, "state"); PrivateAcl(state, true);
        var initialPath = Path.Combine(state, "initialization.json"); PrivateAcl(initialPath, false);
        var initial = Wire425.Parse<InitialState425>(File.ReadAllText(initialPath, Wire425.Utf8));
        Wire425.Require(initial.SchemaVersion == 1 && initial.LedgerId == c.LedgerId, "LEDGER_INITIALIZATION_REQUIRED");
        return c;
    }
}
