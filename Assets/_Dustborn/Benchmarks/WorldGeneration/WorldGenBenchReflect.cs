using System;
using System.Reflection;

public static class WorldGenBenchReflect
{
    private const BindingFlags ANY = BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic;

    public static MethodInfo Method(Type type, string name, params Type[] parameters)
    {
        MethodInfo method = parameters.Length == 0
            ? type.GetMethod(name, ANY)
            : type.GetMethod(name, ANY, null, parameters, null);

        if (method == null)
            throw new InvalidOperationException($"{type.Name} has no method '{name}'.");

        return method;
    }

    public static T Bind<T>(object target, string name) where T : Delegate
    {
        Type type = target.GetType();
        MethodInfo method = FindByDelegate<T>(type, name);
        return (T)Delegate.CreateDelegate(typeof(T), target, method);
    }

    public static T BindStatic<T>(Type type, string name) where T : Delegate
    {
        MethodInfo method = FindByDelegate<T>(type, name);
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    public static object Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, ANY);

        if (field == null)
            throw new InvalidOperationException($"{target.GetType().Name} has no field '{name}'.");

        return field.GetValue(target);
    }

    public static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, ANY);

        if (field == null)
            throw new InvalidOperationException($"{target.GetType().Name} has no field '{name}'.");

        field.SetValue(target, value);
    }

    private static MethodInfo FindByDelegate<T>(Type type, string name) where T : Delegate
    {
        MethodInfo signature = typeof(T).GetMethod("Invoke");
        ParameterInfo[] wanted = signature.GetParameters();

        foreach (MethodInfo candidate in type.GetMethods(ANY))
        {
            if (candidate.Name != name)
                continue;

            ParameterInfo[] parameters = candidate.GetParameters();

            if (parameters.Length != wanted.Length)
                continue;

            bool matched = true;

            for (int index = 0; index < parameters.Length && matched; index++)
                matched = parameters[index].ParameterType == wanted[index].ParameterType;

            if (matched)
                return candidate;
        }

        throw new InvalidOperationException($"{type.Name} has no method '{name}' matching {typeof(T).Name}.");
    }
}
