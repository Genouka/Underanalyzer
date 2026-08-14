/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
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
    /// Lookup of object name to a name resolver, used to share instance variable types across the
    /// events of the same object (e.g. a variable used as a sprite in the Draw event, whose literals
    /// assigned in the Create event can then be expanded).
    /// </summary>
    private Dictionary<string, NameMacroTypeResolver> ObjectNames { get; }

    /// <summary>
    /// Initializes an empty global macro resolver.
    /// </summary>
    public GlobalMacroTypeResolver()
    {
        GlobalNames = new NameMacroTypeResolver();
        CodeEntryNames = [];
        ObjectNames = [];
    }

    /// <summary>
    /// Defines a name resolver for a specific code entry.
    /// </summary>
    public void DefineCodeEntry(string codeEntry, NameMacroTypeResolver resolver)
    {
        CodeEntryNames[codeEntry] = resolver;
    }

    /// <summary>
    /// Defines a variable's macro type for a specific code entry, creating a name resolver for that
    /// code entry if one does not already exist.
    /// </summary>
    public void DefineVariableTypeForCodeEntry(string codeEntry, string variableName, IMacroType? type)
    {
        if (!CodeEntryNames.TryGetValue(codeEntry, out NameMacroTypeResolver? resolver))
        {
            resolver = new NameMacroTypeResolver();
            CodeEntryNames[codeEntry] = resolver;
        }
        resolver.DefineVariableType(variableName, type);
    }

    /// <summary>
    /// Defines a variable's macro type for the given object, creating a name resolver for that object
    /// if one does not already exist. Used to share instance variable types across an object's events.
    /// Types already defined for the object are never overwritten.
    /// </summary>
    public void DefineVariableTypeForObject(string objectName, string variableName, IMacroType? type)
    {
        if (!ObjectNames.TryGetValue(objectName, out NameMacroTypeResolver? resolver))
        {
            resolver = new NameMacroTypeResolver();
            ObjectNames[objectName] = resolver;
        }
        if (resolver.ResolveVariableType(null!, variableName) is null)
        {
            resolver.DefineVariableType(variableName, type);
        }
    }

    public IMacroType? ResolveVariableType(ASTCleaner cleaner, string? variableName)
    {
        if (variableName is null)
        {
            return null;
        }

        string? codeEntryName = cleaner.TopFragmentContext?.CodeEntryName;
        if (codeEntryName is not null)
        {
            if (CodeEntryNames.TryGetValue(codeEntryName, out NameMacroTypeResolver? resolver))
            {
                IMacroType? resolved = resolver.ResolveVariableType(cleaner, variableName);
                if (resolved is not null)
                {
                    return resolved;
                }
            }

            // Fall back to the object-wide namespace, so that instance variables can share their
            // types across events of the same object (e.g. inferred in the Draw event, used in the
            // Create event)
            if (GetObjectNameFromCodeEntryName(codeEntryName) is string objectName &&
                ObjectNames.TryGetValue(objectName, out NameMacroTypeResolver? objectResolver))
            {
                IMacroType? resolved = objectResolver.ResolveVariableType(cleaner, variableName);
                if (resolved is not null)
                {
                    return resolved;
                }
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

    /// <summary>
    /// Returns the argument types registered or previously inferred for the given function, without
    /// triggering any new inference. <see langword="null"/> is returned if no type information is available.
    /// </summary>
    /// <param name="codeEntryName">
    /// Name of the code entry for which to first check per-entry type definitions, or <see langword="null"/>
    /// to only check global definitions.
    /// </param>
    /// <param name="isInferred">
    /// Set to <see langword="true"/> when the returned type was produced by automatic inference
    /// (as opposed to a registered definition).
    /// </param>
    public IMacroType? GetResolvedFunctionArgumentTypes(string? codeEntryName, string? functionName, out bool isInferred)
    {
        isInferred = false;
        if (functionName is null)
        {
            return null;
        }

        if (codeEntryName is not null && CodeEntryNames.TryGetValue(codeEntryName, out NameMacroTypeResolver? resolver))
        {
            if (resolver.TryGetFunctionArgumentsType(functionName) is IMacroType registered)
            {
                return registered;
            }
        }

        if (GlobalNames.TryGetFunctionArgumentsType(functionName) is IMacroType globalRegistered)
        {
            return globalRegistered;
        }

        lock (_inferLock)
        {
            if (_inferredFunctionArguments.TryGetValue(functionName, out IMacroType? cached))
            {
                isInferred = true;
                return cached;
            }
        }

        return null;
    }

    // Cache of inferred function argument types, keyed by function name (null values are cached "no result").
    private readonly object _inferLock = new();
    private readonly Dictionary<string, IMacroType?> _inferredFunctionArguments = [];
    private readonly HashSet<string> _inferringFunctionArguments = [];

    /// <summary>
    /// Returns the object name that an object event code entry belongs to, or <see langword="null"/>
    /// if the code entry name does not correspond to an object event. Object event code entries use
    /// the naming convention <c>gml_Object_&lt;object&gt;_&lt;event&gt;_&lt;subtype&gt;</c>.
    /// </summary>
    public static string? GetObjectNameFromCodeEntryName(string codeEntryName)
    {
        const string prefix = "gml_Object_";
        if (!codeEntryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }
        string rest = codeEntryName[prefix.Length..];
        int bestIndex = -1;
        int bestLength = 0;
        foreach (string eventName in ObjectEventNames)
        {
            int index = rest.LastIndexOf("_" + eventName + "_", StringComparison.Ordinal);
            if (index > bestIndex)
            {
                bestIndex = index;
                bestLength = eventName.Length;
            }
        }
        if (bestIndex <= 0 || bestIndex + bestLength + 2 >= rest.Length)
        {
            return null;
        }
        return rest[..bestIndex];
    }

    /// <summary>
    /// Known object event names used in code entry names (e.g. "gml_Object_obj_Step_0").
    /// </summary>
    private static readonly string[] ObjectEventNames =
    [
        "KeyRelease", "KeyPress", "PreCreate", "Collision", "Destroy", "CleanUp",
        "Keyboard", "Trigger", "Create", "Alarm", "Step", "Mouse", "Gesture", "Other", "Draw"
    ];

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
