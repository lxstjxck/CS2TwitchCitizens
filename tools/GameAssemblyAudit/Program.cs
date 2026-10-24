using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: GameAssemblyAudit <Game.dll>");
    return 1;
}
using var file = File.OpenRead(args[0]);
using var pe = new PEReader(file);
var reader = pe.GetMetadataReader();
var terms = new[] { "Fire", "Disaster", "Tornado", "Flood", "Electricity", "Emergency" };
foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    var name = reader.GetString(type.Name);
    var ns = reader.GetString(type.Namespace);
    if (!terms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
        ns.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
    Console.WriteLine(ns + "." + name);
    foreach (var fieldHandle in type.GetFields())
    {
        var field = reader.GetFieldDefinition(fieldHandle);
        Console.WriteLine("  FIELD " + (field.Attributes & FieldAttributes.FieldAccessMask) + " " + reader.GetString(field.Name));
    }
    foreach (var methodHandle in type.GetMethods())
    {
        var method = reader.GetMethodDefinition(methodHandle);
        Console.WriteLine("  METHOD " + (method.Attributes & MethodAttributes.MemberAccessMask) + " " + reader.GetString(method.Name));
    }
}
return 0;
