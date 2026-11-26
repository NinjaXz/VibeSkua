using System.Reflection;
using Skua.Core.Interfaces;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Contract tests for the members the agent control API needs.
///
/// The agent API (map/quest/shop/monster introspection for an AI client) is
/// written against <see cref="IScriptInterface"/>. Four members it relies on
/// exist only on the concrete script classes, or not at all, so the agent API
/// cannot compile against the interfaces. These tests pin the contract before
/// the implementation is lifted.
/// </summary>
public class AgentApiContractTests
{
    private static MethodInfo? Method(Type t, string name, Type[]? parameterTypes = null)
    {
        MethodInfo? m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
        if (m is null || parameterTypes is null)
            return m;
        return m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes) ? m : null;
    }

    [Fact]
    public void ScriptQuest_ExposesAbandon_OnInterfaceAndClass()
    {
        MethodInfo? onInterface = Method(typeof(IScriptQuest), "Abandon", new[] { typeof(int) });
        Assert.NotNull(onInterface);
        Assert.Equal(typeof(bool), onInterface!.ReturnType);

        Assert.NotNull(Method(typeof(Skua.Core.Scripts.ScriptQuest), "Abandon", new[] { typeof(int) }));
    }

    [Fact]
    public void ScriptShop_ExposesClose_OnInterfaceAndClass()
    {
        Assert.NotNull(Method(typeof(IScriptShop), "Close"));
        Assert.NotNull(Method(typeof(Skua.Core.Scripts.ScriptShop), "Close"));
    }

    [Fact]
    public void ScriptPlayer_ExposesAliveAndInCombat_OnInterface()
    {
        foreach (string name in new[] { "Alive", "InCombat" })
        {
            PropertyInfo? pi = typeof(IScriptPlayer).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(pi);
            Assert.Equal(typeof(bool), pi!.PropertyType);
            Assert.True(pi.CanRead, $"{name} must be readable");
        }
    }

    /// <summary>
    /// The agent API resolves the script folder from this constant. If the path
    /// helper moves, script writes land somewhere the client never loads.
    /// </summary>
    [Fact]
    public void ClientFileSources_ExposesScriptsDirectory()
    {
        PropertyInfo? pi = typeof(Skua.Core.Models.ClientFileSources)
            .GetProperty("SkuaScriptsDIR", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(pi);
        Assert.Equal(typeof(string), pi!.PropertyType);

        string path = (string)pi.GetValue(null)!;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith("Scripts", path.TrimEnd('\\', '/'));
    }
}
