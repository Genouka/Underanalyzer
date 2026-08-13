/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace Underanalyzer.Compiler.Errors;

/// <summary>
/// Represents a compile error that carries source position information.
/// </summary>
/// <remarks>
/// Implemented by errors that originate from lexing or parsing, where the
/// location in the source code is known. Allows editor tooling to map errors
/// back to specific locations in the code.
/// </remarks>
public interface IPositionedCompileError
{
    /// <summary>
    /// The 1-based line number of the error in the source code, or <see langword="null"/> if unknown.
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// The 1-based column number of the error in the source code, or <see langword="null"/> if unknown.
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// The absolute text position (offset) of the error in the source code, or <see langword="null"/> if unknown.
    /// </summary>
    public int? TextPosition { get; }
}
