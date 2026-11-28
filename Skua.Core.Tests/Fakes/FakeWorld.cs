using System.Reflection;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Core.Tests.Fakes;

/// <summary>
/// Game-state fake built on <see cref="DispatchProxy"/>.
///
/// The Skua bot interfaces are wide (IScriptInterface exposes 38 sub-services,
/// IScriptWait has 40 members). Hand-writing stubs for every member would bury
/// the assertions, so this fake answers members by dotted path:
/// <c>world.Set("Player.Playing", true)</c> makes <c>bot.Player.Playing</c>
/// return true, and every member that is read or called is recorded.
///
/// Unset members return an empty collection or the type default, which is how
/// a client that is logged in but standing in an empty room behaves.
/// </summary>
public sealed class FakeWorld
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _throwing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every member path the code under test touched, in order.</summary>
    public List<string> Calls { get; } = new();

    public IScriptInterface Bot => Service<IScriptInterface>("");
    public IScriptManager Manager => Service<IScriptManager>("Manager");
    public ILogService Logs => Service<ILogService>("Logs");

    /// <summary>Sets what a property getter or method returns at this path.</summary>
    public void Set(string path, object? value) => _values[path] = value;

    /// <summary>Stores a value without recording a call (used to fake a setter's effect).</summary>
    public void Supply(string path, object? value) => _values[path] = value;

    /// <summary>Value handed to a property setter at this path, or null if never assigned.</summary>
    public object? Received(string path) => _values.TryGetValue(path, out object? v) ? v : null;

    /// <summary>Makes this member throw, the way a generated binding does on a half-loaded map.</summary>
    public void ThrowOn(string path) => _throwing.Add(path);

    public bool WasCalled(string path) => Calls.Contains(path);

    public int CallCount(string path) => Calls.Count(c => c == path);

    /// <summary>The fake for one bot sub-service, created once per path.</summary>
    public T Service<T>(string path = "") where T : class => (T)MakeProxy(typeof(T), path);

    private object MakeProxy(Type contract, string path)
    {
        string key = "svc:" + (path.Length == 0 ? contract.Name : path);
        if (_proxies.TryGetValue(key, out object? existing))
            return existing;

        object proxy = DispatchProxy.Create(contract, typeof(StubProxy));
        ((StubProxy)proxy).Attach(this, path);
        _proxies[key] = proxy;
        return proxy;
    }

    internal bool TryValue(string path, out object? value) => _values.TryGetValue(path, out value);

    internal void Record(string path) => Calls.Add(path);

    internal bool ThrowsOn(string path) => _throwing.Contains(path);

    internal static object? Default(Type type)
    {
        if (type == typeof(bool)) return false;
        if (type == typeof(int)) return 0;
        if (type == typeof(long)) return 0L;
        if (type == typeof(double)) return 0d;
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return default(DateTime);
        if (type.IsGenericType)
        {
            Type def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(IList<>))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
            if (def == typeof(Dictionary<,>))
                return Activator.CreateInstance(type);
        }
        if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            return Activator.CreateInstance(type);
        return null;
    }

    public class StubProxy : DispatchProxy
    {
        private FakeWorld _world = null!;
        private string _path = "";

        internal void Attach(FakeWorld world, string path)
        {
            _world = world;
            _path = path;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            MethodInfo method = targetMethod!;
            string member = method.Name;

            if (member.StartsWith("set_", StringComparison.Ordinal))
            {
                string name = member[4..];
                string path = Join(name);
                _world.Supply(path, args is { Length: > 0 } ? args[0] : null);
                _world.Record("set:" + path);
                return null;
            }

            string key = member.StartsWith("get_", StringComparison.Ordinal) ? member[4..] : member;
            string fullPath = Join(key);
            _world.Record(fullPath);

            if (_world.ThrowsOn(fullPath))
                throw new InvalidOperationException($"fake binding read failed: {fullPath}");

            if (_world.TryValue(fullPath, out object? scripted))
            {
                // A scripted delegate stands in for behaviour a value cannot
                // express: blocking, throwing, or capturing arguments.
                if (scripted is Delegate handler)
                {
                    object?[] callArgs = args ?? Array.Empty<object?>();
                    MethodInfo signature = handler.Method;
                    ParameterInfo[] expected = signature.GetParameters();
                    try
                    {
                        if (expected.Length == callArgs.Length)
                            return handler.DynamicInvoke(callArgs);
                        object?[] trimmed = new object?[expected.Length];
                        for (int i = 0; i < expected.Length && i < callArgs.Length; i++)
                            trimmed[i] = callArgs[i];
                        return handler.DynamicInvoke(trimmed);
                    }
                    catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
                    {
                        // The router must see the game's exception type, for
                        // example OperationCanceledException from a kill timer.
                        throw wrapped.InnerException;
                    }
                }
                return scripted;
            }

            Type returnType = method.ReturnType;

            // Sub-services return a proxy of their own so a chain such as
            // bot.Quests.Tree walks the path without any setup.
            if (key.Length > 0 && returnType.Namespace is not null && returnType.Namespace.StartsWith("Skua.Core.Interfaces", StringComparison.Ordinal))
                return _world.MakeProxyOf(returnType, fullPath);

            // A scripted out value carries the TryX contract with it: if the
            // fake hands back a value, the method reported success.
            bool filledOut = FillOutParameters(method, fullPath, args);
            if (filledOut && returnType == typeof(bool))
                return true;
            return Completed(returnType);
        }

        private string Join(string name) => _path.Length == 0 ? name : _path + "." + name;

        private bool FillOutParameters(MethodInfo method, string fullPath, object?[]? args)
        {
            if (args is null)
                return false;
            bool filled = false;
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].IsOut)
                    continue;
                if (_world.TryValue(fullPath + ".out", out object? outValue))
                {
                    args[i] = outValue;
                    filled = true;
                }
            }
            return filled;
        }

        private static object? Completed(Type returnType)
        {
            if (returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                Type inner = returnType.GetGenericArguments()[0];
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(inner)
                    .Invoke(null, new[] { Default(inner) });
            }
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return Activator.CreateInstance(returnType, Default(returnType.GetGenericArguments()[0]));
            return Default(returnType);
        }

        internal static object? Default(Type type) => FakeWorld.Default(type);
    }

    internal object MakeProxyOf(Type contract, string path)
    {
        if (_proxies.TryGetValue(path, out object? existing) && contract.IsInstanceOfType(existing))
            return existing;
        object proxy = DispatchProxy.Create(contract, typeof(StubProxy));
        ((StubProxy)proxy).Attach(this, path);
        _proxies[path] = proxy;
        return proxy;
    }
}
