using System.IO;
using System.Security.Cryptography;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: proves the update guard in Fork/msbuild/Fork.targets is in effect, i.e. a fork build can never
// accept an official update package (which would silently replace the fork with the official release).
public sealed class ForkIdentityTests
{
    [Fact]
    public void EmbeddedUpdateKeyIsTheForkKeyNotTheOfficialOne()
    {
        string root = FindRepositoryRoot();
        string embedded = Normalize(SignedUpdateManifest.ReadPinnedPublicKey());
        string official = Normalize(File.ReadAllText(Path.Combine(root, "Security", "update-public-key.pem")));
        string fork = Normalize(File.ReadAllText(Path.Combine(root, "Fork", "msbuild", "no-update-public-key.pem")));

        Assert.NotEqual(official, embedded);
        Assert.Equal(fork, embedded);
    }

    [Fact]
    public void ForkUpdateKeyIsAValidP256PublicKey()
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(SignedUpdateManifest.ReadPinnedPublicKey());

        Assert.Equal(256, key.KeySize);
    }

    private static string Normalize(string pem) => pem.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "V-Notch.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the V-Notch repository root.");
    }
}
