using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin;

namespace XivSyncManager;

internal static class ReflectionAccess
{
    internal const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly ConditionalWeakTable<Type, MemberCache> Caches = new();

    internal static object? Read(object? instance, string name)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        var member = Caches.GetValue(type, static memberType => new(memberType)).Readers.GetOrAdd(name,
            static (memberName, memberType) => new(FindMember(memberType, memberName)), type).Member;
        return member switch
        {
            PropertyInfo property => property.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => null,
        };
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(name, Members | BindingFlags.DeclaredOnly);
            if (property != null) return property;
            var field = current.GetField(name, Members | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    internal static MethodInfo? Method(object instance, string name, int parameterCount) =>
        Caches.GetValue(instance.GetType(), static type => new(type)).Methods.Value.GetValueOrDefault((name, parameterCount));

    private sealed record Reader(MemberInfo? Member);

    private sealed class MemberCache(Type type)
    {
        internal ConcurrentDictionary<string, Reader> Readers { get; } = new(StringComparer.Ordinal);
        internal Lazy<Dictionary<(string Name, int ParameterCount), MethodInfo>> Methods { get; } = new(() => type.GetMethods(Members)
            .GroupBy(method => (method.Name, method.GetParameters().Length))
            .ToDictionary(group => group.Key, group => group.First()));
    }

    internal static object? Invoke(object instance, string name, params object?[] args) =>
        (Method(instance, name, args.Length) ?? throw new MissingMethodException(instance.GetType().FullName, name))
        .Invoke(instance, args);

    internal static object GetPluginInstance(IExposedPlugin exposed)
    {
        // Dalamud's wrapper captures a LocalPlugin. Search by type, not a compiler-generated field name.
        object? localPlugin = null;
        for (var type = exposed.GetType(); type != null && localPlugin == null; type = type.BaseType)
            localPlugin = type.GetFields(Members | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType.FullName == "Dalamud.Plugin.Internal.Types.LocalPlugin")
                .Select(f => f.GetValue(exposed)).FirstOrDefault(v => v != null);

        return Read(localPlugin, "instance") ?? throw new NotSupportedException("Dalamud's plugin instance interface has changed.");
    }

    internal static IServiceProvider GetServices(object plugin)
    {
        var host = Read(plugin, "_host") ?? Read(Read(plugin, "_lifecycle"), "_host");
        return Read(host, "Services") as IServiceProvider
            ?? throw new NotSupportedException("The sync plugin's service host is unavailable.");
    }

    internal static IEnumerable<object> Values(object collection)
    {
        var values = Read(collection, "Values") ?? collection;
        if (values is not IEnumerable enumerable)
            throw new NotSupportedException("The sync plugin's pair collection has changed.");
        return enumerable.Cast<object>().ToArray();
    }

    internal static string Text(object? value) => value?.ToString() ?? string.Empty;
    internal static bool Boolean(object? value) => value is true;
    internal static nint Address(object? value) => value switch
    {
        nint address => address,
        nuint address => unchecked((nint)address),
        _ => nint.Zero,
    };

    internal static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : exception;
}
