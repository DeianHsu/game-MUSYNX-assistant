
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class IlDump
{
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var t in types)
        {
            yield return t;
            foreach (var n in AllTypes(t.NestedTypes)) yield return n;
        }
    }

    static string Format(object o)
    {
        if (o == null) return "";
        var s = o as string;
        if (s != null) return "\"" + s + "\"";
        return o.ToString();
    }

    static void Main(string[] args)
    {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        string filter = args.Length > 1 && args[1] != "ALL" ? args[1] : null;
        TextWriter w = args.Length > 2 ? new StreamWriter(args[2]) : Console.Out;
        foreach (var t in AllTypes(asm.MainModule.Types))
        {
            if (filter != null && t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            w.WriteLine("=== " + t.FullName + " : " + (t.BaseType == null ? "" : t.BaseType.FullName));
            foreach (var f in t.Fields) w.WriteLine("  field " + f.FieldType.Name + " " + f.Name);
            foreach (var m in t.Methods)
            {
                w.WriteLine("--- " + m.FullName);
                if (!m.HasBody) { w.WriteLine("  (no body)"); continue; }
                foreach (var v in m.Body.Variables) w.WriteLine("  local " + v.VariableType.Name + " V_" + v.Index);
                foreach (var i in m.Body.Instructions)
                    w.WriteLine("  " + i.Offset.ToString("X4") + "  " + i.OpCode + " " + Format(i.Operand));
            }
        }
        w.Flush();
        if (w != Console.Out) w.Close();
    }
}
