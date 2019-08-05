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
            var parsed_args = new Dictionary<string, string>();
            foreach (var str in args)
            {
                if (!string.IsNullOrWhiteSpace(str) && str.Contains('-'))
                {
                    var arg = str.TrimStart('-');
                    var end = arg.IndexOf("=");
                    if (end == -1)
                    {
                        parsed_args.Add(arg, null);
                    }
                    else
                    {
                        parsed_args.Add(arg.Substring(0, arg.IndexOf('=')), arg.Substring(arg.IndexOf('=') + 1));
                    }
                }
            }

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
                Console.WriteLine("    --bench");
                Console.WriteLine("        Measures the amount of time required to run the specified file.");
                Console.WriteLine();
                Console.WriteLine("    --compile=<filename>");
                Console.WriteLine("        Compiles a brainfuck file.");
                Console.WriteLine("    --out=<filename>");
                Console.WriteLine("        Specifies the output of the compiler. Defaults to <in_filename> + \".exe\".");
                Console.WriteLine("        If you wish to output a class library, make sure the file name ends in \".dll\".");
                Console.WriteLine();
                Console.WriteLine("    --assembly=<name>");
                Console.WriteLine("        Specifies the resulting assembly name. Defaults to \"BF2IL\".");
                Console.WriteLine("    --type=<name>");
                Console.WriteLine("        Specifies the resulting type name. Can contain namespaces. Defaults to \"BF2IL\".");
                Console.WriteLine("    --method=<name>");
                Console.WriteLine("        Specifies the resulting method name. Defaults to \"Execute\".");
                Console.WriteLine();

                return;
            }

            var compiler = new Compiler();

            if (parsed_args.TryGetValue("out", out var o))
            {
                compiler.OutputFileName = o;
            }

            if (parsed_args.TryGetValue("assembly", out var a))
            {
                compiler.AssemblyName = a;
            }

            if (parsed_args.TryGetValue("type", out var t))
            {
                compiler.TypeName = t;
            }

            if (parsed_args.TryGetValue("method", out var m))
            {
                compiler.MethodName = m;
            }

            if (parsed_args.ContainsKey("skip_io"))
            {
                compiler.SkipIO = true;
            }

            if (parsed_args.TryGetValue("compile", out var file))
            {
                using (var stream = File.OpenRead(file))
                {
                    if (compiler.OutputFileName == null)
                    {
                        compiler.OutputFileName = Path.GetFileNameWithoutExtension(file) + ".exe";
                    }

                    Console.WriteLine($"Compiling {Path.GetFileName(file)}...");
                    var tokens = compiler.Tokenise(stream);

                    Console.WriteLine($"Compiling {tokens.Count} tokens...");

                    compiler.CompileTokens(tokens);
                    Console.WriteLine($"Compiled {tokens.Count} tokens!");

                    if (Path.GetExtension(compiler.OutputFileName) == ".exe")
                    {
                        compiler.SaveAsExecutable();
                    }
                    else
                    {
                        compiler.SaveAsClassLibrary();
                    }

                    Console.WriteLine($"Saved to {compiler.OutputFileName}!");
                }

                return;
            }

            if (parsed_args.TryGetValue("run", out var file1))
            {
                using (var stream = File.OpenRead(file1))
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

                    if(watch != null)
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
