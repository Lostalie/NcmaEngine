using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

internal static class PipeSecurityTests
{
    internal static void Verify(NamedPipeClientStream client)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        using var identity=WindowsIdentity.GetCurrent();var security=client.GetAccessControl();
        if(security.GetOwner(typeof(SecurityIdentifier))!=identity.Owner||!security.AreAccessRulesProtected)
            throw new Exception("Pipe owner must match the token owner used by .NET CurrentUserOnly and DACL must be protected.");
        var rules=security.GetAccessRules(true,true,typeof(SecurityIdentifier));
        if(rules.Count!=1||rules[0] is not PipeAccessRule rule||rule.IdentityReference!=identity.User||
            rule.AccessControlType!=AccessControlType.Allow||rule.IsInherited)
            throw new Exception("Pipe DACL must grant ONLY the actual current User SID; no Administrators/Everyone/remote relaxation.");
        Console.WriteLine("PASS pipe protected actual-user-only DACL and token-owner/.NET CurrentUserOnly match (including elevated token)");
    }
}
