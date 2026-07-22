using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CodeMap;

static class TypeScriptMapper
{
    public static IEnumerable<MemberInfo> Generate(string file, bool showMethodParams)
    {
        return Generate(File.ReadAllLines(file), showMethodParams).Structure();
        // future: consider a proper TypeScript parser integration
    }

    // TypeScript-specific regexes (more permissive: generics, export default, async)
    // interface Foo { ... }
    static Regex interfaceRegex = new Regex(@"^\s*(?:export\s+)?(?:default\s+)?interface\s+([A-Za-z_][\w_]*)(?:\s*<[^>]+>)?\b", RegexOptions.Compiled);
    // type Foo = ...
    static Regex typeRegex = new Regex(@"^\s*(?:export\s+)?(?:default\s+)?type\s+([A-Za-z_][\w_]*)(?:\s*<[^>]+>)?\b", RegexOptions.Compiled);
    // enum Foo { ... }
    static Regex enumRegex = new Regex(@"^\s*(?:export\s+)?(?:default\s+)?enum\s+([A-Za-z_][\w_]*)(?:\s*<[^>]+>)?\b", RegexOptions.Compiled);
    // class Foo<T> { ... } optionally exported/default
    static Regex classRegex = new Regex(@"^\s*(?:export\s+)?(?:default\s+)?class\s+([A-Za-z_][\w_]*)(?:\s*<[^>]+>)?\b", RegexOptions.Compiled);
    // function foo<T>(...) or export function foo(...)
    static Regex functionRegex = new Regex(@"^(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s+([A-Za-z_][\w_]*)(?:\s*<[^>]+>)?\s*\(([^)]*)\)", RegexOptions.Compiled);
    // class method with optional visibility/async/readonly/static and optional return type and generics
    static Regex classMethod = new Regex(@"^(?:\s*(?:public|private|protected|static|async|readonly)\s+)*([A-Za-z_][\w_]*)(?:\s*<[^>]+>)?\s*\(([^)]*)\)\s*(?::\s*[^({]+)?\s*{", RegexOptions.Compiled);
    // var foo = function(...) {
    static Regex varFuncExpr = new Regex(@"^(?:export\s+)?(?:var|let|const)\s+(?:async\s+)?([A-Za-z_][\w_]*)\s*=\s*function\s*\(([^)]*)\)", RegexOptions.Compiled);
    // var foo = (...) => {   (handles export and async)
    static Regex varArrowFunc = new Regex(@"^(?:export\s+)?(?:var|let|const)\s+(?:async\s+)?([A-Za-z_][\w_]*)\s*=\s*\(([^)]*)\)\s*=>", RegexOptions.Compiled);
    // var foo = x => {
    static Regex varArrowFuncSingle = new Regex(@"^(?:export\s+)?(?:var|let|const)\s+(?:async\s+)?([A-Za-z_][\w_]*)\s*=\s*([A-Za-z_$][\w$]*)\s*=>", RegexOptions.Compiled);
    // property: foo: Type; or foo?: Type; or readonly foo: Type = ...
    static Regex propertyRegex = new Regex(@"^(?:\s*(?:public|private|protected|readonly|static|export)\s+)*([A-Za-z_][\w_]*)\s*[:?]\s*[^;=\{]+[;=]?", RegexOptions.Compiled);

    static string ParentLineOf(string[] code, int childIndex)
    {
        var childIndent = code[childIndex].GetIndent();

        for (int i = childIndex - 1; i >= 0; i--)
        {
            if (code[i].Length > 0)
            {
                var currentIndent = code[i].GetIndent();
                if (currentIndent < childIndent)
                    return code[i];
            }
        }
        return null;
    }

    public static IEnumerable<MemberInfo> Generate(string[] code, bool showMethodParams)
    {
        var map = new List<MemberInfo>();

        // control keywords to exclude from method-like matching
        var controlKeywords = new HashSet<string> { "if", "for", "while", "switch", "catch", "with", "else", "do", "try" };

        for (int i = 0; i < code.Length; i++)
        {
            // Support decorators: if a line starts with '@' the declaration may be on the next line
            int parseIndex = i;
            var raw = code[parseIndex].TrimStart();
            if (raw.StartsWith("@"))
            {
                // find next non-empty, non-comment line
                int k = parseIndex + 1;
                while (k < code.Length && string.IsNullOrWhiteSpace(code[k])) k++;
                if (k < code.Length)
                    parseIndex = k;
                raw = code[parseIndex].TrimStart();
            }

            var line = raw;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            Match match;

            // skip import statements and export blocks
            if (line.StartsWith("import ") || line.StartsWith("export {") || line.StartsWith("export default {"))
                continue;

            if ((match = interfaceRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Interface, Content = name };
                map.Add(info);
                continue;
            }

            if ((match = typeRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Type, Content = name };
                map.Add(info);
                continue;
            }

            if ((match = enumRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Type, Content = name };
                map.Add(info);
                continue;
            }

            if ((match = classRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Class, Content = name };
                map.Add(info);
                continue;
            }

            // top-level functions
            if ((match = functionRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parms = "(" + match.Groups[2].Value + ")";

                var info = new MemberInfo();
                info.Line = parseIndex;
                info.ParentPath = "";
                info.Name = name;
                info.MemberContext = "";
                info.MemberType = MemberType.Method;
                info.Content = showMethodParams ? name + parms : name + "(...)";
                map.Add(info);
                continue;
            }

            // class methods (avoid matching control keywords)
            if (!controlKeywords.Contains(line.Split(" (".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "") && (match = classMethod.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parms = "(" + match.Groups[2].Value + ")";

                var parent = ParentLineOf(code, i);
                if (parent != null)
                {
                    var parentTitle = parent.Split('(').First()
                        .Replace("export", "")
                        .Replace("public", "")
                        .Replace("static", "")
                        .Replace("class ", "")
                        .Replace("{", "");
                    parentTitle = parentTitle.Split('(').First().Trim();
                    name = parentTitle + "." + name;
                }

                var info = new MemberInfo();
                info.Line = parseIndex;
                info.ParentPath = "";
                info.Name = name;
                info.MemberContext = "";
                info.MemberType = name.EndsWith(".constructor") ? MemberType.Constructor : MemberType.Method;
                info.Content = showMethodParams ? name + parms : name + "(...)";
                map.Add(info);
                continue;
            }

            if ((match = varFuncExpr.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parms = "(" + match.Groups[2].Value + ")";

                var info = new MemberInfo { Line = i, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Method, Content = showMethodParams ? name + parms : name + "(...)" };
                map.Add(info);
                continue;
            }

            if ((match = varArrowFunc.Match(line)).Success || (match = varArrowFuncSingle.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parms = match.Groups.Count > 2 ? "(" + match.Groups[2].Value + ")" : "(...)";
                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Method, Content = showMethodParams ? name + parms : name + "(...)" };
                map.Add(info);
                continue;
            }

            if ((match = propertyRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parent = ParentLineOf(code, parseIndex);
                if (parent != null)
                {
                    var parentTitle = parent.Split('(').First()
                        .Replace("export", "")
                        .Replace("public", "")
                        .Replace("static", "")
                        .Replace("class ", "")
                        .Replace("{", "");
                    parentTitle = parentTitle.Split('(').First().Trim();
                    name = parentTitle + "." + name;
                }

                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Property, Content = name };
                map.Add(info);
                continue;
            }
        }

        return map;
    }

    // Note: Structure extension method is implemented in JavaScriptMapper to
    // avoid duplicate extension methods causing ambiguity. We rely on that
    // implementation here.
}
