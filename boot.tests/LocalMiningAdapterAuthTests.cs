using boot_portal;
using boot_portal.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace boot.tests;

[TestClass]
public sealed class LocalMiningAdapterAuthTests
{
    [TestMethod]
    public void GeneratesAndValidatesPersistentToken()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"gridpool-adapter-auth-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "adapter.token");
        try
        {
            var config = new PoolConfig { LocalAdapterTokenFile = path };
            var auth = new LocalMiningAdapterAuth(config, NullLogger<LocalMiningAdapterAuth>.Instance);
            string token = File.ReadAllText(path).Trim();

            Assert.IsTrue(token.Length >= 32);
            Assert.IsTrue(auth.IsAuthorized(token));
            Assert.IsFalse(auth.IsAuthorized(token + "bad"));
            Assert.IsFalse(auth.IsAuthorized(null));

            var reloaded = new LocalMiningAdapterAuth(config, NullLogger<LocalMiningAdapterAuth>.Instance);
            Assert.IsTrue(reloaded.IsAuthorized(token));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
