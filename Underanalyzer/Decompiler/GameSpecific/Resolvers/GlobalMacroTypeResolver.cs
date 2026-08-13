/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using Underanalyzer.Decompiler.AST;

namespace Underanalyzer.Decompiler.GameSpecific;

/// <summary>
/// Macro type resolver for a global game context. Delegates resolution to individual code entries, then global lookup.
/// When no type information is registered for a user-defined function's arguments, this resolver may
/// automatically infer and cache it by decompiling the function's code (see <see cref="FunctionArgTypeInference"/>).
/// All deferred inference and caching is thread-safe.
/// </summary>
public class GlobalMacroTypeResolver : IMacroTypeResolver
{
    public NameMacroTypeResolver GlobalNames { get; set; }
    private Dictionary<string, NameMacroTypeResolver> CodeEntryNames { get; }

    /// <summary>
    /// Initializes an empty global macro resolver.
    /// </summary>
    public GlobalMacroTypeResolver()
    {
        GlobalNames = new NameMacroTypeResolver();
        CodeEntryNames = [];
    }

    /// <summary>
    /// Defines a name resolver for a specific code entry.
    /// </summary>
    public void DefineCodeEntry(string codeEntry, NameMacroTypeResolver resolver)
    {
        CodeEntryNames[codeEntry] = resolver;
    }

    public IMacroType? ResolveVariableType(ASTCleaner cleaner, string? variableName)
    {
        if (variableName is null)
        {
            return null;
        }

        if (CodeEntryNames.TryGetValue(cleaner.TopFragmentContext!.CodeEntryName!, out NameMacroTypeResolver? resolver))
        {
            IMacroType? resolved = resolver.ResolveVariableType(cleaner, variableName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return GlobalNames.ResolveVariableType(cleaner, variableName);
    }

    public IMacroType? ResolveFunctionArgumentTypes(ASTCleaner cleaner, string? functionName)
    {
        if (functionName is null)
        {
            return null;
        }

        if (CodeEntryNames.TryGetValue(cleaner.TopFragmentContext!.CodeEntryName!, out NameMacroTypeResolver? resolver))
        {
            IMacroType? resolved = resolver.ResolveFunctionArgumentTypes(cleaner, functionName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (GlobalNames.ResolveFunctionArgumentTypes(cleaner, functionName) is IMacroType globalResolved)
        {
            return globalResolved;
        }

        // No pre-registered type information was found for this function. If enabled, attempt to
        // infer its argument types by analyzing its code, and cache the result for future lookups.
        if (cleaner.Context.Settings.InferFunctionArgumentTypes)
        {
            return TryInferArgumentTypes(cleaner, functionName);
        }

        return null;
    }

    public IMacroType? ResolveReturnValueType(ASTCleaner cleaner, string? functionName)
    {
        if (functionName is null)
        {
            return null;
        }

        if (CodeEntryNames.TryGetValue(cleaner.TopFragmentContext!.CodeEntryName!, out NameMacroTypeResolver? resolver))
        {
            IMacroType? resolved = resolver.ResolveReturnValueType(cleaner, functionName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return GlobalNames.ResolveReturnValueType(cleaner, functionName);
    }

    // Cache of inferred function argument types, keyed by function name (null values are cached "no result").
    private readonly object _inferLock = new();
    private readonly Dictionary<string, IMacroType?> _inferredFunctionArguments = [];
    private readonly HashSet<string> _inferringFunctionArguments = [];

    /// <summary>
    /// Attempts to infer the argument types of the given function by decompiling and analyzing its code.
    /// The result is cached so that subsequent lookups are instant. This is best-effort; failures result
    /// in a cached <see langword="null"/> and a fallback to no resolution.
    /// </summary>
    private IMacroType? TryInferArgumentTypes(ASTCleaner cleaner, string functionName)
    {
        // No provider available means inference is not supported in this game context
        if (cleaner.Context.GameContext.FunctionArgTypeProvider is not IFunctionArgTypeProvider provider)
        {
            return null;
        }

        lock (_inferLock)
        {
            // Check cached result (including cached "no result")
            if (_inferredFunctionArguments.TryGetValue(functionName, out IMacroType? cached))
            {
                return cached;
            }

            // Avoid infinite recursion when inferring a function that calls itself (directly or transitively)
            if (_inferringFunctionArguments.Contains(functionName))
            {
                return null;
            }

            // Find the code entry for the function
            IGMCode? code = provider.GetFunctionCode(functionName);
            if (code is null)
            {
                _inferredFunctionArguments[functionName] = null;
                return null;
            }

            _inferringFunctionArguments.Add(functionName);
            try
            {
                // Decompile the function's code entry in a fresh context
                DecompileContext targetContext = new(cleaner.Context.GameContext, code, cleaner.Context.Settings);
                AST.IStatementNode ast = targetContext.DecompileToAST();

                // Set up a cleaner for the target, with a valid fragment context for macro resolution
                AST.ASTCleaner targetCleaner = new(targetContext);
                if (targetContext.FragmentNodes is { Count: > 0 } fragments)
                {
                    targetCleaner.PushFragmentContext(new AST.ASTFragmentContext(null, fragments[0]));
                }
                try
                {
                    IMacroType?[] types = FunctionArgTypeInference.Infer(targetCleaner, ast, code);

                    // Only register if at least one argument type was inferred
                    IMacroType? argsMacroType = null;
                    foreach (IMacroType? type in types)
                    {
                        if (type is not null)
                        {
                            argsMacroType = new FunctionArgsMacroType(types);
                            break;
                        }
                    }
                    if (argsMacroType is not null)
                    {
                        GlobalNames.DefineFunctionArgumentsType(functionName, argsMacroType);
                    }

                    _inferredFunctionArguments[functionName] = argsMacroType;
                    return argsMacroType;
                }
                finally
                {
                    if (targetCleaner.TopFragmentContext is not null)
                    {
                        targetCleaner.PopFragmentContext();
                    }
                }
            }
            catch
            {
                // Inference is best-effort; fall back to no resolution
                _inferredFunctionArguments[functionName] = null;
                return null;
            }
            finally
            {
                _inferringFunctionArguments.Remove(functionName);
            }
        }
    }
}
