using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil;

static class ApiSurface {
  public static void Trim(ModuleDefinition module, string apiPath) {
    var definitions = AllTypes(module.Types).ToDictionary(type => type.FullName);
    var types = new HashSet<TypeDefinition>();
    var methods = new HashSet<MethodDefinition>();
    var fields = new HashSet<FieldDefinition>();

    IEnumerable<TypeDefinition> Hierarchy(TypeDefinition type) {
      for (var current = type; current != null; current = LocalType(current.BaseType))
        yield return current;
    }

    TypeDefinition LocalType(TypeReference reference) {
      if (reference == null) return null;
      var element = reference.GetElementType();
      return (element.Scope == module || element.Scope is AssemblyNameReference scope &&
          scope.Name == module.Assembly.Name.Name) &&
          definitions.TryGetValue(element.FullName, out var definition) ? definition : null;
    }

    void KeepReference(TypeReference reference) {
      if (reference == null) return;
      if (reference is GenericInstanceType instance)
        foreach (var argument in instance.GenericArguments) KeepReference(argument);
      if (reference is IModifierType modifier) KeepReference(modifier.ModifierType);
      if (reference is FunctionPointerType pointer) {
        KeepReference(pointer.ReturnType);
        foreach (var parameter in pointer.Parameters) KeepReference(parameter.ParameterType);
      }
      if (reference is TypeSpecification specification) KeepReference(specification.ElementType);
      KeepType(LocalType(reference));
    }

    void KeepConstraints(IGenericParameterProvider owner) {
      foreach (var parameter in owner.GenericParameters)
        foreach (var constraint in parameter.Constraints) KeepReference(constraint.ConstraintType);
    }

    void KeepField(FieldDefinition field) {
      if (!fields.Add(field)) return;
      KeepType(field.DeclaringType);
      KeepReference(field.FieldType);
    }

    void KeepMethod(MethodDefinition method) {
      if (!methods.Add(method)) return;
      KeepType(method.DeclaringType);
      KeepReference(method.ReturnType);
      foreach (var parameter in method.Parameters) KeepReference(parameter.ParameterType);
      KeepConstraints(method);
      foreach (var contract in method.Overrides) {
        KeepReference(contract.DeclaringType);
        var declaration = LocalType(contract.DeclaringType)?.Methods.FirstOrDefault(m => Signature(m) == Signature(contract));
        if (declaration != null) KeepMethod(declaration);
      }
      if (method.IsVirtual && !method.IsNewSlot && LocalType(method.DeclaringType.BaseType) is TypeDefinition parent) {
        var slot = Hierarchy(parent).SelectMany(t => t.Methods)
            .FirstOrDefault(m => m.IsVirtual && Signature(m) == Signature(method));
        if (slot != null) KeepMethod(slot);
      }
    }

    var resolver = (DefaultAssemblyResolver)module.AssemblyResolver;
    resolver.AddSearchDirectory(Path.GetDirectoryName(module.FileName));
    resolver.AddSearchDirectory(RuntimeEnvironment.GetRuntimeDirectory());

    void KeepType(TypeDefinition type) {
      if (type == null || !types.Add(type)) return;
      KeepType(type.DeclaringType);
      KeepReference(type.BaseType);
      KeepConstraints(type);
      if (type.IsValueType)
        foreach (var field in type.Fields) KeepField(field);
      if (type.IsInterface)
        foreach (var method in type.Methods) KeepMethod(method);
      foreach (var implementation in type.Interfaces) {
        var reference = implementation.InterfaceType;
        KeepReference(reference);
        var contract = LocalType(reference) ?? reference.Resolve()
            ?? throw new InvalidDataException("Cannot resolve interface " + reference.FullName);
        foreach (var slot in contract.Methods) {
          var implementationMethod = Hierarchy(type).SelectMany(t => t.Methods).FirstOrDefault(m =>
              m.IsVirtual && (Signature(m) == Signature(slot) ||
                  m.Overrides.Any(o => o.DeclaringType.FullName == contract.FullName && Signature(o) == Signature(slot))));
          if (implementationMethod != null) KeepMethod(implementationMethod);
        }
      }
    }

    KeepType(definitions["<Module>"]);
    foreach (var line in File.ReadLines(apiPath)) {
      var key = line.Split('#')[0].Trim();
      if (key.Length == 0) continue;
      var parts = key.Split(new[] { "::" }, 2, StringSplitOptions.None);
      if (!definitions.TryGetValue(parts[0], out var type))
        throw new InvalidDataException("Missing Android SDK type: " + parts[0]);
      KeepType(type);
      if (parts.Length == 1) continue;
      var named = parts[1];
      var members = Hierarchy(type).SelectMany(t => t.Methods).Where(m => m.Name == named).ToArray();
      var namedFields = Hierarchy(type).SelectMany(t => t.Fields).Where(f => f.Name == named).ToArray();
      if (members.Length + namedFields.Length == 0)
        throw new InvalidDataException("Missing Android SDK member: " + key);
      foreach (var method in members) KeepMethod(method);
      foreach (var field in namedFields) KeepField(field);
    }

    // Preserve overrides of abstract slots selected by the manifest or an interface contract.
    int previous;
    do {
      previous = methods.Count;
      foreach (var type in types.ToArray())
        foreach (var method in type.Methods.Where(m => m.IsVirtual && !m.IsNewSlot))
          if (LocalType(type.BaseType) is TypeDefinition parent &&
              Hierarchy(parent).SelectMany(t => t.Methods).Any(m => m.IsAbstract &&
                  methods.Contains(m) && Signature(m) == Signature(method)))
            KeepMethod(method);
    } while (methods.Count != previous);

    // JNI binding attributes and resources are unused by the Linux shim and can reference removed types.
    module.Assembly.CustomAttributes.Clear();
    module.CustomAttributes.Clear();
    module.Resources.Clear();
    foreach (var type in types) {
      type.CustomAttributes.Clear();
      foreach (var implementation in type.Interfaces) implementation.CustomAttributes.Clear();
      ClearGenericAttributes(type);
      Filter(type.NestedTypes, types.Contains);
      Filter(type.Fields, fields.Contains);
      Filter(type.Methods, methods.Contains);
      foreach (var field in type.Fields) field.CustomAttributes.Clear();
      foreach (var method in type.Methods) {
        method.CustomAttributes.Clear();
        method.MethodReturnType.CustomAttributes.Clear();
        foreach (var parameter in method.Parameters) parameter.CustomAttributes.Clear();
        ClearGenericAttributes(method);
      }

      MethodDefinition Accessor(MethodDefinition method) => methods.Contains(method) ? method : null;
      foreach (var property in type.Properties) {
        property.CustomAttributes.Clear();
        property.GetMethod = Accessor(property.GetMethod);
        property.SetMethod = Accessor(property.SetMethod);
        Filter(property.OtherMethods, methods.Contains);
      }
      Filter(type.Properties, p => p.GetMethod != null || p.SetMethod != null || p.HasOtherMethods);
      // Event accessors still work as methods. No caller looks up Android event metadata.
      type.Events.Clear();
    }
    Filter(module.Types, types.Contains);
    Console.WriteLine($"Compact {module.Assembly.Name.Name}: {types.Count} types, {methods.Count} methods.");
  }

  static string Signature(MethodReference method) =>
      $"{method.Name}`{method.GenericParameters.Count}({string.Join(",", method.Parameters.Select(p => p.ParameterType.FullName))})";

  static void ClearGenericAttributes(IGenericParameterProvider owner) {
    foreach (var parameter in owner.GenericParameters) {
      parameter.CustomAttributes.Clear();
      foreach (var constraint in parameter.Constraints) constraint.CustomAttributes.Clear();
    }
  }

  static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) =>
      types.SelectMany(type => new[] { type }.Concat(AllTypes(type.NestedTypes)));

  static void Filter<T>(IList<T> items, Func<T, bool> keep) {
    for (int i = items.Count - 1; i >= 0; i--)
      if (!keep(items[i])) items.RemoveAt(i);
  }
}
