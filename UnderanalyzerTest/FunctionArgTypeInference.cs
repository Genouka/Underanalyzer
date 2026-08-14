/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using Underanalyzer;
using Underanalyzer.Decompiler;
using Underanalyzer.Decompiler.GameSpecific;
using Underanalyzer.Mock;

namespace UnderanalyzerTest;

public class FunctionArgTypeInferenceTests
{
    /// <summary>
    /// Mock implementation of <see cref="IFunctionArgTypeProvider"/>, mapping function names to their code entries.
    /// </summary>
    private sealed class MockFunctionArgTypeProvider : IFunctionArgTypeProvider
    {
        private readonly Dictionary<string, IGMCode> _codes = [];

        public void AddCode(string functionName, string assembly, GameContextMock gameContext, string? codeName = null)
        {
            if (codeName is null)
            {
                _codes[functionName] = TestUtil.GetCode(assembly, gameContext);
            }
            else
            {
                // Use the given code entry name (e.g. "gml_Script_..."), so that return value type
                // inference keys on the same code entry name at clean time.
                _codes[functionName] = VMAssembly.ParseAssemblyFromLines(
                    assembly.Split('\n'), gameContext, codeName);
            }
        }

        public IGMCode? GetFunctionCode(string functionName)
        {
            return _codes.TryGetValue(functionName, out IGMCode? code) ? code : null;
        }
    }

    private static GameContextMock CreateGameContext(out MockFunctionArgTypeProvider provider)
    {
        GameContextMock gameContext = new()
        {
            UsingAssetReferences = false
        };
        gameContext.GameSpecificRegistry.RegisterBasic();
        provider = new MockFunctionArgTypeProvider();
        gameContext.FunctionArgTypeProvider = provider;
        return gameContext;
    }

    private static void DefineGlobalFunction(GameContextMock gameContext, string name)
    {
        ((GlobalFunctions)gameContext.GlobalFunctions).DefineFunction(name, new GMFunction(name));
    }

    [Fact]
    public void TestInferFromTypedVariableAssignment()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 1, "spr_enemy");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("sprite_index",
            gameContext.GameSpecificRegistry.FindType("Asset.Sprite"));
        DefineGlobalFunction(gameContext, "scr_fade_sprite");

        provider.AddCode("scr_fade_sprite", """
            push.v argument.argument0
            pop.v.i self.sprite_index
            """, gameContext);

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 1
            call.i scr_fade_sprite 1
            popz.v
            """,
            """
            scr_fade_sprite(spr_enemy);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferFromFunctionCallArg()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 3, "spr_player");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_sprite",
            new FunctionArgsMacroType(
            [
                gameContext.GameSpecificRegistry.FindType("Asset.Sprite"),
                null,
                null,
                null
            ]));
        DefineGlobalFunction(gameContext, "scr_draw");

        provider.AddCode("scr_draw", """
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            push.v argument.argument0
            call.i draw_sprite 4
            popz.v
            """, gameContext);

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 3
            call.i scr_draw 1
            popz.v
            """,
            """
            scr_draw(spr_player);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferThroughLocalVariable()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 2, "spr_enemy2");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("sprite_index",
            gameContext.GameSpecificRegistry.FindType("Asset.Sprite"));
        DefineGlobalFunction(gameContext, "scr_set_sprite");

        provider.AddCode("scr_set_sprite", """
            push.v argument.argument0
            pop.v.i local.s
            push.v local.s
            pop.v.i self.sprite_index
            """, gameContext);

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 2
            call.i scr_set_sprite 1
            popz.v
            """,
            """
            scr_set_sprite(spr_enemy2);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferTransitivelyThroughUserScript()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 1, "spr_enemy");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("sprite_index",
            gameContext.GameSpecificRegistry.FindType("Asset.Sprite"));
        DefineGlobalFunction(gameContext, "scr_outer");
        DefineGlobalFunction(gameContext, "scr_inner");

        // scr_outer passes argument0 through to scr_inner
        provider.AddCode("scr_outer", """
            push.v argument.argument0
            call.i scr_inner 1
            popz.v
            """, gameContext);

        // scr_inner's argument0 flows into sprite_index
        provider.AddCode("scr_inner", """
            push.v argument.argument0
            pop.v.i self.sprite_index
            """, gameContext);

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 1
            call.i scr_outer 1
            popz.v
            """,
            """
            scr_outer(spr_enemy);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferEnumThroughTypedVariable()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        IMacroType enumType = new EnumMacroType("MyEnum", new Dictionary<long, string>
        {
            [0] = "Value0",
            [1] = "Value1"
        });
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("mode", enumType);
        DefineGlobalFunction(gameContext, "scr_setmode");

        provider.AddCode("scr_setmode", """
            push.v argument.argument0
            pop.v.i global.mode
            """, gameContext);

        DecompileSettings settings = new()
        {
            CreateEnumDeclarations = false
        };

        TestUtil.VerifyDecompileResult(
            """
            push.l 1
            call.i scr_setmode 1
            popz.v
            """,
            """
            scr_setmode(MyEnum.Value1);
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestNoExpansionWhenInferenceDisabled()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 1, "spr_enemy");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("sprite_index",
            gameContext.GameSpecificRegistry.FindType("Asset.Sprite"));
        DefineGlobalFunction(gameContext, "scr_fade_sprite");

        provider.AddCode("scr_fade_sprite", """
            push.v argument.argument0
            pop.v.i self.sprite_index
            """, gameContext);

        DecompileSettings settings = new()
        {
            InferFunctionArgumentTypes = false
        };

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 1
            call.i scr_fade_sprite 1
            popz.v
            """,
            """
            scr_fade_sprite(1);
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestNoExpansionWithoutProvider()
    {
        GameContextMock gameContext = new()
        {
            UsingAssetReferences = false
        };
        gameContext.GameSpecificRegistry.RegisterBasic();
        gameContext.DefineMockAsset(AssetType.Sprite, 1, "spr_enemy");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("sprite_index",
            gameContext.GameSpecificRegistry.FindType("Asset.Sprite"));
        DefineGlobalFunction(gameContext, "scr_fade_sprite");

        // Note: no FunctionArgTypeProvider is set

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 1
            call.i scr_fade_sprite 1
            popz.v
            """,
            """
            scr_fade_sprite(1);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestNoExpansionForUnrelatedArguments()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineVariableType("sprite_index",
            gameContext.GameSpecificRegistry.FindType("Asset.Sprite"));
        DefineGlobalFunction(gameContext, "scr_add_one");

        // argument0 is only used in arithmetic, so no type can be inferred
        provider.AddCode("scr_add_one", """
            push.v argument.argument0
            pushi.e 1
            add.i.v
            pop.v.i self.result
            """, gameContext);

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 1
            call.i scr_add_one 1
            popz.v
            """,
            """
            scr_add_one(1);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferThroughBuiltinWithUnionArgTypes()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);

        // Mimic the registered type information for builtins such as event_perform, which is a union
        // of multiple argument type variants.
        IMacroType eventType = new ConstantsMacroType(new Dictionary<int, string>
        {
            [3] = "ev_step",
            [8] = "ev_draw"
        });
        IMacroType stepSubtype = new ConstantsMacroType(new Dictionary<int, string>
        {
            [0] = "ev_step_normal"
        });
        IMacroType drawSubtype = new ConstantsMacroType(new Dictionary<int, string>
        {
            [0] = "ev_draw_normal"
        });
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("event_perform",
            new UnionMacroType(
            [
                new FunctionArgsMacroType([eventType, stepSubtype]),
                new FunctionArgsMacroType([eventType, drawSubtype])
            ]));
        DefineGlobalFunction(gameContext, "genouka_event_perform");

        // genouka_event_perform wraps event_perform, passing its arguments straight through
        provider.AddCode("genouka_event_perform", """
            push.v argument.argument1
            push.v argument.argument0
            call.i event_perform 2
            popz.v
            """, gameContext);

        DecompileSettings settings = new()
        {
            CreateEnumDeclarations = false
        };

        TestUtil.VerifyDecompileResult(
            """
            push.i 0
            push.i 3
            call.i genouka_event_perform 2
            popz.v
            """,
            """
            genouka_event_perform(ev_step, ev_step_normal);
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestInferThroughBuiltinWithComplexUnionArgTypes()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);

        // Replicate the exact structure of the "event_perform" registration in gamemaker.json,
        // which is a union of multiple argument variants with conditional matches.
        ConstantsMacroType eventType = new(new Dictionary<int, string>
        {
            [0] = "ev_create", [1] = "ev_destroy", [2] = "ev_alarm", [3] = "ev_step",
            [4] = "ev_collision", [5] = "ev_keyboard", [6] = "ev_mouse", [7] = "ev_other",
            [8] = "ev_draw", [9] = "ev_keypress", [10] = "ev_keyrelease", [11] = "ev_trigger",
            [12] = "ev_cleanup", [13] = "ev_gesture", [14] = "ev_pre_create"
        });
        ConstantsMacroType stepSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_step_normal", [1] = "ev_step_begin", [2] = "ev_step_end"
        });
        ConstantsMacroType drawSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_draw_normal", [64] = "ev_gui", [72] = "ev_draw_begin", [73] = "ev_draw_end"
        });
        ConstantsMacroType mouseSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_left_button", [1] = "ev_right_button", [2] = "ev_middle_button",
            [3] = "ev_no_button", [4] = "ev_left_press", [5] = "ev_right_press", [6] = "ev_middle_press"
        });
        ConstantsMacroType otherSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_outside", [1] = "ev_boundary"
        });
        ConstantsMacroType gestureSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_gesture_tap", [1] = "ev_gesture_double_tap"
        });
        ConstantsMacroType virtualKey = new(new Dictionary<int, string>
        {
            [0] = "vk_nokey"
        });

        IMacroType match3 = new MatchMacroType(null, "Integer", "3");
        IMacroType match8 = new MatchMacroType(null, "Integer", "8");
        IMacroType match6 = new MatchMacroType(null, "Integer", "6");
        IMacroType match7 = new MatchMacroType(null, "Integer", "7");
        IMacroType match13 = new MatchMacroType(null, "Integer", "13");
        IMacroType match4 = new MatchMacroType(null, "Integer", "4");
        IMacroType matchKey = new UnionMacroType(
        [
            new MatchMacroType(null, "Integer", "5"),
            new MatchMacroType(null, "Integer", "9"),
            new MatchMacroType(null, "Integer", "10")
        ]);

        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("event_perform",
            new UnionMacroType(
            [
                new FunctionArgsMacroType([new IntersectMacroType([match3, eventType]), stepSubtype]),
                new FunctionArgsMacroType([new IntersectMacroType([match8, eventType]), drawSubtype]),
                new FunctionArgsMacroType([new IntersectMacroType([match6, eventType]), mouseSubtype]),
                new FunctionArgsMacroType([new IntersectMacroType([match7, eventType]), otherSubtype]),
                new FunctionArgsMacroType([new IntersectMacroType([match13, eventType]), gestureSubtype]),
                new FunctionArgsMacroType([new IntersectMacroType([match4, eventType]), gameContext.GameSpecificRegistry.FindType("Asset.Object")]),
                new FunctionArgsMacroType([new IntersectMacroType([matchKey, eventType]), virtualKey]),
                new FunctionArgsMacroType([eventType, null])
            ]));
        DefineGlobalFunction(gameContext, "genouka_event_perform");

        provider.AddCode("genouka_event_perform", """
            push.v argument.argument1
            push.v argument.argument0
            call.i event_perform 2
            popz.v
            """, gameContext);

        DecompileSettings settings = new()
        {
            CreateEnumDeclarations = false
        };

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 4
            conv.i.v
            pushi.e 6
            conv.i.v
            call.i genouka_event_perform 2
            popz.v
            """,
            """
            genouka_event_perform(ev_mouse, ev_left_press);
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestInferWithInflatedDeclaredArgumentCount()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);

        ConstantsMacroType eventType = new(new Dictionary<int, string>
        {
            [0] = "ev_create", [1] = "ev_destroy", [2] = "ev_alarm", [3] = "ev_step",
            [4] = "ev_collision", [5] = "ev_keyboard", [6] = "ev_mouse", [7] = "ev_other",
            [8] = "ev_draw", [9] = "ev_keypress", [10] = "ev_keyrelease", [11] = "ev_trigger",
            [12] = "ev_cleanup", [13] = "ev_gesture", [14] = "ev_pre_create"
        });
        ConstantsMacroType mouseSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_left_button", [1] = "ev_right_button", [2] = "ev_middle_button",
            [3] = "ev_no_button", [4] = "ev_left_press", [5] = "ev_right_press", [6] = "ev_middle_press"
        });
        IMacroType match6 = new MatchMacroType(null, "Integer", "6");

        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("event_perform",
            new UnionMacroType(
            [
                new FunctionArgsMacroType([new IntersectMacroType([match6, eventType]), mouseSubtype]),
                new FunctionArgsMacroType([eventType, null])
            ]));
        DefineGlobalFunction(gameContext, "genouka_event_perform");

        // Some games store a large (inflated) ArgumentsCount on script code entries,
        // even though only a couple of arguments are ever actually referenced.
        provider.AddCode("genouka_event_perform", """
            >root (locals=0, args=16)
            push.v argument.argument1
            push.v argument.argument0
            call.i event_perform 2
            popz.v
            """, gameContext);

        DecompileSettings settings = new()
        {
            CreateEnumDeclarations = false
        };

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 4
            conv.i.v
            pushi.e 6
            conv.i.v
            call.i genouka_event_perform 2
            popz.v
            """,
            """
            genouka_event_perform(ev_mouse, ev_left_press);
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestInferThroughBuiltinWithPreGMLv2ArgumentVariables()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);

        // Replicate a pre-2.3 game (e.g. MVZ2), where arguments are regular self variables named "argument0", etc.,
        // rather than using the "argument" instance type. This was reported to break inference for genouka_event_perform.
        ConstantsMacroType eventType = new(new Dictionary<int, string>
        {
            [0] = "ev_create", [1] = "ev_destroy", [2] = "ev_alarm", [3] = "ev_step",
            [4] = "ev_collision", [5] = "ev_keyboard", [6] = "ev_mouse", [7] = "ev_other",
            [8] = "ev_draw", [9] = "ev_keypress", [10] = "ev_keyrelease", [11] = "ev_trigger",
            [12] = "ev_cleanup", [13] = "ev_gesture", [14] = "ev_pre_create"
        });
        ConstantsMacroType mouseSubtype = new(new Dictionary<int, string>
        {
            [0] = "ev_left_button", [1] = "ev_right_button", [2] = "ev_middle_button",
            [3] = "ev_no_button", [4] = "ev_left_press", [5] = "ev_right_press", [6] = "ev_middle_press"
        });
        IMacroType match6 = new MatchMacroType(null, "Integer", "6");

        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("event_perform",
            new UnionMacroType(
            [
                new FunctionArgsMacroType([new IntersectMacroType([match6, eventType]), mouseSubtype]),
                new FunctionArgsMacroType([eventType, null])
            ]));
        DefineGlobalFunction(gameContext, "genouka_event_perform");

        provider.AddCode("genouka_event_perform", """
            push.v self.argument1
            push.v self.argument0
            call.i event_perform 2
            popz.v
            """, gameContext);

        DecompileSettings settings = new()
        {
            CreateEnumDeclarations = false
        };

        TestUtil.VerifyDecompileResult(
            """
            pushi.e 4
            conv.i.v
            pushi.e 6
            conv.i.v
            call.i genouka_event_perform 2
            popz.v
            """,
            """
            genouka_event_perform(ev_mouse, ev_left_press);
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestReversePropagateVariableTypeDisabledBySetting()
    {
        GameContextMock gameContext = CreateGameContext(out _);
        DefineGlobalFunction(gameContext, "scr_set_color");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("scr_set_color",
            new FunctionArgsMacroType([new ColorMacroType()]));

        GMCode code = VMAssembly.ParseAssemblyFromLines(
            """
            push.v argument.argument0
            pop.v.i self.color
            pushi.e 255
            pop.v.i self.color
            """.Split('\n'), gameContext, "gml_Script_scr_set_color");

        // With inline propagation disabled, the literal must not be expanded
        DecompileSettings settings = new()
        {
            InlinePropagateVariableTypes = false
        };
        string result = new DecompileContext(gameContext, code, settings).DecompileToString().Trim();
        Assert.Equal("color = argument0;\ncolor = 255;", result);
    }

    [Fact]
    public void TestInferReturnTypeFromCallSiteUsage()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 441, "spr_card441");
        gameContext.DefineMockAsset(AssetType.Sprite, 440, "spr_card440");
        DefineGlobalFunction(gameContext, "scr_get_sprite");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_sprite",
            new FunctionArgsMacroType(
            [
                gameContext.GameSpecificRegistry.FindType("Asset.Sprite"),
                null,
                null,
                null
            ]));

        // The function assigns sprite indices to a variable and returns it. Its return type is
        // only known from how the function is used at a call site.
        provider.AddCode("scr_get_sprite", """
            pushi.e 441
            pop.v.i local.spr
            pushi.e 440
            pop.v.i local.spr
            push.v local.spr
            ret.v
            """, gameContext, "gml_Script_scr_get_sprite");

        // (a) Decompiling a caller that uses the function at a typed (sprite) argument position
        // registers the function's return type
        TestUtil.VerifyDecompileResult(
            """
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 5
            call.i scr_get_sprite 1
            call.i draw_sprite 4
            popz.v
            """,
            """
            draw_sprite(scr_get_sprite(5), 0, 0, 0);
            """,
            gameContext
        );

        // (b) The function body now expands the literals assigned to the returned variable
        IGMCode bodyCode = provider.GetFunctionCode("scr_get_sprite")!;
        string bodyResult = new DecompileContext(gameContext, bodyCode, new DecompileSettings()).DecompileToString().Trim();
        Assert.Equal("var spr = spr_card441;\nspr = spr_card440;\nreturn spr;", bodyResult);
    }

    [Fact]
    public void TestInferReturnTypeFromEqualityComparison()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 12, "sprWatered");
        gameContext.DefineMockAsset(AssetType.Sprite, 441, "spr_card441");
        DefineGlobalFunction(gameContext, "scr_card_type");

        // The function returns a sprite index stored in a variable; its return type is inferred
        // from a caller comparing the result against a sprite asset reference.
        provider.AddCode("scr_card_type", """
            pushi.e 441
            pop.v.i local.spr
            push.v local.spr
            ret.v
            """, gameContext, "gml_Script_scr_card_type");

        // (a) Decompiling a caller with "scr_card_type(...) == sprWatered" registers the return type
        TestUtil.VerifyDecompileResult(
            """
            :[0]
            pushi.e 5
            call.i scr_card_type 1
            pushref.i 12 Sprite
            cmp.i.v EQ
            bf [1]

            :[1]
            """,
            """
            if (scr_card_type(5) == sprWatered)
            {
            }
            """,
            gameContext
        );

        // (b) The function body now expands the returned sprite index
        IGMCode bodyCode = provider.GetFunctionCode("scr_card_type")!;
        string bodyResult = new DecompileContext(gameContext, bodyCode, new DecompileSettings()).DecompileToString().Trim();
        Assert.Equal("var spr = spr_card441;\nreturn spr;", bodyResult);
    }

    [Fact]
    public void TestNoReturnTypeInferenceForBuiltinFunctions()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.DefineMockAsset(AssetType.Sprite, 1, "spr_enemy");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_sprite",
            new FunctionArgsMacroType(
            [
                gameContext.GameSpecificRegistry.FindType("Asset.Sprite"),
                null,
                null,
                null
            ]));

        // "random" is not a user function (not registered with the provider), so its return type
        // must NOT be inferred when used at a typed argument position.
        TestUtil.VerifyDecompileResult(
            """
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 100
            call.i random 1
            call.i draw_sprite 4
            popz.v
            """,
            """
            draw_sprite(random(100), 0, 0, 0);
            """,
            gameContext
        );

        // Only the return type inference for builtins must be skipped
        Assert.Null(((GlobalMacroTypeResolver)gameContext.GameSpecificRegistry.MacroResolver).
            GlobalNames.TryGetFunctionReturnType("random"));
    }

    [Fact]
    public void TestNoReturnTypeInferenceForBuiltinsFromLinkedScript()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        DefineGlobalFunction(gameContext, "dialogs_replay");

        // dialogs_replay's return type is already known (e.g. inferred from call-site usage). Its
        // body assigns a builtin call's result to a local variable, and returns a builtin call
        // directly. These builtins must NOT inherit dialogs_replay's return type, otherwise their
        // results would be treated as sprites everywhere, expanding unrelated integer literals
        // (e.g. "string_length(x) - 1" becoming "string_length(x) - sprNightUse").
        provider.AddCode("dialogs_replay", """
            push.v argument.argument0
            call.i string_length 1
            pop.v.i local.len
            push.v local.len
            pushi.e 2
            conv.i.v
            push.v argument.argument0
            call.i string_copy 3
            ret.v
            """, gameContext, "gml_Script_dialogs_replay");

        GlobalMacroTypeResolver resolver = (GlobalMacroTypeResolver)gameContext.GameSpecificRegistry.MacroResolver;
        resolver.GlobalNames.DefineFunctionReturnType("dialogs_replay", new AssetMacroType(AssetType.Sprite));

        // Decompiling the body must not register the builtins as returning sprites
        IGMCode code = provider.GetFunctionCode("dialogs_replay")!;
        _ = new DecompileContext(gameContext, code, new DecompileSettings()).DecompileToString();

        Assert.Null(resolver.GlobalNames.TryGetFunctionReturnType("string_length"));
        Assert.Null(resolver.GlobalNames.TryGetFunctionReturnType("string_copy"));
    }

    [Fact]
    public void TestInferReturnTypeDisabledBySetting()
    {
        GameContextMock gameContext = CreateGameContext(out _);
        gameContext.DefineMockAsset(AssetType.Sprite, 441, "spr_card441");
        gameContext.DefineMockAsset(AssetType.Sprite, 440, "spr_card440");

        DecompileSettings settings = new()
        {
            InferFunctionArgumentTypes = false
        };
        TestUtil.VerifyDecompileResult(
            """
            pushi.e 441
            pop.v.i local.spr
            pushi.e 440
            pop.v.i local.spr
            push.v local.spr
            ret.v
            """,
            """
            var spr = 441;
            spr = 440;
            return spr;
            """,
            gameContext,
            settings
        );
    }

    [Fact]
    public void TestInferVariableTypeFromUsage()
    {
        GameContextMock gameContext = CreateGameContext(out _);
        gameContext.DefineMockAsset(AssetType.Sprite, 496, "spr_fire_small");
        gameContext.DefineMockAsset(AssetType.Sprite, 518, "spr_fire_cursed");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_sprite_ext",
            new FunctionArgsMacroType(
            [
                gameContext.GameSpecificRegistry.FindType("Asset.Sprite"),
                null, null, null, null, null, null, null, null
            ]));

        // A local variable is assigned sprite-index literals and used at a sprite-typed argument
        // position; its type is inferred from that usage, expanding the literals. This runs even on
        // a direct decompile of the script body (no caller triggering inference first).
        TestUtil.VerifyDecompileResult(
            """
            pushi.e 496
            pop.v.i local.fire_spr
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            push.v local.fire_spr
            call.i draw_sprite_ext 9
            popz.v
            """,
            """
            var fire_spr = spr_fire_small;
            draw_sprite_ext(fire_spr, 0, 0, 0, 0, 0, 0, 0, 0);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferVariableTypeFromUsageConflictsAreSkipped()
    {
        GameContextMock gameContext = CreateGameContext(out _);
        gameContext.DefineMockAsset(AssetType.Sprite, 496, "spr_fire_small");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_sprite_ext",
            new FunctionArgsMacroType(
            [
                gameContext.GameSpecificRegistry.FindType("Asset.Sprite"),
                null, null, null, null, null, null, null, null
            ]));
        gameContext.DefineMockAsset(AssetType.Object, 12, "obj_enemy");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("instance_create",
            new FunctionArgsMacroType([null, null, gameContext.GameSpecificRegistry.FindType("Asset.Object")]));

        // The variable is used at BOTH a sprite-typed and an object-typed position; with conflicting
        // usage types, no type is inferred and the literal must NOT be expanded.
        TestUtil.VerifyDecompileResult(
            """
            pushi.e 496
            pop.v.i local.thing
            push.v local.thing
            pushi.e 64
            conv.i.v
            pushi.e 0
            conv.i.v
            call.i instance_create 3
            popz.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            push.v local.thing
            call.i draw_sprite_ext 9
            popz.v
            """,
            """
            var thing = 496;
            instance_create(0, 64, thing);
            draw_sprite_ext(thing, 0, 0, 0, 0, 0, 0, 0, 0);
            """,
            gameContext
        );
    }

    [Fact]
    public void TestInferInstanceVariableTypeFromOtherObjectEvent()
    {
        GameContextMock gameContext = CreateGameContext(out _);
        gameContext.DefineMockAsset(AssetType.Sprite, 14, "spr_lilypad");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_sprite_ext",
            new FunctionArgsMacroType(
            [
                gameContext.GameSpecificRegistry.FindType("Asset.Sprite"),
                null, null, null, null, null, null, null, null
            ]));

        // The Draw event uses the instance variable "spr" at a sprite-typed argument position,
        // inferring its type for the whole object
        IGMCode drawCode = VMAssembly.ParseAssemblyFromLines(
            """
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            pushi.e 0
            conv.i.v
            push.v self.spr
            call.i draw_sprite_ext 9
            popz.v
            """.Split('\n'), gameContext, "gml_Object_ClearItem_Draw_0");
        string drawResult = new DecompileContext(gameContext, drawCode, new DecompileSettings()).DecompileToString().Trim();
        Assert.Equal("draw_sprite_ext(spr, 0, 0, 0, 0, 0, 0, 0, 0);", drawResult);

        // The Create event assigns the instance variable a literal, which is now expanded using the
        // type inferred from the Draw event of the same object
        IGMCode createCode = VMAssembly.ParseAssemblyFromLines(
            """
            pushi.e 14
            pop.v.i self.spr
            """.Split('\n'), gameContext, "gml_Object_ClearItem_Create_0");
        string createResult = new DecompileContext(gameContext, createCode, new DecompileSettings()).DecompileToString().Trim();
        Assert.Equal("spr = spr_lilypad;", createResult);
    }

    [Fact]
    public void TestReversePropagateVariableTypeFromRegisteredArgs()
    {
        GameContextMock gameContext = CreateGameContext(out _);
        DefineGlobalFunction(gameContext, "scr_set_color");
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("scr_set_color",
            new FunctionArgsMacroType([new ColorMacroType()]));

        // The script body assigns its (color-typed) argument to a variable, then assigns a literal
        // to that variable. Because the argument type is registered, the literal should be expanded.
        GMCode code = VMAssembly.ParseAssemblyFromLines(
            """
            push.v argument.argument0
            pop.v.i self.color
            pushi.e 255
            pop.v.i self.color
            """.Split('\n'), gameContext, "gml_Script_scr_set_color");

        string result = new DecompileContext(gameContext, code, new DecompileSettings()).DecompileToString().Trim();

        Assert.Equal("color = argument0;\ncolor = c_red;", result);
    }

    [Fact]
    public void TestReversePropagateVariableTypeFromInferredArgs()
    {
        GameContextMock gameContext = CreateGameContext(out MockFunctionArgTypeProvider provider);
        gameContext.GameSpecificRegistry.MacroResolver.GlobalNames.DefineFunctionArgumentsType("draw_set_color",
            new FunctionArgsMacroType([new ColorMacroType()]));
        DefineGlobalFunction(gameContext, "scr_set_color");

        // Body: local receives the argument, is used in a typed builtin call (inferring the argument
        // type), and is also assigned a literal that should be expanded once the type is known.
        provider.AddCode("scr_set_color", """
            push.v argument.argument0
            pop.v.i local.c
            pushi.e 255
            pop.v.i local.c
            push.v local.c
            call.i draw_set_color 1
            popz.v
            """, gameContext);

        // (a) Decompiling a caller triggers inference, which registers the variable's type
        TestUtil.VerifyDecompileResult(
            """
            pushi.e 255
            call.i scr_set_color 1
            popz.v
            """,
            """
            scr_set_color(c_red);
            """,
            gameContext
        );

        // (b) The script body now expands the literal assigned to the variable
        IGMCode bodyCode = provider.GetFunctionCode("scr_set_color");
        string bodyResult = new DecompileContext(gameContext, bodyCode, new DecompileSettings()).DecompileToString().Trim();
        Assert.Equal("var c = argument0;\nc = c_red;\ndraw_set_color(c);", bodyResult);
    }
}
