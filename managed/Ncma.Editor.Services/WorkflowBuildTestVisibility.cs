using System.Runtime.CompilerServices;

// The lifecycle foundation has no public executor/approval registration. Tests
// exercise the actual controller with explicitly synthetic, non-process leases.
[assembly: InternalsVisibleTo("Ncma.Editor.Services.Tests")]
