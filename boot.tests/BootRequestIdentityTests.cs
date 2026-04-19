using System.Net;
using boot_portal.Utils;
using Microsoft.AspNetCore.Http;

namespace boot.tests;

[TestClass]
public sealed class BootRequestIdentityTests
{
    [TestMethod]
    public void GetClientKeyIgnoresForwardedHeadersFromUntrustedDirectClient()
    {
        var config = new PoolConfig
        {
            TrustedForwardedProxyRanges = ["127.0.0.1/32"]
        };
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.77";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.88";

        string clientKey = BootRequestIdentity.GetClientKey(context, config);

        Assert.AreEqual("198.51.100.10", clientKey);
    }

    [TestMethod]
    public void GetClientKeyUsesForwardedHeadersFromTrustedProxy()
    {
        var config = new PoolConfig
        {
            TrustedForwardedProxyRanges = ["127.0.0.1/32", "::1/128"]
        };
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.77, 10.0.0.1";

        string clientKey = BootRequestIdentity.GetClientKey(context, config);

        Assert.AreEqual("203.0.113.77", clientKey);
    }

    [TestMethod]
    public void TrustedProxyRangesHonorCidrMatching()
    {
        var config = new PoolConfig
        {
            TrustedForwardedProxyRanges = ["10.0.0.0/8"]
        };
        var trustedContext = new DefaultHttpContext();
        trustedContext.Connection.RemoteIpAddress = IPAddress.Parse("10.25.4.9");
        trustedContext.Request.Headers["X-Real-IP"] = "203.0.113.44";

        var untrustedContext = new DefaultHttpContext();
        untrustedContext.Connection.RemoteIpAddress = IPAddress.Parse("11.25.4.9");
        untrustedContext.Request.Headers["X-Real-IP"] = "203.0.113.44";

        Assert.AreEqual("203.0.113.44", BootRequestIdentity.GetClientKey(trustedContext, config));
        Assert.AreEqual("11.25.4.9", BootRequestIdentity.GetClientKey(untrustedContext, config));
    }
}
