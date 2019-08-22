using System;
using NUnit.Framework;
using static WinArgsParser.CmdParser;

namespace WinArgsParser.Tests {
    [TestFixture]
    public class ArgsParserTests {
        [Test]
        public void Test() {
            var cmdParser = CmdParserCreate(
                new[] {
                    CmdOption("a|abc", () => { }, "The abc option.")
                },
                new[] {
                    CmdArgument(v => { }, "VALUE")
                },
                command: "MyCommand"
            );
            Assert.IsFalse(cmdParser.Parse(new string[] { }));
            Assert.AreEqual(@"Usage: MyCommand [options] VALUE
Options:
    /A, /ABC - The abc option.
", cmdParser.GetUsage().Replace("\t", "    "));
        }
    }
}
