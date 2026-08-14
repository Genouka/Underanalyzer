/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Underanalyzer.Decompiler.AST;
using static Underanalyzer.IGMInstruction;

namespace Underanalyzer.Decompiler.GameSpecific;

/// <summary>
/// Analyzes the decompiled AST of a script/function to infer the types of its arguments,
/// by tracking how arguments such as <c>argument0</c> ultimately flow through the code.
/// </summary>
/// <remarks>
/// Types are inferred based on a few signals:
/// <list type="bullet">
/// <item>An argument passed to a builtin function at a position with a known argument type (e.g. <c>draw_sprite(argument0, ...)</c>).</item>
/// <item>An argument assigned to a variable with a known macro type (e.g. <c>sprite_index = argument0</c>).</item>
/// <item>An argument compared against a typed constant (e.g. <c>if (argument0 == obj_player)</c>).</item>
/// <item>Data flow through local variables: <c>var s = argument0; draw_sprite(s, ...);</c>.</item>
/// <item>An argument passed through to another function with known (or inferred) argument types.</item>
/// </list>
/// Inferred types are then used at call sites to expand integer literals into named constants.
/// </remarks>
public static class FunctionArgTypeInference
{
    /// <summary>
    /// Upper bound for "argument[n]" array accessor indices (introduced in GameMaker 2024.8).
    /// </summary>
    private const int MaxArgumentArrayIndex = 1024;

    /// <summary>
    /// Infers the types of all arguments referenced in the given script AST, in argument index order.
    /// Returns an array where each element is the inferred macro type of the argument at that index,
    /// or <see langword="null"/> if no type could be inferred.
    /// </summary>
    /// <param name="cleaner">Cleaner to use for macro type resolution. Must have a valid fragment context.</param>
    /// <param name="ast">The decompiled AST of the script to analyze.</param>
    /// <param name="code">The code entry the AST was decompiled from.</param>
    public static IMacroType?[] Infer(ASTCleaner cleaner, IStatementNode ast, IGMCode code)
    {
        Analyzer analyzer = new(cleaner);

        // If the code entry has a declared argument count, ensure we consider all of those arguments,
        // even if some are never actually referenced within the body.
        if (code.ArgumentCount > 0)
        {
            for (int i = 0; i < code.ArgumentCount; i++)
            {
                analyzer.EnsureArgumentSize(i);
            }
        }

        analyzer.VisitStatement(ast);

        // Reverse inference: propagate the inferred argument types back onto variables that were
        // assigned from those arguments, so that literals assigned to those variables (e.g. colors)
        // can be expanded at clean time.
        if (cleaner.Context.Settings.ReverseInferVariableTypes)
        {
            analyzer.RegisterInferredVariableTypes();
        }

        int resultSize = analyzer.MaxReferencedArgument + 1;
        if (resultSize <= 0)
        {
            return [];
        }

        IMacroType?[] result = new IMacroType?[resultSize];
        for (int i = 0; i < resultSize; i++)
        {
            result[i] = analyzer.GetInferredType(i);
        }
        return result;
    }

    /// <summary>
    /// Extracts the per-argument-position macro types from a function argument macro type.
    /// Handles unions (merging position types in order), intersections, and conditionals that
    /// delegate to inner function argument types. Returns <see langword="null"/> if the types
    /// cannot be decomposed into per-position types.
    /// </summary>
    public static IMacroType?[]? GetFunctionArgumentTypes(IMacroTypeFunctionArgs argsMacroType)
    {
        return Analyzer.GetFunctionArgumentTypes(argsMacroType);
    }

    /// <summary>
    /// Returns the argument index for the given variable node, or -1 if it is not an argument variable.
    /// Supports both 2.3+ games (where arguments use the "argument" instance type) and pre-2.3 games
    /// (where arguments are regular self/builtin variables named "argument0", or array accesses "argument[i]").
    /// </summary>
    public static int GetArgumentIndex(VariableNode node)
    {
        // Handle "argument0".."argument15" names (both instance type styles) and "argument[i]" with i >= 16
        int index = node.GetArgumentIndex(MaxArgumentArrayIndex, onlyNamedArguments: false);
        if (index != -1)
        {
            return index;
        }

        // Handle pre-2.3 / 2.3 "argument[i]" array accesses with a static index below 16
        if (node.Variable.Name.Content == "argument" &&
            node.ArrayIndices is [Int16Node { Value: >= 0 } arrayIndex, ..])
        {
            return arrayIndex.Value;
        }

        return -1;
    }

    /// <summary>
    /// Returns whether the given variable node references a function argument.
    /// </summary>
    public static bool IsArgumentVariable(VariableNode node)
    {
        return GetArgumentIndex(node) != -1;
    }

    /// <summary>
    /// Checks whether a non-local variable belongs to an unambiguous instance that inference can
    /// propagate types to (self, global, or a plain unqualified variable), as opposed to e.g. a
    /// variable on another instance or an array element.
    /// </summary>
    public static bool IsPropagatableVariable(VariableNode node)
    {
        if (node.ArrayIndices is not null)
        {
            return false;
        }
        return node.Left is null or InstanceTypeNode { InstanceType: InstanceType.Self or InstanceType.Global } or
            Int16Node { Value: (short)InstanceType.Self or (short)InstanceType.Global };
    }

    /// <summary>
    /// Lightweight pass that registers the types of the variables returned by the current function,
    /// based on the function's return type. The return type is only ever derived from reliable
    /// sources: a return type that is already registered (e.g. inferred from call-site usage, or from
    /// game data), or the registered type of a returned variable (e.g. <c>sprite_index</c>). This
    /// allows literals that flow into the return value (e.g. <c>spr = 441; return spr;</c>) to be
    /// expanded as named constants. Functions that are returned directly are linked recursively.
    /// </summary>
    public static void MaybeInferReturnValueTypes(ASTCleaner cleaner, IStatementNode ast)
    {
        if (cleaner.TopFragmentContext?.CodeEntryName is not string codeEntryName)
        {
            return;
        }
        if (cleaner.GlobalMacroResolver is not GlobalMacroTypeResolver globalResolver)
        {
            return;
        }

        List<IExpressionNode> returns = [];
        Dictionary<string, List<IExpressionNode>> variableAssignments = [];
        List<string> linkedFunctions = [];
        CollectReturnInfo(ast, returns, variableAssignments, linkedFunctions);

        if (returns.Count == 0)
        {
            return;
        }

        string? functionName = GetFunctionNameFromCodeEntryName(codeEntryName);
        if (functionName is null)
        {
            return;
        }

        // The return type is taken from a registered return type, or from the registered type of a
        // returned variable (e.g. "return sprite_index;")
        IMacroType? returnType = globalResolver.GlobalNames.TryGetFunctionReturnType(functionName)
                                 ?? globalResolver.GlobalNames.TryGetFunctionReturnType(codeEntryName);
        if (returnType is null)
        {
            returnType = InferReturnTypeFromReturnedVariableTypes(cleaner, returns);
        }
        if (returnType is null)
        {
            return;
        }

        // Register the function's return type under both the bare function name (for call sites)
        // and the code entry name form (for "return" statements in the body itself)
        RegisterFunctionReturnType(globalResolver, functionName, returnType);
        if (!string.Equals(functionName, codeEntryName, StringComparison.Ordinal))
        {
            RegisterFunctionReturnType(globalResolver, codeEntryName, returnType);
        }

        // Register the types of the variables that are returned, so that literals assigned to them
        // (e.g. "spr = 441") are expanded at clean time
        foreach (IExpressionNode value in returns)
        {
            if (value is VariableNode variable && !IsArgumentVariable(variable))
            {
                string variableName = variable.Variable.Name.Content;
                if (globalResolver.ResolveVariableType(cleaner, variableName) is null)
                {
                    globalResolver.DefineVariableTypeForCodeEntry(codeEntryName, variableName, returnType);
                }
            }
        }

        // Recursively link functions that are directly returned (or assigned to a returned variable)
        foreach (string linkedFunction in linkedFunctions)
        {
            if (!IsReturnTypeRegistered(globalResolver, linkedFunction))
            {
                RegisterFunctionReturnType(globalResolver, linkedFunction, returnType);
                RegisterFunctionReturnType(globalResolver, "gml_Script_" + linkedFunction, returnType);
            }
        }
    }

    /// <summary>
    /// Derives a function's return type from the registered types of the variables it returns
    /// (e.g. <c>return sprite_index;</c>), requiring all returned variables to agree.
    /// </summary>
    private static IMacroType? InferReturnTypeFromReturnedVariableTypes(ASTCleaner cleaner, List<IExpressionNode> returns)
    {
        IMacroType? candidate = null;
        foreach (IExpressionNode value in returns)
        {
            if (value is not VariableNode variable)
            {
                continue;
            }
            IMacroType? variableType = cleaner.GlobalMacroResolver.ResolveVariableType(cleaner, variable.Variable.Name.Content);
            if (variableType is null)
            {
                continue;
            }
            if (candidate is null)
            {
                candidate = variableType;
            }
            else if (!SameMacroType(candidate, variableType))
            {
                return null;
            }
        }
        return candidate;
    }

    /// <summary>
    /// When a function call is used in a context that expects a specific macro type (for example a
    /// nested call at a typed argument position of another function, an assignment to a typed
    /// variable, or a comparison against a typed constant), infers that function's return type.
    /// This is the reliable source of return type information.
    /// </summary>
    public static void InferReturnTypeFromCallSiteUsage(ASTCleaner cleaner, IExpressionNode call, IMacroType usedAsType)
    {
        if (cleaner.GlobalMacroResolver is not GlobalMacroTypeResolver globalResolver)
        {
            return;
        }
        if (usedAsType is null)
        {
            return;
        }
        if (!cleaner.Context.Settings.InferFunctionArgumentTypes)
        {
            return;
        }
        string? functionName = call switch
        {
            FunctionCallNode functionCall => functionCall.FunctionName,
            VariableCallNode variableCall => variableCall.FunctionName,
            NewObjectNode newObject => newObject.FunctionName,
            _ => null
        };
        if (string.IsNullOrEmpty(functionName))
        {
            return;
        }
        // Only infer return types for user-defined functions. Builtin functions (e.g. "random")
        // must never be registered based on call-site usage, otherwise unrelated literals would be
        // wrongly expanded as named constants.
        if (cleaner.Context.GameContext.FunctionArgTypeProvider?.GetFunctionCode(functionName) is null)
        {
            return;
        }
        if (IsReturnTypeRegistered(globalResolver, functionName))
        {
            return;
        }
        RegisterFunctionReturnType(globalResolver, functionName, usedAsType);
        if (!functionName.StartsWith("gml_Script_", StringComparison.Ordinal))
        {
            RegisterFunctionReturnType(globalResolver, "gml_Script_" + functionName, usedAsType);
        }
    }

    /// <summary>
    /// Recursively walks the given AST, collecting the values returned by the current function
    /// (following the compiler-generated return temporary variable), as well as the literal/call
    /// values assigned to each variable, and any functions returned directly.
    /// </summary>
    private static void CollectReturnInfo(IStatementNode statement, List<IExpressionNode> returns,
                                          Dictionary<string, List<IExpressionNode>> variableAssignments,
                                          List<string> linkedFunctions)
    {
        switch (statement)
        {
            case ReturnNode returnNode:
                {
                    // If the returned value is the compiler's temporary return variable, it is
                    // handled through the assignment that produces it (below); otherwise treat the
                    // returned value directly as a return expression.
                    if (returnNode.Value is VariableNode { Variable.Name.Content: VMConstants.TempReturnVariable })
                    {
                        break;
                    }
                    returns.Add(returnNode.Value);
                    if (returnNode.Value is FunctionCallNode call)
                    {
                        linkedFunctions.Add(call.FunctionName);
                    }
                    break;
                }
            case FunctionDeclNode:
                {
                    // Do not descend into nested function declarations; they have their own return values
                    break;
                }
            case AssignNode assign when assign.AssignKind == AssignNode.AssignType.Normal &&
                                     assign.Value is not null &&
                                     assign.Variable is VariableNode destVariable:
                {
                    if (destVariable.Variable.Name.Content == VMConstants.TempReturnVariable)
                    {
                        // Assignment producing the returned value: the assigned expression is what
                        // this function returns
                        returns.Add(assign.Value);
                        if (assign.Value is FunctionCallNode call)
                        {
                            linkedFunctions.Add(call.FunctionName);
                        }
                    }
                    else
                    {
                        // Track concrete values assigned to each variable, so that variables that
                        // flow into the return value can be analyzed
                        if (!variableAssignments.TryGetValue(destVariable.Variable.Name.Content, out List<IExpressionNode>? list))
                        {
                            list = [];
                            variableAssignments[destVariable.Variable.Name.Content] = list;
                        }
                        list.Add(assign.Value);
                        if (assign.Value is FunctionCallNode call)
                        {
                            linkedFunctions.Add(call.FunctionName);
                        }
                    }
                    break;
                }
        }

        // Walk child statements, but not into nested function declarations
        foreach (IBaseASTNode child in statement.EnumerateChildren())
        {
            if (child is IStatementNode childStatement && childStatement is not FunctionDeclNode)
            {
                CollectReturnInfo(childStatement, returns, variableAssignments, linkedFunctions);
            }
        }
    }

    /// <summary>
    /// Returns whether the given function already has a registered return type in global names.
    /// </summary>
    private static bool IsReturnTypeRegistered(GlobalMacroTypeResolver resolver, string functionName)
    {
        return resolver.GlobalNames.TryGetFunctionReturnType(functionName) is not null;
    }

    /// <summary>
    /// Registers the given function's return type in global names, unless one is already registered.
    /// </summary>
    private static void RegisterFunctionReturnType(GlobalMacroTypeResolver resolver, string functionName, IMacroType type)
    {
        if (resolver.GlobalNames.TryGetFunctionReturnType(functionName) is null)
        {
            resolver.GlobalNames.DefineFunctionReturnType(functionName, type);
        }
    }

    /// <summary>
    /// Compares whether two macro types are the "same" for consensus purposes.
    /// </summary>
    private static bool SameMacroType(IMacroType a, IMacroType b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        return a is AssetMacroType assetA && b is AssetMacroType assetB && assetA.Type == assetB.Type;
    }

    /// <summary>
    /// When a function's argument types are already known, and an argument is assigned to a
    /// variable within the function body, registers the variable's type so that literals assigned
    /// to it later can be expanded. Used by <see cref="AST.Nodes.AssignNode"/> at clean time.
    /// </summary>
    public static void PropagateArgumentTypeToVariableOnAssignment(ASTCleaner cleaner, VariableNode destination, VariableNode value)
    {
        int argIndex = GetArgumentIndex(value);
        if (argIndex < 0 || !IsPropagatableVariable(destination) || IsArgumentVariable(destination))
        {
            return;
        }
        if (cleaner.TopFragmentContext?.CodeEntryName is not string codeEntryName)
        {
            return;
        }
        if (cleaner.GlobalMacroResolver is not GlobalMacroTypeResolver globalResolver)
        {
            return;
        }
        // Don't override an already-registered (potentially more specific) type
        if (globalResolver.ResolveVariableType(cleaner, destination.Variable.Name.Content) is not null)
        {
            return;
        }
        string functionName = GetFunctionNameFromCodeEntryName(codeEntryName);
        if (functionName is null ||
            globalResolver.GetResolvedFunctionArgumentTypes(codeEntryName, functionName, out _) is not IMacroTypeFunctionArgs args)
        {
            return;
        }
        if (GetFunctionArgumentTypes(args) is not IMacroType?[] perArg ||
            argIndex >= perArg.Length || perArg[argIndex] is not IMacroType argType)
        {
            return;
        }
        globalResolver.DefineVariableTypeForCodeEntry(codeEntryName, destination.Variable.Name.Content, argType);
    }

    /// <summary>
    /// Derives the function name from a code entry name, stripping the "gml_Script_" prefix used
    /// for script assets. Returns the entry name as-is when it doesn't match that prefix.
    /// </summary>
    private static string GetFunctionNameFromCodeEntryName(string codeEntryName)
    {
        const string prefix = "gml_Script_";
        if (codeEntryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return codeEntryName[prefix.Length..];
        }
        return codeEntryName;
    }

    /// <summary>
    /// Internal AST analyzer that performs the actual data-flow tracking.
    /// </summary>
    private sealed class Analyzer
    {
        private readonly ASTCleaner _cleaner;

        /// <summary>
        /// Inferred macro type per argument index.
        /// </summary>
        private readonly List<IMacroType?> _argumentTypes = [];

        /// <summary>
        /// Mapping of local variable name to the set of argument indices that may currently flow into it.
        /// </summary>
        private readonly Dictionary<string, HashSet<int>> _localToArgSources = [];

        /// <summary>
        /// Mapping of non-local variable name to the set of argument indices that were assigned to it,
        /// used for reverse propagation of inferred argument types onto variables.
        /// </summary>
        private readonly Dictionary<string, HashSet<int>> _variableToArgSources = [];

        /// <summary>
        /// The highest argument index referenced so far.
        /// </summary>
        public int MaxReferencedArgument { get; private set; } = -1;

        /// <summary>
        /// Shared empty set, to avoid allocations.
        /// </summary>
        private static readonly HashSet<int> EmptySources = [];

        public Analyzer(ASTCleaner cleaner)
        {
            _cleaner = cleaner;
        }

        public IMacroType? GetInferredType(int index)
        {
            if (index < 0 || index >= _argumentTypes.Count)
            {
                return null;
            }
            return _argumentTypes[index];
        }

        public void EnsureArgumentSize(int index)
        {
            while (_argumentTypes.Count <= index)
            {
                _argumentTypes.Add(null);
            }
            if (index > MaxReferencedArgument)
            {
                MaxReferencedArgument = index;
            }
        }

        private void RecordTypesForSources(HashSet<int> sources, IMacroType type)
        {
            if (sources.Count == 0)
            {
                return;
            }
            foreach (int index in sources)
            {
                EnsureArgumentSize(index);
                IMacroType? existing = _argumentTypes[index];
                if (existing is null)
                {
                    _argumentTypes[index] = type;
                }
                else if (!ReferenceEquals(existing, type))
                {
                    _argumentTypes[index] = new UnionMacroType([existing, type]);
                }
            }
        }

        private void RecordLocalSources(string name, HashSet<int> sources)
        {
            if (sources.Count == 0)
            {
                return;
            }
            if (!_localToArgSources.TryGetValue(name, out HashSet<int>? existing))
            {
                existing = [];
                _localToArgSources[name] = existing;
            }
            existing.UnionWith(sources);
        }

        /// <summary>
        /// Records that the given non-local variable was assigned from the given argument sources.
        /// </summary>
        private void RecordVariableSources(string name, HashSet<int> sources)
        {
            if (sources.Count == 0)
            {
                return;
            }
            if (!_variableToArgSources.TryGetValue(name, out HashSet<int>? existing))
            {
                existing = [];
                _variableToArgSources[name] = existing;
            }
            existing.UnionWith(sources);
        }

        /// <summary>
        /// Reverse inference: registers the inferred argument types onto variables that were assigned
        /// from those arguments, so that literals assigned to those variables (e.g. colors) can be
        /// expanded by the cleaner. Types are registered per code entry, and never override types
        /// that are already registered.
        /// </summary>
        public void RegisterInferredVariableTypes()
        {
            if (_cleaner.TopFragmentContext?.CodeEntryName is not string codeEntryName)
            {
                return;
            }
            if (_cleaner.GlobalMacroResolver is not GlobalMacroTypeResolver globalResolver)
            {
                return;
            }

            // Gather all variable assignments (locals plus non-locals) in one place
            Dictionary<string, HashSet<int>> allSources = [];
            foreach ((string name, HashSet<int> sources) in _localToArgSources)
            {
                allSources[name] = sources;
            }
            foreach ((string name, HashSet<int> sources) in _variableToArgSources)
            {
                if (allSources.TryGetValue(name, out HashSet<int>? existing))
                {
                    existing.UnionWith(sources);
                }
                else
                {
                    allSources[name] = sources;
                }
            }

            foreach ((string name, HashSet<int> sources) in allSources)
            {
                // Collect the inferred types of all argument indices assigned to this variable
                List<IMacroType> types = [];
                foreach (int index in sources)
                {
                    if (index < _argumentTypes.Count && _argumentTypes[index] is IMacroType type)
                    {
                        types.Add(type);
                    }
                }
                if (types.Count == 0)
                {
                    continue;
                }

                // Don't override an already-registered (potentially more specific) type
                if (globalResolver.ResolveVariableType(_cleaner, name) is not null)
                {
                    continue;
                }

                IMacroType resultType = types.Count == 1 ? types[0] : new UnionMacroType(types);
                globalResolver.DefineVariableTypeForCodeEntry(codeEntryName, name, resultType);
            }
        }

        private static bool IsLocalVariable(VariableNode node)
        {
            if (node.ArrayIndices is not null)
            {
                return false;
            }
            return node.Left is InstanceTypeNode { InstanceType: InstanceType.Local } or
                   Int16Node { Value: (short)InstanceType.Local };
        }

        /// <summary>
        /// Returns the set of argument indices that could be the value of the given variable.
        /// </summary>
        private HashSet<int> GetVariableSources(VariableNode node)
        {
            // Direct argument reference
            if (IsArgumentVariable(node))
            {
                int argIndex = GetArgumentIndex(node);
                if (argIndex != -1)
                {
                    EnsureArgumentSize(argIndex);
                    return [argIndex];
                }
            }

            // Local variable, tracking data flow
            if (IsLocalVariable(node))
            {
                if (_localToArgSources.TryGetValue(node.Variable.Name.Content, out HashSet<int>? sources) && sources.Count > 0)
                {
                    return new HashSet<int>(sources);
                }
            }

            return EmptySources;
        }

        public void VisitStatement(IStatementNode statement)
        {
            switch (statement)
            {
                case BlockNode block:
                    {
                        foreach (IStatementNode child in block.Children)
                        {
                            VisitStatement(child);
                        }
                        break;
                    }
                case IfNode iff:
                    {
                        VisitExpression(iff.Condition);
                        VisitStatement(iff.TrueBlock);
                        if (iff.ElseBlock is not null)
                        {
                            VisitStatement(iff.ElseBlock);
                        }
                        break;
                    }
                case WhileLoopNode whileLoop:
                    {
                        VisitExpression(whileLoop.Condition);
                        VisitStatement(whileLoop.Body);
                        break;
                    }
                case DoUntilLoopNode doUntil:
                    {
                        VisitStatement(doUntil.Body);
                        VisitExpression(doUntil.Condition);
                        break;
                    }
                case RepeatLoopNode repeat:
                    {
                        VisitExpression(repeat.TimesToRepeat);
                        VisitStatement(repeat.Body);
                        break;
                    }
                case ForLoopNode forLoop:
                    {
                        if (forLoop.Initializer is not null)
                        {
                            VisitStatement(forLoop.Initializer);
                        }
                        if (forLoop.Condition is not null)
                        {
                            VisitExpression(forLoop.Condition);
                        }
                        if (forLoop.Incrementor is not null)
                        {
                            VisitStatement(forLoop.Incrementor);
                        }
                        VisitStatement(forLoop.Body);
                        break;
                    }
                case WithLoopNode with:
                    {
                        VisitExpression(with.Target);
                        VisitStatement(with.Body);
                        break;
                    }
                case SwitchNode switchNode:
                    {
                        // The expression being switched upon can be inferred from case constants
                        HashSet<int> switchSources = VisitExpression(switchNode.Expression);
                        foreach (IStatementNode child in switchNode.Body.Children)
                        {
                            if (child is SwitchCaseNode switchCase && switchCase.Expression is not null)
                            {
                                if (GetConstantType(switchCase.Expression) is IMacroType caseType && switchSources.Count > 0)
                                {
                                    RecordTypesForSources(switchSources, caseType);
                                }
                            }
                        }
                        VisitStatement(switchNode.Body);
                        break;
                    }
                case TryCatchNode tryCatch:
                    {
                        VisitStatement(tryCatch.Try);
                        if (tryCatch.Catch is not null)
                        {
                            VisitStatement(tryCatch.Catch);
                        }
                        if (tryCatch.Finally is not null)
                        {
                            VisitStatement(tryCatch.Finally);
                        }
                        break;
                    }
                case ReturnNode returnNode:
                    {
                        VisitExpression(returnNode.Value);
                        break;
                    }
                case ThrowNode throwNode:
                    {
                        VisitExpression(throwNode.Value);
                        break;
                    }
                case AssignNode assign:
                    {
                        ProcessAssignment(assign);
                        break;
                    }
                case FunctionDeclNode functionDecl:
                    {
                        VisitStatement(functionDecl.Body);
                        break;
                    }
                case StaticInitNode staticInit:
                    {
                        VisitStatement(staticInit.Body);
                        break;
                    }
                case FunctionCallNode functionCall:
                    {
                        VisitExpression(functionCall);
                        break;
                    }
                case VariableCallNode variableCall:
                    {
                        VisitExpression(variableCall);
                        break;
                    }
                case NewObjectNode newObject:
                    {
                        VisitExpression(newObject);
                        break;
                    }
                default:
                    {
                        // Fallback: process any child statements/expressions generically
                        foreach (IBaseASTNode child in statement.EnumerateChildren())
                        {
                            if (child is IStatementNode childStatement)
                            {
                                VisitStatement(childStatement);
                            }
                            else if (child is IExpressionNode childExpression)
                            {
                                VisitExpression(childExpression);
                            }
                        }
                        break;
                    }
            }
        }

        private HashSet<int> VisitExpression(IExpressionNode expression)
        {
            switch (expression)
            {
                case VariableNode variable:
                    return GetVariableSources(variable);
                case FunctionCallNode functionCall:
                    return ProcessFunctionCall(functionCall);
                case VariableCallNode variableCall:
                    ProcessVariableCall(variableCall);
                    return EmptySources;
                case NewObjectNode newObject:
                    ProcessNewObject(newObject);
                    return EmptySources;
                case BinaryNode binary:
                    ProcessBinary(binary);
                    return EmptySources;
                case AssignNode assign:
                    ProcessAssignment(assign);
                    return EmptySources;
                case ConditionalNode conditional:
                    VisitExpression(conditional.Condition);
                    VisitExpression(conditional.True);
                    VisitExpression(conditional.False);
                    return EmptySources;
                case UnaryNode unary:
                    VisitExpression(unary.Value);
                    return EmptySources;
                case ShortCircuitNode shortCircuit:
                    foreach (IExpressionNode condition in shortCircuit.Conditions)
                    {
                        VisitExpression(condition);
                    }
                    return EmptySources;
                case NullishCoalesceNode nullish:
                    VisitExpression(nullish.Left);
                    VisitExpression(nullish.Right);
                    return EmptySources;
                case ArrayInitNode arrayInit:
                    foreach (IExpressionNode element in arrayInit.Elements)
                    {
                        VisitExpression(element);
                    }
                    return EmptySources;
                case FunctionDeclNode functionDecl:
                    VisitStatement(functionDecl.Body);
                    return EmptySources;
                case StructNode structNode:
                    VisitStatement(structNode.Body);
                    return EmptySources;
                default:
                    return EmptySources;
            }
        }

        private HashSet<int> ProcessFunctionCall(FunctionCallNode functionCall)
        {
            string functionName = functionCall.FunctionName;

            // Get argument types of the called function, if defined (may trigger lazy inference for user scripts)
            IMacroType?[]? argTypes = null;
            if (_cleaner.GlobalMacroResolver.ResolveFunctionArgumentTypes(_cleaner, functionName) is IMacroTypeFunctionArgs resolved)
            {
                argTypes = GetFunctionArgumentTypes(resolved);
            }

            // Handle the script_execute argument shift
            int argsStart = functionName == VMConstants.ScriptExecuteFunction ? 1 : 0;

            for (int i = 0; i < functionCall.Arguments.Count; i++)
            {
                HashSet<int> sources = VisitExpression(functionCall.Arguments[i]);
                if (argTypes is not null && i >= argsStart)
                {
                    int typeIndex = i - argsStart;
                    if (typeIndex < argTypes.Length && argTypes[typeIndex] is IMacroType type && sources.Count > 0)
                    {
                        RecordTypesForSources(sources, type);
                    }
                }
            }

            return EmptySources;
        }

        private void ProcessVariableCall(VariableCallNode variableCall)
        {
            VisitExpression(variableCall.Function);
            if (variableCall.Instance is not null)
            {
                VisitExpression(variableCall.Instance);
            }
            foreach (IExpressionNode arg in variableCall.Arguments)
            {
                VisitExpression(arg);
            }
        }

        private void ProcessNewObject(NewObjectNode newObject)
        {
            VisitExpression(newObject.Function);

            string? functionName = newObject.FunctionName;
            IMacroType?[]? argTypes = null;
            if (functionName is not null &&
                _cleaner.GlobalMacroResolver.ResolveFunctionArgumentTypes(_cleaner, functionName) is IMacroTypeFunctionArgs resolved)
            {
                argTypes = GetFunctionArgumentTypes(resolved);
            }

            for (int i = 0; i < newObject.Arguments.Count; i++)
            {
                HashSet<int> sources = VisitExpression(newObject.Arguments[i]);
                if (argTypes is not null)
                {
                    if (i < argTypes.Length && argTypes[i] is IMacroType type && sources.Count > 0)
                    {
                        RecordTypesForSources(sources, type);
                    }
                }
            }
        }

        /// <summary>
        /// Extracts the per-argument-position macro types from a function argument macro type.
        /// Handles unions (merging position types in order), intersections, and conditionals that
        /// delegate to inner function argument types. Returns <see langword="null"/> if the types
        /// cannot be decomposed into per-position types.
        /// </summary>
        internal static IMacroType?[]? GetFunctionArgumentTypes(IMacroTypeFunctionArgs argsMacroType)
        {
            switch (argsMacroType)
            {
                case FunctionArgsMacroType functionArgs:
                    {
                        int count = functionArgs.ArgumentCount;
                        IMacroType?[] result = new IMacroType?[count];
                        for (int i = 0; i < count; i++)
                        {
                            result[i] = functionArgs.GetArgumentType(i);
                        }
                        return result;
                    }
                case UnionMacroType union:
                    {
                        // Gather per-position types across all members, in member order
                        List<IMacroType?[]> memberTypes = [];
                        int maxCount = 0;
                        foreach (IMacroType member in union.GetTypes())
                        {
                            if (member is IMacroTypeFunctionArgs memberArgs &&
                                GetFunctionArgumentTypes(memberArgs) is IMacroType?[] memberArgTypes)
                            {
                                memberTypes.Add(memberArgTypes);
                                if (memberArgTypes.Length > maxCount)
                                {
                                    maxCount = memberArgTypes.Length;
                                }
                            }
                        }
                        if (memberTypes.Count == 0)
                        {
                            return null;
                        }
                        IMacroType?[] result = new IMacroType?[maxCount];
                        for (int i = 0; i < maxCount; i++)
                        {
                            List<IMacroType> positionTypes = [];
                            foreach (IMacroType?[] memberArgTypes in memberTypes)
                            {
                                if (i < memberArgTypes.Length && memberArgTypes[i] is IMacroType positionType)
                                {
                                    positionTypes.Add(positionType);
                                }
                            }
                            if (positionTypes.Count == 1)
                            {
                                result[i] = positionTypes[0];
                            }
                            else if (positionTypes.Count > 1)
                            {
                                result[i] = new UnionMacroType(positionTypes);
                            }
                        }
                        return result;
                    }
                case IntersectMacroType intersect:
                    {
                        // Intersect per-position types across all members
                        List<IMacroType?[]> memberTypes = [];
                        int maxCount = 0;
                        foreach (IMacroType member in intersect.GetTypes())
                        {
                            if (member is IMacroTypeFunctionArgs memberArgs &&
                                GetFunctionArgumentTypes(memberArgs) is IMacroType?[] memberArgTypes)
                            {
                                memberTypes.Add(memberArgTypes);
                                if (memberArgTypes.Length > maxCount)
                                {
                                    maxCount = memberArgTypes.Length;
                                }
                            }
                        }
                        if (memberTypes.Count == 0)
                        {
                            return null;
                        }
                        IMacroType?[] result = new IMacroType?[maxCount];
                        for (int i = 0; i < maxCount; i++)
                        {
                            List<IMacroType> positionTypes = [];
                            foreach (IMacroType?[] memberArgTypes in memberTypes)
                            {
                                if (i < memberArgTypes.Length && memberArgTypes[i] is IMacroType positionType)
                                {
                                    positionTypes.Add(positionType);
                                }
                            }
                            if (positionTypes.Count == 1)
                            {
                                result[i] = positionTypes[0];
                            }
                            else if (positionTypes.Count > 1)
                            {
                                result[i] = new IntersectMacroType(positionTypes);
                            }
                        }
                        return result;
                    }
                case ConditionalMacroType conditional when conditional.InnerType is not null:
                    {
                        if (conditional.InnerType is IMacroTypeFunctionArgs innerArgs)
                        {
                            return GetFunctionArgumentTypes(innerArgs);
                        }
                        return null;
                    }
                default:
                    return null;
            }
        }

        private void ProcessBinary(BinaryNode binary)
        {
            HashSet<int> leftSources = VisitExpression(binary.Left);
            HashSet<int> rightSources = VisitExpression(binary.Right);

            // Equality comparisons against typed constants imply the type of the compared argument
            if (binary.Instruction.Kind == Opcode.Compare &&
                binary.Instruction.ComparisonKind is ComparisonType.EqualTo or ComparisonType.NotEqualTo)
            {
                if (leftSources.Count > 0 && GetConstantType(binary.Right) is IMacroType rightType)
                {
                    RecordTypesForSources(leftSources, rightType);
                }
                if (rightSources.Count > 0 && GetConstantType(binary.Left) is IMacroType leftType)
                {
                    RecordTypesForSources(rightSources, leftType);
                }
            }
        }

        private void ProcessAssignment(AssignNode assign)
        {
            if (assign.Value is null)
            {
                // Prefix/postfix assignments carry no type information by themselves
                return;
            }

            HashSet<int> valueSources = VisitExpression(assign.Value);
            if (valueSources.Count == 0)
            {
                return;
            }

            // Only normal assignments carry type information
            if (assign.AssignKind != AssignNode.AssignType.Normal)
            {
                return;
            }

            if (assign.Variable is not VariableNode destination)
            {
                return;
            }

            // Reassigning an argument variable is not meaningful for inference
            if (IsArgumentVariable(destination))
            {
                return;
            }

            // Assignment to a simple local variable -> track data flow through it
            if (IsLocalVariable(destination))
            {
                RecordLocalSources(destination.Variable.Name.Content, valueSources);
                return;
            }

            // Assignment to a variable with a known macro type (e.g. sprite_index) -> infer argument types
            if (destination.ArrayIndices is null &&
                _cleaner.GlobalMacroResolver.ResolveVariableType(_cleaner, destination.Variable.Name.Content) is IMacroType destinationType)
            {
                RecordTypesForSources(valueSources, destinationType);
                return;
            }

            // Assignment of an argument to a regular self/global variable -> track it for reverse
            // propagation, so that literals assigned to the variable later (e.g. colors) get expanded.
            if (IsPropagatableVariable(destination))
            {
                RecordVariableSources(destination.Variable.Name.Content, valueSources);
            }
        }

        /// <summary>
        /// Determines the macro type of a constant expression, if it can be determined.
        /// </summary>
        private static IMacroType? GetConstantType(IExpressionNode expression)
        {
            switch (expression)
            {
                case AssetReferenceNode assetReference:
                    return new AssetMacroType(assetReference.AssetType);
                case EnumValueNode enumValue when !enumValue.IsUnknownEnum:
                    // Build a partial enum macro type from the single known value
                    return new EnumMacroType(enumValue.EnumName, new Dictionary<long, string>
                    {
                        [enumValue.EnumValue] = enumValue.EnumValueName
                    });
                default:
                    return null;
            }
        }
    }
}
