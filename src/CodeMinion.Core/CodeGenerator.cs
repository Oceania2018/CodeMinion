﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CodeMinion.Core.Attributes;
using CodeMinion.Core.Helpers;
using CodeMinion.Core.Models;

namespace CodeMinion.Core
{
    public class CodeGenerator
    {
        public CodeGenerator()
        {
            LoadTemplates();
        }

        public List<StaticApi> StaticApis { get; set; } = new List<StaticApi>();

        public string NameSpace { get; set; } = "Numpy";
        public HashSet<string> Usings { get; set; } = new HashSet<string>()
        {
            @"using System;",
            @"using System.Collections;",
            @"using System.Collections.Generic;",
            @"using System.IO;",
            @"using System.Linq;",
            @"using System.Runtime.InteropServices;",
            @"using System.Text;",
            @"using Python.Runtime;",
        };

        protected Dictionary<string, BodyTemplate> _templates;
        protected virtual void LoadTemplates()
        {
            _templates = Assembly.GetEntryAssembly().GetTypes()
                .Where(x => x.GetCustomAttribute<TemplateAttribute>() != null)
                .Select(x => (BodyTemplate)Activator.CreateInstance(x)).ToDictionary(x =>
                    x.GetType().GetCustomAttribute<TemplateAttribute>().ApiFunction);
        }

        // generate an entire API function declaration
        protected virtual void GenerateApiFunction(Declaration input_decl, CodeWriter s)
        {
            foreach (var decl in ExpandOverloads(input_decl))
            {
                var retval = GenerateReturnType(decl);
                var arguments = GenerateArguments(decl);
                GenerateDocString(decl, s);
                var generics = decl.Generics == null ? "" : $"<{string.Join(",", decl.Generics)}>";
                string declaration = $"public {retval} {decl.Name}{generics}({arguments})";
                s.Out(declaration);
                s.Block(()=>{
                    GenerateBody(decl, s);
                });
                s.Break();
            }
        }

        // generate the argument list between the parentheses of a generated API function
        protected virtual string GenerateArguments(Declaration decl)
        {
            var s = new StringBuilder();
            int i = 0;
            foreach (var arg in decl.Arguments)
            {
                // TODO modifier (if any)
                // parameter type
                s.Append(MapType(arg));
                if (arg.IsNullable && arg.IsValueType)
                    s.Append("?");
                s.Append(" ");
                // parameter name
                s.Append(EscapeName(arg.Name));
                if (!string.IsNullOrWhiteSpace(arg.DefaultValue))
                    s.Append($" = {MapDefaultValue(arg.DefaultValue)}");
                else if (arg.IsNullable)
                    s.Append($" = null");
                i++;
                if (i < decl.Arguments.Count)
                    s.Append(", ");
            }
            return s.ToString();
        }

        private string GeneratePassedArgs(Declaration decl)
        {
            var s = new StringBuilder();
            int i = 0;
            foreach (var arg in decl.Arguments)
            {
                // TODO modifier (if any)
                var argname = EscapeName(arg.Name);
                if (arg.IsNamedArg)
                    s.Append($"{argname}:");
                s.Append(argname);
                i++;
                if (i < decl.Arguments.Count)
                    s.Append(", ");
            }
            return s.ToString();
        }

        protected virtual IEnumerable<Declaration> ExpandOverloads(Declaration decl)
        {
            // todo: let's hope there are not multiple expansions in one declaration, or else this will get complicated
            if (decl.Arguments.Any(a => a.Type == "(array_like)"))
            {
                foreach (var type in "NumSharp.NDArray T[]".Split())
                {
                    var clone_decl = decl.Clone();
                    clone_decl.Arguments.ForEach(a =>
                    {
                        if (a.Type == "(array_like)")
                            a.Type = type;
                    });
                    if (type == "T[]")
                        clone_decl.Generics = new string[] { "T" };
                    yield return clone_decl;
                }
                yield break;
            }
            yield return decl;
        }


        // maps None to null, etc
        protected virtual string MapDefaultValue(string DefaultValue)
        {
            switch (DefaultValue)
            {
                case "None": return "null";
                case "True": return "true";
                case "False": return "false";
            }
            return DefaultValue;
        }

        // list of c# keywords that are not allowed as variable names or parameter names
        protected readonly HashSet<string> _disallowed_names = new HashSet<string>()
        {
            "abstract", "as", "base", "bool", "break",
            "byte", "case", "catch", "char", "checked",
            "class",   "const",   "continue", "decimal", "default",
            "delegate",    "do", "double",  "else", "enum",
            "event",   "explicit", "extern", "false", "finally",
            "fixed", "float",   "for", "foreach", "goto",
            "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace",
            "new", "null", "object", "operator",    "out",
            "override", "params",  "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string",
            "struct",  "switch", "this",    "throw", "true",
            "try", "typeof", "uint",    "ulong",   "unchecked",
            "unsafe", "ushort", "using", "var", "virtual",
            "void", "volatile", "while",
            "add", "alias",   "async", "await", "dynamic",
            "get", "global",  "nameof",  "partial", "remove",
            "set", "value", "when",    "where", "yield",
            "ascending", "by",  "descending", "equals",  "from",
            "group",   "in", "into", "join",    "let",
            "on",  "orderby", "select",  "where"
        };

        // escape a varibale name if it violates C# syntax
        protected virtual string EscapeName(string name)
        {
            if (_disallowed_names.Contains(name))
                return "@" + name;
            return name;
        }

        // generates the return type declaration of a generated API function declaration
        protected virtual string GenerateReturnType(Declaration decl)
        {
            if (decl.returns == null || decl.returns.Count == 0)
                return "void";
            else if (decl.returns.Count == 1)
            {
                return MapType(decl.returns[0]);
            }
            else
            {
                throw new NotImplementedException("Return tuple");
            }
        }

        // maps a Python type to C# type
        protected virtual string MapType(Argument arg)
        {
            switch (arg.Type)
            {
                // basic types
                case "bool":
                    arg.IsValueType = true;
                    return "bool";
                case "int":
                    arg.IsValueType = true;
                    return "int";
                case "int64_t":
                    arg.IsValueType = true;
                    return "long";
                case "double":
                    arg.IsValueType = true;
                    return "double";
                case "string":
                    return "string";
                case "Object":
                    return "object";
                // sequence types
                case "IntArrayRef":
                    if (arg.Name == "size")
                        return "NumSharp.Shape"; // <-- int[] size usually means Shape of the tensor. 
                    return "int[]";
                // torch types
                case "int...":
                    return "NumSharp.Shape";
                case "Tensor":
                    return "Tensor";
                default:
                    arg.IsValueType = true;
                    // Console.WriteLine("MapType doesn't handle type: " + arg.type);
                    return arg.Type.Replace("torch.", string.Empty);
            }
        }

        protected virtual void GenerateDocString(Declaration decl, CodeWriter s)
        {
            // TODO: generate xml doc strings from _torch_docs.py
        }

        // generates only the body of the API function declaration
        protected virtual void GenerateBody(Declaration decl, CodeWriter s)
        {
            if (_templates.ContainsKey(decl.Name))
            {
                // use generator template instead
                _templates[decl.Name].GenerateBody(decl, s);
                return;
            }
            s.Out( "//auto-generated code, do not change");
            // first generate the positional args
            s.Out( $"var args=ToTuple(new object[]");
            s.Block(()=>{
                foreach (var arg in decl.Arguments.Where(a => a.IsNamedArg == false))
                {
                    s.Out( $"{EscapeName(arg.Name)},");
                }
            }, "{", "});");
            // then generate the named args
            s.Out( $"var kwargs=new PyDict();");
            foreach (var arg in decl.Arguments.Where(a => a.IsNamedArg == true))
            {
                var name = EscapeName(arg.Name);
                s.Out( $"if ({name}!=null) kwargs[\"{arg.Name}\"]=ToPython({name});");
            }
            // then call the function
            s.Out( $"dynamic py = self.InvokeMethod(\"{decl.Name}\", args, kwargs);");
            // return the return value if any
            if (decl.returns.Count == 0)
                return;
            if (decl.returns.Count == 1)
                s.Out( $"return ToCsharp<{decl.returns[0].Type}>(py);");
            else
            {
                throw new NotImplementedException("return a tuple or array of return values");
            }
        }

        public virtual void GenerateStaticApi(StaticApi api, CodeWriter s)
        {
            GenerateUsings(s);
            s.Out($"namespace {NameSpace}");
            s.Block(() =>
            {
                s.Out($"public static partial class {api.StaticName}");
                s.Block(() =>
                {
                    s.Break();
                    foreach (var decl in api.Declarations)
                    {
                        GenerateStaticApiRedirection(api, decl, s);
                    }
                    s.Break();
                });
            });
        }

        private void GenerateUsings(CodeWriter s)
        {
            foreach (var @using in Usings)
            {
                s.AppendLine(@using);
            }
            s.AppendLine();
        }

        protected virtual void GenerateStaticApiRedirection(StaticApi api, Declaration input_decl, CodeWriter s)
        {
            foreach (var decl in ExpandOverloads(input_decl))
            {
                var retval = GenerateReturnType(decl);
                var arguments = GenerateArguments(decl);
                var passed_args = GeneratePassedArgs(decl);
                GenerateDocString(decl, s);
                var generics = decl.Generics == null ? "" : $"<{string.Join(",", decl.Generics)}>";
                s.Out($"public static {retval} {decl.Name}{generics}({arguments})");
                s.Indent(()=>s.Out(
                    $"=> {api.SingletonName}.Instance.{decl.Name}({passed_args});"));
                s.AppendLine();
            }
        }

        public virtual void GenerateApiImplementation(StaticApi api, CodeWriter s)
        {
            GenerateUsings(s);
            s.AppendLine($"namespace {NameSpace}");
            s.Block(()=>{
                s.Out($"public partial class {api.SingletonName} : IDisposable");
                s.Block(() => {
                    s.AppendLine();
                    foreach (var decl in api.Declarations)
                    {
                        if (!decl.ManualOverride)
                            GenerateApiFunction(decl, s);
                    }
                    s.Break();
                    s.AppendLine($"private static Lazy<{api.SingletonName}> _instance = new Lazy<{api.SingletonName}>(() => new {api.SingletonName}());");
                    s.AppendLine($"public static {api.SingletonName} Instance => _instance.Value;");
                    s.Break();
                    s.AppendLine($"Lazy<PyObject> _pyobj = new Lazy<PyObject>(() => Py.Import(\"{api.PythonModule}\"));");
                    s.AppendLine($"public dynamic self => _pyobj.Value;");
                    s.Break();
                    s.AppendLine($"Lazy<PyObject> _np = new Lazy<PyObject>(() => Py.Import(\"numpy\"));");
                    s.AppendLine($"public dynamic np => _np.Value;");
                    s.AppendLine($"private {api.SingletonName}() {{ PythonEngine.Initialize(); }}");
                    s.AppendLine($"public void Dispose() {{ PythonEngine.Shutdown(); }}");
                    s.Break();
                });
            });
        }

        protected void WriteFile(string path, Action<CodeWriter> generate_action)
        {
            var s = new CodeWriter();
            try
            {
                generate_action(s);
            }
            catch (Exception e)
            {
                s.AppendLine("\r\n --------------- generator exception ---------------------");
                s.AppendLine(e.Message);
                s.AppendLine(e.StackTrace);
            }
            File.WriteAllText(path, s.ToString());
        }

        public void Generate()
        {
            foreach (var api in StaticApis)
            {
                var api_file = Path.Combine(api.OutputPath, $"{api.StaticName}.gen.cs");
                var impl_file = Path.Combine(api.OutputPath, $"{api.SingletonName}.gen.cs");

                WriteFile(api_file, s => { GenerateStaticApi(api, s); });
                WriteFile(impl_file, s => { GenerateApiImplementation(api, s); });
            }
        }
    }
}
