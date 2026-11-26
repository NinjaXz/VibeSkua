using System.Runtime.CompilerServices;

// The agent API parser and the SWF label reader are implementation detail, but
// the tests exercise them directly with fixtures instead of a game client.
[assembly: InternalsVisibleTo("Skua.Core.Tests")]
