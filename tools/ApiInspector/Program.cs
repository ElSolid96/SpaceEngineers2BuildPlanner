using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

var game = args[0];
var filter = args[1];
foreach (var path in Directory.EnumerateFiles(game, "*.dll"))
{
    if (!Path.GetFileName(path).StartsWith("VRage") && !Path.GetFileName(path).StartsWith("Game2")) continue;
    ModuleDefinition loaded;
    try { loaded = ModuleDefinition.ReadModule(path); }
    catch (BadImageFormatException) { continue; }
    using var module = loaded;
    foreach (var type in module.Types.SelectMany(Flatten).Where(t => filter.StartsWith("=") ? t.Name == filter.Substring(1) : t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"TYPE {type.FullName} : {type.BaseType} [{Path.GetFileName(path)}]");
        foreach (var field in type.Fields) Console.WriteLine($" FIELD {field.Attributes} {field.FieldType} {field.Name}");
        foreach (var method in type.Methods)
        {
            Console.WriteLine($" METHOD {method.Attributes} {method.FullName}");
            if (args.Length > 2 && method.HasBody && (args[2] == "*" || method.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
                foreach (var instruction in method.Body.Instructions) Console.WriteLine($"   {instruction}");
        }
    }
}
static System.Collections.Generic.IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    => new[] { type }.Concat(type.NestedTypes.SelectMany(Flatten));
