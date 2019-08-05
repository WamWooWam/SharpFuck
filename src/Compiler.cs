using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace SharpFuck
{
    public struct Token
    {
        public byte token;
        public int count;
    }

    public class Compiler
    {
        private struct Loop
        {
            public Label start;
            public Label condition;
        }

        private AssemblyBuilder _assemblyBuilder;
        private ModuleBuilder _moduleBuilder;
        private MethodInfo _method;
        private Type _type;
        private TypeBuilder _typeBuilder;

        private static readonly byte[] _validTokens = new[]
        {
            (byte)'>',
            (byte)'<',
            (byte)'+',
            (byte)'-',
            (byte)'.',
            (byte)',',
            (byte)'[',
            (byte)']'
        };

        private static readonly MethodInfo _writeConsoleMethod
            = typeof(Console).GetMethod("Write", new[] { typeof(char) });
        private static readonly MethodInfo _readKeyMethod
            = typeof(Console).GetMethod("ReadKey", Type.EmptyTypes);
        private static readonly MethodInfo _getKeyCodeMethod
            = typeof(ConsoleKeyInfo).GetProperty("KeyChar").GetMethod;

        public Compiler()
        {
            AssemblyName = "BF2IL";
            TypeName = "BF2IL";
            MethodName = "Execute";
        }

        public string AssemblyName { get; set; }
        public string OutputFileName { get; set; }
        public string TypeName { get; set; }
        public string MethodName { get; set; }
        public bool SkipIO { get; set; }

        /// <summary>
        /// Processes a list of <see cref="Token"/>s from a stream
        /// </summary>
        /// <param name="stream">The stream to read tokens from</param>
        /// <returns>A list of <see cref="Token"/>s</returns>
        public IReadOnlyList<Token> Tokenise(Stream stream)
        {
            var i = 0;
            var tokens = new List<Token>();
            var currentToken = new Token();
            while ((i = stream.ReadByte()) != -1)
            {
                var b = (byte)i;

                if (currentToken.token == b)
                {
                    currentToken.count++;
                }
                else
                {
                    if (currentToken.token != 0 && currentToken.count != 0)
                        tokens.Add(currentToken);

                    currentToken = new Token() { count = 1, token = b };
                }
            }

            tokens.RemoveAll(t => !_validTokens.Contains(t.token));

            return tokens;
        }

        /// <summary>
        /// Compiles a list of <see cref="Token"/>s to IL.
        /// </summary>
        public void CompileTokens(IReadOnlyList<Token> tokens)
        {
            var type = CreateTypeBuilder();
            var method = type.DefineMethod(MethodName, MethodAttributes.Public | MethodAttributes.Static);

            CompileTokens(tokens, method);

            _type = type.CreateType();
            _method = _typeBuilder.GetMethod(MethodName);
        }

        /// <summary>
        /// Compiles a list of <see cref="Token"/>s into a specific <see cref="MethodBuilder"/>.
        /// </summary>
        public void CompileTokens(IReadOnlyList<Token> tokens, MethodBuilder builder)
        {
            var gen = builder.GetILGenerator();
            EmitTokenList(tokens, gen);
        }

        /// <summary>
        /// Creates a type builder which you can populate with your own methods through 
        /// <see cref="CompileTokens(IReadOnlyList{Token}, MethodBuilder)"/>
        /// </summary>
        public TypeBuilder CreateTypeBuilder()
        {
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(AssemblyName), AssemblyBuilderAccess.RunAndSave);
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(AssemblyName, OutputFileName ?? AssemblyName + ".exe");
            _typeBuilder = _moduleBuilder.DefineType(TypeName, TypeAttributes.Public);

            return _typeBuilder;
        }

        /// <summary>
        /// Executes the compiled output from <see cref="CompileTokens(IReadOnlyList{Token})"/>
        /// </summary>
        public void Execute()
        {
            if (_assemblyBuilder == null)
                throw new InvalidOperationException("Nothing has been compiled yet!");

            _method.Invoke(null, null);
        }

        /// <summary>
        /// Saves the compiled output from <see cref="CompileTokens(IReadOnlyList{Token})"/> to disk as a class library.
        /// </summary>
        public void SaveAsClassLibrary()
        {
            if (_assemblyBuilder == null)
                throw new InvalidOperationException("Nothing has been compiled yet!");

            _assemblyBuilder.Save(OutputFileName);
        }

        /// <summary>
        /// Saves the compiled output from <see cref="CompileTokens(IReadOnlyList{Token})"/> to disk as an executable.
        /// </summary>
        public void SaveAsExecutable(MethodInfo entryPoint = null)
        {
            if (_assemblyBuilder == null)
                throw new InvalidOperationException("Nothing has been compiled yet!");

            _assemblyBuilder.SetEntryPoint(entryPoint ?? _method);
            _assemblyBuilder.Save(OutputFileName);
        }

        /// <summary>
        /// Emits a list of tokens into an <see cref="ILGenerator"/>
        /// </summary>
        private void EmitTokenList(IReadOnlyList<Token> tokens, ILGenerator gen)
        {
            var labels = new Stack<Loop>();
            gen.DeclareLocal(typeof(int)); // 0
            gen.DeclareLocal(typeof(byte[])); // 1
            gen.DeclareLocal(typeof(ConsoleKeyInfo)); // 2

            // init the ptr field to 0
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Stloc_0);

            // create the mem array
            gen.Emit(OpCodes.Ldc_I4, 262144);
            gen.Emit(OpCodes.Newarr, typeof(byte));
            gen.Emit(OpCodes.Stloc_1);

            foreach (var token in tokens)
            {
                switch (token.token)
                {
                    case (byte)'>':
                        CompileIncrementPointerToken(gen, token);
                        break;
                    case (byte)'<':
                        CompileDecrementPointerToken(gen, token);
                        break;
                    case (byte)'+':
                        CompileIncrementValueToken(gen, token);
                        break;
                    case (byte)'-':
                        CompileDecrementValueToken(gen, token);
                        break;
                    default:
                        for (var i = 0; i < token.count; i++)
                        {
                            switch (token.token)
                            {
                                case (byte)'.':
                                    if (!SkipIO) CompilePutCharToken(gen);
                                    break;
                                case (byte)',':
                                    if (!SkipIO) CompileGetCharToken(gen);
                                    break;
                                case (byte)'[':
                                    CompileLoopStartToken(labels, gen);
                                    break;
                                case (byte)']':
                                    CompileLoopEndToken(labels, gen);
                                    break;
                                default:
                                    throw new InvalidOperationException("Invalid token encountered!");
                            }
                        }
                        break;
                }
            }

            gen.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Compiles a group of > tokens
        /// </summary>
        private void CompileIncrementPointerToken(ILGenerator gen, Token token)
        {
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldc_I4, token.count);
            gen.Emit(OpCodes.Add);
            gen.Emit(OpCodes.Stloc_0);
        }

        /// <summary>
        /// Compiles a group of < tokens
        /// </summary>
        private void CompileDecrementPointerToken(ILGenerator gen, Token token)
        {
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldc_I4, token.count);
            gen.Emit(OpCodes.Sub);
            gen.Emit(OpCodes.Stloc_0);
        }

        /// <summary>
        /// Compiles a group of + tokens
        /// </summary>
        private void CompileIncrementValueToken(ILGenerator gen, Token token)
        {
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldelem_U1);
            gen.Emit(OpCodes.Ldc_I4, token.count);
            gen.Emit(OpCodes.Add);
            gen.Emit(OpCodes.Conv_U1);
            gen.Emit(OpCodes.Stelem_I1);
        }

        /// <summary>
        /// Compiles a group of - tokens
        /// </summary>
        private void CompileDecrementValueToken(ILGenerator gen, Token token)
        {
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldelem_U1);
            gen.Emit(OpCodes.Ldc_I4, token.count);
            gen.Emit(OpCodes.Sub);
            gen.Emit(OpCodes.Conv_U1);
            gen.Emit(OpCodes.Stelem_I1);
        }

        /// <summary>
        /// Compiles a . token
        /// </summary>
        private void CompilePutCharToken(ILGenerator gen)
        {
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldelem_U1);
            gen.Emit(OpCodes.Call, _writeConsoleMethod);
        }

        /// <summary>
        /// Compiles a , token
        /// </summary>
        private void CompileGetCharToken(ILGenerator gen)
        {
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Call, _readKeyMethod);
            gen.Emit(OpCodes.Stloc_2);
            gen.Emit(OpCodes.Ldloca, 2);
            gen.Emit(OpCodes.Call, _getKeyCodeMethod);
            gen.Emit(OpCodes.Conv_U1);
            gen.Emit(OpCodes.Stelem_I1);
        }

        /// <summary>
        /// Compiles a [ token
        /// </summary>
        private void CompileLoopStartToken(Stack<Loop> labels, ILGenerator gen)
        {
            var loop = new Loop
            {
                condition = gen.DefineLabel(),
                start = gen.DefineLabel()
            };

            gen.Emit(OpCodes.Br, loop.condition);
            gen.MarkLabel(loop.start);
            labels.Push(loop);
        }

        /// <summary>
        /// Compiles a ] token
        /// </summary>
        private void CompileLoopEndToken(Stack<Loop> labels, ILGenerator gen)
        {
            var loop = labels.Pop();
            gen.MarkLabel(loop.condition);

            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ldelem_U1);
            gen.Emit(OpCodes.Brtrue, loop.start);
        }
    }
}
