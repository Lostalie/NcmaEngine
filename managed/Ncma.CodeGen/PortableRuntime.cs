using System.Security.Cryptography;
using System.Text;

namespace Ncma.CodeGen
{
    internal static class PortableRuntime
    {
        internal static string SourceHash(string value)
        {
            using var hash = SHA256.Create();
            var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) text.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}

// Compiler marker only, internal to this netstandard2.0 generator assembly.
namespace System.Runtime.CompilerServices { internal sealed class IsExternalInit { } }
