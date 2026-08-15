/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using Underanalyzer;
using Underanalyzer.Mock;

namespace UnderanalyzerTest;

public class RoundTrip
{
    [Fact]
    public void TestEmpty()
    {
        TestUtil.VerifyRoundTrip("");
    }

    [Fact]
    public void TestSimpleReturns()
    {
        TestUtil.VerifyRoundTrip(
            """
            return 123;
            return true;
            return false;
            return "test string";
            return 123123123123123123;
            """
        );
    }

    [Fact]
    public void TestIf()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                if (d)
                {
                }
                else
                {
                }
            }
            else if (b)
            {
                if (c)
                {
                }
            }
            """
        );
    }

    [Fact]
    public void TestWhileIfContinueBreak()
    {
        TestUtil.VerifyRoundTrip(
            """
            top = 1;
            while (a)
            {
                if (b)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }
            bottom = 2;
            """
        );
    }

    [Fact]
    public void TestForIfContinueBreak()
    {
        TestUtil.VerifyRoundTrip(
            """
            top = 1;
            for (var i = 0; i < 10; i++)
            {
                if (a)
                {
                    break;
                }
                continue;
            }
            bottom = 2;
            """
        );
    }

    [Fact]
    public void TestDoUntilContinueBreak()
    {
        TestUtil.VerifyRoundTrip(
            """
            top = 1;
            do
            {
                if (a)
                {
                    break;
                }
                continue;
            }
            until (b);
            bottom = 2;
            """
        );
    }

    [Fact]
    public void TestConditional()
    {
        TestUtil.VerifyRoundTrip(
            """
            basic = a ? b : c;
            basic_cast = a ? 1 : 2;
            nested = (a ? b : c) ? d : (e ? f : g);
            """
        );
    }

    [Fact]
    public void TestBinaryAndUnary()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = b + (c - d);
            a = (b + c) - d;
            a = (b * c) / d;
            a = (b div c) % d;
            a = b + c + d + e;
            a = b - c - d - 5;
            a = b * c * 5 * e;
            a = b && c && d;
            a = b || c || d;
            a = (b && c) || (d && e);
            a = b << (c >> d);
            a = b ^ (c & (d | e));
            a = !b + ~b + -b;
            """
        );
    }

    [Fact]
    public void TestTypesOld()
    {
        GameContextMock mock = new()
        {
            UsingAssetReferences = false,
            UsingTypedBooleans = false
        };
        mock.DefineMockAsset(AssetType.Sprite, 8, "spr_test");

        TestUtil.VerifyRoundTrip(
            """
            a = spr_test;
            b = true;
            """,
            """
            a = 8;
            b = 1;
            """,
            false, mock
        );
    }

    [Fact]
    public void TestTypesNew()
    {
        GameContextMock mock = new()
        {
            UsingAssetReferences = true,
            UsingTypedBooleans = true
        };
        mock.DefineMockAsset(AssetType.Sprite, 8, "spr_test");

        TestUtil.VerifyRoundTrip(
            """
            a = spr_test;
            b = true;
            """,
            false, mock
        );
    }

    [Fact]
    public void TestWithContinueBreakExit()
    {
        GameContextMock mock = new()
        {
            UsingAssetReferences = true,
            UsingTypedBooleans = true
        };
        mock.DefineMockAsset(AssetType.Object, 8, "obj_test");

        TestUtil.VerifyRoundTrip(
            """
            with (-3)
            {
                a = 123;
                return 0;
            }
            with (abc)
            {
                b = 456;
                exit;
            }
            with (obj_test)
            {
                if (c)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }
            """,
            false, mock
        );
    }

    [Fact]
    public void TestRepeatContinueBreakExit()
    {
        TestUtil.VerifyRoundTrip(
            """
            repeat (123)
            {
                if (a)
                {
                    continue;
                }
                else if (b)
                {
                    exit;
                }
                else
                {
                    break;
                }
            }
            """
        );
    }

    [Fact]
    public void TestSwitchContinueBreakExit()
    {
        string testCode =
            """
            repeat (a)
            {
                switch (b)
                {
                    case 123:
                        c = 0;
                        break;
                    case 456:
                        d = 0;
                        break;
                        return 123;
                    case 789:
                        continue;
                    default:
                        exit;
                }
            }
            """;
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingExtraRepeatInstruction = true
        });
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingExtraRepeatInstruction = false
        });
    }

    [Fact]
    public void TestAssignments()
    {
        string testCode =
            """
            a = 0;
            a.b = 0;
            a.b.c = 0;
            a[0] = 1;
            a.b[0] = 1;
            a.b.c[0] = 1;
            a[0].b[1] = 2;
            a[0].b[1].c = 2;
            global.a = 0;
            global.a.b = 0;
            global.a.b.c = 0;
            global.a[0] = 1;
            global.a.b[0] = 1;
            global.a.b.c[0] = 1;
            global.a[0].b[1] = 2;
            global.a[0].b[1].c = 2;
            """;
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = false
        });
    }

    [Fact]
    public void TestAssignments2dArray()
    {
        TestUtil.VerifyRoundTrip(
            """
            a[0, 1] = 2;
            a.b[0, 1] = 2;
            a.b.c[0, 1] = 2;
            a[0, 1].b[0, 1] = 2;
            a[0, 1].b[0, 1].c = 2;
            global.a[0, 1] = 2;
            global.a.b[0, 1] = 2;
            global.a.b.c[0, 1] = 2;
            global.a[0, 1].b[0, 1] = 2;
            global.a[0, 1].b[0, 1].c = 2;
            """,
            false, new()
            {
                UsingGMLv2 = false
            }
        );
    }

    [Fact]
    public void TestCompoundAssignments()
    {
        string testCode =
            """
            a += 1;
            a.b += 1;
            a.b.c += 1;
            a[0] += 1;
            a.b[0] += 1;
            a.b.c[0] += 1;
            a[0].b[1] += 2;
            a[0].b[1].c += 2;
            global.a += 1;
            global.a.b += 1;
            global.a.b.c += 1;
            global.a[0] += 1;
            global.a.b[0] += 1;
            global.a.b.c[0] += 1;
            global.a[0].b[1] += 2;
            global.a[0].b[1].c += 2;
            a.b = a.b + 1;
            a.b.c = a.b.c + 1;
            a[0] = a[0] + 1;
            a.b[0] = a.b[0] + 1;
            a.b.c[0] = a.b.c[0] + 1;
            a[0].b[1] = a[0].b[1] + 2;
            a[0].b[1].c = a[0].b[1].c + 2;
            global.a = global.a + 1;
            global.a.b = global.a.b + 1;
            global.a.b.c = global.a.b.c + 1;
            global.a[0] = global.a[0] + 1;
            global.a.b[0] = global.a.b[0] + 1;
            global.a.b.c[0] = global.a.b.c[0] + 1;
            global.a[0].b[1] = global.a[0].b[1] + 2;
            global.a[0].b[1].c = global.a[0].b[1].c + 2;
            """;
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = false
        });
    }

    [Fact]
    public void TestCompoundAssignments2dArray()
    {
        TestUtil.VerifyRoundTrip(
            """
            a[0, 1] += 2;
            a.b[0, 1] += 2;
            a.b.c[0, 1] += 2;
            a[0, 1].b[0, 1] += 2;
            a[0, 1].b[0, 1].c += 2;
            global.a[0, 1] += 2;
            global.a.b[0, 1] += 2;
            global.a.b.c[0, 1] += 2;
            global.a[0, 1].b[0, 1] += 2;
            global.a[0, 1].b[0, 1].c += 2;
            a[0, 1] = a[0, 1] + 2;
            a.b[0, 1] = a.b[0, 1] + 2;
            a.b.c[0, 1] = a.b.c[0, 1] + 2;
            a[0, 1].b[0, 1] = a[0, 1].b[0, 1] + 2;
            a[0, 1].b[0, 1].c = a[0, 1].b[0, 1].c + 2;
            global.a[0, 1] = global.a[0, 1] + 2;
            global.a.b[0, 1] = global.a.b[0, 1] + 2;
            global.a.b.c[0, 1] = global.a.b.c[0, 1] + 2;
            global.a[0, 1].b[0, 1] = global.a[0, 1].b[0, 1] + 2;
            global.a[0, 1].b[0, 1].c = global.a[0, 1].b[0, 1].c + 2;
            """,
            false, new()
            {
                UsingGMLv2 = false
            }
        );
    }

    [Fact]
    public void TestPrefixPostfix()
    {
        string testCode =
            """
            a++;
            a.b++;
            a.b.c++;
            a[0]++;
            a.b[0]++;
            a.b.c[0]++;
            a[0].b[1]++;
            a[0].b[1].c++;
            global.a++;
            global.a.b++;
            global.a.b.c++;
            global.a[0]++;
            global.a.b[0]++;
            global.a.b.c[0]++;
            global.a[0].b[1]++;
            global.a[0].b[1].c++;
            """;
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = false
        });
    }

    [Fact]
    public void TestPrefixExpr()
    {
        string testCode =
            """
            d = ++a;
            d = ++a.b;
            d = ++a.b.c;
            d = ++a[0];
            d = ++a.b[0];
            d = ++a.b.c[0];
            d = ++a[0].b[1];
            d = ++a[0].b[1].c;
            d = ++global.a;
            d = ++global.a.b;
            d = ++global.a.b.c;
            d = ++global.a[0];
            d = ++global.a.b[0];
            d = ++global.a.b.c[0];
            d = ++global.a[0].b[1];
            d = ++global.a[0].b[1].c;
            """;
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = false
        });
    }

    [Fact]
    public void TestPostfixExpr()
    {
        string testCode =
            """
            d = a++;
            d = a.b++;
            d = a.b.c++;
            d = a[0]++;
            d = a.b[0]++;
            d = a.b.c[0]++;
            d = a[0].b[1]++;
            d = a[0].b[1].c++;
            d = global.a++;
            d = global.a.b++;
            d = global.a.b.c++;
            d = global.a[0]++;
            d = global.a.b[0]++;
            d = global.a.b.c[0]++;
            d = global.a[0].b[1]++;
            d = global.a[0].b[1].c++;
            """;
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip(testCode, false, new()
        {
            UsingGMLv2 = false
        });
    }

    [Fact]
    public void TestPrefixExpr2dArray()
    {
        TestUtil.VerifyRoundTrip(
            """
            d = ++a[0, 1];
            d = ++a.b[0, 1];
            d = ++a.b.c[0, 1];
            d = ++a[0, 1].b[0, 1];
            d = ++a[0, 1].b[0, 1].c;
            d = ++global.a[0, 1];
            d = ++global.a.b[0, 1];
            d = ++global.a.b.c[0, 1];
            d = ++global.a[0, 1].b[0, 1];
            d = ++global.a[0, 1].b[0, 1].c;
            """,
            false, new()
            {
                UsingGMLv2 = false
            }
        );
    }

    [Fact]
    public void TestPostfixExpr2dArray()
    {
        TestUtil.VerifyRoundTrip(
            """
            d = a[0, 1]++;
            d = a.b[0, 1]++;
            d = a.b.c[0, 1]++;
            d = a[0, 1].b[0, 1]++;
            d = a[0, 1].b[0, 1].c++;
            d = global.a[0, 1]++;
            d = global.a.b[0, 1]++;
            d = global.a.b.c[0, 1]++;
            d = global.a[0, 1].b[0, 1]++;
            d = global.a[0, 1].b[0, 1].c++;
            """,
            false, new()
            {
                UsingGMLv2 = false
            }
        );
    }

    [Fact]
    public void TestMultiArrays()
    {
        TestUtil.VerifyRoundTrip(
            """
            a[0] = 1;
            a[0][1] = 2;
            a[0][1][2] = 3;
            a[0][1][2][3] = 4;
            a.b[0][1][2] = 3;
            a.b[0][1].c[2][3] = 4;
            a.b[0][1].c[2][3].d = 4;
            global.a[0] = 1;
            global.a[0][1] = 2;
            global.a[0][1][2] = 3;
            global.a[0][1][2][3] = 4;
            global.a.b[0][1][2] = 3;
            global.a.b[0][1].c[2][3] = 4;
            global.a.b[0][1].c[2][3].d = 4;
            e = a[0];
            e = a[0][1];
            e = a[0][1][2];
            e = a[0][1][2][3];
            e = a.b[0][1][2];
            e = a.b[0][1].c[2][3];
            e = a.b[0][1].c[2][3].d;
            """
        );
    }

    [Fact]
    public void TestMultiArraysPrePost()
    {
        TestUtil.VerifyRoundTrip(
            """
            a[0]++;
            a[0][1]++;
            a[0][1][2]++;
            a[0][1][2][3]++;
            a.b[0][1][2]++;
            a.b[0][1].c[2][3]++;
            a.b[0][1].c[2][3].d++;
            global.a[0]++;
            global.a[0][1]++;
            global.a[0][1][2]++;
            global.a[0][1][2][3]++;
            global.a.b[0][1][2]++;
            global.a.b[0][1].c[2][3]++;
            global.a.b[0][1].c[2][3].d++;
            e = a[0]++;
            e = a[0][1]++;
            e = a[0][1][2]++;
            e = a[0][1][2][3]++;
            e = a.b[0][1][2]++;
            e = a.b[0][1].c[2][3]++;
            e = a.b[0][1].c[2][3].d++;
            e = ++a[0];
            e = ++a[0][1];
            e = ++a[0][1][2];
            e = ++a[0][1][2][3];
            e = ++a.b[0][1][2];
            e = ++a.b[0][1].c[2][3];
            e = ++a.b[0][1].c[2][3].d;
            """
        );
    }

    [Fact]
    public void TestMultiArraysCompound()
    {
        TestUtil.VerifyRoundTrip(
            """
            a[0] += 1;
            a[0][1] += 2;
            a[0][1][2] += 3;
            a[0][1][2][3] += 4;
            a.b[0][1][2] += 3;
            a.b[0][1].c[2][3] += 4;
            a.b[0][1].c[2][3].d += 4;
            global.a[0] += 1;
            global.a[0][1] += 2;
            global.a[0][1][2] += 3;
            global.a[0][1][2][3] += 4;
            global.a.b[0][1][2] += 3;
            global.a.b[0][1].c[2][3] += 4;
            global.a.b[0][1].c[2][3].d += 4;
            """
        );
    }

    [Fact]
    public void TestVariableCalls()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false
        };
        ((BuiltinsMock)gameContext.Builtins).BuiltinFunctions["show_debug_message"] =
            new("show_debug_message", 1, 1);
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip(
            """
            var d;
            show_debug_message("a");
            test("a");
            self.test("a");
            other.test("a");
            global.test("a");
            a.b("a");
            a.b.c("a");
            a[0].b("a");
            d("a");
            a.d("a");
            d.a("a");
            obj_test.a("a");
            obj_test.a.b("a");
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestVariableCallsAssetRefs()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = true,
            UsingSelfToBuiltin = true
        };
        ((BuiltinsMock)gameContext.Builtins).BuiltinFunctions["show_debug_message"] =
            new("show_debug_message", 1, 1);
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip(
            """
            var d;
            show_debug_message("a");
            test("a");
            self.test("a");
            other.test("a");
            global.test("a");
            a.b("a");
            a.b.c("a");
            a[0].b("a");
            d("a");
            a.d("a");
            d.a("a");
            obj_test.a("a");
            obj_test.a.b("a");
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestRepeatVariableCalls()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false,
            UsingSelfToBuiltin = true
        };
        ((BuiltinsMock)gameContext.Builtins).BuiltinFunctions["show_debug_message"] =
            new("show_debug_message", 1, 1);
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip(
            """
            a(1)(2)(3)(4)(5);
            a.b(1)(2)(3);
            a(1).b(2);
            self.a(1).b(2);
            a(1).b(2)(3)(4);
            a(1).b(2)(3).c(4);
            obj_test.b(1)(2)(3);
            obj_test.b.c(1)(2)(3);
            obj_test.a(1).b(2);
            obj_test.a(1).b(2)(3)(4);
            obj_test.a(1).b(2)(3).c(4);
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestRepeatVariableCallsAssetRefs()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = true,
            UsingSelfToBuiltin = true
        };
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip(
            """
            a(1)(2)(3)(4)(5);
            a.b(1)(2)(3);
            a(1).b(2);
            self.a(1).b(2);
            a(1).b(2)(3)(4);
            a(1).b(2)(3).c(4);
            obj_test.b(1)(2)(3);
            obj_test.b.c(1)(2)(3);
            obj_test.a(1).b(2);
            obj_test.a(1).b(2)(3)(4);
            obj_test.a(1).b(2)(3).c(4);
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestVariableCallsArrays()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false
        };
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip(
            """
            a[0](1);
            self.a[0](1);
            global.a[0](1);
            obj_test.a[0](1);
            a[0].b(1);
            a[0].b[1](2);
            a[0](1).b[2](3);
            a.b[0](1).c.d[2](3);
            a.b[0].c(1).d.e[2].f(3);
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestVariableCallsArraysMulti()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false
        };
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip(
            """
            a[0][1](2);
            self.a[0][1](2);
            global.a[0][1](2);
            obj_test.a[0][1](2);
            a.b[0][1](2);
            a[0][1].b(2);
            a[0][1].b[1][2](3);
            a[0][1](2).b[2][3](4);
            a.b[0][1](2).c.d[3][4](5);
            a.b[0][1].c(2).d.e[3][4].f(5);
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestFunctionDecls()
    {
        TestUtil.VerifyRoundTrip(
            """
            abc();
            
            function abc()
            {
            }

            anon = function()
            {
            };

            struct = 
            {
                a: 123,
                
                b: function()
                {
                },
                
                c: [1, 2, 3, 4, 5],
                d: 
                {
                    e: 3.14
                }
            };

            function Parent(arg0) constructor
            {
                a = arg0;
            }

            function Child(arg0, arg1 = 789) : Parent(arg0) constructor
            {
                static testFunc = function()
                {
                };
                
                b = arg1;
            }
            """
        );
    }

    [Fact]
    public void TestNewObject()
    {
        TestUtil.VerifyRoundTrip(
            """
            function Test() constructor
            {
            }

            new Test();
            new self.Test();
            new global.Test();
            a = new Test();
            b = new VariableCall();
            b = new self.VariableCall();
            b = new global.VariableCall();
            new Complex.Variable.Call(123, 456);
            """,
            """
            function Test() constructor
            {
            }
            
            new Test();
            new Test();
            new Test();
            a = new Test();
            b = new VariableCall();
            b = new self.VariableCall();
            b = new global.VariableCall();
            new Complex.Variable.Call(123, 456);
            """
        );
    }

    [Fact]
    public void TestNonSelfToBuiltin()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 0;
            self.a = 0;
            a = b;
            a = self.b;
            a += 1;
            self.a += 1;
            a++;
            self.a++;
            a.b = 0;
            a[0] = 1;
            self.a[0] = 1;
            a.b[0] = 1;
            a[0] += 1;
            self.a[0] += 1;
            a.b[0] += 1;
            a = a[0];
            a = self.a[0];
            a = a.b[0];
            global.a = 0;
            global.a[0] = 0;
            self.a.b = 1;
            a.b = 1;
            a = 1;
            other.a[0] = 1;
            global.a[0] = 1;
            a[0].b = 1;
            a = a[0].b;
            a = a[0];
            a[0].b[0]("a");
            a[0].b.c("a");
            a[0].b("a");
            """,
            """
            a = 0;
            a = 0;
            a = b;
            a = b;
            a += 1;
            a += 1;
            a++;
            a++;
            a.b = 0;
            a[0] = 1;
            self.a[0] = 1;
            a.b[0] = 1;
            a[0] += 1;
            self.a[0] += 1;
            a.b[0] += 1;
            a = a[0];
            a = self.a[0];
            a = a.b[0];
            global.a = 0;
            global.a[0] = 0;
            a.b = 1;
            a.b = 1;
            a = 1;
            other.a[0] = 1;
            global.a[0] = 1;
            a[0].b = 1;
            a = a[0].b;
            a = a[0];
            a[0].b[0]("a");
            a[0].b.c("a");
            a[0].b("a");
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = false,
                UsingGlobalConstantFunction = false
            }
        );
    }

    [Fact]
    public void TestNonSelfToBuiltin2()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 0;
            self.a = 0;
            a = b;
            a = self.b;
            a += 1;
            self.a += 1;
            a++;
            self.a++;
            a.b = 0;
            a[0] = 1;
            self.a[0] = 1;
            a.b[0] = 1;
            a[0] += 1;
            self.a[0] += 1;
            a.b[0] += 1;
            a = a[0];
            a = self.a[0];
            a = a.b[0];
            global.a = 0;
            global.a[0] = 0;
            self.a.b = 1;
            a.b = 1;
            a = 1;
            other.a[0] = 1;
            global.a[0] = 1;
            a[0].b = 1;
            a = a[0].b;
            a = a[0];
            a[0].b[0]("a");
            a[0].b.c("a");
            a[0].b("a");
            """,
            """
            a = 0;
            a = 0;
            a = b;
            a = b;
            a += 1;
            a += 1;
            a++;
            a++;
            a.b = 0;
            a[0] = 1;
            a[0] = 1;
            a.b[0] = 1;
            a[0] += 1;
            a[0] += 1;
            a.b[0] += 1;
            a = a[0];
            a = a[0];
            a = a.b[0];
            global.a = 0;
            global.a[0] = 0;
            a.b = 1;
            a.b = 1;
            a = 1;
            other.a[0] = 1;
            global.a[0] = 1;
            a[0].b = 1;
            a = a[0].b;
            a = a[0];
            a[0].b[0]("a");
            a[0].b.c("a");
            a[0].b("a");
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = false,
                UsingGlobalConstantFunction = true
            }
        );
    }

    [Fact]
    public void TestSelfToBuiltin()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true,
            UsingGlobalConstantFunction = true
        };
        TestUtil.VerifyRoundTrip(
            """
            a = 0;
            self.a = 0;
            a = b;
            a = self.b;
            a += 1;
            self.a += 1;
            a++;
            self.a++;
            a.b = 0;
            a[0] = 1;
            self.a[0] = 1;
            a.b[0] = 1;
            a[0] += 1;
            self.a[0] += 1;
            a.b[0] += 1;
            a = a[0];
            a = self.a[0];
            a = a.b[0];
            global.a = 0;
            global.a[0] = 0;
            self.a.b = 1;
            a.b = 1;
            a = 1;
            other.a[0] = 1;
            global.a[0] = 1;
            a[0].b = 1;
            a = a[0].b;
            a = a[0];
            a[0].b[0]("a");
            a[0].b.c("a");
            a[0].b("a");
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestThrow()
    {
        TestUtil.VerifyRoundTrip(
            """
            throw "test";
            throw new ExampleException();
            """
        );
    }

    [Fact]
    public void TestNullishCoalesce()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = b ?? c;
            d = (e ?? f) ?? (g ?? h);
            j ??= k;
            l ??= m ?? n;
            """
        );
    }

    [Fact]
    public void TestBooleanControlFlow()
    {
        TestUtil.VerifyRoundTrip(
            """
            while (1)
            {
            }
            while (true)
            {
            }
            do
            {
            }
            until (1);
            do
            {
            }
            until (true);
            if (a && 1 && b)
            {
            }
            if (a && true && b)
            {
            }
            switch (1)
            {
                case true:
                    break;
                case false:
                    break;
                case 1:
                    break;
                case 0:
                    break;
            }
            switch (true)
            {
                case true:
                    break;
                case false:
                    break;
                case 1:
                    break;
                case 0:
                    break;
            }
            """
        );
    }

    [Fact]
    public void TestTryCatchBasic()
    {
        TestUtil.VerifyRoundTrip(
            """
            try
            {
                throw "Error!";
            }
            catch (ex)
            {
                a = ex;
            }
            """
        );
    }

    [Fact]
    public void TestTryCatchFinally1()
    {
        TestUtil.VerifyRoundTrip(
            """
            try
            {
                throw "Error!";
            }
            catch (ex)
            {
                a = ex;
            }
            finally
            {
                b = 123;
            }
            """
        );
    }

    [Fact]
    public void TestTryCatchFinally2()
    {
        TestUtil.VerifyRoundTrip(
            """
            try
            {
                throw "Error!";
                return 456;
            }
            catch (ex)
            {
                a = ex;
            }
            finally
            {
                b = 123;
            }
            """
        );
    }

    [Fact]
    public void TestTryFinally()
    {
        TestUtil.VerifyRoundTrip(
            """
            try
            {
                return 123;
            }
            finally
            {
                a = "finally code";
            }
            """
        );
    }

    [Fact]
    public void TestTryBreakContinue()
    {
        TestUtil.VerifyRoundTrip(
            """
            repeat (123)
            {
                try
                {
                    if (c)
                    {
                        continue;
                    }
                    if (d)
                    {
                        break;
                    }
                }
                catch (ex)
                {
                }
            }
            """
        );
    }

    [Fact]
    public void TestTryCatchBreakContinue()
    {
        TestUtil.VerifyRoundTrip(
            """
            repeat (123)
            {
                try
                {
                    continue;
                    break;
                }
                catch (ex)
                {
                    continue;
                    break;
                }
            }
            """
        );
    }

    [Fact]
    public void TestTrySwitchBreak()
    {
        TestUtil.VerifyRoundTrip(
            """
            try
            {
                switch (a)
                {
                    default:
                        break;
                }
            }
            catch (ex)
            {
            }
            """
        );
    }

    [Fact]
    public void TestArguments()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true
        };
        TestUtil.VerifyRoundTrip(
            """
            a = argument0;
            b = argument0[1];
            c = argument[1];

            function args(arg0)
            {
                test = arg0;
                test2 = arg0[0];
                test3 = argument0;
                test4 = argument0[0];
                test5 = argument[0];
            }
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestArgumentsOld()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = false
        };
        TestUtil.VerifyRoundTrip(
            """
            a = argument0;
            b = argument0[1];
            c = argument[1];

            function args(arg0)
            {
                test = arg0;
                test2 = arg0[0];
                test3 = argument0;
                test4 = argument0[0];
                test5 = argument[0];
            }
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestStruct()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true
        };
        TestUtil.VerifyRoundTrip(
            """
            a = 
            {
                b: c + d
            };
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestRoomInstances()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true
        };
        TestUtil.VerifyRoundTrip(
            """
            a = (101234).b;
            (101234).c = d;
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestSelfAssignOld()
    {
        TestUtil.VerifyRoundTrip(
            """
            x = x;
            """,
            false,
            new GameContextMock()
            {
                Bytecode14OrLower = true
            }
        );
    }

    [Fact]
    public void TestSelfAssign()
    {
        TestUtil.VerifyRoundTrip(
            """
            x = x;
            """,
            "",
            false,
            new GameContextMock()
            {
                Bytecode14OrLower = false
            }
        );
    }

    [Fact]
    public void TestInnerLoopLocalArray()
    {
        TestUtil.VerifyRoundTrip(
            """
            while (a)
            {
                var arr;
                arr[0] = 1;
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestForWhileEdgeCases()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 0;
            while (a < 10)
            {
                a++;
            }
            for (c = 0; c < 10; c++)
            {
                test = test2;
            }
            for (d = 10; d > 0; d--)
            {
                test2 = test;
            }
            for (;;)
            {
            }
            for (; a;)
            {
                continue;
            }
            i = 0;
            for (;;)
            {
                i++;
            }
            for (;;)
            {
                if (a)
                {
                    continue;
                }
            }
            i = 0;
            for (;;)
            {
                if (a)
                {
                }
                else
                {
                }
                i++;
            }
            for (;;)
            {
                if (a)
                {
                    if (b)
                    {
                        continue;
                    }
                }
                c = 1;
            }
            for (i = 0; ; i++)
            {
                if (a)
                {
                    if (b)
                    {
                        continue;
                    }
                }
                if (c)
                {
                }
            }
            while (UnknownEnum.Value_1)
            {
                if (a)
                {
                    continue;
                }
            }

            enum UnknownEnum
            {
                Value_1 = 1
            }
            """
        );
    }

    [Fact]
    public void TestStructKeys()
    {
        GameContextMock gameContext = new();
        gameContext.DefineMockAsset(AssetType.Sprite, 16, "spr_test");
        TestUtil.VerifyRoundTrip(
            """
            a = 
            {
                b: 1,
                spr_test: 2,
                test_constant: 3,
                "even whitespace": 4
            };
            """,
            false,
            gameContext
        );
    }

    [Fact]
    public void TestReturnNewObject()
    {
        TestUtil.VerifyRoundTrip(
            """
            return new Test();
            """
        );
    }

    [Fact]
    public void TestWithWhileContinue()
    {
        TestUtil.VerifyRoundTrip(
            """
            with (a)
            {
                while (b)
                {
                    if (c)
                    {
                        continue;
                    }
                }
            }
            """
        );
    }

    [Fact]
    public void TestWithWhileBreakContinue()
    {
        TestUtil.VerifyRoundTrip(
            """
            with (a)
            {
                while (b)
                {
                    if (c)
                    {
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }
            }
            """
        );
    }

    [Fact]
    public void TestArrayOwners()
    {
        TestUtil.VerifyRoundTrip(
            """
            a[0] = 1;
            a[0] = 1;
            a.b[0] = 1;
            a.b[0] = 1;
            array_set(a(), 0, 1);
            c = [1];
            d = array_create(15);
            e[0]++;
            e[0]++;
            e.f[0]++;
            e.f[0]++;
            ++e[0];
            ++e[0];
            ++e.f[0];
            ++e.f[0];

            function TestFunc()
            {
                a[0] = 1;
                a[0] = 1;
                a.b[0] = 1;
                a.b[0] = 1;
                array_set(a(), 0, 1);
                c = [1];
                d = array_create(15);
                e[0]++;
                e[0]++;
                e.f[0]++;
                e.f[0]++;
                ++e[0];
                ++e[0];
                ++e.f[0];
                ++e.f[0];
            }
            """,
            false,
            new GameContextMock()
            {
                UsingArrayCopyOnWrite = true,
                UsingNewArrayOwners = true
            }
        );
    }

    [Fact]
    public void TestDivideQuirk()
    {
        TestUtil.VerifyRoundTrip(
            """
            a /= 2;
            a = a / 2;
            b /= 1.5;
            c.d /= 1.5;
            c.d = c.d / 1.5;
            """
        );
    }

    [Fact]
    public void TestKeywordLogicOperators()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = b and c and d;
            e = f or g or h;
            i = j xor k xor l;
            """,
            """
            a = b && c && d;
            e = f || g || h;
            i = j ^^ k ^^ l;
            """
        );
    }

    [Fact]
    public void TestModernStructNames()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            var a = 123;
            b = 
            {
                c: a,
                "a b": a,
                "a\nb": a,
                "(": a,
                "a(": a,
                begin: a,
                if: a,
                new: a,
                "function": a,
                string: a,
                "end": a,
                "self": a,
                "other": a,
                "global": a,
                self: a,
                other: a,
                global: a,
                true: a
            };
            """,
            """
            pushi.e 123
            pop.v.i local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            b [2]

            > struct_func___struct__1 (locals=0, args=0)
            :[1]
            call.i @@SetStatic@@ 0
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.c
            pushi.e -15
            pushi.e 1
            push.v [array]self.argument
            pop.v.v self."a b"
            pushi.e -15
            pushi.e 2
            push.v [array]self.argument
            pop.v.v self."a\nb"
            pushi.e -15
            pushi.e 3
            push.v [array]self.argument
            push.s "("
            conv.s.v
            call.i @@This@@ 0
            call.i variable_struct_set 3
            popz.v
            pushi.e -15
            pushi.e 4
            push.v [array]self.argument
            pop.v.v self."a("
            pushi.e -15
            pushi.e 5
            push.v [array]self.argument
            pop.v.v self.begin
            pushi.e -15
            pushi.e 6
            push.v [array]self.argument
            pop.v.v self.if
            pushi.e -15
            pushi.e 7
            push.v [array]self.argument
            pop.v.v self.new
            pushi.e -15
            pushi.e 8
            push.v [array]self.argument
            pop.v.v self.function
            pushi.e -15
            pushi.e 9
            push.v [array]self.argument
            pop.v.v self.string
            pushi.e -15
            pushi.e 10
            push.v [array]self.argument
            pop.v.v self.end
            pushi.e -15
            pushi.e 11
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.self
            pushi.e -15
            pushi.e 12
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.other
            pushi.e -15
            pushi.e 13
            push.v [array]self.argument
            pop.v.v self.global
            pushi.e -15
            pushi.e 14
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.self
            pushi.e -15
            pushi.e 15
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.other
            pushi.e -15
            pushi.e 16
            push.v [array]self.argument
            pop.v.v self.global
            pushi.e -15
            pushi.e 17
            push.v [array]self.argument
            pop.v.v self.true
            exit.i

            :[2]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__1
            call.i @@NewGMLObject@@ 19
            pop.v.v builtin.b
            """,
            """
            var a = 123;
            b = 
            {
                c: a,
                "a b": a,
                "a\nb": a,
                "(": a,
                "a(": a,
                begin: a,
                if: a,
                new: a,
                "function": a,
                string: a,
                "end": a,
                self: a,
                other: a,
                global: a,
                self: a,
                other: a,
                global: a,
                true: a
            };
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingConstructorSetStatic = true,
                UsingNewFunctionVariables = true,
                UsingOptimizedFunctionDeclarations = true,
                UsingStructAnyNonemptyString = true,
                UsingStructSpecialCaseNames = true
            }
        );
    }

    [Fact]
    public void TestNoArbitraryStringsStructNames()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            var a = 123;
            b = 
            {
                case: a,
                default: a,
                throw: a,
                "function": a,
                regular_variable: a,
                begin: a,
                "end": a,
                if: a,
                "a b": a,
                "a\nb": a,
                "a(": a,
                new: a,
                "self": a,
                "other": a,
                "global": a,
                self: a,
                other: a,
                global: a,
                true: a
            };
            """,
            """
            :[0]
            pushi.e 123
            pop.v.i local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            b [2]

            > struct_func___struct__1 (locals=0, args=0)
            :[1]
            call.i @@SetStatic@@ 0
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.case
            pushi.e -15
            pushi.e 1
            push.v [array]self.argument
            pop.v.v self.default
            pushi.e -15
            pushi.e 2
            push.v [array]self.argument
            pop.v.v self.throw
            pushi.e -15
            pushi.e 3
            push.v [array]self.argument
            pop.v.v self.function
            pushi.e -15
            pushi.e 4
            push.v [array]self.argument
            pop.v.v self.regular_variable
            pushi.e -15
            pushi.e 5
            push.v [array]self.argument
            pop.v.v self.begin
            pushi.e -15
            pushi.e 6
            push.v [array]self.argument
            pop.v.v self.end
            pushi.e -15
            pushi.e 7
            push.v [array]self.argument
            pop.v.v self.if
            pushi.e -15
            pushi.e 8
            push.v [array]self.argument
            pop.v.v self."a b"
            pushi.e -15
            pushi.e 9
            push.v [array]self.argument
            pop.v.v self."a\nb"
            pushi.e -15
            pushi.e 10
            push.v [array]self.argument
            pop.v.v self."a("
            pushi.e -15
            pushi.e 11
            push.v [array]self.argument
            pop.v.v self.new
            pushi.e -15
            pushi.e 12
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.self
            pushi.e -15
            pushi.e 13
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.other
            pushi.e -15
            pushi.e 14
            push.v [array]self.argument
            pop.v.v self.global
            pushi.e -15
            pushi.e 15
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.self
            pushi.e -15
            pushi.e 16
            push.v [array]self.argument
            call.i @@This@@ 0
            pushi.e -9
            pop.v.v [stacktop]self.other
            pushi.e -15
            pushi.e 17
            push.v [array]self.argument
            pop.v.v self.global
            pushi.e -15
            pushi.e 18
            push.v [array]self.argument
            pop.v.v self.true
            exit.i

            :[2]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -5
            pop.v.v [stacktop]global.__struct__1
            call.i @@NewGMLObject@@ 20
            pop.v.v builtin.b
            """,
            """
            var a = 123;
            b = 
            {
                case: a,
                default: a,
                throw: a,
                "function": a,
                regular_variable: a,
                begin: a,
                "end": a,
                if: a,
                "a b": a,
                "a\nb": a,
                "a(": a,
                new: a,
                self: a,
                other: a,
                global: a,
                self: a,
                other: a,
                global: a,
                true: a
            };
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingConstructorSetStatic = true,
                UsingNewFunctionVariables = true,
                UsingStructSpecialCaseNames = true
            }
        );
    }

    [Fact]
    public void TestOldStructNames()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            var a = 123;
            b = 
            {
                c: a,
                "a b": a,
                "a\nb": a,
                "a(": a,
                "begin": a,
                "end": a,
                "new": a,
                "self": a,
                "other": a,
                "global": a,
                self: a,
                other: a,
                global: a,
                true: a
            };
            """,
            """
            pushi.e 123
            pop.v.i local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            pushloc.v local.a
            b [2]

            > struct_func___struct__1 (locals=0, args=0)
            :[1]
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.c
            pushi.e -15
            pushi.e 1
            push.v [array]self.argument
            pop.v.v self."a b"
            pushi.e -15
            pushi.e 2
            push.v [array]self.argument
            pop.v.v self."a\nb"
            pushi.e -15
            pushi.e 3
            push.v [array]self.argument
            pop.v.v self."a("
            pushi.e -15
            pushi.e 4
            push.v [array]self.argument
            pop.v.v self.begin
            pushi.e -15
            pushi.e 5
            push.v [array]self.argument
            pop.v.v self.end
            pushi.e -15
            pushi.e 6
            push.v [array]self.argument
            pop.v.v self.new
            pushi.e -15
            pushi.e 7
            push.v [array]self.argument
            pop.v.v self.self
            pushi.e -15
            pushi.e 8
            push.v [array]self.argument
            pop.v.v self.other
            pushi.e -15
            pushi.e 9
            push.v [array]self.argument
            pop.v.v self.global
            pushi.e -15
            pushi.e 10
            push.v [array]self.argument
            pop.v.v self.self
            pushi.e -15
            pushi.e 11
            push.v [array]self.argument
            pop.v.v self.other
            pushi.e -15
            pushi.e 12
            push.v [array]self.argument
            pop.v.v self.global
            pushi.e -15
            pushi.e 13
            push.v [array]self.argument
            pop.v.v self.true
            exit.i

            :[2]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -16
            pop.v.v [stacktop]static.__struct__1
            call.i @@NewGMLObject@@ 15
            pop.v.v self.b
            """,
            """
            var a = 123;
            b = 
            {
                c: a,
                "a b": a,
                "a\nb": a,
                "a(": a,
                "begin": a,
                "end": a,
                "new": a,
                self: a,
                other: a,
                global: a,
                self: a,
                other: a,
                global: a,
                true: a
            };
            """
        );
    }

    [Fact]
    public void TestNonBuiltinDefaultArguments()
    {
        TestUtil.VerifyRoundTrip(
            """
            function test_func(arg0 = 123, arg1 = 456, arg2)
            {
            }
            """,
            true,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewFunctionVariables = true,
                UsingConstructorSetStatic = false,
                UsingBuiltinDefaultArguments = false
            }
        );
    }

    [Fact]
    public void TestBuiltinDefaultArguments()
    {
        TestUtil.VerifyRoundTrip(
            """
            function test_func(arg0 = 123, arg1 = 456, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 = 789)
            {
            }
            """,
            true,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewFunctionVariables = true,
                UsingConstructorSetStatic = true,
                UsingBuiltinDefaultArguments = true
            }
        );
    }

    [Fact]
    public void TestOldFunctionResolution()
    {
        TestUtil.VerifyRoundTrip(
            """
            function event_function()
            {
            }

            function other_event_function()
            {
                event_function();
            }

            event_function();
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewFunctionVariables = true,
                UsingFunctionScriptReferences = true,
                UsingNewFunctionResolution = false
            }
        );
    }

    [Fact]
    public void TestNewFunctionResolution()
    {
        TestUtil.VerifyRoundTrip(
            """
            function event_function()
            {
            }

            function other_event_function()
            {
                event_function();
            }

            event_function();
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewFunctionVariables = true,
                UsingFunctionScriptReferences = true,
                UsingNewFunctionResolution = true
            }
        );
    }

    [Fact]
    public void TestTryWithFinally()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 1;
            try
            {
                with (b)
                {
                    c = 3;
                }
            }
            finally
            {
                d = 4;
            }
            """
        );
    }

    [Fact]
    public void TestTryWhileFinally()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 1;
            try
            {
                while (b)
                {
                    c = 3;
                }
            }
            finally
            {
                d = 4;
            }
            """
        );
    }

    [Fact]
    public void TestTryDoUntilFinally()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 1;
            try
            {
                do
                {
                    c = 3;
                }
                until (b);
            }
            finally
            {
                d = 4;
            }
            """
        );
    }

    [Fact]
    public void TestAnonymousCallWithinStruct()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = 
            {
                b: (function()
                {
                })()
            };
            """
        );
    }

    [Fact]
    public void TestRoomInstanceIdCall()
    {
        TestUtil.VerifyRoundTrip(
            """
            (100123).test();
            inst_id_100123.test();
            """,
            """
            inst_id_100123.test();
            inst_id_100123.test();
            """,
            false,
            new GameContextMock()
            {
                UsingAssetReferences = true,
                UsingRoomInstanceReferences = false
            }
        );
    }

    [Fact]
    public void TestRoomInstanceIdCallRef()
    {
        TestUtil.VerifyRoundTrip(
            """
            (100123).test();
            inst_id_100123.test();
            """,
            false,
            new GameContextMock()
            {
                UsingAssetReferences = true,
                UsingRoomInstanceReferences = true
            }
        );
    }

    [Fact]
    public void TestTemplateStrings()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = $"meow {x} lalala {$""}";
            b = string("{0} now there are two of them. there are two {0}", x);
            c = string("{0} {1}", x);
            d = $"\{";
            e = $"\{x\} \{ 3\} \{\}";
            f = $"{5}";
            g = $"Split\n{
            across
            }\nmultiple lines";
            h = $"\{x\} { 3} \{\}";
            ambiguous_example = string("{0} {1} {2}", 1, 2, 3);
            unresolved = string(some_variable);
            old_bug = $"\{{123}}";
            old_bug_equivalent = string("{{0}}", 123);
            spaces_test = string("{ 0}", 123);
            spaces_test_2 = string("{0 }", 123);
            """,
            """
            a = $"meow {x} lalala {""}";
            b = string("{0} now there are two of them. there are two {0}", x);
            c = string("{0} {1}", x);
            d = "{";
            e = "{x} { 3} {}";
            f = $"{5}";
            g = $"Split\n{across}\nmultiple lines";
            h = $"\{x} {3} \{}";
            ambiguous_example = $"{1} {2} {3}";
            unresolved = string(some_variable);
            old_bug = string("{{0}}", 123);
            old_bug_equivalent = string("{{0}}", 123);
            spaces_test = string("{ 0}", 123);
            spaces_test_2 = string("{0 }", 123);
            """,
            false,
            new GameContextMock()
            {
                UsingModernTemplateStrings = false
            }
        );
    }

    [Fact]
    public void TestModernTemplateStrings()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = $"meow {x} lalala {$""}";
            b = @@string@@("{0} now there are two of them. there are two {0}", x);
            c = @@string@@("{0} {1}", x);
            d = $"\{";
            e = $"\{x\} \{ 3\} \{\}";
            f = $"{5}";
            g = $"Split\n{
            across
            }\nmultiple lines";
            h = $"\{x\} { 3} \{\}";
            no_longer_ambiguous_example = string("{0} {1} {2}", 1, 2, 3);
            unresolved_1 = string(some_variable);
            unresolved_2 = @@string@@(some_variable);
            old_bug_now_fixed = $"\{{123}}";
            spaces_test = @@string@@("{ 0}", 123);
            spaces_test_2 = @@string@@("{0 }", 123);
            """,
            """
            a = $"meow {x} lalala {""}";
            b = @@string@@("{0} now there are two of them. there are two {0}", x);
            c = @@string@@("{0} {1}", x);
            d = "{";
            e = "{x} { 3} {}";
            f = $"{5}";
            g = $"Split\n{across}\nmultiple lines";
            h = $"\{x} {3} \{}";
            no_longer_ambiguous_example = string("{0} {1} {2}", 1, 2, 3);
            unresolved_1 = string(some_variable);
            unresolved_2 = @@string@@(some_variable);
            old_bug_now_fixed = $"\{{123}}";
            spaces_test = @@string@@("{ 0}", 123);
            spaces_test_2 = @@string@@("{0 }", 123);
            """
        );
    }

    [Fact]
    public void TestStructWithImpossibleField()
    {
        TestUtil.VerifyRoundTrip(
            """
            function test()
            {
                a = 
                {
                    ";": 4,
                    "hello world": "hi programmer",
                    b: 6
                };
            }
            """,
            true,
            new GameContextMock()
            {
                UsingStructAnyNonemptyString = true,
                UsingStructSpecialCaseNames = true
            });
    }

    [Fact]
    public void TestVariableGetHashOptimization()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            test = variable_get_hash("abc");
            test = variable_get_hash("");
            test = variable_get_hash("a\nb");
            """,
            """
            push.i [variable]abc
            pop.v.i builtin.test
            push.i [variable]""
            pop.v.i builtin.test
            push.i [variable]"a\nb"
            pop.v.i builtin.test
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingVariableHashFunctions = true
            }
        );
    }

    [Fact]
    public void TestStructEmbeddedArrayOld()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a = 
            {
                b: [1, 2, 3, 4],
                c: 
                {
                    d: 5678
                }
            };
            """,
            """
            b [4]

            > struct_func___struct__1 (locals=0, args=0)
            :[1]
            pushi.e 4
            conv.i.v
            pushi.e 3
            conv.i.v
            pushi.e 2
            conv.i.v
            pushi.e 1
            conv.i.v
            call.i @@NewGMLArray@@ 4
            pop.v.v self.b
            b [3]

            > struct_func___struct__2 (locals=0, args=0)
            :[2]
            pushi.e 5678
            pop.v.i self.d
            exit.i

            :[3]
            push.i [function]struct_func___struct__2
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -5
            pop.v.v [stacktop]global.__struct__2
            call.i @@NewGMLObject@@ 1
            pop.v.v self.c
            exit.i

            :[4]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -5
            pop.v.v [stacktop]global.__struct__1
            call.i @@NewGMLObject@@ 1
            pop.v.v builtin.a
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewFunctionVariables = true
            }
        );
    }

    [Fact]
    public void TestStructEmbeddedArrayNew()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a = 
            {
                b: [1, 2, 3, 4],
                c: 
                {
                    d: 5678
                }
            };
            """,
            """
            pushi.e 4
            conv.i.v
            pushi.e 3
            conv.i.v
            pushi.e 2
            conv.i.v
            pushi.e 1
            conv.i.v
            call.i @@NewGMLArray@@ 4
            b [4]

            > struct_func___struct__1 (locals=0, args=0)
            :[1]
            call.i @@SetStatic@@ 0
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.b
            b [3]

            > struct_func___struct__2 (locals=0, args=0)
            :[2]
            call.i @@SetStatic@@ 0
            pushi.e 5678
            pop.v.i self.d
            exit.i

            :[3]
            push.i [function]struct_func___struct__2
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__2
            call.i @@NewGMLObject@@ 1
            pop.v.v self.c
            exit.i

            :[4]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__1
            call.i @@NewGMLObject@@ 2
            pop.v.v builtin.a
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewFunctionVariables = true,
                UsingOptimizedFunctionDeclarations = true,
                UsingConstructorSetStatic = true,
                UsingExternalStructArrays = true
            }
        );
    }

    [Fact]
    public void TestFunctionDeclsAdvancedOld()
    {
        Underanalyzer.Compiler.CompileContext compileContext = TestUtil.VerifyRoundTripAssembly(
            """
            function test_parent(arg0) constructor
            {
            }

            function test(arg0, arg1 = 
            {
                a: arg0
            }, arg2 = function()
            {
            }) : test_parent(
            {
                b: arg0
            }) constructor
            {
                static c = 
                {
                    d: 123
                };
                
                e = 
                {
                    f: 456
                };
            }
            """,
            """
            b [2]

            > global_func_test_parent (locals=0, args=1)
            :[1]
            exit.i

            :[2]
            push.i [function]global_func_test_parent
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -6
            pop.v.v [stacktop]self.test_parent
            popz.v
            b [20]

            > global_func_test (locals=0, args=3)
            :[3]
            push.v arg.argument1
            pushbltn.v builtin.undefined
            cmp.v.v EQ
            bf [7]

            :[4]
            push.v arg.argument0
            b [6]

            > struct_func___struct__1 (locals=0, args=0)
            :[5]
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.a
            exit.i

            :[6]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -16
            pop.v.v [stacktop]static.__struct__1
            call.i @@NewGMLObject@@ 2
            pop.v.v arg.argument1

            :[7]
            push.v arg.argument2
            pushbltn.v builtin.undefined
            cmp.v.v EQ
            bf [11]

            :[8]
            b [10]

            > anon_func_1 (locals=0, args=0)
            :[9]
            exit.i

            :[10]
            push.i [function]anon_func_1
            conv.i.v
            pushi.e -1
            conv.i.v
            call.i method 2
            pop.v.v arg.argument2

            :[11]
            push.v arg.argument0
            b [13]

            > struct_func___struct__2 (locals=0, args=0)
            :[12]
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.b
            exit.i

            :[13]
            push.i [function]struct_func___struct__2
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -16
            pop.v.v [stacktop]static.__struct__2
            call.i @@NewGMLObject@@ 2
            call.i global_func_test_parent 1
            push.i [function]global_func_test_parent
            conv.i.v
            call.i @@CopyStatic@@ 1
            isstaticok.e
            bt [17]

            :[14]
            b [16]

            > struct_func___struct__3 (locals=0, args=0)
            :[15]
            pushi.e 123
            pop.v.i self.d
            exit.i

            :[16]
            push.i [function]struct_func___struct__3
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -16
            pop.v.v [stacktop]static.__struct__3
            call.i @@NewGMLObject@@ 1
            pop.v.v static.c

            :[17]
            setstatic.e
            b [19]

            > struct_func___struct__4 (locals=0, args=0)
            :[18]
            pushi.e 456
            pop.v.i self.f
            exit.i

            :[19]
            push.i [function]struct_func___struct__4
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -16
            pop.v.v [stacktop]static.__struct__4
            call.i @@NewGMLObject@@ 1
            pop.v.v self.e
            exit.i

            :[20]
            push.i [function]global_func_test
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pushi.e -6
            pop.v.v [stacktop]self.test
            popz.v
            """,
            true
        );

        Assert.Equal(7, compileContext.OutputFunctionEntries!.Count);
        Assert.True(compileContext.OutputFunctionEntries[0].IsConstructor);
        Assert.True(compileContext.OutputFunctionEntries[0].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.FunctionDeclaration);
        Assert.Null(compileContext.OutputFunctionEntries[0].Parent);
        Assert.True(compileContext.OutputFunctionEntries[1].IsConstructor);
        Assert.True(compileContext.OutputFunctionEntries[1].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.FunctionDeclaration);
        Assert.Null(compileContext.OutputFunctionEntries[1].Parent);
        Assert.Null(compileContext.OutputFunctionEntries[2].Parent);
        Assert.True(compileContext.OutputFunctionEntries[2].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
        Assert.Null(compileContext.OutputFunctionEntries[3].Parent);
        Assert.True(compileContext.OutputFunctionEntries[3].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.FunctionDeclaration);
        Assert.Null(compileContext.OutputFunctionEntries[4].Parent);
        Assert.True(compileContext.OutputFunctionEntries[4].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
        Assert.NotNull(compileContext.OutputFunctionEntries[5].Parent);
        Assert.True(compileContext.OutputFunctionEntries[5].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
        Assert.NotNull(compileContext.OutputFunctionEntries[6].Parent);
        Assert.True(compileContext.OutputFunctionEntries[6].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
    }

    [Fact]
    public void TestFunctionDeclsAdvancedNew()
    {
        Underanalyzer.Compiler.CompileContext compileContext = TestUtil.VerifyRoundTripAssembly(
            """
            function test_parent(arg0) constructor
            {
            }

            function test(arg0, arg1 = 
            {
                a: arg0
            }, arg2 = function()
            {
            }) : test_parent(
            {
                b: arg0
            }) constructor
            {
                static c = 
                {
                    d: 123
                };
                
                e = 
                {
                    f: 456
                };
            }
            """,
            """
            b [2]

            > global_func_test_parent (locals=0, args=1)
            :[1]
            call.i @@SetStatic@@ 0
            exit.i

            :[2]
            push.i [function]global_func_test_parent
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v self.test_parent
            popz.v
            b [20]

            > global_func_test (locals=0, args=3)
            :[3]
            pushbltn.v builtin.argument1
            pushbltn.v builtin.undefined
            cmp.v.v EQ
            bf [7]

            :[4]
            push.v arg.argument0
            b [6]

            > struct_func___struct__1 (locals=0, args=0)
            :[5]
            call.i @@SetStatic@@ 0
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.a
            exit.i

            :[6]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__1
            call.i @@NewGMLObject@@ 2
            pop.v.v builtin.argument1

            :[7]
            pushbltn.v builtin.argument2
            pushbltn.v builtin.undefined
            cmp.v.v EQ
            bf [11]

            :[8]
            b [10]

            > anon_func_1 (locals=0, args=0)
            :[9]
            exit.i

            :[10]
            push.i [function]anon_func_1
            conv.i.v
            pushi.e -1
            conv.i.v
            call.i method 2
            pop.v.v builtin.argument2

            :[11]
            push.v arg.argument0
            b [13]

            > struct_func___struct__2 (locals=0, args=0)
            :[12]
            call.i @@SetStatic@@ 0
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.b
            exit.i

            :[13]
            push.i [function]struct_func___struct__2
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__2
            call.i @@NewGMLObject@@ 2
            call.i global_func_test_parent 1
            push.i [function]global_func_test_parent
            conv.i.v
            call.i @@CopyStatic@@ 1
            call.i @@SetStatic@@ 0
            isstaticok.e
            bt [17]

            :[14]
            setstatic.e
            b [16]

            > struct_func___struct__3 (locals=0, args=0)
            :[15]
            call.i @@SetStatic@@ 0
            pushi.e 123
            pop.v.i self.d
            exit.i

            :[16]
            push.i [function]struct_func___struct__3
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__3
            call.i @@NewGMLObject@@ 1
            pop.v.v static.c

            :[17]
            b [19]

            > struct_func___struct__4 (locals=0, args=0)
            :[18]
            call.i @@SetStatic@@ 0
            pushi.e 456
            pop.v.i self.f
            exit.i

            :[19]
            push.i [function]struct_func___struct__4
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__4
            call.i @@NewGMLObject@@ 1
            pop.v.v builtin.e
            exit.i

            :[20]
            push.i [function]global_func_test
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v self.test
            popz.v
            """,
            true,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingConstructorSetStatic = true,
                UsingNewFunctionVariables = true,
                UsingOptimizedFunctionDeclarations = true,
                UsingBuiltinDefaultArguments = true,
                UsingReentrantStatic = false,
                UsingFixedDefaultArgumentFunctionDecls = true
            }
        );

        Assert.Equal(7, compileContext.OutputFunctionEntries!.Count);
        Assert.True(compileContext.OutputFunctionEntries[0].IsConstructor);
        Assert.True(compileContext.OutputFunctionEntries[0].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.FunctionDeclaration);
        Assert.Null(compileContext.OutputFunctionEntries[0].Parent);
        Assert.True(compileContext.OutputFunctionEntries[1].IsConstructor);
        Assert.True(compileContext.OutputFunctionEntries[1].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.FunctionDeclaration);
        Assert.Null(compileContext.OutputFunctionEntries[1].Parent);
        Assert.NotNull(compileContext.OutputFunctionEntries[2].Parent);
        Assert.True(compileContext.OutputFunctionEntries[2].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
        Assert.NotNull(compileContext.OutputFunctionEntries[3].Parent);
        Assert.True(compileContext.OutputFunctionEntries[3].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.FunctionDeclaration);
        Assert.Null(compileContext.OutputFunctionEntries[4].Parent);
        Assert.True(compileContext.OutputFunctionEntries[4].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
        Assert.NotNull(compileContext.OutputFunctionEntries[5].Parent);
        Assert.True(compileContext.OutputFunctionEntries[5].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
        Assert.NotNull(compileContext.OutputFunctionEntries[6].Parent);
        Assert.True(compileContext.OutputFunctionEntries[6].Kind == Underanalyzer.Compiler.Bytecode.FunctionEntryKind.StructInstantiation);
    }

    [Fact]
    public void TestFunctionChainParentheses()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            function first_call()
            {
            }

            first_call().test();
            (first_call()).test();
            var_first_call().test();
            (var_first_call()).test();
            """,
            """
            b [2]

            > global_func_first_call (locals=0, args=0)
            :[1]
            exit.i

            :[2]
            push.i [function]global_func_first_call
            conv.i.v
            pushi.e -1
            conv.i.v
            call.i method 2
            dup.v 0
            pushi.e -6
            pop.v.v [stacktop]self.first_call
            popz.v
            call.i global_func_first_call 0
            dup.v 0
            pushi.e -9
            push.v [stacktop]self.test
            callv.v 0
            popz.v
            call.i global_func_first_call 0
            dup.v 0 1
            dup.v 0
            push.v stacktop.test
            callv.v 0
            popz.v
            call.i @@This@@ 0
            push.v builtin.var_first_call
            callv.v 0
            dup.v 0
            pushi.e -9
            push.v [stacktop]self.test
            callv.v 0
            popz.v
            call.i @@This@@ 0
            push.v builtin.var_first_call
            callv.v 0
            dup.v 0 1
            dup.v 0
            push.v stacktop.test
            callv.v 0
            popz.v
            """,
            true
        );
    }

    [Fact]
    public void TestFunctionChainParenthesesNew()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            function first_call()
            {
            }

            first_call().test();
            (first_call()).test();
            var_first_call().test();
            (var_first_call()).test();
            """,
            """
            b [2]

            > global_func_first_call (locals=0, args=0)
            :[1]
            exit.i

            :[2]
            push.i [function]global_func_first_call
            conv.i.v
            pushi.e -1
            conv.i.v
            call.i method 2
            dup.v 0
            pop.v.v self.first_call
            popz.v
            call.i global_func_first_call 0
            dup.v 0
            pushi.e -9
            push.v [stacktop]self.test
            dup.e 2 0
            callv.v 0
            popz.v
            call.i global_func_first_call 0
            dup.v 0 1
            dup.v 0
            push.v stacktop.test
            callv.v 0
            popz.v
            call.i @@This@@ 0
            push.v builtin.var_first_call
            callv.v 0
            dup.v 0
            pushi.e -9
            push.v [stacktop]self.test
            dup.e 2 0
            callv.v 0
            popz.v
            call.i @@This@@ 0
            push.v builtin.var_first_call
            callv.v 0
            dup.v 0 1
            dup.v 0
            push.v stacktop.test
            callv.v 0
            popz.v
            """,
            true,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingOptimizedFunctionDeclarations = true,
                UsingNewChainedFunctionArgumentOrder = true
            }
        );
    }

    [Fact]
    public void TestFunctionChainParentheses2()
    {
        TestUtil.VerifyRoundTrip(
            """
            b.c();
            self.b().c();
            a().b.c();
            """
        );
    }

    [Fact]
    public void TestFunctionChainParentheses3()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
                b = 123;
            if (c)
            {
                (test()).test();
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestFunctionChainParentheses4()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                (-b).c();
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestFunctionChainParentheses5()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                (b ? c : d).e();
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestUnaryGroupings()
    {
        TestUtil.VerifyRoundTrip(
            """
            a = -b;
            a = -(-b);
            a = -(-(-b));
            """
        );
    }

    [Fact]
    public void TestUnaryGroupings2()
    {
        TestUtil.VerifyRoundTrip(
            """
            (-a).b();
            """
        );
    }

    [Fact]
    public void TestUnaryGroupings3()
    {
        TestUtil.VerifyRoundTrip(
            """
            (-a)();
            """
        );
    }

    [Fact]
    public void TestChainParentheses()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                (b ? c : d).e = 123;
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestStandaloneStruct()
    {
        TestUtil.VerifyRoundTrip(
            """
            ({});
            """
        );
    }

    [Fact]
    public void TestStandaloneStruct2()
    {
        TestUtil.VerifyRoundTrip(
            """
            (
            {
                a: 123
            }
            );
            """
        );
    }

    [Fact]
    public void TestStandaloneStruct3()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                ({});
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestStandaloneStruct4()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                (
                {
                    b: 123
                }
                );
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestStandaloneStruct4b()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a) {
                ({
                    b: 123
                });
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true,
                OpenBlockBraceOnSameLine = true
            }
        );
    }

    [Fact]
    public void TestStandaloneStruct5()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            ({});
            """,
            """
            call.i @@NewGMLObject@@ 0
            popz.v
            """,
            false,
            new GameContextMock()
            {
                UsingOptimizedFunctionDeclarations = true
            }
        );
    }

    [Fact]
    public void TestStandaloneStruct6()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            if (a)
            {
                ({});
            }
            """,
            """
            push.v builtin.a
            conv.v.b
            bf [2]

            :[1]
            call.i @@NewGMLObject@@ 0
            popz.v

            :[2]
            """,
            false,
            new GameContextMock()
            {
                UsingOptimizedFunctionDeclarations = true,
                UsingSelfToBuiltin = true
            },
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestStandaloneStruct7()
    {
        TestUtil.VerifyRoundTrip(
            """
            (
            {
                a: -4
            }
            );
            (
            {
                a: noone
            }
            );
            (
            {
                ";": 123
            }
            );
            (
            {
                "hello world": 456
            }
            );
            """,
            false,
            new GameContextMock()
            {
                UsingStructAnyNonemptyString = true
            }
        );
    }

    [Fact]
    public void TestNonRoomInstanceParentheses()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
                a.b = 123;
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestRoomInstanceParentheses()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
            {
                (101234).abc = 789;
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestBinaryParentheses()
    {
        TestUtil.VerifyRoundTrip(
            """
            if (a)
                0(123, 456);
            if (b)
            {
                (c + d)(789);
            }
            if (e)
            {
                (function()
                {
                })(123, 456);
            }
            if (f)
            {
                (function()
                {
                } + 1)(123, 456);
            }
            """,
            false,
            null,
            new Underanalyzer.Decompiler.DecompileSettings()
            {
                RemoveSingleLineBlockBraces = true
            }
        );
    }

    [Fact]
    public void TestStructHoisting()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a = 
            {
                b: noone
            };
            c = 
            {
                d: -4
            };
            e = 
            {
                f: 4,
                g: 4.5,
                h: -4.5,
                i: -1234
            };
            """,
            """
            b [2]

            > struct_func___struct__1 (locals=0, ars=0)
            :[1]
            call.i @@SetStatic@@ 0
            pushi.e -4
            pop.v.i self.b
            exit.i

            :[2]
            push.i [function]struct_func___struct__1
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__1
            call.i @@NewGMLObject@@ 1
            pop.v.v builtin.a
            pushi.e -4
            conv.i.v
            b [4]

            > struct_func___struct__2 (locals=0, args=0)
            :[3]
            call.i @@SetStatic@@ 0
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.d
            exit.i

            :[4]
            push.i [function]struct_func___struct__2
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__2
            call.i @@NewGMLObject@@ 2
            pop.v.v builtin.c
            pushi.e -1234
            conv.i.v
            push.d -4.5
            conv.d.v
            b [6]

            > struct_func___struct__3 (locals=0, args=0)
            :[5]
            call.i @@SetStatic@@ 0
            pushi.e 4
            pop.v.i self.f
            push.d 4.5
            pop.v.d self.g
            pushi.e -15
            pushi.e 0
            push.v [array]self.argument
            pop.v.v self.h
            pushi.e -15
            pushi.e 1
            push.v [array]self.argument
            pop.v.v self.i
            exit.i

            :[6]
            push.i [function]struct_func___struct__3
            conv.i.v
            call.i @@NullObject@@ 0
            call.i method 2
            dup.v 0
            pop.v.v global.__struct__3
            call.i @@NewGMLObject@@ 3
            pop.v.v builtin.e
            """,
            true,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingConstructorSetStatic = true,
                UsingOptimizedFunctionDeclarations = true
            }
        );
    }

    [Fact]
    public void TestDivisionSpecialCase()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a = 2 * (3 + !b) / 4;
            c = (5 * (6 + !d)) / 7;
            """,
            """
            pushi.e 2
            pushi.e 3
            push.v self.b
            conv.v.b
            not.b
            conv.b.i
            add.i.i
            mul.i.i
            pushi.e 4
            conv.i.d
            div.d.i
            pop.v.d self.a
            pushi.e 5
            pushi.e 6
            push.v self.d
            conv.v.b
            not.b
            conv.b.i
            add.i.i
            mul.i.i
            conv.i.d
            pushi.e 7
            conv.i.d
            div.d.d
            pop.v.d self.c
            """
        );
    }

    [Fact]
    public void TestBitwiseSpecialCase()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a |= b;
            a = a | b;
            a &= b;
            a = a & b;
            a ^= b;
            a = a ^ b;
            """,
            """
            push.v self.a
            push.v self.b
            conv.v.l
            or.l.v
            pop.v.v self.a
            push.v self.a
            conv.v.l
            push.v self.b
            conv.v.l
            or.l.l
            pop.v.l self.a
            push.v self.a
            push.v self.b
            conv.v.l
            and.l.v
            pop.v.v self.a
            push.v self.a
            conv.v.l
            push.v self.b
            conv.v.l
            and.l.l
            pop.v.l self.a
            push.v self.a
            push.v self.b
            conv.v.l
            xor.l.v
            pop.v.v self.a
            push.v self.a
            conv.v.l
            push.v self.b
            conv.v.l
            xor.l.l
            pop.v.l self.a
            """
        );
    }

    [Fact]
    public void TestNullishPrePostOld()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a[b++] ??= 123;
            a[++b] ??= 123;
            a[b[c]++] ??= 123;
            a[++b[c]] ??= 123;
            a[b[c++]] ??= 123;
            a[b[++c]] ??= 123;
            a[b[c++]++] ??= 123;
            a[b[++c]++] ??= 123;
            a[++b[c++]] ??= 123;
            a[++b[++c]] ??= 123;
            a[b(c++, d++)] ??= 123;
            """,
            """
            pushi.e -6
            push.v builtin.b
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [2]

            :[1]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            push.v builtin.b
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.b
            conv.v.i
            pop.v.v [array]self.a
            b [3]

            :[2]
            popz.v

            :[3]
            pushi.e -6
            push.v builtin.b
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [5]

            :[4]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            push.v builtin.b
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.b
            conv.v.i
            pop.v.v [array]self.a
            b [6]

            :[5]
            popz.v

            :[6]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [8]

            :[7]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [9]

            :[8]
            popz.v

            :[9]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [11]

            :[10]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [12]

            :[11]
            popz.v

            :[12]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [14]

            :[13]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [15]

            :[14]
            popz.v

            :[15]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [17]

            :[16]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [18]

            :[17]
            popz.v

            :[18]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [20]

            :[19]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [21]

            :[20]
            popz.v

            :[21]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [23]

            :[22]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [24]

            :[23]
            popz.v

            :[24]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [26]

            :[25]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [27]

            :[26]
            popz.v

            :[27]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [29]

            :[28]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [30]

            :[29]
            popz.v

            :[30]
            pushi.e -6
            push.v builtin.d
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.d
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            call.i @@This@@ 0
            push.v builtin.b
            callv.v 2
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [32]

            :[31]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            push.v builtin.d
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.d
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            call.i @@This@@ 0
            push.v builtin.b
            callv.v 2
            conv.v.i
            pop.v.v [array]self.a
            b [33]

            :[32]
            popz.v

            :[33]
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true
            }
        );
    }

    [Fact]
    public void TestNullishPrePostNew()
    {
        TestUtil.VerifyRoundTripAssembly(
            """
            a[b++] ??= 123;
            a[++b] ??= 123;
            a[b[c]++] ??= 123;
            a[++b[c]] ??= 123;
            a[b[c++]] ??= 123;
            a[b[++c]] ??= 123;
            a[b[c++]++] ??= 123;
            a[b[++c]++] ??= 123;
            a[++b[c++]] ??= 123;
            a[++b[++c]] ??= 123;
            a[b(c++, d++)] ??= 123;
            """,
            """
            :[0]
            pushi.e -6
            push.v builtin.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [2]

            :[1]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            push.v builtin.b
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.b
            conv.v.i
            pop.v.v [array]self.a
            b [3]

            :[2]
            popz.v
            push.v builtin.b
            push.e 1
            add.i.v
            pop.v.v builtin.b

            :[3]
            pushi.e -6
            push.v builtin.b
            pushi.e 1
            add.i.v
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [5]

            :[4]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            push.v builtin.b
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.b
            conv.v.i
            pop.v.v [array]self.a
            b [6]

            :[5]
            popz.v
            push.v builtin.b
            push.e 1
            add.i.v
            pop.v.v builtin.b

            :[6]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [8]

            :[7]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [9]

            :[8]
            popz.v
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            pop.i.v [array]self.b

            :[9]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            push.v [array]self.b
            pushi.e 1
            add.i.v
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [11]

            :[10]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [12]

            :[11]
            popz.v
            pushi.e -6
            push.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            pop.i.v [array]self.b

            :[12]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [14]

            :[13]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [15]

            :[14]
            popz.v
            push.v builtin.c
            push.e 1
            add.i.v
            pop.v.v builtin.c

            :[15]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            pushi.e 1
            add.i.v
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [17]

            :[16]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [18]

            :[17]
            popz.v
            push.v builtin.c
            push.e 1
            add.i.v
            pop.v.v builtin.c

            :[18]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [20]

            :[19]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [21]

            :[20]
            popz.v
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            pop.i.v [array]self.b

            :[21]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [23]

            :[22]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            dup.v 0
            dup.i 4 6
            push.e 1
            add.i.v
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [24]

            :[23]
            popz.v
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            pop.i.v [array]self.b

            :[24]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            pushi.e 1
            add.i.v
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [26]

            :[25]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [27]

            :[26]
            popz.v
            pushi.e -6
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            pop.i.v [array]self.b

            :[27]
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            push.v [array]self.b
            pushi.e 1
            add.i.v
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [29]

            :[28]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            dup.v 0
            dup.i 4 6
            pop.i.v [array]self.b
            conv.v.i
            pop.v.v [array]self.a
            b [30]

            :[29]
            popz.v
            pushi.e -6
            push.v builtin.c
            push.e 1
            add.i.v
            dup.v 0
            pop.v.v builtin.c
            conv.v.i
            dup.i 1
            push.v [array]self.b
            push.e 1
            add.i.v
            pop.i.v [array]self.b

            :[30]
            pushi.e -6
            push.v builtin.d
            push.v builtin.c
            call.i @@This@@ 0
            push.v builtin.b
            callv.v 2
            conv.v.i
            push.v [array]self.a
            isnullish.e
            bf [32]

            :[31]
            popz.v
            pushi.e 123
            conv.i.v
            pushi.e -6
            push.v builtin.d
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.d
            push.v builtin.c
            dup.v 0
            push.e 1
            add.i.v
            pop.v.v builtin.c
            call.i @@This@@ 0
            push.v builtin.b
            callv.v 2
            conv.v.i
            pop.v.v [array]self.a
            b [33]

            :[32]
            popz.v
            push.v builtin.c
            push.e 1
            add.i.v
            pop.v.v builtin.c
            push.v builtin.d
            push.e 1
            add.i.v
            pop.v.v builtin.d

            :[33]
            """,
            false,
            new GameContextMock()
            {
                UsingSelfToBuiltin = true,
                UsingNewNullishAssignSideEffects = true
            }
        );
    }
}
