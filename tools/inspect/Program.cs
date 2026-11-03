// Lists types and members in FM27's interop assemblies.
// Usage: inspect <dll-or-folder> [type-name-regex] [--members]
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

var target = args[0];
var filter = args.Length > 1 && !args[1].StartsWith("--") ? new Regex(args[1], RegexOptions.IgnoreCase) : null;
var showMembers = args.Contains("--members");
var files = Directory.Exists(target) ? Directory.GetFiles(target, "*.dll") : new[] { target };

foreach (var file in files)
{
    using var fs = File.OpenRead(file);
    using var pe = new PEReader(fs);
    if (!pe.HasMetadata) continue;
    var md = pe.GetMetadataReader();
    foreach (var th in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(th);
        var ns = md.GetString(t.Namespace);
        var name = md.GetString(t.Name);
        var full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        if (filter != null && !filter.IsMatch(full)) continue;
        if (name.StartsWith("<")) continue;
        var baseName = "";
        if (!t.BaseType.IsNil && t.BaseType.Kind == HandleKind.TypeReference)
            baseName = md.GetString(md.GetTypeReference((TypeReferenceHandle)t.BaseType).Name);
        Console.WriteLine($"{Path.GetFileName(file)}: {full}{(baseName != "" ? " : " + baseName : "")}");
        if (!showMembers) continue;
        foreach (var ph in t.GetProperties())
            Console.WriteLine($"    prop {md.GetString(md.GetPropertyDefinition(ph).Name)}");
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            var mn = md.GetString(m.Name);
            if (mn.StartsWith("set_") || mn.StartsWith("<")) continue;
            var sig = m.DecodeSignature(new Names(md), null);
            var pnames = m.GetParameters().Select(h => md.GetString(md.GetParameter(h).Name)).Where(n => n != "").ToList();
            var ps = sig.ParameterTypes.Select((t2, k) => t2 + (k < pnames.Count ? " " + pnames[k] : ""));
            Console.WriteLine($"    meth {sig.ReturnType} {mn}({string.Join(", ", ps)})");
        }
        foreach (var fh in t.GetFields())
        {
            var f = md.GetFieldDefinition(fh);
            var fn = md.GetString(f.Name);
            if (fn.StartsWith("NativeFieldInfoPtr_") || fn.StartsWith("NativeMethodInfoPtr_")) continue;
            var ch = f.GetDefaultValue();
            string cv = "";
            if (!ch.IsNil)
            {
                var c = md.GetConstant(ch);
                var br = md.GetBlobReader(c.Value);
                cv = " = " + (c.TypeCode switch
                {
                    ConstantTypeCode.UInt32 => br.ReadUInt32().ToString(),
                    ConstantTypeCode.Int32 => br.ReadInt32().ToString(),
                    ConstantTypeCode.UInt64 => br.ReadUInt64().ToString(),
                    ConstantTypeCode.Int64 => br.ReadInt64().ToString(),
                    ConstantTypeCode.String => br.ReadUTF16(br.Length),
                    _ => c.TypeCode.ToString(),
                });
            }
            Console.WriteLine($"    field {fn}{cv}");
        }
    }
}

class Names : ISignatureTypeProvider<string, object>
{
    readonly MetadataReader md;
    public Names(MetadataReader md) { this.md = md; }
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => r.GetString(r.GetTypeDefinition(h).Name);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => r.GetString(r.GetTypeReference(h).Name);
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "spec";
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[,]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericTypeParameter(object c, int i) => "T" + i;
    public string GetGenericMethodParameter(object c, int i) => "M" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
}
