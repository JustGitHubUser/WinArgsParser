using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace WinArgsParser {
    public sealed class CmdOption<T> {
        internal CmdOption(string keys, CmdOptionStore<T> store, string help, bool allowMultiple = false)
            : this(null, keys, store, help, allowMultiple) { }
        internal CmdOption(string keys, CmdOptionStoreValue<T> storeValue, string help, string metaValue, bool allowMultiple = false)
            : this(metaValue, keys, storeValue, help, allowMultiple) { }
        CmdOption(string? metaValue, string keys, object storeValue, string help, bool allowMultiple = false) {
            MetaValue = metaValue;
            StoreValue = storeValue;
            AllowMultiple = allowMultiple;
            Help = help;
            var keysSet = new HashSet<string>();
            Keys = keys.ToUpperInvariant().Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(key => {
                if(key.Any(c => !char.IsLetterOrDigit(c)))
                    throw new ArgumentException($"Forbidden char in 'keys' argument.", "keys");
                if(keysSet.Contains(key))
                    throw new ArgumentException($"Duplicated part in 'keys' argument: {key}.", "keys");
                keysSet.Add(key);
                return key;
            }).ToList().AsReadOnly();
        }
        internal readonly bool AllowMultiple;
        internal readonly IReadOnlyList<string> Keys;
        internal readonly string Help;
        internal readonly object StoreValue;
        internal readonly string? MetaValue;
    }
    public sealed class CmdArgumentChain {
        public bool NextCmdArgument { get; set; }
    }
    public sealed class CmdArgument<T> {
        internal CmdArgument(CmdListStoreValue<T> storeValue, string metaValue, bool optional = false, bool allowMultiple = false) {
            Optional = optional;
            AllowMultiple = allowMultiple;
            StoreValue = storeValue;
            MetaValue = metaValue;
        }
        internal readonly bool Optional;
        internal readonly bool AllowMultiple;
        internal readonly CmdListStoreValue<T> StoreValue;
        internal readonly string MetaValue;
    }
    public sealed class CmdParser<T> {
        readonly char optionPrefix;
        readonly char[] optionValueDelimiters;
        static readonly char[] DefaultOptionValueDelimiters = new[] { '=', ':' };
        readonly string command;
        readonly IEnumerable<CmdOption<T>> optionsList;
        readonly ReadOnlyDictionary<string, CmdOption<T>> options;
        readonly IEnumerable<CmdArgument<T>> arguments;

        internal CmdParser(IEnumerable<CmdOption<T>> options, IEnumerable<CmdArgument<T>> arguments, string? command = null, char optionPrefix = '/', char[]? optionValueDelimiters = null) {
            this.optionPrefix = optionPrefix;
            this.optionValueDelimiters = optionValueDelimiters ?? DefaultOptionValueDelimiters;
            this.command = command ?? Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) ?? throw new InvalidOperationException("CmdParser");
            this.arguments = arguments;
            this.optionsList = options;
            try {
                this.options = new ReadOnlyDictionary<string, CmdOption<T>>(
                    options.SelectMany(option => option.Keys.Select(key => new { key, option })).ToDictionary(x => x.key, x => x.option)
                );
            } catch(ArgumentException e) {
                throw new ArgumentException($"Duplicated option key.", "options", e);
            }
        }
        public bool Parse(IEnumerable<string> args, T context) {
            var allowOptions = false;
            var discoveredOptionsAndArguments = new HashSet<object>();
            IEnumerator<CmdArgument<T>>? argument = arguments.GetEnumerator();
            if(!argument.MoveNext())
                argument = null;
            foreach(var arg in args) {
                if(!ParseArg(arg, ref allowOptions, ref argument, ref discoveredOptionsAndArguments, context)) return false;
            }
            return arguments.Where(x => !x.Optional).All(discoveredOptionsAndArguments.Contains);
        }
        bool ParseArg(string arg, ref bool allowOptions, ref IEnumerator<CmdArgument<T>>? argument, ref HashSet<object> discoveredOptionsAndArguments, T context) {
            if(string.IsNullOrEmpty(arg)) return false;
            return !allowOptions || arg.Length == 0 || arg[0] != optionPrefix
                ? ParseArgument(arg, ref argument, ref discoveredOptionsAndArguments, context)
                : ParseOption(arg, ref allowOptions, ref discoveredOptionsAndArguments, context);
        }
        bool ParseArgument(string arg, ref IEnumerator<CmdArgument<T>>? argument, ref HashSet<object> discoveredOptionsAndArguments, T context) {
            if(argument == null) return false;
            if(discoveredOptionsAndArguments.Contains(argument.Current)) {
                if(!argument.Current.AllowMultiple) return false;
            } else {
                discoveredOptionsAndArguments.Add(argument.Current);
            }
            var chain = new CmdArgumentChain { NextCmdArgument = !argument.Current.AllowMultiple };
            argument.Current.StoreValue(context, arg, chain);
            if(!chain.NextCmdArgument && !argument.Current.AllowMultiple)
                throw new InvalidOperationException($"The {nameof(CmdArgument<T>)} does not allow multiple usage.");
            if(chain.NextCmdArgument) {
                if(!argument.MoveNext())
                    argument = null;
            }
            return true;
        }
        bool ParseOption(string arg, ref bool allowOptions, ref HashSet<object> discoveredOptionsAndArguments, T context) {
            if(arg.Length == 1) return false;
            if(arg[1] == optionPrefix) {
                if(arg.Length != 2) return false;
                allowOptions = false;
                return true;
            }
            var keyValue = arg.Substring(1).Split(optionValueDelimiters);
            if(keyValue.Length != 1 && keyValue.Length != 2 || keyValue[0].Length == 0) return false;
            if(!options.TryGetValue(keyValue[0].ToUpperInvariant(), out CmdOption<T> option)) return false;
            if(discoveredOptionsAndArguments.Contains(option)) {
                if(!option.AllowMultiple) return false;
            } else {
                discoveredOptionsAndArguments.Add(option);
            }
            switch(option.MetaValue) {
                case string _:
                    if(keyValue.Length == 1) return false;
                    ((CmdOptionStoreValue<T>)option.StoreValue)(context, keyValue[1]);
                    break;
                case null:
                    if(keyValue.Length == 2) return false;
                    ((CmdOptionStore<T>)option.StoreValue)(context);
                    break;
            }
            return true;
        }
        static readonly char[] CharsToQuote = new[] { ' ', '\t', '\v', '\n', '\"' };
        static string Quote(string arg) {
            if(arg.Length != 0 && arg.IndexOfAny(CharsToQuote) < 0) return arg;
            var result = new StringBuilder(2 * arg.Length);
            result.Append('\"');
            int backslashesCount = 0;
            foreach(var c in arg) {
                if(c == '\\') {
                    ++backslashesCount;
                } else if(c == '\"') {
                    result.Append(new string('\\', 2 * backslashesCount + 1));
                    backslashesCount = 0;
                    result.Append('\"');
                } else {
                    result.Append(new string('\\', backslashesCount));
                    backslashesCount = 0;
                    result.Append(c);
                }
            }
            result.Append(new string('\\', 2 * backslashesCount));
            result.Append('\"');
            return result.ToString();
        }
        public string GetUsage() {
            var usage = new StringBuilder();
            usage.Append($"Usage: {Quote(command)} [options]");
            foreach(var argument in arguments) {
                usage.Append(' ');
                usage.Append(Quote(argument.MetaValue));
                if(argument.AllowMultiple)
                    usage.Append("...");
            }
            usage.AppendLine();
            usage.AppendLine("Options:");
            foreach(var option in optionsList) {
                usage.Append('\t');
                usage.AppendLine(GetOptionUsage(option));
            }
            bool first = true;
            foreach(var option in optionsList.Where(x => x.AllowMultiple)) {
                if(first) {
                    usage.Append("Option(s) ");
                    first = false;
                } else {
                    usage.Append(" ");
                }
                usage.Append(optionPrefix + option.Keys[0]);
            }
            if(!first)
                usage.AppendLine(" can be used multiple times.");
            return usage.ToString();
        }
        string GetOptionUsage(CmdOption<T> option) {
            string MetaValueUsage(string? maybeMetaValue) {
                switch(maybeMetaValue) {
                    case string metaValue: return "=" + Quote(metaValue);
                    case null: return "";
                }
            }
            var usage = new StringBuilder();
            usage.Append(string.Join(", ", option.Keys.Select(x => optionPrefix + x + MetaValueUsage(option.MetaValue))));
            if(option.Help.Length != 0)
                usage.Append(" - " + option.Help);
            return usage.ToString();
        }
    }
    public delegate void CmdOptionStore<T>(T context);
    public delegate void CmdOptionStoreValue<T>(T context, string value);
    public delegate void CmdOptionStore();
    public delegate void CmdOptionStoreValue(string value);
    public delegate void CmdListStoreValue<T>(T context, string value, CmdArgumentChain argumentChain);
    public delegate void CmdArgumentStoreValue<T>(T context, string value);
    public delegate void CmdListStoreValue(string value, CmdArgumentChain argumentChain);
    public delegate void CmdArgumentStoreValue(string value);
    public struct Unit { }
    public static class CmdParser {
        public static CmdOption<T> CmdOption<T>(string keys, CmdOptionStore<T> store, string help, bool allowMultiple = false) {
            if(store == null)
                throw new ArgumentNullException("store");
            if(help == null)
                throw new ArgumentNullException("help");
            if(keys == null)
                throw new ArgumentNullException("keys");
            return new CmdOption<T>(keys, store, help, allowMultiple: allowMultiple);
        }
        public static CmdOption<T> CmdOption<T>(string keys, CmdOptionStoreValue<T> storeValue, string help, string metaValue, bool allowMultiple = false) {
            if(storeValue == null)
                throw new ArgumentNullException("storeValue");
            if(help == null)
                throw new ArgumentNullException("help");
            if(keys == null)
                throw new ArgumentNullException("keys");
            if(metaValue == null)
                throw new ArgumentNullException("metaValue");
            return new CmdOption<T>(keys, storeValue, help, metaValue, allowMultiple);
        }
        public static CmdOption<Unit> CmdOption(string keys, CmdOptionStore store, string help, bool allowMultiple = false) {
            if(store == null)
                throw new ArgumentNullException("store");
            if(help == null)
                throw new ArgumentNullException("help");
            if(keys == null)
                throw new ArgumentNullException("keys");
            return new CmdOption<Unit>(keys, _ => store(), help, allowMultiple: allowMultiple);
        }
        public static CmdOption<Unit> CmdOption(string keys, CmdOptionStoreValue storeValue, string help, string metaValue, bool allowMultiple = false) {
            if(storeValue == null)
                throw new ArgumentNullException("storeValue");
            if(help == null)
                throw new ArgumentNullException("help");
            if(keys == null)
                throw new ArgumentNullException("keys");
            if(metaValue == null)
                throw new ArgumentNullException("metaValue");
            return new CmdOption<Unit>(keys, (_, v) => storeValue(v), help, metaValue, allowMultiple);
        }
        public static CmdArgument<T> CmdList<T>(CmdListStoreValue<T> storeValue, string metaValue, bool optional = false) {
            if(storeValue == null)
                throw new ArgumentNullException("storeValue");
            if(metaValue == null)
                throw new ArgumentNullException("metaValue");
            return new CmdArgument<T>(storeValue, metaValue, optional, true);
        }
        public static CmdArgument<T> CmdArgument<T>(CmdArgumentStoreValue<T> storeValue, string metaValue, bool optional = false) {
            if(storeValue == null)
                throw new ArgumentNullException("storeValue");
            if(metaValue == null)
                throw new ArgumentNullException("metaValue");
            return new CmdArgument<T>((c, v, r) => storeValue(c, v), metaValue, optional, false);
        }
        public static CmdArgument<Unit> CmdList(CmdListStoreValue storeValue, string metaValue, bool optional = false) {
            if(storeValue == null)
                throw new ArgumentNullException("storeValue");
            if(metaValue == null)
                throw new ArgumentNullException("metaValue");
            return new CmdArgument<Unit>((_, v, r) => storeValue(v, r), metaValue, optional, true);
        }
        public static CmdArgument<Unit> CmdArgument(CmdArgumentStoreValue storeValue, string metaValue, bool optional = false) {
            if(storeValue == null)
                throw new ArgumentNullException("storeValue");
            if(metaValue == null)
                throw new ArgumentNullException("metaValue");
            return new CmdArgument<Unit>((_, v, r) => storeValue(v), metaValue, optional, false);
        }
        public static CmdParser<T> CmdParserCreate<T>(IEnumerable<CmdOption<T>> options, IEnumerable<CmdArgument<T>> arguments, string? command = null, char optionPrefix = '/', char[]? optionValueDelimiters = null) {
            if(options == null)
                throw new ArgumentNullException("options");
            if(arguments == null)
                throw new ArgumentNullException("arguments");
            return new CmdParser<T>(options, arguments, command, optionPrefix, optionValueDelimiters);
        }
        public static bool Parse(this CmdParser<Unit> cmdParser, IEnumerable<string> args) => cmdParser.Parse(args, default);
    }
}
