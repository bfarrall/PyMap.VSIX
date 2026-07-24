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
        return Generate(File.ReadAllLines(file), showMethodParams).StructureTs();
        // future: consider a proper TypeScript parser integration
    }

    static string ShortDecl(string decl)
    {
        if (string.IsNullOrWhiteSpace(decl))
            return decl;
        var s = decl.Trim();
        // remove leading export/const/let/var keywords
        s = s.Replace("export ", "");
        s = s.Replace("const ", "");
        s = s.Replace("let ", "");
        s = s.Replace("var ", "");
        // trim at ':' or '=' if present
        int idx = s.IndexOf(':');
        if (idx >= 0)
            s = s.Substring(0, idx).Trim();
        else
        {
            idx = s.IndexOf('=');
            if (idx >= 0)
                s = s.Substring(0, idx).Trim();
        }
        return s;
    }

    static bool IsDeclarationFunctionOrClass(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        var t = line.TrimStart();
        if (classRegex.IsMatch(t))
            return true;
        if (functionRegex.IsMatch(t))
            return true;
        if (varFuncExpr.IsMatch(t) || varArrowFunc.IsMatch(t) || varArrowFuncSingle.IsMatch(t))
            return true;
        if (topLevelVarRegex.IsMatch(t))
            return true;
        return false;
    }

    // TypeScript-specific Structure implementation to avoid splitting on dots inside
    // TypeScript type annotations (e.g., "UserCard: React.FC<Props>").
    public static IEnumerable<MemberInfo> StructureTs(this IEnumerable<MemberInfo> map)
    {
        var all = map.ToList();
        var roots = new List<MemberInfo>();
        // Build a lookup of names to original items for accurate parent matching
        var nameLookup = all.ToDictionary(x => x.Name, x => x);

        foreach (var item in all)
        {
            var content = item.Name;
            // Try to find a parent by scanning dots from the end and checking for an existing name
            int scanPos = content.LastIndexOf('.');
            bool attached = false;
            while (scanPos > 0)
            {
                var candidateParent = content.Substring(0, scanPos);
                if (nameLookup.ContainsKey(candidateParent))
                {
                    var parent = nameLookup[candidateParent];
                    parent.Children.Add(item);
                    item.ParentPath = candidateParent;
                    if (item.Content.Length > candidateParent.Length)
                        item.Content = item.Content.Substring(candidateParent.Length).TrimStart('.');
                    else
                        item.Content = item.Content.TrimStart('.');
                    item.ContentType = "    ";
                    attached = true;
                    break;
                }
                scanPos = content.LastIndexOf('.', scanPos - 1);
            }

            if (!attached)
            {
                // If this root item has a TypeScript-style annotation (':' in the name)
                // and is a top-level property (const/let component), preserve the full
                // annotated name as the display content so UI shows e.g. "UserCard: React.FC<...>".
                if (item.Name.IndexOf(':') != -1 && item.MemberType == MemberType.Property)
                    item.Content = item.Name;

                roots.Add(item);
            }
        }

        return roots;
    }

    static int FindAncestorIndexMatching(string[] code, int childIndex, Func<string, bool> predicate)
    {
        int idx = ParentLineIndexOf(code, childIndex);
        while (idx != -1)
        {
            var line = code[idx];
            if (predicate(line))
                return idx;
            idx = ParentLineIndexOf(code, idx);
        }
        return -1;
    }

    static string AppendTypeAnnotation(string rawLine, string name)
    {
        if (string.IsNullOrEmpty(rawLine) || string.IsNullOrEmpty(name))
            return name;

        // Find the declaration occurrence of the name as a standalone token (avoid matching inside UserCardProps etc.)
        var nameMatch = Regex.Match(rawLine, "\\b" + Regex.Escape(name) + "\\b");
        if (!nameMatch.Success)
            return name;
        var idx = nameMatch.Index;

        // ensure we don't call IndexOf with a start beyond the string length
        if (idx + name.Length >= rawLine.Length)
            return name;

        var idxColon = rawLine.IndexOf(':', idx + name.Length);
        if (idxColon < 0)
            return name;

        // find '=' after colon to limit the type annotation range
        var idxEq = rawLine.IndexOf('=', idxColon);
        int end = idxEq >= 0 ? idxEq : rawLine.Length;
        var typePart = rawLine.Substring(idxColon, end - idxColon).Trim();
        return name + typePart;
    }

    // Find first 'return' index in a block started at ancestorIndex (works for class methods too)
    static int FirstReturnIndexInBlock(string[] code, int ancestorIndex)
    {
        if (ancestorIndex < 0 || ancestorIndex >= code.Length)
            return -1;

        int braceDepth = 0;
        bool started = false;
        for (int i = ancestorIndex; i < code.Length; i++)
        {
            var line = code[i];
            if (line.TrimStart().StartsWith("//"))
                continue;

            if (line.Contains("{"))
            {
                started = true;
                braceDepth += line.Count(c => c == '{');
            }
            if (started && line.Contains("return "))
                return i;

            if (started && line.Contains("}"))
            {
                braceDepth -= line.Count(c => c == '}');
                if (braceDepth <= 0)
                    break;
            }
        }

        return -1;
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
    // var foo = (...) => {   (handles export, async and optional TypeScript type annotation)
    static Regex varArrowFunc = new Regex(@"^(?:export\s+)?(?:var|let|const)\s+(?:async\s+)?([A-Za-z_][\w_]*)(?:\s*:\s*[^=]+)?\s*=\s*\(([^)]*)\)\s*=>", RegexOptions.Compiled);
    // var foo = x => {
    static Regex varArrowFuncSingle = new Regex(@"^(?:export\s+)?(?:var|let|const)\s+(?:async\s+)?([A-Za-z_][\w_]*)(?:\s*:\s*[^=]+)?\s*=\s*([A-Za-z_$][\w$]*)\s*=>", RegexOptions.Compiled);
    // class property arrow functions: increment = () => { } or increment = x => { }
    static Regex classPropArrow = new Regex(@"^(?:\s*(?:public|private|protected|readonly|static)\s+)*([A-Za-z_][\w_]*)\s*=\s*\(([^)]*)\)\s*=>", RegexOptions.Compiled);
    static Regex classPropArrowSingle = new Regex(@"^(?:\s*(?:public|private|protected|readonly|static)\s+)*([A-Za-z_][\w_]*)\s*=\s*([A-Za-z_$][\w$]*)\s*=>", RegexOptions.Compiled);
    // property: foo: Type; or foo?: Type; or readonly foo: Type = ...
    // Do not match method signatures (e.g., foo(): Type;) — ensure no '(' follows the name or optional '?'
    // Allow trailing comma for object literal properties.
    static Regex propertyRegex = new Regex(@"^(?:\s*(?:public|private|protected|readonly|static|export)\s+)*([A-Za-z_][\w_]*)(?:\?)?\s*(?!\()\s*[:?]\s*[^,;=\{]+[,;]?", RegexOptions.Compiled);
    // top-level const/let/var with optional type annotation
    static Regex topLevelVarRegex = new Regex(@"^(?:export\s+)?(?:const|let|var)\s+([A-Za-z_][\w_]*)(?:\s*:\s*[^=;]+)?\s*(?:=|;)", RegexOptions.Compiled);

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

    static int ParentLineIndexOf(string[] code, int childIndex)
    {
        var childIndent = code[childIndex].GetIndent();

        for (int i = childIndex - 1; i >= 0; i--)
        {
            if (code[i].Length > 0)
            {
                var currentIndent = code[i].GetIndent();
                if (currentIndent < childIndent)
                    return i;
            }
        }
        return -1;
    }

    // Heuristic: determine if a parent declaration is a const/var/let component
    // with a function body that contains an explicit 'return' (e.g., React functional component).
    // Determine whether the declaration line is explicitly typed as a React component
    static bool IsTypedReactComponentDeclaration(string declLine)
    {
        if (string.IsNullOrWhiteSpace(declLine))
            return false;

        // look for a TypeScript type annotation after ':' that mentions React/FC/JSX
        var idx = declLine.IndexOf(':');
        if (idx < 0)
            return false;

        var typePart = declLine.Substring(idx + 1);
        return typePart.Contains("React") || typePart.Contains("FC") || typePart.Contains("JSX.Element") || typePart.Contains("ReactNode") || typePart.Contains("ComponentType");
    }

    // Find the first 'return' line index within the function/block starting at parentIndex; -1 if not found
    static int FirstReturnIndexInFunction(string[] code, int parentIndex)
    {
        if (parentIndex < 0 || parentIndex >= code.Length)
            return -1;

        var decl = code[parentIndex].TrimStart();
        // only consider variable declarations that look like arrow functions or function expressions
        if (!(decl.StartsWith("const ") || decl.StartsWith("let ") || decl.StartsWith("var ") || decl.Contains("= function")))
            return -1;

        // quick check: must contain => or = function somewhere on the declaration or following lines
        if (!decl.Contains("=>") && !decl.Contains("= function"))
        {
            int limit = Math.Min(code.Length - 1, parentIndex + 3);
            bool foundArrow = false;
            for (int j = parentIndex; j <= limit; j++)
            {
                if (code[j].Contains("=>") || code[j].Contains("= function"))
                {
                    foundArrow = true;
                    break;
                }
            }
            if (!foundArrow)
                return -1;
        }

        // Detect concise arrow JSX returns (e.g., () => <div/> or () => (
        //   <div/>
        // ) ). If the declaration (or few following lines) contains '=>' and
        // the expression starts with '<' (JSX) then treat that line as the return index.
        int arrowLine = -1;
        for (int j = parentIndex; j < Math.Min(code.Length, parentIndex + 6); j++)
        {
            if (code[j].Contains("=>"))
            {
                arrowLine = j;
                // if the arrow and JSX are on same line
                var idx = code[j].IndexOf("=>");
                if (idx >= 0 && code[j].Substring(idx + 2).TrimStart().StartsWith("<"))
                    return j;
                break;
            }
        }
        if (arrowLine != -1)
        {
            // look for next non-empty, non-comment line after arrow
            int k = arrowLine + 1;
            while (k < code.Length && string.IsNullOrWhiteSpace(code[k])) k++;
            if (k < code.Length)
            {
                var next = code[k].TrimStart();
                if (next.StartsWith("<") || (next.StartsWith("(") && next.Contains("<")))
                    return k;
            }
        }

        int braceDepth = 0;
        bool started = false;
        for (int i = parentIndex; i < code.Length; i++)
        {
            var line = code[i];
            if (line.TrimStart().StartsWith("//"))
                continue;

            if (line.Contains("{"))
            {
                started = true;
                braceDepth += line.Count(c => c == '{');
            }
            if (started && line.Contains("return "))
                return i;

            if (started && line.Contains("}"))
            {
                braceDepth -= line.Count(c => c == '}');
                if (braceDepth <= 0)
                    break;
            }
        }

        return -1;
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

            // If this line is inside the return(...) block of a typed React component
            // skip processing it entirely so nested callbacks/props inside returned JSX
            // are not tracked. Find nearest declaration ancestor and check its return index.
            int declAncestor = FindAncestorIndexMatching(code, parseIndex, l => IsDeclarationFunctionOrClass(l));
            if (declAncestor != -1)
            {
                var declLine = code[declAncestor].TrimStart();
                if (IsTypedReactComponentDeclaration(declLine))
                {
                    int retIdx = classRegex.IsMatch(declLine) ? FirstReturnIndexInBlock(code, declAncestor) : FirstReturnIndexInFunction(code, declAncestor);
                    // only skip lines that are after the return index (do not skip the declaration line itself)
                    if (retIdx != -1 && parseIndex > retIdx)
                        continue;
                }
            }

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

                // detect if this function is nested inside another function/method/arrow/etc
                int parentIndex = ParentLineIndexOf(code, parseIndex);
                if (parentIndex != -1)
                {
                    // find nearest ancestor that is a typed React component declaration
                    int componentAncestor = FindAncestorIndexMatching(code, parseIndex, l => IsDeclarationFunctionOrClass(l));
                    if (componentAncestor != -1)
                    {
                        var declLine = code[componentAncestor].TrimStart();
                        int returnIndex = -1;
                        if (classRegex.IsMatch(declLine))
                            returnIndex = FirstReturnIndexInBlock(code, componentAncestor);
                        else
                            returnIndex = FirstReturnIndexInFunction(code, componentAncestor);

                        if (returnIndex != -1 && parseIndex >= returnIndex)
                            continue;
                    }
                    var parent = code[parentIndex];

                    var ptrim = parent.TrimStart();
                    if (functionRegex.IsMatch(ptrim) || classMethod.IsMatch(ptrim) || varFuncExpr.IsMatch(ptrim) || varArrowFunc.IsMatch(ptrim) || varArrowFuncSingle.IsMatch(ptrim))
                    {
                        // prefer enclosing class as parent so nested functions inside methods appear under the class
                        int classAncestor = FindAncestorIndexMatching(code, parseIndex, l => classRegex.IsMatch(l.TrimStart()));
                        var titleSource = classAncestor != -1 ? code[classAncestor] : parent;
                        string parentTitle;
                        var titleTrimmed = titleSource.TrimStart();
                        var classMatch = classRegex.Match(titleTrimmed);
                        if (classMatch.Success)
                        {
                            parentTitle = classMatch.Groups[1].Value;
                        }
                        else
                        {
                            parentTitle = titleSource.Split('(').First()
                                .Replace("export", "")
                                .Replace("public", "")
                                .Replace("static", "")
                                .Replace("class ", "")
                                .Replace("interface ", "")
                                .Replace("{", "");
                            parentTitle = parentTitle.Split('(').First().Trim();
                        }
                        name = parentTitle + "." + name;
                    }
                }

                var info = new MemberInfo();
                info.Line = parseIndex;
                info.MemberContext = "";
                // if nested under a class, assign ParentPath to class and Name to the dotted function name.
                // Use the short name for Content so Structure() shows the short name while Name is dotted.
                string shortName = name;
                if (name.Contains('.'))
                {
                    var parts = name.Split(new[] { '.' }, 2);
                    info.ParentPath = parts[0];
                    info.Name = parts[0] + "." + parts[1];
                    shortName = parts[1];
                }
                else
                {
                    info.ParentPath = "";
                    info.Name = name;
                }
                info.MemberType = MemberType.Method;
                info.MethodParameters = match.Groups[2].Value;
                // Content should start as the dotted name (Structure() will trim to short name)
                info.Content = showMethodParams ? info.Name + parms : info.Name + "(...)";
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
                    // prefer enclosing class as parent so nested methods/properties inside methods appear under the class
                    int classAncestor = FindAncestorIndexMatching(code, i, l => classRegex.IsMatch(l.TrimStart()));
                    var titleSource = classAncestor != -1 ? code[classAncestor] : parent;
                    string parentTitle;
                    var titleTrimmed = titleSource.TrimStart();
                    var classMatch = classRegex.Match(titleTrimmed);
                    if (classMatch.Success)
                    {
                        parentTitle = classMatch.Groups[1].Value;
                    }
                    else
                    {
                        parentTitle = titleSource.Split('(').First()
                            .Replace("export", "")
                            .Replace("public", "")
                            .Replace("static", "")
                            .Replace("class ", "")
                            .Replace("interface ", "")
                            .Replace("{", "");
                        parentTitle = parentTitle.Split('(').First().Trim();
                    }
                    name = parentTitle + "." + name;
                }

                var info = new MemberInfo();
                info.Line = parseIndex;
                info.MemberContext = "";
                string shortName = name;
                if (name.Contains('.'))
                {
                    var parts = name.Split(new[] { '.' }, 2);
                    info.ParentPath = parts[0];
                    info.Name = parts[0] + "." + parts[1];
                    shortName = parts[1];
                }
                else
                {
                    info.ParentPath = "";
                    info.Name = name;
                }
                info.MemberType = shortName == "constructor" ? MemberType.Constructor : MemberType.Method;
                info.MethodParameters = match.Groups[2].Value;
                // Content should start as the dotted name (Structure() will trim to short name)
                info.Content = showMethodParams ? info.Name + parms : info.Name + "(...)";
                map.Add(info);
                continue;
            }

            // class property arrow functions like: increment = () => { }
            if ((match = classPropArrow.Match(line)).Success || (match = classPropArrowSingle.Match(line)).Success)
            {
                // determine enclosing class
                int classAncestor = FindAncestorIndexMatching(code, parseIndex, l => classRegex.IsMatch(l.TrimStart()));
                if (classAncestor == -1)
                {
                    // treat as top-level property if no enclosing class
                    var name = match.Groups[1].Value;
                    var info = new MemberInfo { Line = parseIndex, MemberContext = "" };
                    if (name.Contains('.'))
                    {
                        var parts = name.Split(new[] { '.' }, 2);
                        info.ParentPath = parts[0];
                        // store dotted name so Structure() can group under the parent (e.g., Parent.Child)
                        info.Name = parts[0] + "." + parts[1];
                        // Content should be the short member name for display
                        info.Content = parts[1];
                    }
                    else
                    {
                        info.ParentPath = "";
                        info.Name = name;
                        info.Content = name;
                    }
                    info.MemberType = MemberType.Property;
                    map.Add(info);
                    continue;
                }

                // If inside a method's return block, skip
                int methodAncestor = FindAncestorIndexMatching(code, parseIndex, l => classMethod.IsMatch(l.TrimStart()));
                if (methodAncestor != -1)
                {
                    int returnIndex = FirstReturnIndexInBlock(code, methodAncestor);
                    if (returnIndex != -1 && parseIndex >= returnIndex)
                        continue;
                }

                var classLine = code[classAncestor];
                var classMatch = classRegex.Match(classLine.TrimStart());
                string parentTitle = classMatch.Success ? classMatch.Groups[1].Value : classLine.Split('(').First().Trim();
                var propName = match.Groups[1].Value;
                var infoProp = new MemberInfo { Line = parseIndex, MemberContext = "" };
                infoProp.ParentPath = parentTitle;
                infoProp.Name = parentTitle + "." + propName;
                infoProp.MemberType = MemberType.Property;
                infoProp.Content = infoProp.Name;
                map.Add(infoProp);
                continue;
            }

            if ((match = varFuncExpr.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parms = "(" + match.Groups[2].Value + ")";

                    var parentIndex = ParentLineIndexOf(code, parseIndex);
                    if (parentIndex != -1)
                    {
                    int componentAncestor = FindAncestorIndexMatching(code, parseIndex, l => IsDeclarationFunctionOrClass(l));
                    if (componentAncestor != -1)
                    {
                        var declLine = code[componentAncestor].TrimStart();
                        int returnIndex = -1;
                        if (classRegex.IsMatch(declLine))
                            returnIndex = FirstReturnIndexInBlock(code, componentAncestor);
                        else
                            returnIndex = FirstReturnIndexInFunction(code, componentAncestor);

                        if (returnIndex != -1 && parseIndex >= returnIndex)
                            continue;
                    }
                        var parent = code[parentIndex];

                    var ptrim = parent.TrimStart();
                    // only treat as nested when parent is a function-like declaration
                    if (functionRegex.IsMatch(ptrim) || classMethod.IsMatch(ptrim) || varFuncExpr.IsMatch(ptrim) || varArrowFunc.IsMatch(ptrim) || varArrowFuncSingle.IsMatch(ptrim))
                    {
                        int classAncestor = FindAncestorIndexMatching(code, parseIndex, l => classRegex.IsMatch(l.TrimStart()));
                        var titleSource = classAncestor != -1 ? code[classAncestor] : parent;
                        string parentTitle;
                        var titleTrimmed = titleSource.TrimStart();
                        var classMatch = classRegex.Match(titleTrimmed);
                        if (classMatch.Success)
                        {
                            parentTitle = classMatch.Groups[1].Value;
                        }
                        else
                        {
                            parentTitle = titleSource.Split('(').First()
                                .Replace("export", "")
                                .Replace("public", "")
                                .Replace("static", "")
                                .Replace("class ", "")
                                .Replace("interface ", "")
                                .Replace("{", "");
                            parentTitle = parentTitle.Split('(').First().Trim();
                        }
                        name = parentTitle + "." + name;
                    }
                    else
                    {
                        // skip function expressions inside non-function blocks (e.g., inside if/for) to avoid noise
                        continue;
                    }
                }

                var info = new MemberInfo { Line = parseIndex, MemberContext = "" };
                // If this is a top-level declaration (no parent index), allow treating
                // typed React function expressions as properties (components).
                if (parentIndex == -1)
                {
                    var rawDecl = code[parseIndex].TrimStart();
                    bool isConstDecl = rawDecl.StartsWith("const ") || rawDecl.StartsWith("let ") || rawDecl.StartsWith("export const ") || rawDecl.StartsWith("export let ");
                    bool isComponent = IsTypedReactComponentDeclaration(rawDecl);
                    info.ParentPath = "";
                    // include type annotation in name when present on const declarations
                    var nameWithType = AppendTypeAnnotation(rawDecl, name);
                    info.Name = nameWithType;
                    info.Content = nameWithType;
                    // const declarations should be treated as properties; typed components are properties too
                    info.MemberType = isConstDecl ? MemberType.Property : (isComponent ? MemberType.Property : MemberType.Method);
                    info.MethodParameters = match.Groups[2].Value;
                    if (!isComponent && !isConstDecl)
                        info.Content = showMethodParams ? nameWithType + parms : nameWithType + "(...)";
                    map.Add(info);
                    continue;
                }

                if (name.Contains('.'))
                {
                    var parts = name.Split(new[] { '.' }, 2);
                    info.ParentPath = parts[0];
                    info.Name = parts[1];
                }
                else
                {
                    info.ParentPath = "";
                    info.Name = name;
                }
                info.MemberType = MemberType.Method;
                info.MethodParameters = match.Groups[2].Value;
                info.Content = showMethodParams ? info.Name + parms : info.Name + "(...)";
                map.Add(info);
                continue;
            }

            if ((match = varArrowFunc.Match(line)).Success || (match = varArrowFuncSingle.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                var parms = match.Groups.Count > 2 ? "(" + match.Groups[2].Value + ")" : "(...)";

                // if this arrow function appears inside a component's return(...) block, skip it
                int componentAncestor = FindAncestorIndexMatching(code, parseIndex, l => IsDeclarationFunctionOrClass(l));
                if (componentAncestor != -1)
                {
                    var declLine = code[componentAncestor].TrimStart();
                    int returnIndex = -1;
                    if (classRegex.IsMatch(declLine))
                        returnIndex = FirstReturnIndexInBlock(code, componentAncestor);
                    else
                        returnIndex = FirstReturnIndexInFunction(code, componentAncestor);

                    if (IsTypedReactComponentDeclaration(declLine) && returnIndex != -1 && parseIndex >= returnIndex)
                    {
                        // inside return block of a typed component
                        continue;
                    }
                }

                var parentIndex = ParentLineIndexOf(code, parseIndex);
                if (parentIndex != -1)
                {
                    var parent = code[parentIndex];
                    // if parent is a typed React component, skip tracking any nested members inside it
                    if (IsTypedReactComponentDeclaration(parent))
                        continue;

                    var ptrim = parent.TrimStart();
                    if (functionRegex.IsMatch(ptrim) || classMethod.IsMatch(ptrim) || varFuncExpr.IsMatch(ptrim) || varArrowFunc.IsMatch(ptrim) || varArrowFuncSingle.IsMatch(ptrim))
                    {
                        int classAncestor = FindAncestorIndexMatching(code, parseIndex, l => classRegex.IsMatch(l.TrimStart()));
                        var titleSource = classAncestor != -1 ? code[classAncestor] : parent;
                        string parentTitle;
                        var titleTrimmed = titleSource.TrimStart();
                        var classMatch = classRegex.Match(titleTrimmed);
                        if (classMatch.Success)
                        {
                            parentTitle = classMatch.Groups[1].Value;
                        }
                        else
                        {
                            parentTitle = titleSource.Split('(').First()
                                .Replace("export", "")
                                .Replace("public", "")
                                .Replace("static", "")
                                .Replace("class ", "")
                                .Replace("interface ", "")
                                .Replace("{", "");
                            parentTitle = parentTitle.Split('(').First().Trim();
                        }
                        name = parentTitle + "." + name;
                    }
                    else
                    {
                        // skip arrow functions inside non-function blocks
                        continue;
                    }
                }

    // Treat const arrow declarations as properties and append any type annotation present
    var rawDecl = code[parseIndex].TrimStart();
    bool isConstDecl = rawDecl.StartsWith("const ") || rawDecl.StartsWith("let ") || rawDecl.StartsWith("export const ") || rawDecl.StartsWith("export let ");
    bool isComponentDecl = IsTypedReactComponentDeclaration(rawDecl);
    var info = new MemberInfo { Line = parseIndex, MemberContext = "" };
    if (name.Contains('.'))
    {
        var parts = name.Split(new[] { '.' }, 2);
        info.ParentPath = parts[0];
        // store dotted name so Structure() can group under the parent (e.g., Class.Member)
        info.Name = parts[0] + "." + parts[1];
        // Content should start as the dotted name so Structure() can trim to the short name
        info.Content = info.Name;
    }
    else
    {
        info.ParentPath = "";
        var nameWithType = AppendTypeAnnotation(rawDecl, name);
        info.Name = nameWithType;
        // Content should preserve the type annotation for typed React components,
        // otherwise show a short, user-facing name without 'const' or type annotation
        info.Content = isComponentDecl ? nameWithType : ShortDecl(nameWithType);
    }
    info.MemberType = isConstDecl ? MemberType.Property : (isComponentDecl ? MemberType.Property : MemberType.Method);
    info.MethodParameters = match.Groups.Count > 2 ? match.Groups[2].Value : "";
    // For display: methods show parameters; consts/components keep the short name (with type if present)
    if (!isConstDecl && !isComponentDecl)
        info.Content = showMethodParams ? info.Content + parms : info.Content + "(...)";
    map.Add(info);
    continue;
            }

            if ((match = propertyRegex.Match(line)).Success)
            {
                var name = match.Groups[1].Value;
                int parentIndex = ParentLineIndexOf(code, parseIndex);
                if (parentIndex != -1)
                {
                    // If the parent is a function/method/arrow/var function, this is likely a parameter line - skip it
                    var parent = code[parentIndex];
                    // if parent is a typed React component, skip tracking nested members that appear in its return block
                    var returnIndex = FirstReturnIndexInFunction(code, parentIndex);
                    if (IsTypedReactComponentDeclaration(parent) && returnIndex != -1 && parseIndex >= returnIndex)
                    {
                        // do not track nested properties inside the component's return block
                        continue;
                    }

                    var ptrim = parent.TrimStart();
                    if (functionRegex.IsMatch(ptrim) || classMethod.IsMatch(ptrim) || varFuncExpr.IsMatch(ptrim) || varArrowFunc.IsMatch(ptrim) || varArrowFuncSingle.IsMatch(ptrim))
                    {
                        // nested property inside a function-like parent: skip
                        continue;
                    }

                    // If the parent line is a top-level var/const/let declaration with an object literal,
                    // attach this property as a child of that top-level declaration and display the simple key name.
                    var topVarMatch = topLevelVarRegex.Match(ptrim);
                    if (topVarMatch.Success)
                    {
                        var parentVar = topVarMatch.Groups[1].Value;
                        var parentNameWithType = AppendTypeAnnotation(ptrim, parentVar);

                        // detect if the property value is a function type or arrow function (=>) so we
                        // represent it as a Method rather than a plain Property
                        var matchedText = match.Value;
                        bool isFuncTyped = matchedText.Contains("=>") || matchedText.Contains("function(");
                        string funcParams = "";
                        if (isFuncTyped)
                        {
                            var pstart = matchedText.IndexOf('(');
                            if (pstart >= 0)
                            {
                                var pend = matchedText.IndexOf(')', pstart);
                                if (pend > pstart)
                                    funcParams = matchedText.Substring(pstart + 1, pend - pstart - 1);
                            }
                        }

                        var infoVarProp = new MemberInfo
                        {
                            Line = parseIndex,
                            ParentPath = parentNameWithType,
                            Name = parentNameWithType + "." + name,
                            MemberContext = "",
                            MemberType = isFuncTyped ? MemberType.Method : MemberType.Property,
                            Content = isFuncTyped ? (showMethodParams ? name + "(" + funcParams + ")" : name + "(...)") : name
                        };
                        map.Add(infoVarProp);
                        continue;
                    }

                    // prefer enclosing class as parent for property nesting
                    int classAncestor = FindAncestorIndexMatching(code, parseIndex, l => classRegex.IsMatch(l.TrimStart()));
                    var titleSource = classAncestor != -1 ? code[classAncestor] : parent;
                    string parentTitle;
                    var titleTrimmed = titleSource.TrimStart();
                    var classMatch = classRegex.Match(titleTrimmed);
                    if (classMatch.Success)
                    {
                        parentTitle = classMatch.Groups[1].Value;
                    }
                    else
                    {
                        parentTitle = titleSource.Split('(').First()
                            .Replace("export", "")
                            .Replace("public", "")
                            .Replace("static", "")
                            .Replace("class ", "")
                            .Replace("interface ", "")
                            .Replace("{", "");
                        parentTitle = parentTitle.Split('(').First().Trim();
                    }
                    name = parentTitle + "." + name;
                }

                var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = name, MemberContext = "", MemberType = MemberType.Property, Content = name };
                // If the property's type is a function expression (e.g., (): void => ...), mark it as a method
                var fullMatch = match.Value;
                if (fullMatch.Contains("=>") || fullMatch.Contains("function("))
                {
                    var pstart = fullMatch.IndexOf('(');
                    if (pstart >= 0)
                    {
                        var pend = fullMatch.IndexOf(')', pstart);
                        if (pend > pstart)
                        {
                            var parms = fullMatch.Substring(pstart + 1, pend - pstart - 1);
                            info.MemberType = MemberType.Method;
                            info.MethodParameters = parms;
                            info.Content = showMethodParams ? info.Name + "(" + parms + ")" : info.Name + "(...)";
                        }
                    }
                }
                map.Add(info);
                continue;
            }

            // top-level const/let/var (file-level properties). Do not track properties inside functions or interfaces.
            if ((match = topLevelVarRegex.Match(line)).Success)
            {
                var parent = ParentLineOf(code, parseIndex);
                if (parent == null)
                {
                    var name = match.Groups[1].Value;
                    var rawDecl = code[parseIndex].TrimStart();
                    var nameWithType = AppendTypeAnnotation(rawDecl, name);
                    // If this top-level var is a typed React component, preserve the type annotation in the display Content
                    var content = IsTypedReactComponentDeclaration(rawDecl) ? nameWithType : ShortDecl(nameWithType);
                    var info = new MemberInfo { Line = parseIndex, ParentPath = "", Name = nameWithType, MemberContext = "", MemberType = MemberType.Property, Content = content };
                    map.Add(info);
                }
                continue;
            }
        }

        return map;
    }

    // Note: Structure extension method is implemented in JavaScriptMapper to
    // avoid duplicate extension methods causing ambiguity. We rely on that
    // implementation here.
}