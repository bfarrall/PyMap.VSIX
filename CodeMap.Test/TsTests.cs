namespace CodeMap.Test
{
    public class TsTests
    {
        [Fact]
        public void CommonTypeScriptSyntax()
        {
            var code = """
                import fs from 'fs';

                export interface IThing {
                    id: number;
                    name?: string;
                }

                export type Maybe<T> = T | null;

                export enum Colors {
                    Red,
                    Blue,
                }

                @decorator()
                export default class MyClass<T> {
                    constructor(private value: T) {
                    }

                    async compute(x: number): Promise<number> {
                        return x * 2;
                    }

                    static helper<U>(v: U): U {
                        return v;
                    }
                }

                export function topLevel(a: string, b?: number) {
                }

                const arrow = (x: number) => x + 1;

                const exportedArrow = export const foo = (s: string) => s.length;

                namespace MyNs {
                    export class Nested {
                        nestedMethod() {}
                    }
                }
                """;

            var codeLines = code.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);

            var map = TypeScriptMapper.Generate(codeLines, true);

            // flatten and collect ids
            var items = map.Select(x => x).Concat(map.SelectMany(x => x.Children))
                .Select(x => new { x.Id, Item = x }).ToArray();

            Assert.NotEmpty(items);

            var structured = map.Structure();
            Assert.NotEmpty(structured);
        }

        [Fact]
        public void FileExtensionsAreRecognizedByParser()
        {
            var parser = new SyntaxParser();
            var exts = new[] { ".ts", ".tsx", ".mts", ".cts" };
            foreach (var ext in exts)
            {
                Assert.True(parser.CanParse("file" + ext), $"Parser should recognize {ext}");
            }
        }

        [Fact]
        public void MapperGeneratesForVariousTsFileTypes()
        {
            var code = """
                export class TestClass {
                    methodA() {}
                }

                export function helper(x: number) {}
                """;

            var codeLines = code.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);

            var exts = new[] { ".ts", ".tsx", ".mts", ".cts" };
            foreach (var ext in exts)
            {
                var map = TypeScriptMapper.Generate(codeLines, true);
                Assert.NotEmpty(map);
                var structured = map.Structure();
                Assert.NotEmpty(structured);
            }
        }

        [Fact]
        public void TsxReactComponentsAreDetected()
        {
            var code = """
                import React from 'react';

                export const MyComp = () => (<div>Hello</div>);

                export class MyClassComp extends React.Component {
                    render() { return <span/>; }
                }
                """;

            var codeLines = code.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);

            var map = TypeScriptMapper.Generate(codeLines, true);
            var items = map.Select(x => x).Concat(map.SelectMany(x => x.Children)).ToArray();
            Assert.NotEmpty(items);
            var structured = map.Structure();
            Assert.NotEmpty(structured);
        }
    }
}
