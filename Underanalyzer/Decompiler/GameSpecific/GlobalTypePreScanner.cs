/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;

namespace Underanalyzer.Decompiler.GameSpecific;

/// <summary>
/// Runs type inference over all code entries up front, so that types inferred in one entry are
/// registered before any entry is decompiled for display. This makes literal expansion independent
/// of the order in which code entries are opened.
/// </summary>
public static class GlobalTypePreScanner
{
    /// <summary>
    /// Decompiles each root code entry for inference only (no cleaning or printing), registering
    /// return and variable types in the game context's shared macro type resolver.
    /// </summary>
    public static void Scan(IGameContext gameContext, IEnumerable<IGMCode> codeEntries, IDecompileSettings settings)
    {
        if (!settings.PreScanAllCodeForTypes ||
            !settings.InferFunctionArgumentTypes ||
            !settings.ReverseInferVariableTypes)
        {
            return;
        }

        foreach (IGMCode code in codeEntries)
        {
            // Child entries are part of their parent's AST; scanning them separately would register
            // the same types twice with a different code entry name.
            if (code.Parent is not null || code.InstructionCount <= 0)
            {
                continue;
            }

            try
            {
                new DecompileContext(gameContext, code, settings).DecompileForTypeInference();
            }
            catch
            {
                // Pre-scanning is best-effort; a failing entry must not prevent other entries from
                // contributing their types, nor prevent the game from being decompiled.
            }
        }
    }
}
