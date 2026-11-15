using System.Runtime.CompilerServices;

// M8.7 fixed original AIContracts graph tests the engine-wide observation core with deterministic readers.
// No production dependency on AI, public callback, external process selector or new test executor.
[assembly: InternalsVisibleTo("Ncma.AI.Tests")]
