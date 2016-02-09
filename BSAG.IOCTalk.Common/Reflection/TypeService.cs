﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Collections;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Text.RegularExpressions;

namespace BSAG.IOCTalk.Common.Reflection
{
    /// <summary>
    /// Reflection type service
    /// </summary>
    public static class TypeService
    {
        /// <summary>
        /// Auto generated proxy namespace
        /// </summary>
        public const string AutoGeneratedProxiesNamespace = "IOCTalk.AutoGeneratedProxies";

        private static string autoGeneratedDllName = "IOCTalkAutoGenerated.dll";

        private static readonly char[] GenericTypeChars = new char[] { '`', '<' };

        /// <summary>
        /// Builds the type of the interface implementation.
        /// </summary>
        /// <param name="interfaceTypeFullname">The interface type fullname.</param>
        /// <param name="isAssemblyDebuggable">if set to <c>true</c> is auto generated assembly debuggable.</param>
        /// <returns></returns>
        public static Type BuildInterfaceImplementationType(string interfaceTypeFullname, bool isAssemblyDebuggable)
        {
            try
            {
                Type implementationType = null;
                // lookup interface type
                Type interfaceType;
                if (TryGetTypeByName(interfaceTypeFullname, out interfaceType))
                {
                    if (interfaceType.GetInterface(typeof(IEnumerable).FullName) != null)
                    {
                        // type is a collection
                        // check if collection is generic IEnumerable<T>
                        Type enumerableClassType = null;
                        Type genericCollectionInterface = interfaceType.GetInterface("IEnumerable`1");
                        if (interfaceType.IsGenericType
                            || genericCollectionInterface != null)
                        {
                            Type[] genericTypes = interfaceType.GetGenericArguments();
                            if (genericTypes.Length == 1)
                            {
                                Type listType = typeof(List<>);
                                enumerableClassType = listType.MakeGenericType(genericTypes);
                            }
                            else if (genericTypes.Length == 0
                                && genericCollectionInterface != null)
                            {
                                genericTypes = genericCollectionInterface.GetGenericArguments();
                                Type listType = typeof(List<>);
                                enumerableClassType = listType.MakeGenericType(genericTypes);
                            }
                            else
                            {
                                throw new NotImplementedException("More than one generic arguments is not supported yet!");
                            }
                        }
                        else
                        {
                            // untyped collection
                            enumerableClassType = typeof(ArrayList);
                        }

                        if (interfaceType.IsAssignableFrom(enumerableClassType))
                        {
                            // enumeration has no inheritance -> return concrete collection type
                            implementationType = enumerableClassType;
                        }
                        else
                        {
                            // create collection type with parent concreate collection implementation
                            implementationType = CreateImplementation(interfaceType, enumerableClassType, isAssemblyDebuggable);
                        }
                    }
                    else
                    {
                        // create new interface implementation object
                        implementationType = CreateImplementation(interfaceType, isAssemblyDebuggable);
                    }
                }
                return implementationType;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.ToString());
                return null;
            }

        }

        private static Type CreateImplementation(Type interfaceType, bool isAssemblyDebuggable)
        {
            return CreateImplementation(interfaceType, null, isAssemblyDebuggable);
        }

        private static Type CreateImplementation(Type interfaceType, Type parentType, bool isAssemblyDebuggable)
        {
            AssemblyBuilder assemblyBuilder;
            TypeBuilder typeBuilder = CreateTypeBuilder(interfaceType, out assemblyBuilder, isAssemblyDebuggable);

            // implement get/set properties
            foreach (var pi in interfaceType.GetProperties())
            {
                BuildProperty(typeBuilder, pi.Name, pi.PropertyType);
            }

            // implement base interface properties
            Type[] baseInterfaces = interfaceType.GetInterfaces();

            foreach (var baseInterface in baseInterfaces)
            {
                foreach (var piBase in baseInterface.GetProperties())
                {
                    BuildProperty(typeBuilder, piBase.Name, piBase.PropertyType);
                }
            }

            if (parentType != null)
            {
                typeBuilder.SetParent(parentType);
            }

            return typeBuilder.CreateType();
        }

        private static TypeBuilder CreateTypeBuilder(Type interfaceType, out AssemblyBuilder assemblyBuilder, bool isAssemblyDebuggable)
        {
            AssemblyName assemblyName = new AssemblyName("IOCTalk.AutoGeneratedAssembly");
            assemblyBuilder = Thread.GetDomain().DefineDynamicAssembly(assemblyName, (isAssemblyDebuggable ? AssemblyBuilderAccess.RunAndSave : AssemblyBuilderAccess.Run));

            ModuleBuilder moduleBuilder;
            if (isAssemblyDebuggable)
            {
                moduleBuilder = assemblyBuilder.DefineDynamicModule(autoGeneratedDllName, autoGeneratedDllName);
            }
            else
            {
                moduleBuilder = assemblyBuilder.DefineDynamicModule(autoGeneratedDllName, false);
            }

            string typeName = string.Concat(interfaceType.Name, "AutoGenerated");
            TypeBuilder typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Class | TypeAttributes.Public);
            typeBuilder.AddInterfaceImplementation(interfaceType);
            return typeBuilder;
        }


        /// <summary>
        /// Gets the type associated with the specified name.
        /// </summary>
        /// <param name="typeName">Full name of the type.</param>
        /// <param name="type">The type.</param>
        /// <param name="customAssemblies">Additional loaded assemblies (optional).</param>
        /// <returns>Returns <c>true</c> if the type was found; otherwise <c>false</c>.</returns>
        public static bool TryGetTypeByName(string typeName, out Type type, params Assembly[] customAssemblies)
        {
            type = null;

            if (typeName.Contains("Version=")
                && !typeName.Contains("`"))
            {
                // remove full qualified assembly type name
                typeName = typeName.Substring(0, typeName.IndexOf(','));
            }

            int genericCharIndex = typeName.IndexOfAny(GenericTypeChars);

            if (genericCharIndex >= 0)
            {
                // try get generic types
                if (typeName[genericCharIndex] == '`')
                {
                    // check if generic definition contains type parameters
                    if (typeName.IndexOf('[', genericCharIndex) > genericCharIndex)
                    {
                        var match = Regex.Match(typeName, "(?<MainType>.+`(?<ParamCount>[0-9]+))\\[(?<Types>.*)\\]");

                        if (match.Success)
                        {
                            int genericParameterCount = int.Parse(match.Groups["ParamCount"].Value);
                            string genericDef = match.Groups["Types"].Value;
                            List<string> typeArgs = new List<string>(genericParameterCount);
                            foreach (Match typeArgMatch in Regex.Matches(genericDef, "\\[(?<Type>.*?)\\],?"))
                            {
                                if (typeArgMatch.Success)
                                {
                                    typeArgs.Add(typeArgMatch.Groups["Type"].Value.Trim());
                                }
                            }

                            Type[] genericArgumentTypes = new Type[typeArgs.Count];
                            for (int genTypeIndex = 0; genTypeIndex < typeArgs.Count; genTypeIndex++)
                            {
                                Type genericType;
                                if (TryGetTypeByName(typeArgs[genTypeIndex], out genericType, customAssemblies))
                                {
                                    genericArgumentTypes[genTypeIndex] = genericType;
                                }
                                else
                                {
                                    // cant find generic type
                                    return false;
                                }
                            }

                            string genericTypeString = match.Groups["MainType"].Value;
                            Type genericMainType;
                            if (TryGetTypeByName(genericTypeString, out genericMainType))
                            {
                                // make generic type
                                type = genericMainType.MakeGenericType(genericArgumentTypes);
                            }
                        }
                    }
                    else
                    {
                        // try to get plain generic type (without type parameters)
                        type = Type.GetType(typeName);

                        if (type == null)
                        {
                            type = GetTypeFromAssemblies(typeName, customAssemblies);
                        }
                    }
                }
                else if (typeName[genericCharIndex] == '<')
                {
                    var match = Regex.Match(typeName, "(?<MainType>.+)<(?<Types>.+)>");

                    if (match.Success)
                    {
                        string genericDef = match.Groups["Types"].Value;
                        string[] typeArgs = genericDef.Split(',');


                        Type[] genericArgumentTypes = new Type[typeArgs.Length];
                        for (int genTypeIndex = 0; genTypeIndex < typeArgs.Length; genTypeIndex++)
                        {
                            Type genericType;
                            if (TryGetTypeByName(typeArgs[genTypeIndex].Trim(), out genericType, customAssemblies))
                            {
                                genericArgumentTypes[genTypeIndex] = genericType;
                            }
                            else
                            {
                                // cant find generic type
                                return false;
                            }
                        }

                        string genericTypeString = string.Concat(match.Groups["MainType"].Value, "`", typeArgs.Length);
                        Type genericMainType;
                        if (TryGetTypeByName(genericTypeString, out genericMainType))
                        {
                            // make generic type
                            type = genericMainType.MakeGenericType(genericArgumentTypes);
                        }
                    }
                }
            }
            else
            {
                type = Type.GetType(typeName);

                if (type == null)
                {
                    type = GetTypeFromAssemblies(typeName, customAssemblies);
                }
            }


            return type != null;
        }

        private static Type GetTypeFromAssemblies(string typeName, params Assembly[] customAssemblies)
        {
            Type type = null;

            if (customAssemblies != null
               && customAssemblies.Length > 0)
            {
                foreach (var assembly in customAssemblies)
                {
                    type = assembly.GetType(typeName);

                    if (type != null)
                        return type;
                }
            }

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in loadedAssemblies)
            {
                type = assembly.GetType(typeName);

                if (type != null)
                    return type;
            }

            return type;
        }


        private static void BuildProperty(TypeBuilder typeBuilder, string name, Type type)
        {
            FieldBuilder field = typeBuilder.DefineField("m" + name, type, FieldAttributes.Private);
            PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(name, PropertyAttributes.None, type, null);

            MethodAttributes getSetAttr = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Virtual;

            MethodBuilder getter = typeBuilder.DefineMethod("get_" + name, getSetAttr, type, Type.EmptyTypes);

            ILGenerator getIL = getter.GetILGenerator();
            getIL.Emit(OpCodes.Ldarg_0);
            getIL.Emit(OpCodes.Ldfld, field);
            getIL.Emit(OpCodes.Ret);

            MethodBuilder setter = typeBuilder.DefineMethod("set_" + name, getSetAttr, null, new Type[] { type });

            ILGenerator setIL = setter.GetILGenerator();
            setIL.Emit(OpCodes.Ldarg_0);
            setIL.Emit(OpCodes.Ldarg_1);
            setIL.Emit(OpCodes.Stfld, field);
            setIL.Emit(OpCodes.Ret);


            propertyBuilder.SetGetMethod(getter);
            propertyBuilder.SetSetMethod(setter);
        }


        /// <summary>
        /// Builds the proxy implementation with MEF binding.
        /// </summary>
        /// <param name="interfaceType">Type of the interface.</param>
        /// <param name="addDebugInformation">if set to <c>true</c> [add debug information].</param>
        /// <returns></returns>
        public static Type BuildProxyImplementation(Type interfaceType, bool isAssemblyDebuggable)
        {
            return BuildProxyImplementation(interfaceType, isAssemblyDebuggable, "[System.ComponentModel.Composition.Import(RequiredCreationPolicy = System.ComponentModel.Composition.CreationPolicy.NonShared)]");
        }


        /// <summary>
        /// Builds the proxy implementation.
        /// </summary>
        /// <param name="interfaceType">Type of the interface.</param>
        /// <param name="addDebugInformation">if set to <c>true</c> [add debug information].</param>
        /// <param name="communicationServiceImportAttributeSource">The communication service import attribute source code.</param>
        /// <returns></returns>
        public static Type BuildProxyImplementation(Type interfaceType, bool isAssemblyDebuggable, string communicationServiceImportAttributeSource)
        {
            if (!interfaceType.IsInterface)
                throw new Exception("Type must be an interface!");

            string name = interfaceType.Name + "AutoGeneratedProxy";

            StringBuilder source = new StringBuilder();

            source.AppendLine("using System;");
            source.AppendLine("using BSAG.IOCTalk.Common.Reflection;");
            source.AppendLine("using BSAG.IOCTalk.Common.Interface.Reflection;");
            source.AppendLine("using BSAG.IOCTalk.Common.Interface.Communication;");

            source.AppendLine();
            source.Append("namespace ");
            source.AppendLine(AutoGeneratedProxiesNamespace);
            source.AppendLine("{");
            source.AppendFormat(" public class {0} : {1}", name, interfaceType.FullName);
            source.AppendLine(" {");
            List<string> referencedAssemblies = new List<string>();
            CreateProxyInterfaceMethodSourceCode(source, interfaceType, referencedAssemblies);

            // add communication service import property
            source.AppendLine();
            source.Append("     ");
            source.AppendLine(communicationServiceImportAttributeSource);
            source.AppendLine("     public IGenericCommunicationService CommunicationService { get; set; }");
            source.AppendLine();

            source.AppendLine(" }");
            source.AppendLine("}");

            Dictionary<string, string> providerOptions = new Dictionary<string, string>
                {
                    {"CompilerVersion", "v4.0"}
                };


            CSharpCodeProvider provider = new CSharpCodeProvider(providerOptions);

            CompilerParameters compilerParams = new CompilerParameters
                {
                    GenerateInMemory = false,   // must be false otherwise assembly is not loaded in current app domain
                    GenerateExecutable = false,
                    IncludeDebugInformation = isAssemblyDebuggable,
                };

            if (isAssemblyDebuggable)
            {
                compilerParams.TempFiles = new TempFileCollection(Environment.GetEnvironmentVariable("TEMP"), true);
            }

            compilerParams.ReferencedAssemblies.Add("System.dll");
            compilerParams.ReferencedAssemblies.Add("System.ComponentModel.Composition.dll");
            compilerParams.ReferencedAssemblies.Add(GetAssemblyPath(typeof(TypeService)));
            compilerParams.ReferencedAssemblies.Add(GetAssemblyPath(interfaceType));

            foreach (var assemblyRefPath in referencedAssemblies)
            {
                compilerParams.ReferencedAssemblies.Add(assemblyRefPath);
            }

            string sourceCodeString = source.ToString();
            CompilerResults results = provider.CompileAssemblyFromSource(compilerParams, sourceCodeString);

            if (results.Errors.Count > 0)
            {
                StringBuilder sbError = new StringBuilder();
                sbError.AppendFormat("Unable to create proxy implementation for interface \"{0}\"!", interfaceType.FullName);
                sbError.AppendLine();
                sbError.AppendLine();
                sbError.AppendLine("Error: ");

                foreach (var error in results.Errors)
                {
                    sbError.AppendLine(error.ToString());
                    break;  // only write first build exception
                }

                sbError.AppendLine();
                sbError.AppendLine("Source:");
                sbError.Append(sourceCodeString);

                throw new TypeAccessException(sbError.ToString());
            }

            return results.CompiledAssembly.GetType("IOCTalk.AutoGeneratedProxies." + name);

        }

        private static string GetAssemblyPath(Type type)
        {
            return new Uri(type.Assembly.CodeBase).LocalPath;
        }

        private static void CreateProxyInterfaceMethodSourceCode(StringBuilder mainSource, Type interfaceType, IList<string> referencedAssemblies)
        {
            string methodBodyIntention = "\t\t";
            string methodLineIntention = "\t\t\t";


            foreach (var method in GetMethodsByType(interfaceType))
            {
                StringBuilder sbInvokeInfoMember = new StringBuilder();
                sbInvokeInfoMember.AppendLine();

                StringBuilder methodSource = new StringBuilder();

                bool isReturnRequired;
                string returnType;
                if (method.ReturnType == typeof(void))
                {
                    isReturnRequired = false;
                    returnType = "void";
                }
                else
                {
                    isReturnRequired = true;
                    returnType = GetSourceCodeTypeName(method.ReturnType);
                }

                methodSource.AppendLine();
                methodSource.AppendFormat("{0}public {1} {2}(", methodBodyIntention, returnType, method.Name);

                string invokeInfoMemberName = string.Format("methodInfo{0}_", method.Name);

                List<ParameterInfo> outParameters = null;
                StringBuilder sbParameterValues = null;
                StringBuilder sbParameterTypes = null;

                var parameters = method.GetParameters();

                sbParameterTypes = new StringBuilder();
                sbParameterTypes.Append("new Type[] { ");

                if (parameters.Length > 0)
                {
                    sbParameterValues = new StringBuilder();
                    sbParameterValues.Append(methodLineIntention);
                    sbParameterValues.Append("object[] parameterValues = new object[] { ");

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];

                        Type paramType = param.ParameterType;
                        string decoration = null;
                        if (param.IsOut)
                        {
                            decoration = "out";
                            paramType = paramType.GetElementType();

                            if (outParameters == null)
                                outParameters = new List<ParameterInfo>();

                            outParameters.Add(param);
                        }

                        string parameterTypeString = GetSourceCodeTypeName(paramType);

                        methodSource.AppendFormat("{0} {1} {2}", decoration, parameterTypeString, param.Name);

                        string invokeInfoMemberTypeName = string.Concat(decoration, parameterTypeString);
                        invokeInfoMemberTypeName = Regex.Replace(invokeInfoMemberTypeName, @"[^a-zA-Z0-9]", "");    // remove invalid chars
                        invokeInfoMemberName += invokeInfoMemberTypeName;

                        // add reference to parameter value array
                        if (param.IsOut)
                        {
                            // out parameter -> pass null
                            sbParameterValues.Append("null");
                        }
                        else
                        {
                            sbParameterValues.Append(param.Name);
                        }

                        // type array
                        sbParameterTypes.AppendFormat("typeof({0}){1}", parameterTypeString, (param.IsOut ? ".MakeByRefType()" : null));


                        if (i < parameters.Length - 1)
                        {
                            methodSource.Append(", ");
                            sbParameterValues.Append(", ");
                            sbParameterTypes.Append(", ");
                        }

                        // check if assembly load is required
                        if (interfaceType.Assembly != paramType.Assembly
                            && !paramType.FullName.StartsWith("System."))
                        {
                            string assemblyPath = GetAssemblyPath(paramType);

                            if (!referencedAssemblies.Contains(assemblyPath))
                            {
                                referencedAssemblies.Add(assemblyPath);
                            }
                        }
                    }

                    sbParameterValues.AppendLine(" };");
                }

                sbParameterTypes.Append(" } ");

                methodSource.AppendLine(")");

                methodSource.Append(methodBodyIntention);
                methodSource.AppendLine("{");

                if (sbParameterValues != null)
                {
                    methodSource.Append(sbParameterValues);
                }

                sbInvokeInfoMember.Append(methodBodyIntention);
                sbInvokeInfoMember.AppendFormat("private static IInvokeMethodInfo {0} = new InvokeMethodInfo(typeof({1}), \"{2}\", {3});", invokeInfoMemberName, interfaceType.FullName, method.Name, sbParameterTypes.ToString());
                sbInvokeInfoMember.AppendLine();

                methodSource.Append(methodLineIntention);

                if (isReturnRequired)
                    methodSource.Append("object result =  ");

                //methodSource.AppendFormat("CommunicationService.InvokeMethod(this, typeof({0}).GetMethod(\"{1}\", {2}), {3});", interfaceType.FullName, method.Name, sbParameterTypes.ToString(), (sbParameterValues != null ? "parameterValues" : "null"));
                methodSource.AppendFormat("CommunicationService.InvokeMethod(this, {0}, {1});", invokeInfoMemberName, (sbParameterValues != null ? "parameterValues" : "null"));
                methodSource.AppendLine();

                if (outParameters != null)
                {
                    // assign out parameters
                    foreach (var outParam in outParameters)
                    {
                        methodSource.AppendFormat("{0}{1} = ({2})parameterValues[{3}];", methodLineIntention, outParam.Name, outParam.ParameterType.GetElementType().FullName, outParam.Position);
                        methodSource.AppendLine();
                    }
                }

                if (isReturnRequired)
                {
                    methodSource.AppendFormat("{0}return ({1})result;", methodLineIntention, returnType);
                    methodSource.AppendLine();
                }

                methodSource.Append(methodBodyIntention);
                methodSource.AppendLine("}");
                methodSource.AppendLine();

                // add to main source
                mainSource.Append(sbInvokeInfoMember);
                mainSource.Append(methodSource);
            }
        }

        /// <summary>
        /// Gets the name of the parameter type.
        /// </summary>
        /// <param name="paramType">Type of the param.</param>
        /// <returns></returns>
        public static string GetSourceCodeTypeName(Type paramType)
        {
            string parameterTypeString;
            if (paramType.IsGenericType)
            {
                Type genericType = paramType.GetGenericTypeDefinition();
                string[] genericArgTypes = paramType.GetGenericArguments().Select(g => g.FullName).ToArray<string>();

                parameterTypeString = string.Concat(genericType.FullName.Replace("`" + genericArgTypes.Length, ""), "<", string.Join(",", genericArgTypes), ">");
            }
            else
            {
                parameterTypeString = paramType.FullName;
            }
            return parameterTypeString;
        }




        /// <summary>
        /// Gets the methods without get/set properties.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public static IList<MethodInfo> GetMethodsByType(Type type)
        {
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => !m.IsSpecialName).ToList<MethodInfo>();
        }


        /// <summary>
        /// Gets the method by the given name. The name can contain a qualified parameter list.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
        public static MethodInfo GetMethodByName(Type type, string name)
        {
            if (name[name.Length - 1] == ')')
            {
                // full qualified parameter list
                int paramStartIndex = name.IndexOf('(');
                string methodName = name.Substring(0, paramStartIndex);

                string parameterListStr = name.Substring(paramStartIndex + 1, name.Length - paramStartIndex - 2);

                Type[] parameterTypes;
                if (parameterListStr.Length == 0)
                {
                    parameterTypes = new Type[0];
                }
                else
                {
                    string[] parameterListArrayStr = parameterListStr.Split(',');
                    parameterTypes = new Type[parameterListArrayStr.Length];

                    for (int i = 0; i < parameterListArrayStr.Length; i++)
                    {
                        string typeName = parameterListArrayStr[i];

                        Type paramType;
                        if (TryGetTypeByName(typeName, out paramType))
                        {
                            parameterTypes[i] = paramType;
                        }
                        else
                        {
                            throw new InvalidOperationException("Can't find the parameter type: \"" + typeName + "\"; Method: \"" + type.FullName + "." + name + "\"");
                        }
                    }
                }

                return type.GetMethod(methodName, parameterTypes);
            }
            else
            {
                return type.GetMethod(name);
            }
        }

        /// <summary>
        /// Gets the name of the method including the type parameters.
        /// </summary>
        /// <param name="methodInfo">The method info.</param>
        /// <returns></returns>
        public static string GetQualifiedMethodName(MethodInfo methodInfo)
        {
            return GetQualifiedMethodName(methodInfo, methodInfo.GetParameters());
        }

        /// <summary>
        /// Gets the name of the method including the type parameters.
        /// </summary>
        /// <param name="methodInfo">The method info.</param>
        /// <param name="parameterInfos">The parameter infos.</param>
        /// <returns></returns>
        internal static string GetQualifiedMethodName(MethodInfo methodInfo, ParameterInfo[] parameterInfos)
        {
            StringBuilder sbQualifiedMethodName = new StringBuilder();
            sbQualifiedMethodName.Append(methodInfo.Name);
            sbQualifiedMethodName.Append("(");

            foreach (var paramInfo in parameterInfos)
            {
                sbQualifiedMethodName.Append(GetSourceCodeTypeName(paramInfo.ParameterType));
                sbQualifiedMethodName.Append(",");
            }

            if (parameterInfos.Length > 0)
            {
                // remove last comma
                sbQualifiedMethodName.Length -= 1;
            }
            sbQualifiedMethodName.Append(")");

            return sbQualifiedMethodName.ToString();
        }

    }
}
