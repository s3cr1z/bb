using Xunit;

namespace Bb.Core.Tests;

/// <summary>
/// Tests that mutate the <c>BB_HOME</c> environment variable (and therefore
/// the on-disk root for config + credentials) must not run in parallel — they
/// would otherwise clobber each other's tempdir mid-flight. Tag both
/// CredentialStoreTests and BbConfigStoreTests with [Collection("Bb home env")].
/// </summary>
[CollectionDefinition("Bb home env", DisableParallelization = true)]
public sealed class BbHomeEnvCollection
{
}
