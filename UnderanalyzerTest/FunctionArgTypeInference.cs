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

        public void AddCode(string functionName, string assembly, GameContextMock gameContext)
        {
            _codes[functionName] = TestUtil.GetCode(assembly, gameContext);
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
}
