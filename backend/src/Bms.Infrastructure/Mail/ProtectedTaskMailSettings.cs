using System.Security.AccessControl;
using System.Security.Principal;

namespace Bms.Infrastructure.Mail;

public static class ProtectedTaskMailSettings
{
    public const string ServiceSid="S-1-5-80-3928983041-4098722958-1494938123-1762692221-3803012568";
    public static string PathOnVm => BmsInstallationSettings.MailSettingsPath;
    public static TaskMailDelivery Read()
    {
        if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Protected Windows mail configuration required.");
        var info=new FileInfo(PathOnVm);
        var directory=info.Directory??throw new InvalidOperationException("Mail parent directory is missing.");
        if(!info.Exists||info.Length>16384||(info.Attributes&FileAttributes.ReparsePoint)!=0
            ||(directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Protected mail file unavailable.");
        for(var parent=directory;parent is not null;parent=parent.Parent)
        {
            if((parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Mail file ancestor is a reparse path.");
            // Creating a sibling under D:\BMS cannot replace the protected
            // existing secrets directory. Ancestors must forbid replacement;
            // the direct parent must additionally forbid creating its files.
            var creation=parent.FullName==directory.FullName?FileSystemRights.WriteData|FileSystemRights.AppendData:0;
            foreach(FileSystemAccessRule rule in parent.GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier)))
                if(rule.AccessControlType==AccessControlType.Allow && (rule.PropagationFlags&PropagationFlags.InheritOnly)==0
                    && !TrustedSid(rule.IdentityReference.Value) && (rule.FileSystemRights&(creation|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.Delete|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership))!=0)
                    throw new InvalidOperationException("Mail file parent is writable by an untrusted principal.");
        }
        var acl=info.GetAccessControl();
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
            if(rule.AccessControlType==AccessControlType.Allow && !TrustedSid(rule.IdentityReference.Value)
                &&(rule.FileSystemRights&(FileSystemRights.ReadData|FileSystemRights.WriteData|FileSystemRights.AppendData|FileSystemRights.Delete|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership))!=0)
                throw new InvalidOperationException("Mail file ACL is not private.");
        var owner=acl.GetOwner(typeof(SecurityIdentifier))??throw new InvalidOperationException("Mail file owner is missing.");
        if(!TrustedSid(owner.Value))throw new InvalidOperationException("Mail file owner is not trusted.");
        return TaskMailDelivery.Parse(File.ReadAllText(PathOnVm));
    }
    internal static bool TrustedSid(string sid)=>sid is "S-1-5-18" or "S-1-5-32-544" || sid==ServiceSid;
}
