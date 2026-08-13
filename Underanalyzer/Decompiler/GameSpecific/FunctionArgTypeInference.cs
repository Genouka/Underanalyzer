/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
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

        private static bool IsArgumentVariable(VariableNode node)
        {
            return GetArgumentIndex(node) != -1;
        }

        /// <summary>
        /// Returns the argument index for the given variable node, or -1 if it is not an argument variable.
        /// Supports both 2.3+ games (where arguments use the "argument" instance type) and pre-2.3 games
        /// (where arguments are regular self/builtin variables named "argument0", or array accesses "argument[i]").
        /// </summary>
        private static int GetArgumentIndex(VariableNode node)
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
        private static IMacroType?[]? GetFunctionArgumentTypes(IMacroTypeFunctionArgs argsMacroType)
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
