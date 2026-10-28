using System.DirectoryServices.Protocols;
using System.Globalization;
using Bms.Infrastructure.Mail;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ActiveDirectoryEmployeeDirectory : ITaskMailRecipientDirectory
{
    public async Task<string> ResolveMailAsync(string actorExternalId,CancellationToken token)
    {
        if(!IsEnabled())throw new InvalidOperationException("Active Directory mail resolution is disabled.");
        using var connection=CreateConnection();connection.Bind();var baseDn=await ResolveBaseDnAsync(connection,token);
        var filter=$"(&(objectCategory=person)(objectClass=user){DisabledAccountFilter}(objectSid={SidFilter(actorExternalId)}))";
        var response=(SearchResponse)await SendRequestAsync(connection,new SearchRequest(baseDn,filter,SearchScope.Subtree,"objectSid","mail"),token);
        if(response.Entries.Count!=1||!string.Equals(ReadSid(response.Entries[0]),actorExternalId,StringComparison.Ordinal))
            throw new InvalidOperationException("Unique active directory recipient is unavailable.");
        var mail=ReadText(response.Entries[0],"mail");
        if(!TaskMailDelivery.ValidAddress(mail))throw new InvalidOperationException("Corporate mail address is unavailable.");
        return mail??throw new InvalidOperationException("Mail address is missing.");
    }
    internal static string SidFilter(string sid)
    {
        var p=sid.Split('-');
        if(p.Length is <4 or >18||p[0]!="S"||p[1]!="1"||!ulong.TryParse(p[2],NumberStyles.None,CultureInfo.InvariantCulture,out var authority)||authority>0xffffffffffff)
            throw new InvalidOperationException("Invalid recipient SID.");
        var bytes=new byte[8+(p.Length-3)*4];bytes[0]=1;bytes[1]=(byte)(p.Length-3);
        for(var i=0;i<6;i++)bytes[7-i]=(byte)(authority>>(i*8));
        for(var i=3;i<p.Length;i++)
        {
            if(!uint.TryParse(p[i],NumberStyles.None,CultureInfo.InvariantCulture,out var part))throw new InvalidOperationException("Invalid recipient SID.");
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8+(i-3)*4,4),part);
        }
        return string.Concat(bytes.Select(b=>"\\"+b.ToString("X2",CultureInfo.InvariantCulture)));
    }
}
