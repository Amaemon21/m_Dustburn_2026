using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

static class MonoRewrite
{
    public const string NET = "--net";
    private const string REWRITTEN = "--mono-rewritten";
    private const string STRICT_SUFFIX = "$strict";
    private static readonly string[] UnityTypes = { "UnityEngine.Mathf", "UnityEngine.Vector2", "UnityEngine.Vector3" };

    public static bool Wanted(string[] args)
    {
        return Array.IndexOf(args, NET) < 0 && Array.IndexOf(args, REWRITTEN) < 0;
    }

    public static int Run(string[] args)
    {
        string source = typeof(MonoRewrite).Assembly.Location;
        string directory = Path.GetDirectoryName(source);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(directory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));

        using ModuleDefinition module = ModuleDefinition.ReadModule(source, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });

        int transplanted = Transplant(module);
        HashSet<MethodDefinition> strict = StrictClosure(module);
        int cloned = CloneStrict(module, strict);
        int widened = Widen(module);

        Console.WriteLine($"[mono] Unity bodies {transplanted}, strict methods {strict.Count} ({cloned} cloned), widened float loads {widened}");

        using var stream = new MemoryStream();
        module.Write(stream);

        var context = new RewrittenContext(directory);
        Assembly rewritten = context.LoadFromStream(new MemoryStream(stream.ToArray()));
        MethodInfo main = rewritten.GetType("Harness").GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        string[] rest = new[] { REWRITTEN }.Concat(args.Where(arg => arg != "--mono")).ToArray();

        try
        {
            main.Invoke(null, new object[] { rest });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }

        return 0;
    }

    private sealed class RewrittenContext : AssemblyLoadContext
    {
        private readonly string _directory;

        public RewrittenContext(string directory) : base("mono-float", isCollectible: false)
        {
            _directory = directory;
        }

        protected override Assembly Load(AssemblyName name)
        {
            string path = Path.Combine(_directory, name.Name + ".dll");
            return File.Exists(path) && name.Name != "HeadlessCheck" ? Default.LoadFromAssemblyPath(path) : null;
        }
    }

    private static string UnityCoreModule()
    {
        string props = new[]
        {
            Path.GetFullPath("../../Tools/UnityCompileCheck/UnityReferences.props"),
            Path.Combine(AppContext.BaseDirectory, "../../../../UnityCompileCheck/UnityReferences.props")
        }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Tools/UnityCompileCheck/UnityReferences.props names the Unity install; run the harness from a folder two levels under the project");

        Match managed = Regex.Match(File.ReadAllText(props), @"<UnityManaged[^>]*>([^<]+)</UnityManaged>");

        if (!managed.Success)
            throw new InvalidOperationException("UnityManaged is not set in UnityReferences.props");

        return Path.Combine(managed.Groups[1].Value.Trim(), "UnityEngine", "UnityEngine.CoreModule.dll");
    }

    private static int Transplant(ModuleDefinition module)
    {
        string path = UnityCoreModule();

        if (!File.Exists(path))
            throw new FileNotFoundException("Unity's CoreModule is needed for --mono", path);

        using ModuleDefinition unity = ModuleDefinition.ReadModule(path);
        int count = 0;

        foreach (string name in UnityTypes)
        {
            TypeDefinition stub = module.GetType(name);
            TypeDefinition original = unity.GetType(name);

            foreach (MethodDefinition method in stub.Methods.Where(m => m.HasBody))
            {
                MethodDefinition source = original.Methods.FirstOrDefault(candidate => Same(candidate, method));

                if (source == null || !source.HasBody)
                {
                    Console.WriteLine($"[mono] keeps the stub body of {name}::{method.Name}: no managed body in Unity");
                    continue;
                }

                if (TryCopy(module, stub, source, method, out string reason))
                    count++;
                else
                    Console.WriteLine($"[mono] keeps the stub body of {name}::{method.Name}: {reason}");
            }
        }

        return count;
    }

    private static bool Same(MethodDefinition unity, MethodDefinition stub)
    {
        if (unity.Name != stub.Name || unity.Parameters.Count != stub.Parameters.Count || unity.IsStatic != stub.IsStatic)
            return false;

        if (unity.ReturnType.FullName != stub.ReturnType.FullName)
            return false;

        for (int i = 0; i < unity.Parameters.Count; i++)
        {
            if (unity.Parameters[i].ParameterType.FullName != stub.Parameters[i].ParameterType.FullName)
                return false;
        }

        return true;
    }

    private static bool TryCopy(ModuleDefinition module, TypeDefinition stub, MethodDefinition source, MethodDefinition target, out string reason)
    {
        reason = null;
        var instructions = new List<Instruction>();
        var map = new Dictionary<Instruction, Instruction>();
        var locals = source.Body.Variables.Select(variable => new VariableDefinition(Import(module, variable.VariableType, out _))).ToList();

        if (locals.Any(local => local.VariableType == null))
        {
            reason = "a local type is missing";
            return false;
        }

        foreach (Instruction original in source.Body.Instructions)
        {
            Instruction copy;

            switch (original.Operand)
            {
                case MethodReference method:
                    MethodReference resolved = ImportMethod(module, stub, method, out reason);

                    if (resolved == null)
                        return false;

                    copy = Instruction.Create(original.OpCode, resolved);
                    break;
                case FieldReference field:
                    FieldReference resolvedField = ImportField(module, stub, field, out reason);

                    if (resolvedField == null)
                        return false;

                    copy = Instruction.Create(original.OpCode, resolvedField);
                    break;
                case TypeReference type:
                    TypeReference resolvedType = Import(module, type, out reason);

                    if (resolvedType == null)
                        return false;

                    copy = Instruction.Create(original.OpCode, resolvedType);
                    break;
                case VariableDefinition variable:
                    copy = Instruction.Create(original.OpCode, locals[variable.Index]);
                    break;
                case ParameterDefinition parameter:
                    copy = Instruction.Create(original.OpCode, target.Parameters[parameter.Index]);
                    break;
                case Instruction:
                case Instruction[]:
                    copy = Instruction.Create(OpCodes.Nop);
                    copy.OpCode = original.OpCode;
                    break;
                case null:
                    copy = Instruction.Create(original.OpCode);
                    break;
                case float single:
                    copy = Instruction.Create(original.OpCode, single);
                    break;
                case double wide:
                    copy = Instruction.Create(original.OpCode, wide);
                    break;
                case int integer:
                    copy = Instruction.Create(original.OpCode, integer);
                    break;
                case long big:
                    copy = Instruction.Create(original.OpCode, big);
                    break;
                case sbyte small:
                    copy = Instruction.Create(original.OpCode, small);
                    break;
                case string text:
                    copy = Instruction.Create(original.OpCode, text);
                    break;
                default:
                    reason = $"operand {original.Operand.GetType().Name} is not handled";
                    return false;
            }

            instructions.Add(copy);
            map[original] = copy;
        }

        for (int i = 0; i < instructions.Count; i++)
        {
            Instruction original = source.Body.Instructions[i];

            if (original.Operand is Instruction branch)
                instructions[i].Operand = map[branch];
            else if (original.Operand is Instruction[] table)
                instructions[i].Operand = table.Select(entry => map[entry]).ToArray();
        }

        if (source.Body.HasExceptionHandlers)
        {
            reason = "exception handlers are not copied";
            return false;
        }

        MethodBody body = target.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();

        foreach (VariableDefinition local in locals)
            body.Variables.Add(local);

        foreach (Instruction instruction in instructions)
            body.Instructions.Add(instruction);

        body.InitLocals = source.Body.InitLocals;
        return true;
    }

    private static TypeReference Import(ModuleDefinition module, TypeReference type, out string reason)
    {
        reason = null;

        if (type is ByReferenceType byReference)
        {
            TypeReference element = Import(module, byReference.ElementType, out reason);
            return element == null ? null : new ByReferenceType(element);
        }

        if (type is ArrayType array)
        {
            TypeReference element = Import(module, array.ElementType, out reason);
            return element == null ? null : new ArrayType(element, array.Rank);
        }

        if (type.Namespace == "System" || type.Scope.Name.StartsWith("mscorlib") || type.Scope.Name.StartsWith("netstandard") || type.Scope.Name.StartsWith("System"))
            return module.ImportReference(Type.GetType($"{type.Namespace}.{type.Name}") ?? throw new InvalidOperationException(type.FullName));

        TypeDefinition local = module.GetType(type.FullName);

        if (local == null)
            reason = $"type {type.FullName} is not in the harness";

        return local;
    }

    private static MethodReference ImportMethod(ModuleDefinition module, TypeDefinition stub, MethodReference method, out string reason)
    {
        TypeReference owner = Import(module, method.DeclaringType, out reason);

        if (owner == null)
            return null;

        if (owner is TypeDefinition definition)
        {
            MethodDefinition match = definition.Methods.FirstOrDefault(candidate => candidate.Name == method.Name
                && candidate.Parameters.Count == method.Parameters.Count
                && candidate.ReturnType.FullName == method.ReturnType.FullName
                && candidate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName)));

            if (match == null)
                reason = $"method {method.DeclaringType.Name}::{method.Name} is not in the harness";

            return match;
        }

        Type runtime = Type.GetType($"{owner.Namespace}.{owner.Name}");
        Type[] parameters = method.Parameters.Select(p => Type.GetType($"{p.ParameterType.Namespace}.{p.ParameterType.Name}")).ToArray();
        MethodBase found = method.Name == ".ctor"
            ? runtime?.GetConstructor(parameters)
            : runtime?.GetMethod(method.Name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, parameters, null);

        if (found == null)
        {
            reason = $"system method {owner.Name}::{method.Name} not found";
            return null;
        }

        return module.ImportReference(found);
    }

    private static FieldReference ImportField(ModuleDefinition module, TypeDefinition stub, FieldReference field, out string reason)
    {
        TypeReference owner = Import(module, field.DeclaringType, out reason);

        if (owner is not TypeDefinition definition)
        {
            reason ??= $"field owner {field.DeclaringType.FullName} is not a harness type";
            return null;
        }

        FieldDefinition match = definition.Fields.FirstOrDefault(candidate => candidate.Name == field.Name);

        if (match == null)
            reason = $"field {field.DeclaringType.Name}.{field.Name} is not in the harness";

        return match;
    }

    private static HashSet<MethodDefinition> StrictClosure(ModuleDefinition module)
    {
        var strict = new HashSet<MethodDefinition>();
        var pending = new Stack<MethodDefinition>();

        foreach (TypeDefinition type in module.GetTypes())
        {
            if (!type.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "BurstCompileAttribute"))
                continue;

            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
            {
                if (strict.Add(method))
                    pending.Push(method);
            }
        }

        while (pending.Count > 0)
        {
            MethodDefinition method = pending.Pop();

            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference called || called.Module != module)
                    continue;

                MethodDefinition target = called.Resolve();

                if (target == null || !target.HasBody || target.Module != module)
                    continue;

                if (strict.Add(target))
                    pending.Push(target);
            }
        }

        return strict;
    }

    private static readonly Dictionary<MethodDefinition, MethodDefinition> Clones = new();

    private static int CloneStrict(ModuleDefinition module, HashSet<MethodDefinition> strict)
    {
        bool InBurstType(MethodDefinition method) => method.DeclaringType.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "BurstCompileAttribute");

        foreach (MethodDefinition method in strict)
        {
            if (InBurstType(method) || method.HasGenericParameters || method.DeclaringType.HasGenericParameters || !HasFloatWork(method))
                continue;

            var clone = new MethodDefinition(method.Name + STRICT_SUFFIX, method.Attributes & ~MethodAttributes.Virtual & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName, method.ReturnType);

            foreach (ParameterDefinition parameter in method.Parameters)
                clone.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));

            clone.HasThis = method.HasThis;
            clone.Body.InitLocals = method.Body.InitLocals;

            foreach (VariableDefinition variable in method.Body.Variables)
                clone.Body.Variables.Add(new VariableDefinition(variable.VariableType));

            var map = new Dictionary<Instruction, Instruction>();

            foreach (Instruction original in method.Body.Instructions)
            {
                Instruction copy = Instruction.Create(OpCodes.Nop);
                copy.OpCode = original.OpCode;
                copy.Operand = original.Operand switch
                {
                    VariableDefinition variable => clone.Body.Variables[variable.Index],
                    ParameterDefinition parameter when parameter.Index >= 0 => clone.Parameters[parameter.Index],
                    _ => original.Operand
                };

                clone.Body.Instructions.Add(copy);
                map[original] = copy;
            }

            foreach (Instruction copy in clone.Body.Instructions)
            {
                if (copy.Operand is Instruction branch)
                    copy.Operand = map[branch];
                else if (copy.Operand is Instruction[] table)
                    copy.Operand = table.Select(entry => map[entry]).ToArray();
            }

            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            {
                clone.Body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
                {
                    TryStart = map[handler.TryStart],
                    TryEnd = handler.TryEnd == null ? null : map[handler.TryEnd],
                    HandlerStart = map[handler.HandlerStart],
                    HandlerEnd = handler.HandlerEnd == null ? null : map[handler.HandlerEnd],
                    CatchType = handler.CatchType,
                    FilterStart = handler.FilterStart == null ? null : map[handler.FilterStart]
                });
            }

            method.DeclaringType.Methods.Add(clone);
            Clones[method] = clone;
        }

        foreach (MethodDefinition method in strict.Where(InBurstType).Concat(Clones.Values).ToList())
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference called && called.Module == module && called.Resolve() is { } target && Clones.TryGetValue(target, out MethodDefinition clone)
                    && instruction.OpCode != OpCodes.Newobj && instruction.OpCode != OpCodes.Ldftn && instruction.OpCode != OpCodes.Ldvirtftn)
                    instruction.Operand = clone;
            }
        }

        return Clones.Count;
    }

    private static bool HasFloatWork(MethodDefinition method)
    {
        return method.Body.Instructions.Any(instruction => PushesSingle(method, instruction)) || method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called && called.Resolve() is { } target && Clones.ContainsKey(target));
    }

    private static int Widen(ModuleDefinition module)
    {
        var strict = new HashSet<MethodDefinition>(Clones.Values);
        int widened = 0;

        foreach (TypeDefinition type in module.GetTypes())
        {
            bool burst = type.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "BurstCompileAttribute");

            if (burst || type.Name == nameof(MonoRewrite) || type.FullName.StartsWith("MonoRewrite"))
                continue;

            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody && !strict.Contains(m)))
            {
                MethodBody body = method.Body;
                body.SimplifyMacros();
                ILProcessor processor = body.GetILProcessor();
                TypeReference single = module.TypeSystem.Single;

                foreach (Instruction instruction in body.Instructions.ToList())
                {
                    if (StoresSingle(method, instruction))
                    {
                        var store = Instruction.Create(OpCodes.Nop);
                        store.OpCode = instruction.OpCode;
                        store.Operand = instruction.Operand;
                        instruction.OpCode = OpCodes.Conv_R4;
                        instruction.Operand = null;
                        processor.InsertAfter(instruction, store);

                        if (PushesSingle(method, store))
                        {
                            processor.InsertAfter(store, processor.Create(OpCodes.Conv_R8));
                            widened++;
                        }

                        continue;
                    }

                    if (!PushesSingle(method, instruction))
                        continue;

                    processor.InsertAfter(instruction, processor.Create(OpCodes.Conv_R8));
                    widened++;
                }

                body.OptimizeMacros();
            }
        }

        return widened;
    }

    private static bool StoresSingle(MethodDefinition method, Instruction instruction)
    {
        OpCode code = instruction.OpCode;

        if (code == OpCodes.Stloc)
            return ((VariableDefinition)instruction.Operand).VariableType.MetadataType == MetadataType.Single;

        if (code == OpCodes.Starg)
            return ((ParameterDefinition)instruction.Operand).ParameterType.MetadataType == MetadataType.Single;

        if (code == OpCodes.Stfld || code == OpCodes.Stsfld)
            return ((FieldReference)instruction.Operand).FieldType.MetadataType == MetadataType.Single;

        if (code == OpCodes.Stelem_R4 || code == OpCodes.Stind_R4)
            return true;

        if (code == OpCodes.Stelem_Any || code == OpCodes.Stobj || code == OpCodes.Box)
            return ((TypeReference)instruction.Operand).MetadataType == MetadataType.Single;

        if (code == OpCodes.Ret)
            return method.ReturnType.MetadataType == MetadataType.Single;

        if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
        {
            var called = (MethodReference)instruction.Operand;
            return called.Parameters.Count > 0 && called.Parameters[^1].ParameterType.MetadataType == MetadataType.Single;
        }

        return false;
    }

    private static bool PushesSingle(MethodDefinition method, Instruction instruction)
    {
        OpCode code = instruction.OpCode;

        if (code == OpCodes.Ldc_R4 || code == OpCodes.Ldelem_R4 || code == OpCodes.Ldind_R4 || code == OpCodes.Conv_R4)
            return true;

        if (code == OpCodes.Ldloc || code == OpCodes.Ldloc_S || code == OpCodes.Ldloc_0 || code == OpCodes.Ldloc_1 || code == OpCodes.Ldloc_2 || code == OpCodes.Ldloc_3)
            return LocalType(method, instruction)?.MetadataType == MetadataType.Single;

        if (code == OpCodes.Ldarg || code == OpCodes.Ldarg_S || code == OpCodes.Ldarg_0 || code == OpCodes.Ldarg_1 || code == OpCodes.Ldarg_2 || code == OpCodes.Ldarg_3)
            return ArgumentType(method, instruction)?.MetadataType == MetadataType.Single;

        if (code == OpCodes.Ldfld || code == OpCodes.Ldsfld)
            return ((FieldReference)instruction.Operand).FieldType.MetadataType == MetadataType.Single;

        if (code == OpCodes.Ldelem_Any || code == OpCodes.Ldobj)
            return ((TypeReference)instruction.Operand).MetadataType == MetadataType.Single;

        if (code == OpCodes.Call || code == OpCodes.Callvirt)
            return ((MethodReference)instruction.Operand).ReturnType.MetadataType == MetadataType.Single;

        return false;
    }

    private static TypeReference LocalType(MethodDefinition method, Instruction instruction)
    {
        if (instruction.Operand is VariableDefinition variable)
            return variable.VariableType;

        int index = instruction.OpCode == OpCodes.Ldloc_0 ? 0 : instruction.OpCode == OpCodes.Ldloc_1 ? 1 : instruction.OpCode == OpCodes.Ldloc_2 ? 2 : 3;
        return method.Body.Variables[index].VariableType;
    }

    private static TypeReference ArgumentType(MethodDefinition method, Instruction instruction)
    {
        int index;

        if (instruction.Operand is ParameterDefinition parameter)
            return parameter.ParameterType;

        index = instruction.OpCode == OpCodes.Ldarg_0 ? 0 : instruction.OpCode == OpCodes.Ldarg_1 ? 1 : instruction.OpCode == OpCodes.Ldarg_2 ? 2 : 3;

        if (method.HasThis)
        {
            if (index == 0)
                return null;

            index--;
        }

        return method.Parameters[index].ParameterType;
    }
}
