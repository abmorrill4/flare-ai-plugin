// Writes compile-only stubs of the MadCap (B3.*) API surface the plugin uses, so the plugin builds on machines
// without Flare (CI, contributors). Flare's B3 assemblies aren't strong-named, so at runtime Flare's real DLLs load.
//
//   FlareAI.RefStubs <plugin.dll built against lib\> <lib dir with Flare's B3 DLLs> <net48 reference assemblies dir> <out dir>
//
// Only the types and members the plugin's IL references are emitted (plus whatever their signatures need), as
// declarations with `throw null` bodies. Nothing from MadCap's binaries is copied.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

if (args.Length != 4) { Console.Error.WriteLine("usage: RefStubs <plugin.dll> <libDir> <net48RefDir> <outDir>"); return 1; }
var (pluginPath, libDir, refDir, outDir) = (args[0], args[1], args[2], args[3]);

// ---- 1. what the plugin references --------------------------------------------------------------------------
var usedTypes = new HashSet<string>();                         // reflection full names (Outer+Inner)
var usedMembers = new Dictionary<string, HashSet<string>>();   // type full name -> member names
var implemented = new HashSet<string>();                       // B3 types the plugin derives from or implements
using (var fs = File.OpenRead(pluginPath))
using (var pe = new PEReader(fs))
{
    var md = pe.GetMetadataReader();
    string AsmOf(TypeReference tr)
    {
        var scope = tr.ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference) scope = md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) : null;
    }
    string FullName(TypeReferenceHandle h)
    {
        var tr = md.GetTypeReference(h);
        var name = md.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference) return FullName((TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        var ns = md.GetString(tr.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
    bool IsB3(TypeReferenceHandle h) => AsmOf(md.GetTypeReference(h))?.StartsWith("B3.") == true;

    foreach (var h in md.TypeReferences) if (IsB3(h)) usedTypes.Add(FullName(h));
    foreach (var h in md.MemberReferences)
    {
        var mr = md.GetMemberReference(h);
        if (mr.Parent.Kind != HandleKind.TypeReference) continue;   // generic instantiations of B3 types: none so far
        var parent = (TypeReferenceHandle)mr.Parent;
        if (!IsB3(parent)) continue;
        var t = FullName(parent);
        if (!usedMembers.TryGetValue(t, out var set)) usedMembers[t] = set = new HashSet<string>();
        set.Add(md.GetString(mr.Name));
    }
    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        if (td.BaseType.Kind == HandleKind.TypeReference && IsB3((TypeReferenceHandle)td.BaseType)) implemented.Add(FullName((TypeReferenceHandle)td.BaseType));
        foreach (var ih in td.GetInterfaceImplementations())
        {
            var it = md.GetInterfaceImplementation(ih).Interface;
            if (it.Kind == HandleKind.TypeReference && IsB3((TypeReferenceHandle)it)) implemented.Add(FullName((TypeReferenceHandle)it));
        }
    }
}

// ---- 2. load the real B3 assemblies for signatures -----------------------------------------------------------
var paths = Directory.GetFiles(libDir, "B3.*.dll").Concat(Directory.GetFiles(refDir, "*.dll"))
    .Concat(Directory.Exists(Path.Combine(refDir, "Facades")) ? Directory.GetFiles(Path.Combine(refDir, "Facades"), "*.dll") : Array.Empty<string>());
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
var b3 = Directory.GetFiles(libDir, "B3.*.dll").Select(mlc.LoadFromAssemblyPath).ToDictionary(a => a.GetName().Name);
Type Find(string fullName) => b3.Values.Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null)
                              ?? throw new Exception("not found in lib: " + fullName);
static bool IsB3T(Type t) => t.Assembly.GetName().Name.StartsWith("B3.");

// ---- 3. closure: types to emit and the members of each ----------------------------------------------------------
const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var emit = new Dictionary<Type, HashSet<MemberInfo>>();
var queue = new Queue<Type>();
void Need(Type t)
{
    while (t.HasElementType) t = t.GetElementType();
    if (t.IsGenericType && !t.IsGenericTypeDefinition) { foreach (var a in t.GetGenericArguments()) Need(a); t = t.GetGenericTypeDefinition(); }
    if (t.IsGenericParameter || !IsB3T(t) || emit.ContainsKey(t)) return;
    emit[t] = new HashSet<MemberInfo>();
    queue.Enqueue(t);
    if (t.DeclaringType != null) Need(t.DeclaringType);
    if (t.BaseType != null) Need(t.BaseType);
}
void Add(Type t, MemberInfo m)
{
    Need(t);
    if (!Exposed(m)) return;   // private/internal, incl. explicit interface implementations
    if (!emit[t].Add(m)) return;
    switch (m)
    {
        case MethodBase mb:
            foreach (var p in mb.GetParameters()) Need(p.ParameterType);
            if (mb is MethodInfo mi)
            {
                Need(mi.ReturnType);
                var bd = BaseDef(mi);
                if (bd.DeclaringType != mi.DeclaringType && IsB3T(bd.DeclaringType)) Add(bd.DeclaringType, bd);
            }
            break;
        case PropertyInfo pi: Need(pi.PropertyType); foreach (var p in pi.GetIndexParameters()) Need(p.ParameterType); break;
        case EventInfo ei: Need(ei.EventHandlerType); break;
        case FieldInfo fi: Need(fi.FieldType); break;
    }
}
bool Exposed(MemberInfo m) => !m.Name.Contains('.') || m is ConstructorInfo ? m switch {
    MethodBase mb => Visible(mb), PropertyInfo pi => Visible(pi.GetGetMethod(true)) || Visible(pi.GetSetMethod(true)),
    EventInfo ei => Visible(ei.GetAddMethod(true)), FieldInfo fi => fi.IsPublic || fi.IsFamily || fi.IsFamilyOrAssembly, _ => true } : false;
bool Visible(MethodBase m) => m != null && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly);

foreach (var name in usedTypes) Need(Find(name));
foreach (var kv in usedMembers)
{
    var t = Find(kv.Key);
    foreach (var name in kv.Value)
    {
        var bare = name.StartsWith("get_") || name.StartsWith("set_") ? name.Substring(4) : name.StartsWith("add_") ? name.Substring(4) : name.StartsWith("remove_") ? name.Substring(7) : name;
        var hits = t.GetMembers(All).Where(m => m.Name == name || m.Name == bare && (m is PropertyInfo || m is EventInfo) || name == ".ctor" && m is ConstructorInfo).ToList();
        if (hits.Count == 0) Console.Error.WriteLine($"warning: {kv.Key}.{name} not found");
        foreach (var m in hits)
        {
            if (m is MethodInfo mi && mi.IsSpecialName && (name.StartsWith("get_") || name.StartsWith("set_") || name.StartsWith("add_") || name.StartsWith("remove_"))) continue;   // emitted via its property/event
            Add(t, m);
        }
    }
}
foreach (var name in implemented)
{
    var t = Find(name);
    foreach (var it in new[] { t }.Concat(t.IsInterface ? t.GetInterfaces() : Array.Empty<Type>()))
        foreach (var m in it.GetMembers(All)) if (!(m is MethodInfo mi && mi.IsSpecialName) && !(m is Type)) Add(it, m);
}
while (queue.Count > 0)
{
    var t = queue.Dequeue();
    if (t.IsEnum) foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static)) emit[t].Add(f);
    if (typeof(MulticastDelegate).FullName == t.BaseType?.FullName) Add(t, t.GetMethod("Invoke"));
    if (t.IsInterface) foreach (var it in t.GetInterfaces()) Need(it);   // keep the interface hierarchy for conversions
}

// ---- 4. write C# ---------------------------------------------------------------------------------------------------
var alias = new Dictionary<string, string> {
    ["System.Void"] = "void", ["System.Object"] = "object", ["System.String"] = "string", ["System.Boolean"] = "bool",
    ["System.Int32"] = "int", ["System.Int64"] = "long", ["System.Int16"] = "short", ["System.Byte"] = "byte", ["System.Char"] = "char",
    ["System.Double"] = "double", ["System.Single"] = "float", ["System.Decimal"] = "decimal", ["System.UInt32"] = "uint",
    ["System.UInt64"] = "ulong", ["System.UInt16"] = "ushort", ["System.SByte"] = "sbyte", ["System.IntPtr"] = "System.IntPtr" };
string Bare(string n) { var i = n.IndexOf('`'); return i < 0 ? n : n.Substring(0, i); }
string N(Type t)
{
    if (t.IsByRef) return N(t.GetElementType());
    if (t.IsArray) return N(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
    if (t.IsPointer) return N(t.GetElementType()) + "*";
    if (t.IsGenericParameter) return t.Name;
    if (t.FullName != null && alias.TryGetValue(t.FullName, out var a)) return a == "System.IntPtr" ? "global::System.IntPtr" : a;
    var args = t.IsGenericType ? "<" + string.Join(", ", t.GetGenericArguments().Select(N)) + ">" : "";
    var head = t.IsNested ? N(t.DeclaringType.IsGenericType ? t.DeclaringType.GetGenericTypeDefinition() : t.DeclaringType) + "." + Bare(t.Name)
                          : "global::" + (string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace + ".") + Bare(t.Name);
    return head + args;
}
string Lit(object v, Type t)
{
    if (v == null || v is DBNull || v is Missing) return t.IsValueType && !(t.IsGenericType && t.GetGenericTypeDefinition().FullName == "System.Nullable`1") ? "default(" + N(t) + ")" : "null";
    if (t.IsEnum) return "(" + N(t) + ")(" + Convert.ToInt64(v) + ")";
    return v switch { string s => "@\"" + s.Replace("\"", "\"\"") + "\"", bool b => b ? "true" : "false", char c => "'\\u" + ((int)c).ToString("x4") + "'",
                      float f => f.ToString("R") + "f", double d => d.ToString("R") + "d", long l => l + "L", ulong u => u + "UL", uint u => u + "U", _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) };
}
string Params(IEnumerable<ParameterInfo> ps) => string.Join(", ", ps.Select(p =>
{
    var mod = p.ParameterType.IsByRef ? (p.IsOut ? "out " : "ref ") : "";
    var prms = p.GetCustomAttributesData().Any(c => c.AttributeType.FullName == "System.ParamArrayAttribute") ? "params " : "";
    var def = p.HasDefaultValue && !p.ParameterType.IsByRef ? " = " + Lit(p.RawDefaultValue, p.ParameterType) : "";
    return prms + mod + N(p.ParameterType) + " " + (string.IsNullOrEmpty(p.Name) ? "p" + p.Position : "@" + p.Name) + def;
}));
string Access(MethodBase m) => m.IsPublic ? "public " : "protected ";
string TypeParams(Type t) => t.IsGenericTypeDefinition ? "<" + string.Join(", ", t.GetGenericArguments().Where(g => t.DeclaringType == null || !t.DeclaringType.IsGenericType || g.GenericParameterPosition >= t.DeclaringType.GetGenericArguments().Length).Select(g => g.Name)) + ">" : "";

void WriteType(StringBuilder sb, Type t, string ind)
{
    var members = emit[t];
    var name = Bare(t.Name) + TypeParams(t);
    if (t.IsEnum)
    {
        sb.AppendLine($"{ind}public enum {name} : {N(Enum.GetUnderlyingType(t))}");
        sb.AppendLine(ind + "{");
        foreach (var f in members.OfType<FieldInfo>()) sb.AppendLine($"{ind}    {f.Name} = {Convert.ToInt64(f.GetRawConstantValue())},");
        sb.AppendLine(ind + "}");
        return;
    }
    if (t.BaseType?.FullName == "System.MulticastDelegate")
    {
        var inv = t.GetMethod("Invoke");
        sb.AppendLine($"{ind}public delegate {N(inv.ReturnType)} {name}({Params(inv.GetParameters())});");
        return;
    }
    var isStatic = t.IsAbstract && t.IsSealed;
    var kind = t.IsInterface ? "interface" : t.IsValueType ? "struct" : "class";
    var mods = t.IsInterface || t.IsValueType ? "" : isStatic ? "static " : t.IsAbstract ? "abstract " : t.IsSealed ? "sealed " : "";
    var bases = new List<string>();
    if (!t.IsInterface && !t.IsValueType && t.BaseType != null && t.BaseType.FullName != "System.Object") bases.Add(N(t.BaseType));
    // Classes keep the emitted B3 interfaces they implement (conversions), satisfied by explicit stub implementations.
    var ifaces = t.IsInterface ? new List<Type>() : t.GetInterfaces().Where(i => IsB3T(i) && emit.ContainsKey(i))
        .Except(t.BaseType != null && IsB3T(t.BaseType) ? t.BaseType.GetInterfaces() : Array.Empty<Type>()).ToList();
    bases.AddRange(ifaces.Select(N));
    if (t.IsInterface) bases.AddRange(t.GetInterfaces().Where(i => !IsB3T(i) || emit.ContainsKey(i.IsGenericType ? i.GetGenericTypeDefinition() : i)).Except(t.GetInterfaces().SelectMany(i => i.GetInterfaces())).Select(N));
    sb.AppendLine($"{ind}public {mods}partial {kind} {name}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")}");
    sb.AppendLine(ind + "{");
    var i2 = ind + "    ";
    string body(MethodInfo m) => t.IsInterface || m.IsAbstract ? ";" : " { throw null; }";
    string vmods(MethodInfo m)
    {
        if (t.IsInterface) return "";
        var s = Access(m) + (m.IsStatic ? "static " : "");
        if (m.IsAbstract) return s + "abstract ";
        if (BaseDef(m).DeclaringType != m.DeclaringType && m.IsVirtual) return s + (m.IsFinal ? "sealed override " : "override ");
        if (m.IsVirtual && !m.IsFinal && !isStatic && !t.IsSealed) return s + "virtual ";
        return s;
    }
    var hasCtor = false;
    foreach (var m in members.OrderBy(m => m.Name, StringComparer.Ordinal))
    {
        switch (m)
        {
            case ConstructorInfo c when !c.IsStatic:
                hasCtor |= c.GetParameters().Length == 0;
                sb.AppendLine($"{i2}{Access(c)}{Bare(t.Name)}({Params(c.GetParameters())}){BaseCall(t)} {{ }}");
                break;
            case MethodInfo mi:
                var gen = mi.IsGenericMethodDefinition ? "<" + string.Join(", ", mi.GetGenericArguments().Select(g => g.Name)) + ">" : "";
                if (mi.Name == "op_Implicit" || mi.Name == "op_Explicit")
                    sb.AppendLine($"{i2}public static {(mi.Name == "op_Implicit" ? "implicit" : "explicit")} operator {N(mi.ReturnType)}({Params(mi.GetParameters())}) {{ throw null; }}");
                else
                    sb.AppendLine($"{i2}{vmods(mi)}{N(mi.ReturnType)} {mi.Name}{gen}({Params(mi.GetParameters())}){body(mi)}");
                break;
            case PropertyInfo pi:
                var g = pi.GetGetMethod(true); var st = pi.GetSetMethod(true);
                var acc = Visible(g) ? g : st;
                var ix = pi.GetIndexParameters();
                var pname = ix.Length > 0 ? "this[" + Params(ix) + "]" : pi.Name;
                string a(MethodInfo x, string kw) => !Visible(x) ? "" : (x.IsPublic || !acc.IsPublic ? "" : "protected ") + kw + (t.IsInterface || x.IsAbstract ? "; " : kw == "get" ? " { throw null; } " : " { } ");
                sb.AppendLine($"{i2}{vmods(acc)}{N(pi.PropertyType)} {pname} {{ {a(g, "get")}{a(st, "set")}}}");
                break;
            case EventInfo ei:
                var add = ei.GetAddMethod(true);
                sb.AppendLine($"{i2}{vmods(add)}event {N(ei.EventHandlerType)} {ei.Name}{(t.IsInterface || add.IsAbstract ? ";" : " { add { } remove { } }")}");
                break;
            case FieldInfo fi:
                sb.AppendLine(fi.IsLiteral ? $"{i2}public const {N(fi.FieldType)} {fi.Name} = {Lit(fi.GetRawConstantValue(), fi.FieldType)};"
                                           : $"{i2}{(fi.IsPublic ? "public " : "protected ")}{(fi.IsStatic ? "static " : "")}{(fi.IsInitOnly ? "readonly " : "")}{N(fi.FieldType)} {fi.Name};");
                break;
        }
    }
    foreach (var it in ifaces)
        foreach (var m in emit[it])
        {
            var q = N(it) + ".";
            switch (m)
            {
                case MethodInfo mi: sb.AppendLine($"{i2}{N(mi.ReturnType)} {q}{mi.Name}({Params(mi.GetParameters())}) {{ throw null; }}"); break;
                case PropertyInfo pi:
                    var ix2 = pi.GetIndexParameters();
                    sb.AppendLine($"{i2}{N(pi.PropertyType)} {q}{(ix2.Length > 0 ? "this[" + Params(ix2) + "]" : pi.Name)} {{ {(pi.GetGetMethod(true) != null ? "get { throw null; } " : "")}{(pi.GetSetMethod(true) != null ? "set { } " : "")}}}");
                    break;
                case EventInfo ei: sb.AppendLine($"{i2}event {N(ei.EventHandlerType)} {q}{ei.Name} {{ add {{ }} remove {{ }} }}"); break;
            }
        }
    if (kind == "class" && !isStatic && !hasCtor) sb.AppendLine($"{i2}protected {Bare(t.Name)}(){BaseCall(t)} {{ }}");
    foreach (var nested in emit.Keys.Where(n => n.DeclaringType == t).OrderBy(n => n.Name)) WriteType(sb, nested, i2);
    sb.AppendLine(ind + "}");
}
// A stub constructor calls the base's parameterless stub constructor (B3 bases always get one), else the base's
// cheapest accessible constructor with default arguments.
string BaseCall(Type t)
{
    var b = t.BaseType;
    if (b == null || IsB3T(b) || b.FullName == "System.Object") return "";
    var ctors = b.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly).ToList();
    if (ctors.Count == 0 || ctors.Any(c => c.GetParameters().Length == 0)) return "";
    var best = ctors.OrderBy(c => c.GetParameters().Length).First();
    return " : base(" + string.Join(", ", best.GetParameters().Select(p => "default(" + N(p.ParameterType) + ")")) + ")";
}

Directory.CreateDirectory(outDir);
foreach (var asm in emit.Keys.GroupBy(t => t.Assembly.GetName().Name).OrderBy(g => g.Key))
{
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated> by tools/FlareAI.RefStubs: compile-only declarations of the Flare API members the plugin uses.");
    sb.AppendLine("// Never shipped: at runtime Flare loads its own " + asm.Key + ".dll. Regenerate rather than edit.");
    sb.AppendLine("#pragma warning disable");
    foreach (var ns in asm.Where(t => !t.IsNested).GroupBy(t => t.Namespace).OrderBy(g => g.Key))
    {
        sb.AppendLine($"namespace {ns.Key}");
        sb.AppendLine("{");
        foreach (var t in ns.OrderBy(t => t.Name)) WriteType(sb, t, "    ");
        sb.AppendLine("}");
    }
    var dir = Path.Combine(outDir, asm.Key);
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, asm.Key + ".cs"), sb.ToString());
    var real = b3[asm.Key];
    var deps = real.GetReferencedAssemblies().Select(r => r.Name).Where(n => n != asm.Key && emit.Keys.Any(t => t.Assembly.GetName().Name == n)).OrderBy(n => n);
    File.WriteAllText(Path.Combine(dir, asm.Key + ".csproj"), $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <!-- Generated by tools/FlareAI.RefStubs. Compile-only stand-in for Flare's {asm.Key}.dll; never shipped. -->
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>{asm.Key}</AssemblyName>
    <AssemblyVersion>{real.GetName().Version}</AssemblyVersion>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include=""System"" /><Reference Include=""System.Core"" /><Reference Include=""System.Xml"" /><Reference Include=""System.Drawing"" />
    <Reference Include=""System.Windows.Forms"" /><Reference Include=""WindowsBase"" /><Reference Include=""PresentationCore"" />
    <Reference Include=""PresentationFramework"" /><Reference Include=""System.Xaml"" />
  </ItemGroup>
  <ItemGroup>
{string.Concat(deps.Select(d => $"    <ProjectReference Include=\"..\\{d}\\{d}.csproj\" />\n"))}  </ItemGroup>
</Project>
");
    Console.WriteLine($"{asm.Key}: {asm.Count()} types, {asm.Sum(t => emit[t].Count)} members");
}
return 0;

// MetadataLoadContext has no GetBaseDefinition: an override reuses a vtable slot; find the virtual it overrides.
static MethodInfo BaseDef(MethodInfo m)
{
    if (!m.IsVirtual || (m.Attributes & MethodAttributes.NewSlot) != 0) return m;
    var sig = m.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name).ToArray();
    for (var b = m.DeclaringType.BaseType; b != null; b = b.BaseType)
    {
        var hit = b.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                   .FirstOrDefault(x => x.Name == m.Name && x.IsVirtual && x.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name).SequenceEqual(sig));
        if (hit != null) return BaseDef(hit);
    }
    return m;
}
