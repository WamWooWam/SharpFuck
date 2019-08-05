using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpFuck
{
    class Program
    {
        static void Main(string[] args)
        {
            var argsList = new List<(string, string)>();
            foreach (var str in args)
            {
                if (!string.IsNullOrWhiteSpace(str) && str.Contains('-'))
                {
                    var arg = str.TrimStart('-');
                    var end = arg.IndexOf("=");

                    if (end == -1)
                        argsList.Add((arg, null));
                    else
                        argsList.Add((arg.Substring(0, arg.IndexOf('=')), arg.Substring(arg.IndexOf('=') + 1)));
                }
            }

            var parsed_args = argsList
                .GroupBy(g => g.Item1, g => g.Item2)
                .ToDictionary(g => g.Key, g => g.ToArray());

            if (parsed_args.ContainsKey("help"))
            {
                Console.WriteLine("SharpFuck - BrainFuck -> IL Compiler");
                Console.WriteLine();
                Console.WriteLine("Usage: sharpfuck.exe");
                Console.WriteLine("    --help");
                Console.WriteLine("        Show this help text.");
                Console.WriteLine();
                Console.WriteLine("    --run=<filename>");
                Console.WriteLine("        Runs a brainfuck file by compiling then executing it.");
                Console.WriteLine();
                Console.WriteLine("        --bench");
                Console.WriteLine("            Measures the amount of time required to run the specified file.");
                Console.WriteLine();
                Console.WriteLine("    --compile=<filename>");
                Console.WriteLine("        Compiles a brainfuck file.");
                Console.WriteLine();
                Console.WriteLine("        --out=<filename>");
                Console.WriteLine("            Specifies the output of the compiler. Defaults to <in_filename> + \".exe\".");
                Console.WriteLine("            If you wish to output a class library, make sure the file name ends in \".dll\".");
                Console.WriteLine("        --assembly=<name>");
                Console.WriteLine("             Specifies the resulting assembly name. Defaults to \"BF2IL\".");
                Console.WriteLine("        --type=<name>");
                Console.WriteLine("             Specifies the resulting type name. Can contain namespaces. Defaults to \"BF2IL\".");
                Console.WriteLine();

                return;
            }

            var compiler = new Compiler();

            if (parsed_args.TryGetValue("out", out var o))
            {
                compiler.OutputFileName = o[0];
            }

            if (parsed_args.TryGetValue("assembly", out var a))
            {
                compiler.AssemblyName = a[0];
            }

            if (parsed_args.TryGetValue("type", out var t))
            {
                compiler.TypeName = t[0];
            }

            if (parsed_args.ContainsKey("skip_io"))
            {
                compiler.SkipIO = true;
            }

            if (parsed_args.TryGetValue("compile", out var files))
            {
                if (compiler.OutputFileName == null)
                {
                    compiler.OutputFileName = Path.GetFileNameWithoutExtension(files.First()) + ".exe";
                }

                var ext = Path.GetExtension(compiler.OutputFileName);
                if (ext == ".exe" && files.Length > 1)
                {
                    Console.WriteLine("Only class libraries can contain more than one brainfuck program.");
                    return;
                }

                var typeBuilder = compiler.CreateTypeBuilder();
                var entryPoint = "";

                foreach (var file in files)
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static);

                        if (string.IsNullOrEmpty(entryPoint))
                            entryPoint = name;

                        Console.WriteLine($"Compiling {name}...");
                        var tokens = compiler.Tokenise(stream);

                        Console.WriteLine($"Compiling {tokens.Count} tokens...");
                        compiler.CompileTokens(tokens, method);

                        Console.WriteLine($"Compiled {tokens.Count} tokens!");
                    }
                }

                var type = typeBuilder.CreateType();

                if (Path.GetExtension(compiler.OutputFileName) == ".exe")
                {
                    var entry = type.GetMethod(entryPoint);
                    compiler.SaveAsExecutable(entry);
                }
                else
                {
                    compiler.SaveAsClassLibrary();
                }

                Console.WriteLine($"Saved to {compiler.OutputFileName}!");
                return;
            }

            if (parsed_args.TryGetValue("run", out var file1))
            {
                if (files.Length > 1)
                {
                    Console.WriteLine("Cannot run more than one file.");
                    return;
                }

                using (var stream = File.OpenRead(file1[0]))
                {
                    compiler.OutputFileName = "BF2IL.exe";

                    var tokens = compiler.Tokenise(stream);
                    compiler.CompileTokens(tokens);

                    Stopwatch watch = null;
                    if (parsed_args.ContainsKey("bench"))
                    {
                        watch = Stopwatch.StartNew();
                    }

                    compiler.Execute();

                    if (watch != null)
                    {
                        Console.WriteLine(watch.Elapsed);
                    }
                }

                return;
            }

            Console.WriteLine("No input specified. Use \"sharpfuck.exe --help\" for help!");

        }
    }
}
