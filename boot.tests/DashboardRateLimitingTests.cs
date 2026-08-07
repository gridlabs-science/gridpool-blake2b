using System.Reflection;
using boot_portal.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot.tests;

[TestClass]
public sealed class DashboardRateLimitingTests
{
    [TestMethod]
    public void EveryDashboardGetUsesTheDedicatedReadPolicy()
    {
        List<MethodInfo> actions = typeof(DashboardController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<HttpGetAttribute>() != null)
            .ToList();

        Assert.IsTrue(actions.Count >= 10, $"Expected dashboard GET actions, found {actions.Count}.");
        foreach (MethodInfo action in actions)
        {
            EnableRateLimitingAttribute? limiter = action.GetCustomAttribute<EnableRateLimitingAttribute>();
            Assert.IsNotNull(limiter, $"{action.Name} does not declare a read limiter.");
            Assert.AreEqual("dashboard-read", limiter.PolicyName, $"{action.Name} uses the wrong limiter.");
        }
    }
}
