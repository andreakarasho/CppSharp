using System.Collections.Generic;
using System.Linq;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using CppSharp.Generators;
using CppSharp.Generators.AST;
using CppSharp.Generators.CLI;
using CppSharp.Generators.CSharp;

namespace CppSharp.Types.Std
{
    [TypeMap("va_list")]
    public class VaList : TypeMap
    {
        public override bool IsIgnored => true;
    }

    [TypeMap("basic_string<char, char_traits<char>, allocator<char>>")]
    public class String : TypeMap
    {
        public override Type CLISignatureType(TypePrinterContext ctx)
        {
            return new CILType(typeof(string));
        }

        public override void CLIMarshalToNative(MarshalContext ctx)
        {
            ctx.Return.Write("clix::marshalString<clix::E_UTF8>({0})",
                ctx.Parameter.Name);
        }

        public override void CLIMarshalToManaged(MarshalContext ctx)
        {
            ctx.Return.Write("clix::marshalString<clix::E_UTF8>({0})",
                ctx.ReturnVarName);
        }

        public override Type CSharpSignatureType(TypePrinterContext ctx)
        {
            if (ctx.Kind == TypePrinterContextKind.Managed)
                return new CILType(typeof(string));
            ClassTemplateSpecialization basicString = GetBasicString(ctx.Type);
            var typePrinter = new CSharpTypePrinter(null);
            typePrinter.PushContext(TypePrinterContextKind.Native);
            return new CustomType(basicString.Visit(typePrinter).Type);
        }

        public override void CSharpMarshalToNative(CSharpMarshalContext ctx)
        {
            var type = ctx.Parameter.Type.Desugar();
            ClassTemplateSpecialization basicString = GetBasicString(type);
            var typePrinter = new CSharpTypePrinter(ctx.Context);
            if (!ctx.Parameter.Type.Desugar().IsAddress())
                ctx.Return.Write($"*({typePrinter.PrintNative(basicString)}*) ");
            var allocator = ctx.Context.ASTContext.FindClass("allocator", false, true).First(
                a => a.IsDependent && a.TranslationUnit.IsSystemHeader);
            var allocatorChar = allocator.Specializations.First(s => !s.Ignore);
            string qualifiedBasicString = GetQualifiedBasicString(basicString);
            if (type.IsPointer() || (type.IsReference() && ctx.Declaration is Field))
            {
                ctx.Return.Write($@"{qualifiedBasicString}Extensions.{basicString.Name}({
                    ctx.Parameter.Name}, new {allocatorChar.Visit(typePrinter)}()).{
                    Helpers.InstanceIdentifier}");
            }
            else
            {
                var varAllocator = $"__allocator{ctx.ParameterIndex}";
                var varBasicString = $"__basicString{ctx.ParameterIndex}";
                ctx.Before.WriteLine($@"var {varAllocator} = new {
                    allocatorChar.Visit(typePrinter)}();");
                ctx.Before.WriteLine($@"var {varBasicString} = {
                    qualifiedBasicString}Extensions.{basicString.Name}({ctx.Parameter.Name}, {
                    varAllocator});");
                ctx.Return.Write($"{varBasicString}.{Helpers.InstanceIdentifier}");
                ctx.Cleanup.WriteLine($@"{varBasicString}.Dispose({
                    (type.IsPointer() ? "true" : "false")});");
                ctx.Cleanup.WriteLine($"{varAllocator}.Dispose();");
            }
        }

        public override void CSharpMarshalToManaged(CSharpMarshalContext ctx)
        {
            var type = Type.Desugar(resolveTemplateSubstitution: false);
            ClassTemplateSpecialization basicString = GetBasicString(type);
            var c_str = basicString.Methods.First(m => m.OriginalName == "c_str");
            var typePrinter = new CSharpTypePrinter(ctx.Context);
            string qualifiedBasicString = GetQualifiedBasicString(basicString);
            string varBasicString = $"__basicStringRet{ctx.ParameterIndex}";
            ctx.Before.WriteLine($@"var {varBasicString} = {
                basicString.Visit(typePrinter)}.{Helpers.CreateInstanceIdentifier}({
                ctx.ReturnVarName});");
            if (type.IsAddress())
            {
                ctx.Return.Write($@"{qualifiedBasicString}Extensions.{c_str.Name}({
                    varBasicString})");
            }
            else
            {
                string varString = $"__stringRet{ctx.ParameterIndex}";
                ctx.Before.WriteLine($@"var {varString} = {
                    qualifiedBasicString}Extensions.{c_str.Name}({varBasicString});");
                ctx.Before.WriteLine($"{varBasicString}.Dispose(false);");
                ctx.Return.Write(varString);
            }
        }

        private static string GetQualifiedBasicString(ClassTemplateSpecialization basicString)
        {
            var declContext = basicString.TemplatedDecl.TemplatedDecl;
            var names = new Stack<string>();
            while (!(declContext is TranslationUnit))
            {
                var isInlineNamespace = declContext is Namespace && ((Namespace)declContext).IsInline;
                if (!isInlineNamespace)
                    names.Push(declContext.Name);
                declContext = declContext.Namespace;
            }
            var qualifiedBasicString = string.Join(".", names);
            return $"global::{qualifiedBasicString}";
        }

        private static ClassTemplateSpecialization GetBasicString(Type type)
        {
            var desugared = type.Desugar();
            var template = (desugared.GetFinalPointee() ?? desugared).Desugar();
            var templateSpecializationType = template as TemplateSpecializationType;
            if (templateSpecializationType != null)
                return templateSpecializationType.GetClassTemplateSpecialization();
            return (ClassTemplateSpecialization) ((TagType) template).Declaration;
        }
    }

    [TypeMap("std::wstring", GeneratorKind = GeneratorKind.CLI)]
    public class WString : TypeMap
    {
        public override Type CLISignatureType(TypePrinterContext ctx)
        {
            return new CILType(typeof(string));
        }

        public override void CLIMarshalToNative(MarshalContext ctx)
        {
            ctx.Return.Write("clix::marshalString<clix::E_UTF16>({0})",
                ctx.Parameter.Name);
        }

        public override void CLIMarshalToManaged(MarshalContext ctx)
        {
            ctx.Return.Write("clix::marshalString<clix::E_UTF16>({0})",
                ctx.ReturnVarName);
        }

        public override Type CSharpSignatureType(TypePrinterContext ctx)
        {
            return new CILType(typeof(string));
        }

        public override void CSharpMarshalToNative(CSharpMarshalContext ctx)
        {
            ctx.Return.Write("new Std.WString()");
        }

        public override void CSharpMarshalToManaged(CSharpMarshalContext ctx)
        {
            ctx.Return.Write(ctx.ReturnVarName);
        }
    }

    [TypeMap("std::vector", GeneratorKind = GeneratorKind.CLI)]
    public class Vector : TypeMap
    {
        public override bool IsIgnored
        {
            get
            {
                var finalType = Type.GetFinalPointee() ?? Type;
                var type = finalType as TemplateSpecializationType;
                if (type == null)
                {
                    var injectedClassNameType = (InjectedClassNameType) finalType;
                    type = (TemplateSpecializationType) injectedClassNameType.InjectedSpecializationType.Type;
                }
                var checker = new TypeIgnoreChecker(TypeMapDatabase);
                type.Arguments[0].Type.Visit(checker);

                return checker.IsIgnored;
            }
        }

        public override Type CLISignatureType(TypePrinterContext ctx)
        {
            return new CustomType(
                $"System::Collections::Generic::List<{ctx.GetTemplateParameterList()}>^");
        }

        public override void CLIMarshalToNative(MarshalContext ctx)
        {
            var templateType = Type as TemplateSpecializationType;
            var type = templateType.Arguments[0].Type;
            var isPointerToPrimitive = type.Type.IsPointerToPrimitiveType();
            var managedType = isPointerToPrimitive
                ? new CILType(typeof(System.IntPtr))
                : type.Type;

            var entryString = (ctx.Parameter != null) ? ctx.Parameter.Name
                : ctx.ArgName;

            var tmpVarName = "_tmp" + entryString;

            var cppTypePrinter = new CppTypePrinter();
            var nativeType = type.Type.Visit(cppTypePrinter);

            ctx.Before.WriteLine("auto {0} = std::vector<{1}>();",
                tmpVarName, nativeType);
            ctx.Before.WriteLine("for each({0} _element in {1})",
                managedType, entryString);
            ctx.Before.WriteOpenBraceAndIndent();
            {
                var param = new Parameter
                {
                    Name = "_element",
                    QualifiedType = type
                };

                var elementCtx = new MarshalContext(ctx.Context, ctx.Indentation)
                                     {
                                         Parameter = param,
                                         ArgName = param.Name,
                                     };

                var marshal = new CLIMarshalManagedToNativePrinter(elementCtx);
                type.Type.Visit(marshal);

                if (!string.IsNullOrWhiteSpace(marshal.Context.Before))
                    ctx.Before.Write(marshal.Context.Before);

                if (isPointerToPrimitive)
                    ctx.Before.WriteLine("auto _marshalElement = {0}.ToPointer();",
                        marshal.Context.Return);
                else
                    ctx.Before.WriteLine("auto _marshalElement = {0};",
                    marshal.Context.Return);

                ctx.Before.WriteLine("{0}.push_back(_marshalElement);",
                    tmpVarName);
            }
            
            ctx.Before.UnindentAndWriteCloseBrace();

            ctx.Return.Write(tmpVarName);
        }

        public override void CLIMarshalToManaged(MarshalContext ctx)
        {
            var templateType = Type as TemplateSpecializationType;
            var type = templateType.Arguments[0].Type;
            var isPointerToPrimitive = type.Type.IsPointerToPrimitiveType();
            var managedType = isPointerToPrimitive
                ? new CILType(typeof(System.IntPtr))
                : type.Type;
            var tmpVarName = "_tmp" + ctx.ArgName;
            
            ctx.Before.WriteLine(
                "auto {0} = gcnew System::Collections::Generic::List<{1}>();",
                tmpVarName, managedType);
            ctx.Before.WriteLine("for(auto _element : {0})",
                ctx.ReturnVarName);
            ctx.Before.WriteOpenBraceAndIndent();
            {
                var elementCtx = new MarshalContext(ctx.Context, ctx.Indentation)
                                     {
                                         ReturnVarName = "_element",
                                         ReturnType = type
                                     };

                var marshal = new CLIMarshalNativeToManagedPrinter(elementCtx);
                type.Type.Visit(marshal);

                if (!string.IsNullOrWhiteSpace(marshal.Context.Before))
                    ctx.Before.Write(marshal.Context.Before);

                ctx.Before.WriteLine("auto _marshalElement = {0};",
                    marshal.Context.Return);

                if (isPointerToPrimitive)
                    ctx.Before.WriteLine("{0}->Add({1}(_marshalElement));",
                        tmpVarName, managedType);
                else
                    ctx.Before.WriteLine("{0}->Add(_marshalElement);",
                        tmpVarName);
            }
            ctx.Before.UnindentAndWriteCloseBrace();

            ctx.Return.Write(tmpVarName);
        }

        public override Type CSharpSignatureType(TypePrinterContext ctx)
        {
            if (ctx.Kind == TypePrinterContextKind.Native)
                return new CustomType("Std.Vector");

            return new CustomType($"Std.Vector<{ctx.GetTemplateParameterList()}>");
        }

        public override void CSharpMarshalToNative(CSharpMarshalContext ctx)
        {
            ctx.Return.Write("{0}.Internal", ctx.Parameter.Name);
        }

        public override void CSharpMarshalToManaged(CSharpMarshalContext ctx)
        {
            var templateType = Type as TemplateSpecializationType;
            var type = templateType.Arguments[0].Type;

            ctx.Return.Write("new Std.Vector<{0}>({1})", type,
                ctx.ReturnVarName);
        }
    }

    [TypeMap("std::map", GeneratorKind = GeneratorKind.CLI)]
    public class Map : TypeMap
    {
        public override bool IsIgnored { get { return true; } }

        public override Type CLISignatureType(TypePrinterContext ctx)
        {
            var type = Type as TemplateSpecializationType;
            return new CustomType(
                $@"System::Collections::Generic::Dictionary<{
                    type.Arguments[0].Type}, {type.Arguments[1].Type}>^");
        }

        public override void CLIMarshalToNative(MarshalContext ctx)
        {
            throw new System.NotImplementedException();
        }

        public override void CLIMarshalToManaged(MarshalContext ctx)
        {
            throw new System.NotImplementedException();
        }

        public override Type CSharpSignatureType(TypePrinterContext ctx)
        {
            if (ctx.Kind == TypePrinterContextKind.Native)
                return new CustomType("Std.Map");

            var type = Type as TemplateSpecializationType;
            return new CustomType(
                $@"System.Collections.Generic.Dictionary<{
                    type.Arguments[0].Type}, {type.Arguments[1].Type}>");
        }
    }

    [TypeMap("std::list", GeneratorKind = GeneratorKind.CLI)]
    public class List : TypeMap
    {
        public override bool IsIgnored { get { return true; } }
    }

    [TypeMap("std::shared_ptr", GeneratorKind = GeneratorKind.CLI)]
    public class SharedPtr : TypeMap
    {
        public override bool IsIgnored { get { return true; } }
    }

    [TypeMap("basic_ostream<char, char_traits<char>>", GeneratorKind.CLI)]
    public class OStream : TypeMap
    {
        public override Type CLISignatureType(TypePrinterContext ctx)
        {
            return new CILType(typeof(System.IO.TextWriter));
        }

        public override void CLIMarshalToNative(MarshalContext ctx)
        {
            var marshal = (CLIMarshalManagedToNativePrinter) ctx.MarshalToNative;
            if (!ctx.Parameter.Type.Desugar().IsPointer())
                marshal.ArgumentPrefix.Write("*");
            var marshalCtxName = string.Format("ctx_{0}", ctx.Parameter.Name);
            ctx.Before.WriteLine("msclr::interop::marshal_context {0};", marshalCtxName);
            ctx.Return.Write("{0}.marshal_as<std::ostream*>({1})",
                marshalCtxName, ctx.Parameter.Name);
        }
    }

    [TypeMap("std::nullptr_t", GeneratorKind = GeneratorKind.CLI)]
    public class NullPtr : TypeMap
    {
        public override bool DoesMarshalling { get { return false; } }

        public override void CLITypeReference(CLITypeReferenceCollector collector,
            ASTRecord<Declaration> loc)
        {
            var typeRef = collector.GetTypeReference(loc.Value);

            var include = new Include
            {
                File = "cstddef",
                Kind = Include.IncludeKind.Angled,
            };

            typeRef.Include = include;
        }
    }

    [TypeMap("FILE", GeneratorKind = GeneratorKind.CSharp)]
    public class FILE : TypeMap
    {
        public override Type CSharpSignatureType(TypePrinterContext ctx)
        {
            return new CILType(typeof(System.IntPtr));
        }

        public override void CSharpMarshalToNative(CSharpMarshalContext ctx)
        {
            ctx.Return.Write(ctx.Parameter.Name);
        }

        public override void CSharpMarshalToManaged(CSharpMarshalContext ctx)
        {
            ctx.Return.Write(ctx.ReturnVarName);
        }
    }
}
